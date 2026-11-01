using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Player.KnifeController.Kick), nameof(Player.KnifeController.Kick.Start), typeof(Player.EKickType))]
internal static class LegendaryMeleeAttackStartPatch
{
    private static void Prefix(Player.KnifeController.Kick __instance, Player.EKickType __0) => MeleeRuntime.BeginAttack(__instance.Controller, __0 == Player.EKickType.Stab);
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.HackShot))]
internal static class LegendaryMeleeDamagePatch
{
    private static void Prefix(GameWorld __instance, ref DamageInfo damageInfo)
    {
        if (damageInfo.DamageType != EDamageType.Melee || damageInfo.Weapon is null) return;
        var held = MeleeRuntime.Held(MeleeRuntime.OwnerOfDamage(damageInfo));
        if (held is null || held.Item.Id != damageInfo.Weapon.Id) return;
        var body = damageInfo.HittedBallisticCollider as BodyPartCollider;
        var target = body?.PlayerProfileID is { } id ? __instance.GetAlivePlayerByProfileID(id) : null;
        var part = body?.BodyPartType ?? EBodyPart.Common;
        AdvancedMeleeRuntime.Connect(held, target);
        damageInfo.Damage *= MeleeRuntime.DamageMultiplier(held, held.Controller.LastKickType == Player.EKickType.Stab, target, part);
        damageInfo.PenetrationPower *= held.Roll.PenetrationMultiplier;
        if (MeleeRuntime.Enabled(held.Player, held.Effect)) damageInfo.PenetrationPower += held.Effect.PenetrationBonus + AdvancedMeleeRuntime.Penetration(held, held.Controller.LastKickType == Player.EKickType.Stab, part);
    }
}

[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDamage))]
internal static class LegendaryMeleeArmorBlockPatch
{
    private static void Prefix(ArmorComponent __instance, ref DamageInfo damageInfo)
    {
        if (damageInfo.DamageType != EDamageType.Melee || damageInfo.Weapon is null ||
            damageInfo.BlockedBy.HasValue || damageInfo.DeflectedBy.HasValue || __instance.Repairable.Durability <= 0f) return;
        var held = MeleeRuntime.Held(MeleeRuntime.OwnerOfDamage(damageInfo));
        if (held?.Item.Id != damageInfo.Weapon.Id || held.Effect.BlockedHitDamage <= 0f ||
            !MeleeRuntime.Enabled(held.Player, held.Effect)) return;
        // Native knife hits omit the penetration-status pass used by bullets. For this one ability,
        // use that same armor-resistance/chance formula before the normal armor damage calculation.
        var resistance = ShotSharedMethods.RealResistance(__instance.Repairable.Durability,
            __instance.Repairable.TemplateDurability, __instance.ArmorClass, damageInfo.PenetrationPower);
        if (resistance.GetPenetrationChance(damageInfo.PenetrationPower) - UnityEngine.Random.value * 100f < 0f)
            damageInfo.BlockedBy = __instance.Item.Id;
    }
}

[HarmonyPatch]
internal static class LegendaryMeleeHealthPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController),
        nameof(ActiveHealthController.ApplyDamage), new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) });

    [HarmonyPriority(Priority.Last)]
    private static void Prefix(ActiveHealthController __instance, ref float damage, ref DamageInfo damageInfo, out bool __state)
    {
        __state = __instance.IsAlive;
        if (!__state) return;
        var attacker = MeleeRuntime.OwnerOfDamage(damageInfo);
        var heldAttacker = MeleeRuntime.Held(attacker);
        if (damageInfo.DamageType == EDamageType.Melee && damageInfo.Weapon != null && heldAttacker?.Item.Id == damageInfo.Weapon.Id &&
            MeleeRuntime.Enabled(heldAttacker.Player, heldAttacker.Effect) && heldAttacker.Effect.BlockedHitDamage > 0f &&
            (damageInfo.BlockedBy.HasValue || damageInfo.DeflectedBy.HasValue))
        {
            // Native armor has already resolved this hit. Supply the promised blunt damage once, without a recursive second attack.
            damage = heldAttacker.Effect.BlockedHitDamage;
            damageInfo.BleedBlock = true;
        }
        var defenderEffect = MeleeRuntime.Effect(__instance.Player);
        if (damageInfo.DamageType == EDamageType.Fall && defenderEffect?.FallImmunity == true) damage = 0f;
        else damage *= AdvancedMeleeRuntime.Incoming(__instance.Player, attacker, damageInfo.DamageType);
        damageInfo.Damage = damage;
    }

    private static void Postfix(ActiveHealthController __instance, EBodyPart bodyPart, DamageInfo damageInfo, float __result, bool __state) =>
        OnDamage(__instance, bodyPart, damageInfo, __result, __state);

    private static void OnDamage(ActiveHealthController health, EBodyPart part, DamageInfo info, float actual, bool wasAlive)
    {
        if (wasAlive) AdvancedMeleeRuntime.IncomingHit(health.Player, info, actual);
        MeleeRuntime.OnDamage(health.Player, part, info, actual, wasAlive);
    }
}

[HarmonyPatch(typeof(Player.BaseKnifeController), nameof(Player.BaseKnifeController.SetMeleeSpeed))]
internal static class LegendaryMeleeSpeedPatch
{
    private static void Prefix(Player.BaseKnifeController __instance, ref float speed)
    {
        if (__instance.CurrentKnifeOperation is not Player.KnifeController.Kick) return;
        speed *= EarpieceRuntime.MeleeSpeed(MeleeRuntime.Owner(__instance));
        var held = MeleeRuntime.Held(MeleeRuntime.Owner(__instance));
        if (held?.Controller != __instance) return;
        speed *= held.Roll.MeleeAttackSpeedMultiplier * (MeleeRuntime.Enabled(held.Player, held.Effect) ? AdvancedMeleeRuntime.Speed(held) : 1f);
    }
}

[HarmonyPatch(typeof(KnifeCollider), nameof(KnifeCollider.OnFire))]
internal static class LegendaryMeleeReachPatch
{
    private static void Prefix(KnifeCollider __instance, Player.BaseKnifeController ___Knife)
    {
        if (___Knife is null) return;
        var player = MeleeRuntime.Owner(___Knife);
        if (BackpackRuntime.Get(player) is { Key: "BP35-3" } bag && bag.Record.Active("meleeReach")) __instance.MaxDistance *= 1.4f;
        var held = MeleeRuntime.Held(player);
        if (held?.Controller != ___Knife) return;
        __instance.MaxDistance *= held.Roll.MeleeReachMultiplier * (MeleeRuntime.Enabled(held.Player, held.Effect) ? AdvancedMeleeRuntime.Reach(held) : 1f);
    }
}

internal static class MeleeAnimation
{
    internal static readonly Dictionary<Player.BaseKnifeController, float> Baselines = new();
    public static void Reset() => Baselines.Clear();
}

[HarmonyPatch(typeof(Player.KnifeController), nameof(Player.KnifeController.Spawn))]
internal static class LegendaryMeleeDrawPatch
{
    private static void Prefix(Player.KnifeController __instance, ref float animationSpeed)
    {
        MeleeRuntime.Forget(MeleeRuntime.Owner(__instance));
        if (RarityResolver.Resolve(__instance.Item)?.Definition.MeleeEffect is null) return;
        MeleeAnimation.Baselines[__instance] = animationSpeed;
        animationSpeed *= MeleeRuntime.SwapSpeed(__instance);
    }
}

[HarmonyPatch(typeof(Player.KnifeController.SpawnOperation), nameof(Player.KnifeController.SpawnOperation.WeaponAppeared))]
internal static class LegendaryMeleeDrawCompletePatch
{
    private static void Prefix(Player.KnifeController.SpawnOperation __instance)
    {
        if (MeleeAnimation.Baselines.TryGetValue(__instance.Controller, out var baseline))
            __instance.Controller.FirearmsAnimator.SetAnimationSpeed(baseline);
    }
}

[HarmonyPatch(typeof(Player.KnifeController.Remove), nameof(Player.KnifeController.Remove.Start))]
internal static class LegendaryMeleeHolsterPatch
{
    private static void Postfix(Player.KnifeController.Remove __instance)
    {
        var controller = __instance.Controller;
        if (!MeleeAnimation.Baselines.TryGetValue(controller, out var baseline)) return;
        controller.FirearmsAnimator.SetAnimationSpeed(baseline * MeleeRuntime.SwapSpeed(controller));
    }
}

[HarmonyPatch(typeof(Player.BaseKnifeController), nameof(Player.BaseKnifeController.Drop))]
internal static class LegendaryMeleeDropPatch
{
    private static void Prefix(Player.BaseKnifeController __instance, Item nextControllerItem)
    {
        var player = MeleeRuntime.Owner(__instance);
        if (player is not null)
            MeleeWeaponSwitch.Queue(player, nextControllerItem, MeleeRuntime.Effect(player)?.WeaponSwapSpeedMultiplier ?? 1f);
        MeleeRuntime.Forget(player);
    }
}

[HarmonyPatch(typeof(Player.BaseKnifeController), nameof(Player.BaseKnifeController.Destroy))]
internal static class LegendaryMeleeDestroyPatch
{
    private static void Prefix(Player.BaseKnifeController __instance)
    { MeleeAnimation.Baselines.Remove(__instance); MeleeRuntime.Forget(MeleeRuntime.Owner(__instance)); }
}

[HarmonyPatch(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion))]
internal static class LegendaryMeleeMovementPatch
{
    [ThreadStatic] private static int _depth;
    internal sealed class Snapshot
    {
        public int Depth;
        public float Limit;
        public bool Changed;
    }
    private static void Prefix(MovementContext __instance, ref Vector3 motion, out Snapshot __state)
    {
        __state = new Snapshot { Depth = _depth };
        _depth++;
        var player = MeleeRuntime.Owner(__instance);
        if (__state.Depth != 0 || player is null || player.InputDirection.sqrMagnitude <= 0.0001f && !__instance.IsSprintEnabled) return;
        var factor = AdvancedMeleeRuntime.Movement(player) * BackpackRuntime.MotionFactor(player) * ChestRigRuntime.Movement(player) * GrenadeBuffs.Movement(player) * EarpieceRuntime.Movement(player);
        if (factor == 1f) return;
        // One scope composes melee and backpack movement. Scale the resolved
        // motion, including sprint's +1 term, instead of patching an inlined getter.
        motion.x *= factor; motion.z *= factor;
        if (__instance.CharacterController != null)
        {
            __state.Limit = __instance.CharacterController.SpeedLimit;
            __state.Changed = true;
            if (__state.Limit > 0f) __instance.CharacterController.SpeedLimit *= factor;
        }
    }
    private static Exception? Finalizer(MovementContext __instance, Snapshot __state, Exception? __exception)
    {
        if (__state.Changed) __instance.CharacterController.SpeedLimit = __state.Limit;
        _depth = __state.Depth;
        return __exception;
    }
}

[HarmonyPatch(typeof(Stamina), nameof(Stamina.Process))]
internal static class LegendaryMeleeSprintCostPatch
{
    internal sealed class Snapshot
    {
        public Physical.Consumption Consumption = null!;
        public Compute<float> Original = null!;
    }
    private static void Prefix(Stamina __instance, out Snapshot? __state)
    {
        __state = null;
        var factor = MeleeRuntime.Effect(MeleeRuntime.Owner(__instance))?.SprintStaminaMultiplier ?? 1f;
        if (factor == 1f) return;
        var sprint = __instance.Consumptions.FirstOrDefault(value => value.ConsumptionType == Physical.EConsumptionType.Sprint);
        if (sprint is null) return;
        var original = sprint.Delta;
        __state = new Snapshot { Consumption = sprint, Original = original };
        // Change only sprint consumption in this Leg Stamina update. Hands, oxygen, restoration, and other consumption types retain native values.
        sprint.Delta = new Compute<float>(() => original.Value * factor);
    }
    private static Exception? Finalizer(Snapshot? __state, Exception? __exception)
    { if (__state != null) __state.Consumption.Delta = __state.Original; return __exception; }
}

[HarmonyPatch(typeof(JumpPlayerState), nameof(JumpPlayerState.Enter))]
internal static class LegendaryMeleeJumpHeightPatch
{
    private static void Postfix(JumpPlayerState __instance)
    {
        var effect = MeleeRuntime.Effect(MeleeRuntime.Owner(__instance.MovementContext));
        if (effect != null) __instance._skilledBoost *= MeleeRules.JumpVelocityMultiplier(effect.JumpHeightMultiplier);
        AdvancedMeleeRuntime.JumpEntered(__instance);
    }
}

[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.HandleFall))]
internal static class LegendaryMeleeFallPatch
{
    private static bool Prefix(ActiveHealthController __instance, ref float __result)
    {
        if (MeleeRuntime.Effect(__instance.Player)?.FallImmunity != true) return true;
        __result = 0f; return false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.CalculateMovementVolumeDefaultMultiplier))]
internal static class LegendaryMeleeMovementAudioPatch
{
    private static void Postfix(Player __instance, ref float __result) =>
        __result *= MeleeRuntime.Effect(__instance)?.NoiseMultiplier ?? 1f;
}

[HarmonyPatch(typeof(GlobalEventDispatcher), nameof(GlobalEventDispatcher.PlaySound))]
internal static class LegendaryMeleeAiMovementNoisePatch
{
    private static void Prefix(IPlayer person, ref float power, AISoundType type)
    {
        if (type == AISoundType.step) power *= MeleeRuntime.Effect(MeleeRuntime.Actor(person))?.NoiseMultiplier ?? 1f;
    }
}

[HarmonyPatch]
internal static class LegendaryMeleeAttributePatch
{
    private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(KnifeComponent), new[] { typeof(Item), typeof(IKnifeComponentTemplate) });
    private static void Postfix(KnifeComponent __instance)
    {
        var roll = RarityResolver.Resolve(__instance.Item);
        if (roll?.Definition.MeleeEffect is null) return;
        foreach (var attribute in __instance.Item.Attributes)
        {
            if (attribute.Id.Equals(EItemAttributeId.KnifeHitSlashDam))
            {
                attribute.Base = () => MeleeRuntime.DisplayDamage(__instance, false);
                attribute.StringValue = () => MeleeRuntime.DisplayDamage(__instance, false).ToString("0.#", CultureInfo.InvariantCulture);
            }
            else if (attribute.Id.Equals(EItemAttributeId.KnifeHitStabDam))
            {
                attribute.Base = () => MeleeRuntime.DisplayDamage(__instance, true);
                attribute.StringValue = () => MeleeRuntime.DisplayDamage(__instance, true).ToString("0.#", CultureInfo.InvariantCulture);
            }
            else if (attribute.Id.Equals(EItemAttributeId.KnifeHitRadius))
            {
                attribute.Base = () => MeleeRuntime.DisplayReach(__instance);
                attribute.StringValue = () => attribute.Base().ToString("0.##", CultureInfo.InvariantCulture) + " " + LocalizationExtensions.Localized("meters");
            }
        }
    }
}

[HarmonyPatch(typeof(HasItemRequirement), nameof(HasItemRequirement.Met))]
internal static class LegendaryRedRebelExtractionPatch
{
    private const string RedRebel = "5c0126f40db834002a125382";
    private static void Postfix(HasItemRequirement __instance, Player player, ref bool __result)
    {
        if (__result || __instance.Id != RedRebel || player?.InventoryController?.Inventory is null) return;
        __result = player.InventoryController.Inventory.GetPlayerItems(EPlayerItems.Equipment).Any(item =>
            RarityResolver.TryGetDefinition(item, out var definition) && definition.SourceTemplateId == RedRebel);
    }
}
