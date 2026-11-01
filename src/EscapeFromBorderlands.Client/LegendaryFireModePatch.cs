using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch]
internal static class LegendaryFireModePatch
{
    private static readonly Dictionary<Player.FirearmController, InputState> Inputs = new();
    private static readonly Dictionary<Player.FirearmController, int> CallDepth = new();
    private static readonly HashSet<Player.FirearmController> Synthetic = new();
    private static readonly HashSet<string> ActiveSequences = new(StringComparer.Ordinal);
    private static readonly Dictionary<Weapon, ModeScope> ModeScopes = new();
    private static int SessionGeneration;
    private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(Player.ItemHandsController), "_player");

    private sealed class InputState { public bool Held; public bool Accepted; public int ShotsAtPress; }
    internal sealed class ModeScope { public Weapon Weapon = null!; public Weapon.EFireMode Original; public Weapon.EFireMode Carrier; }
    internal sealed class TriggerCall
    {
        public bool Root; public bool Synthetic; public bool PreviousHeld; public bool WasIdle;
        public int AmmoBefore; public int ShotsBefore; public ModeScope? Scope;
    }

    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetTypesFromAssembly(typeof(Player.FirearmController).Assembly)
            .Where(type => typeof(Player.FirearmController).IsAssignableFrom(type))
            .Select(type => AccessTools.DeclaredMethod(type, nameof(Player.FirearmController.SetTriggerPressed), [typeof(bool)]))
            .Where(method => method is not null)!;

    public static bool IsHeld(Player.FirearmController controller) => Inputs.TryGetValue(controller, out var input) ? input.Held : controller.IsTriggerPressed;
    public static string LogicalMode(Weapon weapon) => ModeScopes.TryGetValue(weapon, out var scope) ? scope.Original.ToString() : weapon.FireMode.FireMode.ToString();
    private static bool IsBinary(string key) => key is "wave015_binary_hunter" or "wave015_two_in_one" or "wave015_balance";
    private static bool NeedsCarrier(string key) => IsBinary(key) || key is "wave015_high_noon" or "wave015_gift_basket" or "wave015_buccaneer";
    public static bool IsIdle(Player.FirearmController controller) => controller.CurrentOperation?.GetType() == typeof(Player.FirearmController.Idling);

    public static ModeScope? BeginCarrier(Weapon? weapon)
    {
        if (weapon is null || ModeScopes.ContainsKey(weapon) || weapon.FireMode.FireMode != Weapon.EFireMode.burst ||
            !RarityResolver.TryGetDefinition(weapon, out var definition) || !NeedsCarrier(definition.SpecialId)) return null;
        var scope = new ModeScope { Weapon = weapon, Original = weapon.FireMode.FireMode,
            Carrier = weapon is Revolver ? Weapon.EFireMode.doubleaction : Weapon.EFireMode.single };
        ModeScopes.Add(weapon, scope);
        weapon.FireMode.FireMode = scope.Carrier;
        return scope;
    }

    public static void EndCarrier(ModeScope? scope)
    {
        if (scope is null) return;
        // Do not undo a mode that another action intentionally selected.
        if (scope.Weapon.FireMode.FireMode == scope.Carrier) scope.Weapon.FireMode.FireMode = scope.Original;
        ModeScopes.Remove(scope.Weapon);
    }

    public static void ResetSession()
    {
        SessionGeneration++;
        foreach (var scope in ModeScopes.Values.ToArray()) EndCarrier(scope);
        Inputs.Clear(); CallDepth.Clear(); Synthetic.Clear(); ActiveSequences.Clear();
    }

    private static void Prefix(Player.FirearmController __instance, bool pressed, out TriggerCall __state)
    {
        CallDepth.TryGetValue(__instance, out var depth);
        CallDepth[__instance] = depth + 1;
        var weapon = __instance.Item;
        __state = new TriggerCall { Root = depth == 0, Synthetic = Synthetic.Contains(__instance), WasIdle = IsIdle(__instance),
            AmmoBefore = weapon is null ? 0 : LegendaryRuntime.RemainingAmmo(weapon), ShotsBefore = weapon is null ? 0 : LegendaryRuntime.ShotCount(weapon) };
        if (__state.Root && !__state.Synthetic && weapon is not null && RarityResolver.TryGetDefinition(weapon, out _))
        {
            if (!Inputs.TryGetValue(__instance, out var input)) Inputs[__instance] = input = new InputState();
            __state.PreviousHeld = input.Held;
            if (pressed && !input.Held) { input.Accepted = false; input.ShotsAtPress = __state.ShotsBefore; }
            input.Held = pressed;
            if (!pressed && __state.PreviousHeld) LegendaryRuntime.NotifyTriggerReleased(weapon);
        }
        __state.Scope = BeginCarrier(weapon);
    }

    private static void Postfix(Player.FirearmController __instance, bool pressed, TriggerCall __state)
    {
        EndCarrier(__state.Scope);
        __state.Scope = null;
        if (!__state.Root || __state.Synthetic || __instance.Item is not Weapon weapon ||
            !RarityResolver.TryGetDefinition(weapon, out var definition) || LogicalMode(weapon) != "burst" || !Inputs.TryGetValue(__instance, out var input)) return;
        if (pressed && !__state.PreviousHeld)
        {
            input.Accepted = __state.WasIdle && (!IsIdle(__instance) || LegendaryRuntime.ShotCount(weapon) > __state.ShotsBefore || LegendaryRuntime.RemainingAmmo(weapon) < __state.AmmoBefore);
            if (input.Accepted && definition.SpecialId == "wave015_high_noon")
                StartSequence(__instance, weapon, definition.SpecialId, Math.Min(4, Math.Max(0, __state.AmmoBefore - 1)), 900);
        }
        else if (!pressed && __state.PreviousHeld && IsBinary(definition.SpecialId) &&
            (input.Accepted || LegendaryRuntime.ShotCount(weapon) > input.ShotsAtPress))
        {
            input.Accepted = false;
            StartSequence(__instance, weapon, definition.SpecialId, 1, Math.Max(1, weapon.SingleFireRate));
        }
    }

    private static Exception? Finalizer(Player.FirearmController __instance, TriggerCall? __state, Exception? __exception)
    {
        if (__state is not null) EndCarrier(__state.Scope);
        if (CallDepth.TryGetValue(__instance, out var depth))
        {
            if (depth <= 1) CallDepth.Remove(__instance); else CallDepth[__instance] = depth - 1;
        }
        return __exception;
    }

    private static void StartSequence(Player.FirearmController controller, Weapon weapon, string specialId, int shots, int rpm)
    {
        if (shots <= 0 || Singleton<GameWorld>.Instance is not { } world) return;
        var key = weapon.Id + ":" + specialId;
        if (!ActiveSequences.Add(key)) return;
        world.StartCoroutine(FireSequence(world, controller, weapon, specialId, shots, rpm, key, LegendaryRuntime.ReloadGeneration(weapon), SessionGeneration));
    }

    private static bool CanContinue(GameWorld world, Player.FirearmController controller, Weapon weapon, string specialId, int generation, int session)
    {
        var player = PlayerField.GetValue(controller) as Player;
        // CompoundItem.IsEmpty checks storage grids; receivers have no grids
        // even when loaded. Test actual ammunition before a follow-up shot.
        return session == SessionGeneration && Singleton<GameWorld>.Instance == world && player?.HandsController == controller && controller.Item == weapon && LegendaryRuntime.RemainingAmmo(weapon) > 0 &&
            (IsIdle(controller) || controller.CurrentOperation is Player.FirearmController.FireOperation) &&
            LogicalMode(weapon) == "burst" && LegendaryRuntime.ReloadGeneration(weapon) == generation &&
            RarityResolver.TryGetDefinition(weapon, out var definition) && definition.SpecialId == specialId;
    }

    private static IEnumerator FireSequence(GameWorld world, Player.FirearmController controller, Weapon weapon, string specialId, int shots, int rpm, string key, int generation, int session)
    {
        try
        {
            var delay = new WaitForSeconds(60f / Math.Max(1, rpm));
            for (var index = 0; index < shots; index++)
            {
                yield return delay;
                if (!CanContinue(world, controller, weapon, specialId, generation, session)) yield break;
                // High Noon deliberately finishes the previous firing animation,
                // allowing its cylinder volley to use the advertised cadence.
                if (specialId == "wave015_high_noon" && controller.CurrentOperation is Player.FirearmController.FireOperation)
                {
                    var mode = BeginCarrier(weapon);
                    try { controller.CurrentOperation.FastForward(); } finally { EndCarrier(mode); }
                }
                var deadline = Time.time + 2f;
                while (!IsIdle(controller) && Time.time < deadline)
                {
                    if (!CanContinue(world, controller, weapon, specialId, generation, session)) yield break;
                    yield return null;
                }
                if (!IsIdle(controller) || !CanContinue(world, controller, weapon, specialId, generation, session)) yield break;
                Synthetic.Add(controller);
                try
                {
                    controller.SetTriggerPressed(true);
                    controller.SetTriggerPressed(false);
                }
                finally { Synthetic.Remove(controller); }
            }
        }
        finally { if (session == SessionGeneration) ActiveSequences.Remove(key); }
    }

    public static string? Caption(Weapon? weapon, string mode)
    {
        if (!RarityResolver.TryGetDefinition(weapon, out var definition)) return null;
        if (mode == "doublet" && definition.SpecialId == "wave015_out_with_a_bang") return "Final Stand";
        if (mode != "burst") return null;
        return definition.SpecialId switch
        {
            "wave015_binary_hunter" or "wave015_two_in_one" or "wave015_balance" => "Binary Trigger",
            "wave015_gift_basket" or "wave015_buccaneer" => "Buck-and-Ball",
            "wave015_high_noon" => "High Noon",
            _ => null,
        };
    }
}

[HarmonyPatch]
internal static class LegendaryFireModeNamePatch
{
    private static readonly Type PanelType = typeof(Player).Assembly.GetType("EFT.UI.AmmoCountPanel", true)!;
    private static readonly MethodInfo Show = AccessTools.Method(PanelType, "Show", [typeof(string), typeof(string)]);
    private static MethodBase TargetMethod() => AccessTools.Method(PanelType, "ShowFireMode", [typeof(Weapon.EFireMode)]);

    private static bool Prefix(object __instance)
    {
        var weapon = Singleton<GameWorld>.Instance?.MainPlayer?.HandsController?.Item as Weapon;
        if (weapon is null) return true;
        var caption = LegendaryFireModePatch.Caption(weapon, LegendaryFireModePatch.LogicalMode(weapon));
        if (caption is null) return true;
        Show.Invoke(__instance, [caption, null]);
        return false;
    }
}
