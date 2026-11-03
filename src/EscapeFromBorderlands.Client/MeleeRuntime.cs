using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class MeleeRuntime
{
    private static readonly FieldInfo HandsPlayer = AccessTools.Field(typeof(Player.ItemHandsController), "_player");
    private static readonly FieldInfo MovementPlayer = AccessTools.Field(typeof(MovementContext), "_player");
    private static readonly Dictionary<Player, HeldState> States = new();
    private static readonly Dictionary<Stamina, Player> LegStaminaOwners = new();
    private static readonly EBodyPart[] Parts =
    {
        EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm,
        EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg,
    };

    public static Player? Owner(Player.ItemHandsController controller) => HandsPlayer.GetValue(controller) as Player;
    public static Player? Owner(MovementContext context) => MovementPlayer.GetValue(context) as Player;
    public static Player? Owner(Stamina stamina) => LegStaminaOwners.TryGetValue(stamina, out var player) ? player : null;

    public static void Maintain(GameWorld? world)
    {
        if (world is null) return;
        foreach (var player in world.AllAlivePlayersList)
        {
            if (player?.Physical?.Stamina is Stamina stamina) LegStaminaOwners[stamina] = player;
            Held(player);
        }
        foreach (var player in States.Keys.Where(player => player == null || player.HealthController?.IsAlive != true).ToArray())
            Forget(player);
    }

    public static void Reset()
    {
        States.Clear(); LegStaminaOwners.Clear();
        SessionCleanup.Run(nameof(MeleeWeaponSwitch), MeleeWeaponSwitch.Reset);
        SessionCleanup.Run(nameof(AdvancedMeleeRuntime), AdvancedMeleeRuntime.Reset);
    }
    public static void Forget(Player? player) { if (player is not null && States.TryGetValue(player, out var held)) { States.Remove(player); if (PlayerRefresh.CanObserve(player)) AdvancedMeleeRuntime.EndHold(held); } }

    public static HeldState? Held(Player? player)
    {
        if (!PlayerRefresh.CanObserve(player)) { Forget(player); return null; }
        if (player.HealthController?.IsAlive != true || player.HandsController is not Player.BaseKnifeController controller ||
            controller.Destroyed || RarityResolver.Resolve(controller.Item) is not { } roll || roll.Definition.MeleeEffect is null)
        {
            Forget(player);
            return null;
        }
        if (States.TryGetValue(player, out var held) && held.Controller == controller && held.Item.Id == controller.Item.Id)
            return held;
        if (held != null) AdvancedMeleeRuntime.EndHold(held);
        held = new HeldState(player, controller, roll);
        States[player] = held;
        if (player.Physical?.Stamina is Stamina stamina) LegStaminaOwners[stamina] = player;
        return held;
    }

    public static bool HasAk74(Player? player)
    {
        var equipment = player?.InventoryController?.Inventory?.Equipment;
        if (equipment is null) return false;
        foreach (var slot in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon })
        {
            var item = equipment.GetSlot(slot)?.ContainedItem;
            var id = RarityResolver.TryGetDefinition(item, out var definition) ? definition.SourceTemplateId : item?.StringTemplateId;
            if (id != null && MeleeRules.Ak74Family.Contains(id)) return true;
        }
        return false;
    }

    public static float HealthFraction(Player player)
    {
        var current = 0f; var maximum = 0f;
        if (player.HealthController is null) return 1f;
        foreach (var part in Parts)
        {
            var health = player.HealthController.GetBodyPartHealth(part, false);
            current += health.Current; maximum += health.Maximum;
        }
        return maximum > 0f ? current / maximum : 1f;
    }

    public static bool Enabled(Player player, MeleeEffectDefinition effect) =>
        MeleeRules.Enabled(effect, effect.MaximumHealthFraction < 1f ? HealthFraction(player) : 1f,
            !effect.RequiresAk74 || HasAk74(player)) && AdvancedMeleeRuntime.Enabled(player, effect);

    public static MeleeEffectDefinition? Effect(Player? player)
    {
        var held = Held(player);
        return held != null && Enabled(held.Player, held.Effect) ? held.Effect : null;
    }

    public static float SwapSpeed(Player.BaseKnifeController controller)
    {
        var roll = RarityResolver.Resolve(controller.Item);
        if (roll?.Definition.MeleeEffect is not { } effect || Owner(controller) is not { } player) return 1f;
        return roll.MeleeSwapMultiplier * (Enabled(player, effect) ? effect.SwapSpeedMultiplier * effect.WeaponSwapSpeedMultiplier : 1f);
    }

    public static void BeginAttack(Player.KnifeController controller, bool stab = false)
    {
        var held = Held(Owner(controller));
        if (held?.Controller != controller) return;
        AdvancedMeleeRuntime.BeginAttack(held, stab);
        held.AttackNumber++;
        held.AttackBuildup = held.Buildup;
        held.FleshVictims.Clear(); held.KillVictims.Clear();
    }

    public static float DamageMultiplier(HeldState held, bool stab, Player? target, EBodyPart part)
    {
        var random = stab ? held.Roll.MeleeStabMultiplier : held.Roll.MeleeSlashMultiplier;
        if (!Enabled(held.Player, held.Effect)) return random;
        var behind = false; var bleeding = false;
        if (target != null)
        {
            var direction = target.Position - held.Player.Position;
            direction.y = 0f;
            var forward = target.MovementContext.TransformRotation * Vector3.forward;
            forward.y = 0f;
            behind = direction.sqrMagnitude > 0.0001f && Vector3.Dot(forward.normalized, direction.normalized) > 0.5f;
            bleeding = target.ActiveHealthController?.GetAllEffects().OfType<ActiveHealthController.Bleeding>()
                .Any(effect => effect.Existing) == true;
        }
        return random * AdvancedMeleeRuntime.Damage(held, stab, target, part) * MeleeRules.Damage(held.Effect, stab, held.AttackNumber, held.AttackBuildup,
            behind, bleeding, held.Player.InputDirection.sqrMagnitude > 0.0001f,
            part is EBodyPart.LeftLeg or EBodyPart.RightLeg);
    }

    public static float DisplayDamage(KnifeComponent knife, bool stab)
    {
        var original = stab ? knife.Template.KnifeHitStabDam : knife.Template.KnifeHitSlashDam;
        var roll = RarityResolver.Resolve(knife.Item);
        if (roll?.Definition.MeleeEffect is not { } effect) return original;
        var held = States.Values.ToArray().FirstOrDefault(state => state.Item.Id == knife.Item.Id && Held(state.Player) == state);
        if (held != null) return original * DamageMultiplier(held, stab, null, EBodyPart.Chest);
        // Show unconditional weapon damage when inspecting an unheld item; target- and holder-dependent bonuses stay in its description.
        var unconditional = !effect.RequiresAk74 && !effect.RequiresSniper && !effect.RequiresEmptyPrimaries && !effect.RequiresNight && !effect.RequiresBleeding && !effect.RequiresProne && effect.MaximumHealthFraction >= 1f;
        return original * (stab ? roll.MeleeStabMultiplier : roll.MeleeSlashMultiplier) *
            (unconditional ? effect.DamageMultiplier * (stab ? effect.StabDamageMultiplier : effect.SlashDamageMultiplier) : 1f);
    }

    public static float DisplayReach(KnifeComponent knife)
    {
        var roll = RarityResolver.Resolve(knife.Item);
        var owner = BackpackNative.ItemOwner(knife.Item);
        var backpack = owner?.HandsController?.Item?.Id == knife.Item.Id && BackpackRuntime.Get(owner) is { Key: "BP35-3" } bag && bag.Record.Active("meleeReach") ? 1.4f : 1f;
        if (roll?.Definition.MeleeEffect is not { } effect) return knife.Template.KnifeHitRadius * backpack;
        var held = States.Values.FirstOrDefault(s => s.Item.Id == knife.Item.Id);
        var unconditional = !effect.RequiresAk74 && !effect.RequiresSniper && !effect.RequiresEmptyPrimaries && !effect.RequiresNight && !effect.RequiresBleeding && !effect.RequiresProne && effect.MaximumHealthFraction >= 1f;
        var multiplier = held != null ? (Enabled(held.Player, effect) ? effect.ReachMultiplier : 1f) : (unconditional ? effect.ReachMultiplier : 1f);
        return knife.Template.KnifeHitRadius * roll.MeleeReachMultiplier * multiplier * backpack;
    }

    public static void Heal(Player player, float amount)
    {
        if (amount <= 0f || player.ActiveHealthController is not { IsAlive: true } health) return;
        var missing = new List<(EBodyPart Part, float Missing)>();
        foreach (var part in Parts)
        {
            var hp = health.GetBodyPartHealth(part, false);
            if (hp.Current > 0f && !health.IsBodyPartDestroyed(part) && hp.Maximum > hp.Current)
                missing.Add((part, hp.Maximum - hp.Current));
        }
        var total = missing.Sum(value => value.Missing);
        if (total <= 0f) return;
        amount = Math.Min(amount, total);
        foreach (var value in missing)
            health.ChangeHealth(value.Part, Math.Min(value.Missing, amount * value.Missing / total), DamageHelper.MedKitUse);
    }

    public static void OnDamage(Player target, EBodyPart part, DamageInfo info, float actualDamage, bool wasAlive)
    {
        if (actualDamage <= 0f || !wasAlive || info.DamageType != EDamageType.Melee ||
            info.Weapon is null || OwnerOfDamage(info) is not { } attacker) return;
        var held = Held(attacker);
        if (held is null || held.Item.Id != info.Weapon.Id || !Enabled(attacker, held.Effect)) return;
        var effect = held.Effect;
        var flesh = !info.BlockedBy.HasValue && !info.DeflectedBy.HasValue;
        if (flesh && held.FleshVictims.Add(target.ProfileId))
        {
            if (ConditionalStatusHit(held, target, part))
                EffectStatusModel.Notify(attacker.ProfileId, "melee:" + held.Item.Id, held.Roll.Definition.ShortName, Time.time, "melee hit effect");
            AdvancedMeleeRuntime.FleshHit(held, target, part, info);
            held.Buildup = MeleeRules.NextBuildup(effect, held.Buildup);
            Heal(attacker, effect.HealOnFleshHit);
            if (target.ActiveHealthController is { IsAlive: true } health)
            {
                if (effect.HeavyBleedOnStab && held.Controller.LastKickType == Player.EKickType.Stab) health.DoBleed(true, part);
                if (effect.FractureOnLegHit && part is EBodyPart.LeftLeg or EBodyPart.RightLeg) health.DoFracture(part);
            }
        }
        if (target.HealthController?.IsAlive == false && held.KillVictims.Add(target.ProfileId))
        {
            if (effect.KillPainkillerSeconds <= 0f && (effect.HealOnKill > 0f || effect.KillLegStaminaFraction > 0f || effect.KillArmStaminaFraction > 0f || effect.KillClearsTremor || effect.KillStopsBleeding))
                EffectStatusModel.Notify(attacker.ProfileId, "melee:" + held.Item.Id, held.Roll.Definition.ShortName, Time.time, "melee kill effect");
            AdvancedMeleeRuntime.Kill(held);
            if (effect.KillLegStaminaFraction > 0f)
            {
                var stamina = attacker.Physical.Stamina;
                var restored = MeleeRules.RestoreStamina(stamina.Current, stamina.TotalCapacity, effect.KillLegStaminaFraction);
                if (Math.Abs(restored - stamina.Current) >= 1f) stamina.UpdateStamina(restored);
                else { stamina.Current = restored; stamina.InvokeChangedAction(); }
            }
            Heal(attacker, effect.HealOnKill);
        }
    }

    private static bool ConditionalStatusHit(HeldState held, Player target, EBodyPart part)
    {
        var e = held.Effect; var s = held.Advanced;
        var bleeding = s.BleedingBeforeHit.TryGetValue(target.ProfileId, out var before) && before;
        if (e.HealOnFleshHit > 0f || bleeding && (e.BleedingHitLegStaminaFraction > 0f || e.BleedingTargetDamageMultiplier != 1f)) return true;
        if (s.Stab && (e.HeavyBleedOnStab && target.HealthController.IsAlive || e.FirstPartStabDamageMultiplier != 1f && !s.FirstParts.Contains(target.ProfileId + ":" + part))) return true;
        var distance = target.Position - held.Player.Position; distance.y = 0f;
        if (s.Stab && e.LongStabDistance > 0f && distance.magnitude >= e.LongStabDistance) return true;
        if (!s.Stab && (part == EBodyPart.Head && e.HeadSlashDamageMultiplier != 1f || part == EBodyPart.Chest && e.ChestSlashStomachDamage > 0f && target.HealthController.IsAlive)) return true;
        if (e.FractureOnLegHit && part is EBodyPart.LeftLeg or EBodyPart.RightLeg && target.HealthController.IsAlive) return true;
        if (e.TargetLowHealthFraction > 0f && HealthFraction(target) <= e.TargetLowHealthFraction) return true;
        if (e.RearDamageMultiplier == 1f || distance.sqrMagnitude <= .0001f) return false;
        var forward = target.MovementContext.TransformRotation * Vector3.forward; forward.y = 0f;
        return Vector3.Dot(forward.normalized, distance.normalized) > .5f;
    }

    public static Player? Actor(IPlayer? player) => player as Player ??
        (player != null && Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId) : null);
    public static Player? OwnerOfDamage(DamageInfo info) => Actor(info.Player?.iPlayer);

    internal sealed class HeldState
    {
        public HeldState(Player player, Player.BaseKnifeController controller, RarityResolver.ShotRoll roll)
        { Player = player; Controller = controller; Roll = roll; Advanced.LastPosition = player.Position; }
        public Player Player { get; }
        public Player.BaseKnifeController Controller { get; }
        public Item Item => Controller.Item;
        public RarityResolver.ShotRoll Roll { get; }
        public MeleeEffectDefinition Effect => Roll.Definition.MeleeEffect!;
        public AdvancedMeleeState Advanced { get; } = new();
        public int AttackNumber;
        public float Buildup;
        public float AttackBuildup;
        public HashSet<string> FleshVictims { get; } = new(StringComparer.Ordinal);
        public HashSet<string> KillVictims { get; } = new(StringComparer.Ordinal);
    }
}
