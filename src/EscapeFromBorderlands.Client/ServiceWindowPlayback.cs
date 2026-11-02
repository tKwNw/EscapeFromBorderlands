using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// The starting state need not use UseTimeMultiplier. Apply this signature to
// the selected medical animator after Spawn, retaining native and other speeds.
internal static class ServiceWindowPlayback
{
    private sealed class Entry
    {
        internal Player Player = null!;
        internal Player.MedsController Controller = null!;
        internal FirearmsAnimator Animator = null!;
        internal object Target = null!;
        internal float Native, Factor = 1f, Applied;
        internal bool Logged;
    }
    private static readonly Dictionary<FirearmsAnimator, Entry> Entries = new();
    private static readonly Dictionary<Type, PropertyInfo> SpeedProperties = new();
    private static bool _reported;

    private static PropertyInfo SpeedProperty(object target)
    {
        var type = target.GetType();
        if (SpeedProperties.TryGetValue(type, out var property)) return property;
        // The native backend is IAnimator, which may implement speed explicitly.
        property = type.GetInterfaces().Select(t => t.GetProperty("speed"))
            .FirstOrDefault(p => p?.PropertyType == typeof(float) && p.CanRead && p.CanWrite)
            ?? type.GetProperty("speed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is null || property.PropertyType != typeof(float) || !property.CanRead || !property.CanWrite)
            throw new MissingMemberException(type.FullName, "speed");
        return SpeedProperties[type] = property;
    }
    private static float Read(object target) => (float)SpeedProperty(target).GetValue(target)!;
    private static void Write(object target, float value) => SpeedProperty(target).SetValue(target, value);
    private static bool Same(float a, float b) => Math.Abs(a - b) < .0001f;
    private static bool Owned(Entry e) => !e.Controller.Destroyed && e.Player.HealthController?.IsAlive == true &&
        ReferenceEquals(e.Player.HandsAnimator, e.Animator) &&
        ReferenceEquals(ServiceWindow.MedicalController(e.Player, e.Animator), e.Controller) &&
        ServiceWindow.IsMedical(e.Controller.Item);
    private static void Restore(Entry e)
    {
        if (Same(e.Factor, 1f)) return;
        var actual = Read(e.Target);
        // A native reset has already removed our factor; do not divide it again.
        if (!Same(actual, e.Native)) Write(e.Target, actual / e.Factor);
        e.Factor = 1f;
    }
    private static void Apply(Entry e)
    {
        e.Factor = ServiceWindow.Speed(e.Player);
        var desired = e.Native * e.Factor;
        if (!Same(Read(e.Target), desired)) Write(e.Target, desired);
        e.Applied = desired;
    }
    private static void Report(Exception ex)
    {
        if (_reported) return;
        _reported = true;
        Plugin.LogSource.LogWarning($"Service Window playback unavailable: {ex.Message}");
    }

    internal static void Spawned(Player.MedsController controller, float nativeSpeed)
    {
        try
        {
            var player = BackpackRuntime.Actor(controller);
            if (player is null || !ServiceWindow.IsMedical(controller.Item) ||
                Native.Get(controller, "firearmsAnimator_0") is not FirearmsAnimator animator ||
                !ReferenceEquals(player.HandsAnimator, animator) ||
                !ReferenceEquals(ServiceWindow.MedicalController(player, animator), controller)) return;
            var target = Native.Get(animator, "Animator");
            if (target is null) return;
            var entry = new Entry { Player = player, Controller = controller, Animator = animator, Target = target, Native = nativeSpeed };
            if (Entries.TryGetValue(animator, out var previous))
            {
                if (ReferenceEquals(previous.Controller, controller))
                { entry.Native = previous.Native; entry.Logged = previous.Logged; }
                if (!ReferenceEquals(previous.Target, target)) Restore(previous);
            }
            Entries[animator] = entry;
            Apply(entry);
            if (!entry.Logged && ChestRigRuntime.Is(player, "TR17-2"))
            {
                entry.Logged = true;
                Plugin.LogSource.LogInfo($"Service Window playback {Plugin.PluginVersion}: item={controller.Item.StringTemplateId}, inventory={player.IsInventoryOpened}, native={entry.Native:F2}, factor={entry.Factor:F2}, actual={Read(target):F2}.");
            }
        }
        catch (Exception ex) { Report(ex); }
    }

    internal static void NativeChanged(FirearmsAnimator animator, float speed)
    {
        if (!Entries.TryGetValue(animator, out var e)) return;
        try
        {
            var target = Native.Get(animator, "Animator");
            if (!Owned(e) || target is null)
            {
                // SetAnimationSpeed already supplied a fresh value for this
                // backend. Restore only an abandoned, different backend.
                if (!ReferenceEquals(e.Target, target)) Restore(e);
                Entries.Remove(animator);
                return;
            }
            if (!ReferenceEquals(e.Target, target)) { Restore(e); e.Target = target; }
            e.Native = speed;
            Apply(e);
        }
        catch (Exception ex) { Report(ex); }
    }

    internal static void Maintain()
    {
        foreach (var pair in Entries.ToArray())
        {
            var e = pair.Value;
            try
            {
                if (!Owned(e)) { Restore(e); Entries.Remove(pair.Key); continue; }
                var target = Native.Get(e.Animator, "Animator");
                if (target is null) { Restore(e); Entries.Remove(pair.Key); continue; }
                if (!ReferenceEquals(target, e.Target))
                {
                    Restore(e);
                    e.Target = target;
                    e.Native = Read(target);
                    e.Factor = 1f;
                    e.Applied = e.Native;
                }
                var actual = Read(e.Target);
                if (!Same(actual, e.Applied) && !Same(actual, e.Native)) e.Native = actual / e.Factor;
                Apply(e);
            }
            catch (Exception ex) { Report(ex); }
        }
    }

    internal static void Reset()
    {
        foreach (var e in Entries.Values)
            try { Restore(e); } catch (Exception ex) { Report(ex); }
        Entries.Clear();
        _reported = false;
    }
}

[HarmonyPatch(typeof(Player.MedsController), nameof(Player.MedsController.Spawn))]
internal static class ServiceWindowPlaybackSpawnPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(Player.MedsController __instance, float animationSpeed) => ServiceWindowPlayback.Spawned(__instance, animationSpeed);
}

[HarmonyPatch(typeof(ObjectInHandsAnimator), nameof(ObjectInHandsAnimator.SetAnimationSpeed))]
internal static class ServiceWindowPlaybackSpeedPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(ObjectInHandsAnimator __instance, float speed)
    { if (__instance is FirearmsAnimator animator) ServiceWindowPlayback.NativeChanged(animator, speed); }
}
