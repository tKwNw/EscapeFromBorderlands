using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch]
internal static class BackpackCachedValuePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.PropertyGetter(typeof(Compute<float>), "Value"),
        AccessTools.PropertyGetter(typeof(Deferred<float>), "Value") };
    private static void Postfix(Compute<float> __instance, ref float __result)
    {
        if (BackpackRuntime.Values.TryGetValue(__instance, out var value)) __result *= BackpackRuntime.Value(value.Player, value.Kind) * ChestRigRuntime.Value(value.Player, value.Kind) * GrenadeBuffs.Recovery(value.Player, value.Kind) * EarpieceRuntime.Value(value.Player, value.Kind);
    }
}
internal static class BackpackWeightScope
{
    [ThreadStatic] public static Player? Player;
}
[HarmonyPatch(typeof(Physical), nameof(Physical.OnWeightUpdated))]
internal static class BackpackPhysicalWeightPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var field = AccessTools.Field(typeof(PhysicalBase), "WalkSpeedLimit");
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == System.Reflection.Emit.OpCodes.Stfld && Equals(instruction.operand, field))
            {
                // Correct only the native weight limit, before native movement
                // combines it with aiming, terrain and all other restrictions.
                var opcode = instruction.opcode; var operand = instruction.operand;
                instruction.opcode = System.Reflection.Emit.OpCodes.Ldarg_0; instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(BackpackPhysicalWeightPatch), nameof(WalkLimit)));
                yield return new CodeInstruction(opcode, operand);
                count++;
                continue;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected one native walking weight limit assignment; found {count}.");
    }
    // Feather Light removes only its own template penalties and empty weight.
    // Contents and all other carried gear still incur their ordinary weight penalties.
    internal static float WalkLimit(float value, Physical physical) => value;
    internal sealed class Snapshot
    {
        public Player? Previous;
        public Vector2 Base, Walk, Sprint, Speed;
    }
    private static void Prefix(Physical __instance, out Snapshot __state)
    {
        __state = new Snapshot { Previous = BackpackWeightScope.Player, Base = __instance.BaseOverweightLimits, Walk = __instance.WalkOverweightLimits, Sprint = __instance.SprintOverweightLimits, Speed = __instance.WalkSpeedOverweightLimits };
        var p = BackpackRuntime.Actor(__instance); BackpackWeightScope.Player = p;
        var factor = BackpackRuntime.Get(p)?.Roll.Backpack.OverweightThreshold ?? 1f;
        __instance.BaseOverweightLimits *= factor; __instance.WalkOverweightLimits *= factor;
        __instance.SprintOverweightLimits *= factor; __instance.WalkSpeedOverweightLimits *= factor;
    }
    private static Exception? Finalizer(Physical __instance, Snapshot __state, Exception? __exception)
    {
        __instance.BaseOverweightLimits = __state.Base; __instance.WalkOverweightLimits = __state.Walk;
        __instance.SprintOverweightLimits = __state.Sprint; __instance.WalkSpeedOverweightLimits = __state.Speed;
        BackpackWeightScope.Player = __state.Previous; return __exception;
    }
}
[HarmonyPatch(typeof(Deferred<float>), "get_Value")]
internal static class BackpackWeightValuePatch
{
    private static void Postfix(Deferred<float> __instance, ref float __result)
    {
        var p = BackpackWeightScope.Player;
        if (p is null) return;
        var inv = p.InventoryController.Inventory;
        if (ReferenceEquals(MeleeWeightScope.Inventory, inv)) return; // The shared EFB pass composes bag and melee discounts in order.
        if (ReferenceEquals(__instance, inv.TotalWeight) || ReferenceEquals(__instance, inv.TotalWeightEliteSkill)) __result = BackpackRuntime.Weight(p, __result);
    }
}
[HarmonyPatch(typeof(MovementContext), nameof(MovementContext.OnJump))]
internal static class BackpackJumpInputPatch
{
    private static void Prefix(MovementContext __instance) { var p = BackpackRuntime.Actor(__instance); if (p is not null) BackpackRuntime.Jump(p); }
}
[HarmonyPatch(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter))]
internal static class BackpackJumpHeightPatch
{
    private static void Postfix(JumpPlayerState __instance)
    {
        var s = BackpackRuntime.Get(BackpackRuntime.Actor(__instance.MovementContext));
        if (s?.Key == "BP16-1" && s.JumpSafe) __instance._skilledBoost *= (float)Math.Sqrt(1.5f);
        if (s is not null) __instance._skilledBoost *= BackpackRules.JumpImpulse(s.Key);
    }
}
[HarmonyPatch(typeof(JumpPlayerState), "ProcessJumpMovement")]
internal static class BackpackJumpMomentumPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        for (var i = 0; i < code.Count - 1; i++)
            if (code[i].LoadsField(AccessTools.Field(typeof(JumpPlayerState), "_currentMagnitude")) && code[i + 1].operand is MethodInfo next && next.Name == "op_Multiply" && next.DeclaringType == typeof(Vector3))
            { code[i].opcode = System.Reflection.Emit.OpCodes.Call; code[i].operand = AccessTools.Method(typeof(BackpackJumpMomentumPatch), nameof(Speed)); }
        return code;
    }
    private static float Speed(JumpPlayerState jump)
    {
        var s = BackpackRuntime.Get(BackpackRuntime.Actor(jump.MovementContext));
        if (s?.JumpSafe == true) return jump._initialMagnitude * .9f;
        return jump._currentMagnitude * (s?.JumpHorizontal > 1f ? s.JumpHorizontal : 1f);
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.HandleFall))]
internal static class BackpackFallPatch
{
    [ThreadStatic] internal static bool PreventFracture;
    private static bool Prefix(ActiveHealthController __instance, float height, ref float __result, out bool __state)
    {
        __state = PreventFracture;
        var s = BackpackRuntime.Get(__instance.Player);
        if (s?.Key == "BP07-2") PreventFracture = true;
        if (s?.JumpSafe == true && s.WasAirborne || s?.Key == "BP17-1" && !s.Jumped)
        {
            if (s.Key == "BP17-1" && height > 1.5f) s.Record.Arm("fallSpeed", 15f);
            __result = 0f; return false;
        }
        return true;
    }
    private static Exception? Finalizer(bool __state, Exception? __exception) { PreventFracture = __state; return __exception; }
}
[HarmonyPatch(typeof(MovementContext), nameof(MovementContext.CheckFlying))]
internal static class BackpackGroundStatePatch
{
    private static void Prefix(MovementContext __instance, out BackpackRuntime.State? __state) => __state = BackpackRuntime.GroundCheck(__instance);
    private static Exception? Finalizer(BackpackRuntime.State? __state, Exception? __exception)
    {
        // Native CheckFlying invokes HandleFall before this scope ends. Zero-height
        // push-off checks do not count as landings until flight was observed.
        BackpackRuntime.Landed(__state); return __exception;
    }
}
[HarmonyPatch(typeof(Player), nameof(Player.CalculateMovementVolumeDefaultMultiplier))]
internal static class BackpackMovementSoundPatch
{
    private static void Postfix(Player __instance, ref float __result) => __result *= BackpackRuntime.Noise(__instance, "movement");
}
[HarmonyPatch(typeof(GlobalEventDispatcher), nameof(GlobalEventDispatcher.PlaySound))]
internal static class BackpackAiSoundPatch
{
    private static void Prefix(IPlayer person, ref float power, AISoundType type)
    { if (type == AISoundType.step && person is Player p) power *= BackpackRuntime.Noise(p, "movement"); }
}
[HarmonyPatch]
internal static class BackpackMedBeginPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect", BindingFlags.Public | BindingFlags.NonPublic), "Started");
    private static void Prefix(object __instance) => BackpackNative.Begin(__instance);
}
[HarmonyPatch]
internal static class BackpackMedCompletePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect", BindingFlags.Public | BindingFlags.NonPublic), "Residue");
    private static void Prefix(object __instance, out BackpackMedicalRestoration.Scope? __state) => __state = BackpackMedicalRestoration.Enter(__instance, true);
    private static void Postfix(object __instance) => BackpackNative.End(__instance);
    private static Exception? Finalizer(BackpackMedicalRestoration.Scope? __state, Exception? __exception)
    { BackpackMedicalRestoration.Exit(__state); return __exception; }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!instruction.LoadsField(AccessTools.Field(typeof(JsonType.DamageEffectSpecification), "Duration"))) continue;
            count++;
            yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
            yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(BackpackMedCompletePatch), nameof(Duration)));
        }
        if (count == 0) throw new InvalidOperationException("SPT 4.1.6 painkiller duration hook was not found.");
    }
    private static float Duration(float value, object effect) => BackpackNative.Uses.TryGetValue(effect, out var use) && BackpackRuntime.Is(use.Player, "BP36-3") ? value * 1.5f : value;
}
[HarmonyPatch]
internal static class BackpackPainkillerDoseCapPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("PainKiller", BindingFlags.Public | BindingFlags.NonPublic), "StoreValues");
    private static void Prefix(object __instance, string templateId, ref float duration)
    {
        var p = BackpackRuntime.Actor(Native.Get(__instance, "HealthController"));
        if (BackpackRuntime.Is(p, "BP36-3") && BackpackNative.Uses.Values.Any(u => u.Player == p && u.Item.StringTemplateId == templateId)) duration *= 1.5f;
    }
}
[HarmonyPatch]
internal static class BackpackUseTimePatch
{
    private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(HealthEffectsComponent), "UseTime");
    private static void Postfix(HealthEffectsComponent __instance, ref float __result)
    {
        var p = BackpackNative.ItemOwner(__instance.Item);
        if (p != null) __result *= BackpackNative.UseFactor(p, __instance.Item);
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.RestoreBodyPart))]
internal static class BackpackSurgeryPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float healthPenalty)
    {
        var s = BackpackRuntime.Get(__instance.Player);
        if (s?.Key == "BP23-2" && BackpackNative.Uses.Values.Any(u => u.From(s))) healthPenalty = 1f - (1f - healthPenalty) * .25f;
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeEnergy))]
internal static class BackpackEnergyPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float value)
    {
        value = BackpackMedicalRestoration.Adjust(__instance, value, false);
        var s = BackpackRuntime.Get(__instance.Player); if (s is null) return;
        if (value < 0f && !BackpackMedicalRestoration.InScope && s.Key == "BP06-2" && __instance.Player.IsSprintEnabled && s.Bag.GetAllItems().Where(i => Native.Component<FoodDrinkComponent>(i) is not null && i.Template.ParentId != "5448e8d64bdc2dce718b4568").Select(i => i.StringTemplateId).Distinct().Count() >= 2) value *= .4f;
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHydration))]
internal static class BackpackHydrationPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float value)
    {
        value = BackpackMedicalRestoration.Adjust(__instance, value, true);
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHealth))]
internal static class BackpackHealthDamagePatch
{
    private static void Prefix(ActiveHealthController __instance, ref float value, DamageInfo damageInfo)
    {
        if (value >= 0f) return;
        var s = BackpackRuntime.Get(__instance.Player); if (s is null) return;
        if (BackpackDamageScope.Depth == 0) value *= BackpackRules.IncomingDamage(s.Key);
    }
}
[HarmonyPatch]
internal static class BackpackDoBleedPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController), "DoBleed", new[] { typeof(bool), typeof(EBodyPart) });
    private static bool Prefix(ActiveHealthController __instance, bool isHeavy, EBodyPart bodyPart)
    {
        var s = BackpackRuntime.Get(__instance.Player);
        return !isHeavy || s?.Key != "BP23-3" || !s.Record.Active("bleed:" + bodyPart);
    }
}
[HarmonyPatch]
internal static class BackpackFracturePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController), "DoFracture");
    private static bool Prefix() => !BackpackFallPatch.PreventFracture;
}
internal static class BackpackDamageScope { [ThreadStatic] internal static int Depth; }
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage))]
internal static class BackpackIncomingDamagePatch
{
    private static void Prefix(ActiveHealthController __instance, ref float damage, out int __state)
    {
        __state = BackpackDamageScope.Depth; BackpackDamageScope.Depth++;
        if (BackpackRuntime.Get(__instance.Player) is { } s && damage > 0f) damage *= BackpackRules.IncomingDamage(s.Key);
        if (BackpackRuntime.Is(__instance.Player, "BP07-3") && BackpackRuntime.BlackLeg(__instance.Player) && BackpackLegDamageScope.Player == __instance.Player) damage *= .5f;
    }
    private static void Postfix(ActiveHealthController __instance, EBodyPart bodyPart)
    {
        var s = BackpackRuntime.Get(__instance.Player);
        if (s?.Key != "BP23-3" || !s.Record.Active("bleed:" + bodyPart)) return;
        foreach (var e in __instance.GetAllEffects().Where(e => e.BodyPart == bodyPart && e.Existing && e.GetType().Name == "HeavyBleeding").ToArray()) Native.Call(e, "ForceRemove");
    }
    private static Exception? Finalizer(int __state, Exception? __exception) { BackpackDamageScope.Depth = __state; return __exception; }
}
[HarmonyPatch(typeof(Weapon), "get_ErgonomicsDelta")]
internal static class BackpackErgonomicsPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    { var s = BackpackRuntime.Get(BackpackNative.WeaponOwner(__instance)); var factor = BackpackNative.ErgonomicsFactor(s); if (factor != 1f) __result = (__result + 1f) * factor - 1f; }
}

