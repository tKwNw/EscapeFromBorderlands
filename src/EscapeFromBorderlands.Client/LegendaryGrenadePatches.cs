using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Systems.Effects;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(GrenadeFactoryClass), nameof(GrenadeFactoryClass.Create))]
internal static class LegendaryGrenadeCreatedPatch
{
    private static void Postfix(Grenade __result, ThrowWeapItemClass item, string profileId)
    {
        if (__result == null) return;
        try { LegendaryGrenadeEffects.Created(__result, item, profileId); }
        catch (Exception e) { Plugin.LogSource.LogError("Legendary grenade initialization failed: " + e.Message); }
    }
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.Explosion))]
internal static class LegendaryGrenadeSignaturePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Item originalWeaponItem, out LegendaryGrenadeEffects.Payload? __state)
    {
        __state = LegendaryGrenadeEffects.CurrentExplosion;
        LegendaryGrenadeEffects.CurrentExplosion = LegendaryGrenadeEffects.Try(originalWeaponItem, out var p) ? p : null;
    }
    private static void Postfix(Grenade grenade, Item originalWeaponItem, Vector3 grenadePosition) => LegendaryGrenadeEffects.Detonated(grenade, originalWeaponItem, grenadePosition);
    private static Exception? Finalizer(LegendaryGrenadeEffects.Payload? __state, Exception? __exception)
    { LegendaryGrenadeEffects.CurrentExplosion = __state; return __exception; }
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.OnCollisionEnter))]
internal static class LegendaryGrenadeContactPatch
{
    private static bool Prefix(Grenade __instance, Collision __0) => LegendaryGrenadeEffects.Collision(__instance, __0);
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.LateUpdate))]
internal static class LegendaryGrenadeAnchorPatch
{
    private static void Postfix(Grenade __instance) => LegendaryGrenadeEffects.Follow(__instance);
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.InvokeBlowUpEvent))]
internal static class LegendaryGrenadeTrailFusePatch
{
    private static bool Prefix(Grenade __instance) => LegendaryGrenadeEffects.Fuse(__instance);
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "vmethod_0")]
internal static class LegendaryGrenadeDeferredEmissionPatch
{
    private static bool Prefix(Player.BaseGrenadeHandsController __instance) => !LegendaryGrenadeEffects.DeferInHandSmoke(__instance);
}
[HarmonyPatch(typeof(SmokeGrenade), nameof(SmokeGrenade.OnExplosion))]
internal static class LegendaryGrenadeSmokeStartedPatch
{
    private static void Postfix(SmokeGrenade __instance) => LegendaryGrenadeEffects.Emitting(__instance, Time.time);
}
[HarmonyPatch(typeof(SmokeGrenade), nameof(SmokeGrenade.StartEmissionBehaviour))]
internal static class LegendaryGrenadeSmokeLifetimePatch
{
    private static bool Prefix(SmokeGrenade __instance) => LegendaryGrenadeEffects.StartSmokeTimer(__instance);
}
[HarmonyPatch(typeof(GClass2085), nameof(GClass2085.smethod_0))]
internal static class LegendaryGrenadeFalloffPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var inverse = AccessTools.Method(typeof(Mathf), nameof(Mathf.InverseLerp)); var found = 0;
        var code = instructions.ToList();
        for (var i = 0; i + 3 < code.Count; i++)
        {
            // Native code also interpolates camera shake, armor penetration and
            // contusion. Match the curve multiplied by explosive strength only.
            if (Equals(code[i].operand, inverse) && code[i + 1].opcode == OpCodes.Ldarg_1 &&
                code[i + 2].operand is MethodInfo strength && strength.Name == "get_GetStrength" && code[i + 3].opcode == OpCodes.Mul)
            {
                code[i].operand = AccessTools.Method(typeof(LegendaryGrenadeFalloffPatch), nameof(Falloff));
                found++;
            }
        }
        if (found != 1) throw new InvalidOperationException("Expected 1 native blast-strength curve; found " + found);
        foreach (var instruction in code) yield return instruction;
    }
    internal static float Falloff(float outer, float inner, float distance) => LegendaryGrenadeEffects.CurrentExplosion?.Key == "GR11" && distance <= outer
        ? 1f : Mathf.InverseLerp(outer, inner, distance);
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage))]
internal static class LegendaryGrenadeDamagePatch
{
    [ThreadStatic] private static int Depth;
    internal sealed class Hit { internal int Depth; internal float Before; internal bool Bleed; }
    private static void Prefix(ActiveHealthController __instance, EBodyPart bodyPart, ref float damage, DamageInfoStruct damageInfo, out Hit __state)
    {
        __state = new Hit { Depth = Depth++, Before = __instance.GetBodyPartHealth(bodyPart, false).Current };
        if (__state.Depth != 0) return;
        var target = __instance.Player;
        if (damageInfo.DamageType == EDamageType.Bullet && damageInfo.Weapon is Weapon && damageInfo.Player?.iPlayer is Player shooter && GrenadeBuffs.MarkedBy(target, shooter)) damage *= 1.3f;
        // SPT 4.0.13 tags ballistic grenade fragments as Bullet. The native
        // blast uses GrenadeFragment, and both retain the thrown weapon item.
        if (damageInfo.DamageType is not (EDamageType.Bullet or EDamageType.GrenadeFragment) || damageInfo.Weapon is not ThrowWeapItemClass) return;
        damage *= GrenadeBuffs.Incoming(target);
        if (!LegendaryGrenadeEffects.Try(damageInfo.Weapon, out var p)) return;
        if (p.Key == "GR21" && target.ProfileId == p.Owner) { damage = 0f; return; }
        var blast = damageInfo.SourceId == p.Roll.Definition.SourceTemplateId;
        if (blast) damage *= GrenadeEffectRules.BlastMultiplier(p.Key, Vector3.Distance(target.Position, damageInfo.MasterOrigin),
            bodyPart is EBodyPart.LeftLeg or EBodyPart.RightLeg, target.ProfileId == p.DirectTarget, p.PistolOnly);
        __state.Bleed = !blast && p.Key == "GR07" && !damageInfo.Blunt && damage > 0f;
    }
    private static void Postfix(ActiveHealthController __instance, EBodyPart bodyPart, Hit __state)
    {
        if (__state.Bleed && __instance.IsAlive && __instance.GetBodyPartHealth(bodyPart, false).Current < __state.Before) __instance.DoBleed(true, bodyPart);
    }
    private static Exception? Finalizer(Hit __state, Exception? __exception) { Depth = __state.Depth; return __exception; }
}
[HarmonyPatch(typeof(GClass2080), nameof(GClass2080.ApplyLightAndSoundHealthEffects))]
internal static class LegendaryGrenadeFlashPatch
{
    private static readonly MethodInfo Linecast = AccessTools.Method(AccessTools.TypeByName("UnityEngine.Physics"), "Linecast", new[] { typeof(Vector3), typeof(Vector3), typeof(int) });
    private static bool Prefix(List<IPlayerOwner> players, Dictionary<IPlayerOwner, GStruct230> playerEmitterCaches, Vector3 emitterPosition, Vector3 blindnessSettings, Vector3 contusionSettings)
    {
        var payload = LegendaryGrenadeEffects.CurrentExplosion;
        if (payload == null) return true;
        foreach (var bridge in players)
        {
            var target = GrenadeBuffs.Find(bridge.iPlayer.ProfileId);
            if (target?.ActiveHealthController is not { } health || !health.IsAlive || payload.Key == "GR47" && target.ProfileId == payload.Owner) continue;
            var cache = playerEmitterCaches[bridge];
            if (!cache.TryToApplyStun && !cache.TryToApplyBurnEyes && !cache.TryToApplyContusion) continue;
            if ((bool)Linecast.Invoke(null, new object[] { emitterPosition, target.PlayerBones.Head.position, (int)LayerMaskClass.GrenadeObstaclesColliderMask })!) continue;
            var facing = Mathf.Clamp01(Vector3.Dot(cache.DirectionToEmitter, bridge.iPlayer.LookDirection));
            var radius = payload.Roll.GrenadeFlashRadiusMultiplier;
            var sound = Mathf.InverseLerp(blindnessSettings.y / radius, blindnessSettings.x / radius, cache.Distance);
            var visual = Mathf.InverseLerp(blindnessSettings.y, blindnessSettings.x, cache.Distance) * facing;
            if (bridge.IsAI && target.AIData?.BotOwner is { } bot && bot.BotState == EBotState.Active)
            {
                var duration = blindnessSettings.z * bot.Settings.FileSettings.Grenade.FLASH_GRENADE_TIME_COEF;
                if (sound > 0f) bot.FlashGrenade.AddSoundEffect(duration * sound, emitterPosition);
                if (visual > 0f) bot.FlashGrenade.AddBlindEffect(duration * visual, emitterPosition);
            }
            if (visual > 0f)
            {
                if (cache.TryToApplyStun) health.DoStun(blindnessSettings.z, visual);
                if (cache.TryToApplyBurnEyes) health.DoBurnEyes(emitterPosition, Mathf.InverseLerp(blindnessSettings.y, 0f, cache.Distance), visual, blindnessSettings.z);
            }
            if (cache.TryToApplyContusion && contusionSettings != Vector3.zero)
            {
                var duration = Mathf.InverseLerp(contusionSettings.y, contusionSettings.x, cache.Distance) * contusionSettings.z;
                if (duration > 2f) health.DoContusion(duration, 1f);
            }
        }
        return false;
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.DoBurnEyes))]
internal static class LegendaryGrenadeFlashedPatch
{
    private static void Postfix(ActiveHealthController __instance, float normalTime, float maxTime)
    { if (__instance.DamageCoeff > 0f && normalTime > 0f && maxTime > 0f && LegendaryGrenadeEffects.CurrentExplosion is { } payload) GrenadeBuffs.Flashed(payload, __instance.Player); }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.DoContusion))]
internal static class LegendaryGrenadeConcussionImmunityPatch
{
    private static bool Prefix(ActiveHealthController __instance) => LegendaryGrenadeEffects.CurrentExplosion is not { Key: "GR47" } p || p.Owner != __instance.Player.ProfileId;
}
[HarmonyPatch(typeof(Weapon), "get_RecoilDelta")]
internal static class LegendaryGrenadeRecoilPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Weapon __instance, ref float __result) => __result = (1f + __result) * GrenadeBuffs.Recoil(BackpackNative.WeaponOwner(__instance)) - 1f;
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.UpdateHipInaccuracy))]
internal static class LegendaryGrenadeHipPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Player.FirearmController __instance)
    {
        var p = BackpackRuntime.Actor(__instance);
        __instance.HipInaccuracy *= GrenadeBuffs.Hip(p);
        if (p != null) p.ProceduralWeaponAnimation.Breath.HipPenalty = __instance.HipInaccuracy;
    }
}
[HarmonyPatch(typeof(SmokeGrenade), nameof(SmokeGrenade.UpdateEmissionArea))]
internal static class LegendaryGrenadeSmokeAreaPatch
{
    private static void Prefix(SmokeGrenade __instance, ref float normalTime) => LegendaryGrenadeEffects.SmokeArea(__instance, ref normalTime);
    private static void Postfix(SmokeGrenade __instance) => LegendaryGrenadeEffects.SmokeScale(__instance);
}
[HarmonyPatch(typeof(SmokeGrenade), "get_Radius")]
internal static class LegendaryGrenadeSmokeRadiusPatch
{
    private static void Postfix(SmokeGrenade __instance, ref float __result)
    {
        if (LegendaryGrenadeEffects.Try(__instance.WeaponSource, out var p) && !p.SmokeChild)
            __result *= p.Roll.GrenadeSmokeSizeMultiplier * (p.Key == "GR56" ? 2f : 1f);
    }
}
[HarmonyPatch(typeof(GrenadeEmission), nameof(GrenadeEmission.AttachTo))]
internal static class LegendaryGrenadeEmissionPatch
{
    private static void Postfix(GrenadeEmission __instance, Transform t) => LegendaryGrenadeEffects.Attached(__instance, t);
}
[HarmonyPatch(typeof(Effects), nameof(Effects.DisposeEmissionEffect))]
internal static class LegendaryGrenadeEmissionResetPatch
{
    private static void Prefix(GrenadeEmission __0) => LegendaryGrenadeEffects.RestoreEmission(__0);
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), nameof(Player.BaseGrenadeHandsController.Spawn))]
internal static class LegendaryGrenadeDrawPatch
{
    private static void Prefix(Player.BaseGrenadeHandsController __instance, ref float animationSpeed)
    { animationSpeed *= EarpieceRuntime.Is(BackpackRuntime.Actor(__instance), "EP07-2") ? 1.6f : 1f; if (ReusableGrenadeRuntime.IsReusable(__instance.Item)) animationSpeed *= RarityResolver.Resolve(__instance.Item)!.GrenadeDrawSpeedMultiplier; }
    private static void Postfix(Player.BaseGrenadeHandsController __instance) => LegendaryGrenadeDrawAudio.Drawn(__instance);
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "method_1")]
internal static class LegendaryGrenadeThrowAnimationPatch
{
    private static void Postfix(Player.BaseGrenadeHandsController __instance)
    {
        var p = BackpackRuntime.Actor(__instance);
        var factor = EarpieceRuntime.Is(p, "EP07-2") ? 1.6f : 1f;
        if (ReusableGrenadeRuntime.IsReusable(__instance.Item)) factor *= RarityResolver.Resolve(__instance.Item)!.GrenadeThrowSpeedMultiplier;
        if (factor != 1f && p?.HandsAnimator is { } animator) animator.SetAnimationSpeed(factor);
    }
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.Spawn))]
internal static class LegendaryGrenadeFirearmDrawPatch
{
    private static void Prefix(Player.FirearmController __instance, ref float animationSpeed) => animationSpeed *= GrenadeBuffs.Draw(BackpackRuntime.Actor(__instance), true);
}
