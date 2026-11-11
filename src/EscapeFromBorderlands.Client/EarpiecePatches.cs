using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Player.FirearmController), "method_58")]
internal static class EarpieceCartridgePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player.FirearmController __instance, Weapon weapon, out EarpieceRuntime.Shot? __state)
    {
        var p = BackpackRuntime.Actor(__instance);
        __state = p is null ? null : EarpieceRuntime.Begin(p, weapon);
        WeaponCombatState.Refresh(__instance); WeaponCombatState.RefreshPreparedSingleInterval(__instance, weapon);
    }
    private static Exception? Finalizer(EarpieceRuntime.Shot? __state, Exception? __exception) { EarpieceRuntime.End(__state); return __exception; }
}
[HarmonyPatch(typeof(Player), nameof(Player.OnDead))]
internal static class EarpieceKillPatch
{
    private static void Prefix(Player __instance) => EarpieceRuntime.Killed(__instance);
}
[HarmonyPatch(typeof(Weapon), "get_RecoilDelta")]
internal static class EarpieceRecoilPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    { var owner = Headwear.Owner(__instance); __result = (1f + __result) * EarpieceRuntime.StatRecoil(__instance) * Headwear.Factor(owner, HatStat.Recoil) - 1f; }
}
[HarmonyPatch(typeof(Weapon), "get_CenterOfImpactDelta")]
internal static class EarpieceAccuracyPatch
{
    private static void Postfix(Weapon __instance, ref float __result)
    { var owner = Headwear.Owner(__instance); __result = (1f + __result) * EarpieceRuntime.StatAccuracy(__instance) * Headwear.Factor(owner, HatStat.Dispersion) - 1f; }
}
[HarmonyPatch(typeof(Weapon), "get_VelocityDelta")]
internal static class EarpieceVelocityPatch
{
    private static void Postfix(Weapon __instance, ref float __result) => __result = (1f + __result) * EarpieceRuntime.Velocity(BackpackNative.WeaponOwner(__instance)) - 1f;
}
[HarmonyPatch]
internal static class EarpieceFireRatePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.PropertyGetter(typeof(Weapon), "FireRate"), AccessTools.PropertyGetter(typeof(Weapon), "SingleFireRate") };
    private static void Postfix(Weapon __instance, ref int __result)
    { var owner = Headwear.Owner(__instance); __result = Math.Max(1, (int)Math.Round(__result * EarpieceRuntime.FireRate(owner) * Headwear.Factor(owner, HatStat.FireRate))); }
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.GetWeaponDrawSpeedMultiplier))]
internal static class EarpieceDrawPatch
{
    private static void Postfix(Player.FirearmController __instance, ref float __result) => __result *= EarpieceRuntime.Draw(BackpackRuntime.Actor(__instance));
}
[HarmonyPatch(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateWeaponVariables))]
internal static class EarpieceAimPatch
{
    private static readonly FieldInfo Speed = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_aimingSpeed");
    private static void Postfix(ProceduralWeaponAnimation __instance)
    {
        var player = Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => ReferenceEquals(p.ProceduralWeaponAnimation, __instance));
        var factor = EarpieceRuntime.Aim(player) * Headwear.Factor(Headwear.Get(__instance), HatStat.Aim); if (factor == 1f) return;
        var speed = (float)Speed.GetValue(__instance)! * factor; Speed.SetValue(__instance, speed);
        if (Native.Get(__instance, "_aimSwayBlender") is Player.ValueBlender blend) blend.Speed = __instance.SwayFalloff / speed;
    }
}
[HarmonyPatch(typeof(BreathEffector), nameof(BreathEffector.Process))]
internal static class EarpieceSwayPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var pending = false; var count = 0;
        foreach (var code in instructions)
        {
            yield return code;
            if (code.operand is FieldInfo field && field.DeclaringType == typeof(BreathEffector) && field.Name == "TremorZRandom") pending = true;
            if (!pending || code.operand is not MethodInfo method || method.DeclaringType != typeof(Vector3) || method.Name != "op_Multiply") continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EarpieceSwayPatch), nameof(Injury)));
            pending = false; count++;
        }
        if (count != 1) throw new InvalidOperationException("Expected 1 native injury-sway vector; found " + count);
    }
    internal static Vector3 Injury(Vector3 value, BreathEffector breath) => EarpieceRuntime.Is(BackpackRuntime.Actor(breath.Physical), "EP09-3") ? value * .5f : value;

}
[HarmonyPatch]
internal static class EarpieceRecoilRecoveryPatch
{
    private readonly struct Snapshot
    { internal readonly float Return; internal readonly float Factor; internal Snapshot(float value, float factor) { Return = value; Factor = factor; } }
    private static IEnumerable<MethodBase> TargetMethods() => typeof(RecoilProcessBase).Assembly.GetTypes().Where(t => typeof(RecoilProcessBase).IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).Where(m => m.Name == "CalculateRecoil");
    private static void Prefix(RecoilProcessBase __instance, out Snapshot __state)
    {
        var player = EarpieceRuntime.States.Keys.FirstOrDefault(p => p.ProceduralWeaponAnimation?.Shootingg?.CurrentRecoilEffect is { } effect &&
            (ReferenceEquals(effect.HandPositionRecoilEffect, __instance) || ReferenceEquals(effect.HandRotationRecoilEffect, __instance) || ReferenceEquals(effect.CameraRotationRecoilEffect, __instance)));
        var factor = EarpieceRuntime.Is(player, "EP11-3") && player!.Physical.HandsStamina.Current >= player.Physical.HandsStamina.TotalCapacity * .8f ? 1.4f : 1f;
        __state = new Snapshot(__instance.ReturnSpeed, factor);
        __instance.ReturnSpeed *= factor;
        if (__instance is NewRotationRecoilProcess rotation && rotation.AutoFireOn) rotation.AutoFireSpeed *= factor;
    }
    private static Exception? Finalizer(RecoilProcessBase __instance, Snapshot __state, Exception? __exception)
    {
        __instance.ReturnSpeed = __state.Return;
        if (__instance is NewRotationRecoilProcess rotation && __state.Factor != 1f) rotation.AutoFireSpeed /= __state.Factor;
        return __exception;
    }
}
[HarmonyPatch(typeof(MovementContext), nameof(MovementContext.SetAimingSlowdown))]
internal static class EarpieceAimWalkingPatch
{
    private static readonly Dictionary<MovementContext, float> Originals = new();
    internal static void Reset() => Originals.Clear();
    internal static void Refresh(Player p)
    {
        if (p.HandsController?.IsAiming != true) return;
        var original = Originals.TryGetValue(p.MovementContext, out var value) ? value : .33f + (p.HandsController is Player.FirearmController f ? f.BuffInfo.AimMovementSpeed : 0f);
        p.MovementContext.RemoveStateSpeedLimit(Player.ESpeedLimit.Aiming);
        p.MovementContext.SetAimingSlowdown(true, original);
    }
    private static void Prefix(MovementContext __instance, bool isAiming, ref float slow)
    {
        if (!isAiming) { Originals.Remove(__instance); return; }
        Originals[__instance] = slow;
        if (EarpieceRuntime.Is(BackpackRuntime.Actor(__instance), "EP04-3")) slow = EarpieceEffectRules.AimingWalkLimit(slow);
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage))]
internal static class EarpieceDamagePatch
{
    internal readonly struct Hit
    { internal readonly int Depth; internal readonly float Before; internal Hit(int depth, float before) { Depth = depth; Before = before; } }
    [ThreadStatic] private static int Depth;
    private static void Prefix(ActiveHealthController __instance, ref float damage, DamageInfoStruct damageInfo, out Hit __state)
    {
        __state = new Hit(Depth++, __instance.GetBodyPartHealth(EBodyPart.Common, false).Current);
        if (__state.Depth == 0) damage *= EarpieceRuntime.Incoming(__instance.Player, damageInfo);
    }
    private static void Postfix(ActiveHealthController __instance, DamageInfoStruct damageInfo, Hit __state)
    {
        if (__state.Depth == 0 && __instance.IsAlive && __instance.GetBodyPartHealth(EBodyPart.Common, false).Current < __state.Before)
            EarpieceRuntime.Damaged(__instance.Player, damageInfo);
    }
    private static Exception? Finalizer(Hit __state, Exception? __exception) { Depth = __state.Depth; return __exception; }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.DoContusion))]
internal static class EarpieceContusionPatch
{
    private static bool Prefix(ActiveHealthController __instance, ref float applyTime)
    { if (EarpieceFlashScope.Depth > 0) applyTime *= EarpieceRuntime.Contusion(__instance.Player); return applyTime > 0f; }
}
[HarmonyPatch(typeof(Player), "method_88")]
internal static class EarpieceTinnitusPatch
{
    private static void Prefix(Player __instance, ref float time)
    { if (EarpieceRuntime.Is(__instance, "EP03-1")) time *= .25f; }
}
[HarmonyPatch(typeof(BotEventHandler), nameof(BotEventHandler.PlaySound))]
internal static class EarpieceEmittedStepPatch
{
    private static void Prefix(object person, AISoundType type, ref float power)
    { if (type == AISoundType.step) power *= EarpieceRuntime.EmittedStep(person as Player) * Headwear.Factor(person as Player, HatStat.Noise);
        else if (type is AISoundType.gun or AISoundType.silencedGun) power *= Headwear.Factor(person as Player, HatStat.Gunshot); }
}
[HarmonyPatch(typeof(BreachDoorStateClass), nameof(BreachDoorStateClass.ExecuteDoorInteraction))]
internal static class EarpieceBreachPatch
{
    private static void Prefix(WorldInteractiveObject interactive, out bool __state) => __state = (interactive as Door)?.IsBroken == true;
    private static void Postfix(WorldInteractiveObject interactive, InteractionResult interactionResult, Player user, bool __state)
    { if (!__state && interactionResult.InteractionType == EInteractionType.Breach && interactive is Door { IsBroken: true }) { EarpieceRuntime.Get(user)?.Effect.Breached(); Headwear.Breach(user); } }
}

internal static class EarpieceFoodUse
{
    private static readonly Dictionary<object, EarpieceRuntime.State> Uses = new();
    internal static void Reset() => Uses.Clear();
    internal static void Begin(object effect)
    {
        var item = Native.Get(effect, "MedItem") as Item;
        var p = BackpackRuntime.Actor(Native.Get(effect, "HealthController"));
        if (item is not null && Native.Component<FoodDrinkComponent>(item) is not null && EarpieceRuntime.Get(p) is { Key: "EP07-5" } state) Uses[effect] = state;
    }
    internal static void End(object effect)
    {
        if (!Uses.TryGetValue(effect, out var state)) return;
        Uses.Remove(effect);
        if (EarpieceRuntime.Get(state.Player) == state && Native.Get(effect, "Bool_2") is false) state.Effect.Ate();
    }
}
[HarmonyPatch]
internal static class EarpieceFoodStartPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect", BindingFlags.NonPublic), "Started");
    private static void Prefix(object __instance) => EarpieceFoodUse.Begin(__instance);
    private static void Postfix(object __instance) => HatMedical.Start(__instance);
}
[HarmonyPatch]
internal static class EarpieceFoodEndPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect", BindingFlags.NonPublic), "Residue");
    private static void Postfix(object __instance) { EarpieceFoodUse.End(__instance); HatMedical.End(__instance); }
}

[HarmonyPatch(typeof(GClass2080), nameof(GClass2080.ApplyLightAndSoundHealthEffects))]
internal static class EarpieceFlashScope
{
    [ThreadStatic] internal static int Depth;
    [HarmonyPriority(Priority.First)]
    private static void Prefix(out int __state) { __state = Depth++; }
    private static Exception? Finalizer(int __state, Exception? __exception) { Depth = __state; return __exception; }
}

[HarmonyPatch(typeof(Player), nameof(Player.CheckSurface))]
internal static class EarpieceStepAudibilityPatch
{
    private static void Prefix(Player __instance, ref float range) => range *= EarpieceAudio.Hearing(__instance, "step");
}
