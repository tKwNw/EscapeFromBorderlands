using GlobalEventDispatcher = BotEventHandler;
using Physical = PlayerPhysicalClass;
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
        AccessTools.PropertyGetter(typeof(GClass848<float>), "Value"),
        AccessTools.PropertyGetter(typeof(GClass849<float>), "Value") };
    private static void Postfix(GClass848<float> __instance, ref float __result)
    {
        if (!BackpackRuntime.Values.TryGetValue(__instance, out var value)) return;
        var (player, kind) = value;
        var observed = PlayerRefresh.CanObserve(player);
        __result *= BackpackRuntime.Value(player, kind, observed) * ChestRigRuntime.Value(player, kind, observed) *
            GrenadeBuffs.Recovery(player, kind) * EarpieceRuntime.Value(player, kind, observed) * Headwear.Recovery(player, kind);
    }
}
internal static class BackpackWeightScope
{
    [ThreadStatic] public static Player? Player;
}
[HarmonyPatch(typeof(Physical), nameof(Physical.OnWeightUpdated))]
internal static class BackpackPhysicalWeightPatch
{
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
[HarmonyPatch(typeof(GClass849<float>), "get_Value")]
internal static class BackpackWeightValuePatch
{
    private static void Postfix(GClass849<float> __instance, ref float __result)
    {
        var p = BackpackWeightScope.Player;
        var melee = MeleeWeightScope.Inventory;
        if (p is not null)
        {
            var inv = p.InventoryController.Inventory;
            if (!ReferenceEquals(melee, inv) &&
                (ReferenceEquals(__instance, inv.TotalWeight) || ReferenceEquals(__instance, inv.TotalWeightEliteSkill)))
                __result = BackpackRuntime.Weight(p, __result);
        }
        if (melee is not null &&
            (ReferenceEquals(__instance, melee.TotalWeight) || ReferenceEquals(__instance, melee.TotalWeightEliteSkill)))
        {
            if (p is not null) __result = BackpackRuntime.Weight(p, __result);
            __result *= MeleeWeightScope.Multiplier;
        }
    }
}
[HarmonyPatch(typeof(MovementContext), nameof(MovementContext.OnJump))]
internal static class BackpackJumpInputPatch
{
    private static void Prefix(MovementContext __instance) { var p = BackpackRuntime.Actor(__instance); if (p is not null) BackpackRuntime.Jump(p); }
}
[HarmonyPatch(typeof(JumpStateClass), nameof(JumpStateClass.Enter))]
internal static class BackpackJumpHeightPatch
{
    private static void Postfix(JumpStateClass __instance)
    {
        var s = BackpackRuntime.Get(BackpackRuntime.Actor(__instance.MovementContext));
        if (s?.Key == "BP16-1" && s.JumpSafe) __instance.Float_5 *= (float)Math.Sqrt(1.5f);
        if (s is not null) __instance.Float_5 *= BackpackRules.JumpImpulse(s.Key);
    }
}
[HarmonyPatch(typeof(JumpStateClass), "method_1")]
internal static class BackpackJumpMomentumPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        for (var i = 0; i < code.Count - 1; i++)
            if (code[i].LoadsField(AccessTools.Field(typeof(JumpStateClass), "Float_0")) && code[i + 1].operand is MethodInfo next && next.Name == "op_Multiply" && next.DeclaringType == typeof(Vector3))
            { code[i].opcode = System.Reflection.Emit.OpCodes.Call; code[i].operand = AccessTools.Method(typeof(BackpackJumpMomentumPatch), nameof(Speed)); }
        return code;
    }
    private static float Speed(JumpStateClass jump)
    {
        var s = BackpackRuntime.Get(BackpackRuntime.Actor(jump.MovementContext));
        if (s?.JumpSafe == true) return jump.Float_1 * .9f;
        return jump.Float_0 * (s?.JumpHorizontal > 1f ? s.JumpHorizontal : 1f);
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
[HarmonyPatch(typeof(Player), "method_64")]
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
            if (!instruction.LoadsField(AccessTools.Field(typeof(GClass1443), "Duration"))) continue;
            count++;
            yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
            yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(BackpackMedCompletePatch), nameof(Duration)));
        }
        if (count == 0) throw new InvalidOperationException("SPT 4.0.13 painkiller duration hook was not found.");
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
    private static void Prefix(ActiveHealthController __instance, ref float value, DamageInfoStruct damageInfo)
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

