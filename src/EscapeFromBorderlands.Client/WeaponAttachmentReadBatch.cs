using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// EFT's Mods is lazy: enumerating it walks the equipped weapon's slots.
// Share traversal and native sums for three adjacent change-detection reads.
// Every getter retains its usual postfixes; incompatible patches use EFT's body.
[HarmonyPatch]
internal static class WeaponAttachmentReadBatch
{
    private readonly struct NativeGetter
    {
        internal readonly string Name;
        internal readonly MethodInfo? Method;
        internal readonly string? BindError;

        internal NativeGetter(Type declaringType, string property, Type result)
        {
            Name = declaringType.Name + "." + property;
            Method = null;
            BindError = null;
            try
            {
                // Weapon and Mod both hide Item.Template with a different
                // return type. Select the native declaration and signature.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                Method = declaringType.GetProperty(property, flags, null, result, Type.EmptyTypes, null)?.GetGetMethod(true);
            }
            catch (Exception error) { BindError = error.GetType().Name; }
        }
    }

    private static readonly NativeGetter[] StatGetters =
    {
        new(typeof(Weapon), nameof(Weapon.RecoilDelta), typeof(float)),
        new(typeof(Weapon), nameof(Weapon.ErgonomicsDelta), typeof(float)),
        new(typeof(Weapon), nameof(Weapon.CenterOfImpactDelta), typeof(float))
    };
    private static readonly NativeGetter ModsGetter = new(typeof(Weapon), nameof(Weapon.Mods), typeof(IEnumerable<Mod>));
    private static readonly NativeGetter[] InputGetters =
    {
        ModsGetter,
        new(typeof(Weapon), "AllSlots", typeof(IEnumerable<Slot>)),
        new(typeof(Weapon), nameof(Weapon.Chambers), typeof(Slot[])),
        new(typeof(CompoundItem), "AllSlotsWithoutArrayInside", typeof(IEnumerable<Slot>)),
        new(typeof(ContainerCollection), "Containers", typeof(IEnumerable<IContainer>)),
        new(typeof(CompoundItem), "Containers", typeof(IEnumerable<IContainer>)),
        new(typeof(Slot), nameof(Slot.ContainedItem), typeof(Item)),
        new(typeof(Slot), "Items", typeof(IEnumerable<Item>)),
        new(typeof(Weapon), nameof(Weapon.Folded), typeof(bool)),
        new(typeof(Weapon), nameof(Weapon.Template), typeof(WeaponTemplate)),
        new(typeof(Mod), nameof(Mod.Template), typeof(ModTemplate)),
        new(typeof(Mod), nameof(Mod.Accuracy), typeof(int))
    };
    private const BindingFlags LocalMethod = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo BatchedGetter = typeof(WeaponAttachmentReadBatch).GetMethod(nameof(Read), LocalMethod)!;

    private static int Compatibility; // Recheck after all plugins are loaded, once per raid.
    private static string? Fallback;
    private static bool DirectTraversal;
    [ThreadStatic] private static Weapon? PollWeapon;
    [ThreadStatic] private static int Depth;
    [ThreadStatic] private static List<Mod>? Buffer;
    [ThreadStatic] private static bool Ready, Filling;
    [ThreadStatic] private static bool StatsReady;
    [ThreadStatic] private static bool NativeBuffer;
    [ThreadStatic] private static float Recoil, Ergonomics, Accuracy;

    private static bool Prepare()
    {
        if (ModsGetter.Method is null) return false;
        foreach (var getter in StatGetters)
            if (getter.Method is null) return false;
        return true;
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var getter in StatGetters)
            if (getter.Method is { } method) yield return method;
    }

    private static bool CanUse(in NativeGetter getter, bool requireOurTranspiler)
    {
        if (getter.Method is not { } method)
        {
            Fallback = getter.Name + " exact getter unavailable" +
                (getter.BindError is null ? "" : " (" + getter.BindError + ")");
            return false;
        }
        try
        {
            if (Harmony.GetPatchInfo(method) is { } patches)
            {
                foreach (var owner in patches.Owners)
                    if (!string.Equals(owner, Plugin.PluginGuid, StringComparison.Ordinal))
                    {
                        Fallback = $"{getter.Name} patched by {owner}";
                        return false;
                    }
                if (requireOurTranspiler)
                    foreach (var patch in patches.Transpilers)
                        if (patch.owner == Plugin.PluginGuid && patch.PatchMethod is { } installed &&
                            installed.DeclaringType == typeof(WeaponAttachmentReadBatch) && installed.Name == nameof(Transpiler)) return true;
            }
            if (!requireOurTranspiler) return true;
            Fallback = getter.Name + " attachment hook was not installed";
            return false;
        }
        catch (Exception error)
        {
            Fallback = $"could not inspect {getter.Name} patches ({error.GetType().Name})";
            return false;
        }
    }

    private static bool CanShare()
    {
        if (Compatibility != 0) return Compatibility > 0;
        foreach (var getter in StatGetters)
            if (!CanUse(getter, requireOurTranspiler: true)) { Compatibility = -1; return false; }
        foreach (var getter in InputGetters)
            if (!CanUse(getter, requireOurTranspiler: false)) { Compatibility = -1; return false; }
        DirectTraversal = WeaponAttachmentTraversal.Compatible();
        Compatibility = 1;
        return true;
    }

    internal static void ResetCompatibility()
    {
        WeaponAttachmentInputs.Reset();
        var reason = Fallback;
        Fallback = null;
        Compatibility = 0;
        if (reason is not null) Plugin.LogSource?.LogInfo("EFB firearm batching used the normal getters: " + reason);
    }

    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (ModsGetter.Method is { } native && instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = BatchedGetter;
            }
            yield return instruction;
        }
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly bool Outer;

        internal Scope(Weapon weapon, bool enabled)
        {
            Outer = Depth == 0;
            if (Outer && enabled && WeaponAttachmentTraversal.NativeType(weapon.GetType()) && CanShare()) PollWeapon = weapon;
            Depth++;
        }

        internal bool Used => Outer && (Ready || StatsReady);

        public void Dispose()
        {
            if (Outer && PollWeapon is not null)
            {
                PollWeapon = null;
                Ready = false;
                StatsReady = false;
                NativeBuffer = false;
                Buffer?.Clear();
            }
            Depth--;
        }
    }

    internal static bool IsPolling(Weapon weapon) =>
        Depth == 1 && ReferenceEquals(PollWeapon, weapon);

    // Only the three adjacent live-poll getters enter here. Keep every normal
    // postfix (Legendary, gear and native consumers). Native sums are computed
    // from live inputs once per poll; actual cartridges use the native path.
    private static bool Prefix(Weapon __instance, MethodBase __originalMethod, ref float __result)
    {
        if (!IsPolling(__instance) || Filling) return true;
        if (!StatsReady)
        {
            if (DirectTraversal && WeaponAttachmentInputs.TryRead(__instance, out var retained))
            {
                (Recoil, Ergonomics, Accuracy) = retained;
                StatsReady = true;
            }
        }
        if (!StatsReady)
        {
            double recoil = 0, ergonomics = 0;
            var accuracy = 0;
            var folded = __instance.Folded;
            // Read fills the reusable list inside this guarded polling scope.
            // Enumerating its IEnumerable return would box a list enumerator
            // once for every live firearm refresh, including ordinary weapons.
            Read(__instance);
            var mods = Buffer!;
            for (var i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                // Foreign subclasses can supply stateful template/accuracy
                // accessors; retain their original getter evaluation order.
                if (!NativeBuffer && !WeaponAttachmentTraversal.NativeType(mod.GetType())) return true;
                var template = mod.Template;
                if (!folded || mod is not Stock) recoil += template.Recoil;
                ergonomics += template.Ergonomics;
                accuracy = checked(accuracy + mod.Accuracy);
            }
            // Enumerable.Sum(float selector) accumulates in double and casts
            // before division. Preserve that rounding and int overflow behavior.
            Recoil = (float)recoil / 100f;
            Ergonomics = (float)ergonomics / Mathf.Max(1f, __instance.Template.Ergonomics);
            Accuracy = unchecked(-accuracy) / 100f;
            StatsReady = true;
        }
        __result = __originalMethod.Name == "get_RecoilDelta" ? Recoil :
            __originalMethod.Name == "get_ErgonomicsDelta" ? Ergonomics : Accuracy;
        return false;
    }

    private static IEnumerable<Mod> Read(Weapon weapon)
    {
        if (!IsPolling(weapon) || Filling) return weapon.Mods;
        if (Ready) return Buffer!;

        var buffer = Buffer ??= new List<Mod>(16);
        Filling = true;
        try
        {
            NativeBuffer = DirectTraversal && WeaponAttachmentTraversal.TryRead(weapon, buffer);
            if (!NativeBuffer)
            {
                buffer.Clear();
                buffer.AddRange(weapon.Mods);
            }
            Ready = true;
            return buffer;
        }
        catch
        {
            buffer.Clear();
            throw;
        }
        finally { Filling = false; }
    }
}
