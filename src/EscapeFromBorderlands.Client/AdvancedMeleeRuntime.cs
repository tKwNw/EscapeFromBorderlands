using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal sealed class AdvancedMeleeState
{
    public float LastUpdate = Time.time;
    public bool StationaryRecoveryActive;
    public float StationarySince = Time.time;
    public Vector3 LastPosition;
    public float SprintSince = -1f;
    public bool Charged;
    public bool FirstAttack = true;
    public bool Stab;
    public bool AttackCharged;
    public float SlashSpeed;
    public float AttackSlashSpeed;
    public bool PreviousAttackConnected = true;
    public string ChainTarget = string.Empty;
    public float ChainPenetration;
    public float AttackChainPenetration;
    public readonly HashSet<string> Connected = new();
    public readonly HashSet<string> FirstParts = new();
    public readonly HashSet<string> MarkedEnemies = new();
    public readonly Dictionary<string, bool> BleedingBeforeHit = new();
    public ActiveHealthController.GClass3008? HeldPainkiller;
}

internal static partial class AdvancedMeleeRuntime
{
    private static readonly Dictionary<Player, ActiveHealthController.GClass3008> KillPainkillers = new();
    private static readonly Dictionary<Player, VariantDefinition> KillPainkillerSources = new();
    private static readonly HashSet<Player> WeightReduced = new();
    private static readonly Dictionary<Player, Flight> Flights = new();
    public static bool Resetting { get; private set; }
    private static readonly MethodInfo PainkillerFactory = typeof(ActiveHealthController).GetMethods()
        .Single(m => m.Name == "AddEffect" && m.IsGenericMethodDefinition)
        .MakeGenericMethod(typeof(ActiveHealthController).GetNestedType("PainKiller", BindingFlags.NonPublic)!);
    private static readonly EBodyPart[] Limbs = { EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };

    internal sealed class Flight
    {
        public JumpStateClass State = null!;
        public float LaunchVelocity;
        public bool Available;
        public bool WasAirborne;
    }

    public static void Reset()
    {
        Resetting = true;
        try
        {
            // Session teardown only discards EFB state. Live unequips use
            // EndHold/Maintain instead of refreshing disposed physical objects.
            KillPainkillers.Clear(); KillPainkillerSources.Clear(); WeightReduced.Clear(); Flights.Clear();
        }
        finally { Resetting = false; }
    }

    public static IEnumerable<Weapon> Primaries(Player player)
    {
        var equipment = player.InventoryController?.Inventory?.Equipment;
        if (equipment is null) yield break;
        foreach (var slot in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon })
            if (equipment.GetSlot(slot)?.ContainedItem is Weapon weapon) yield return weapon;
    }

    public static bool Bleeding(Player? player) => player?.ActiveHealthController?.GetAllEffects()
        .OfType<ActiveHealthController.Bleeding>().Any(e => e.Existing) == true;

    public static bool Crouched(Player player) => !player.MovementContext.IsInPronePose && player.PoseLevel < .5f;

    public static bool Enabled(Player player, MeleeEffectDefinition effect)
    {
        if (effect.RequiresSniper && !Primaries(player).Any(w => w.WeapClass == "sniperRifle")) return false;
        if (effect.RequiresEmptyPrimaries)
        {
            var weapons = Primaries(player).ToArray();
            if (weapons.Length == 0 || weapons.Any(w => w.ChamberAmmoCount + w.GetCurrentMagazineCount_1() > 0)) return false;
        }
        if (effect.RequiresBleeding && !Bleeding(player)) return false;
        if (effect.RequiresProne && !player.MovementContext.IsInPronePose) return false;
        if (effect.RequiresNight)
        {
            var clock = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.GameDateTime : null;
            if (clock is null) return false;
            var hour = clock.Calculate().Hour;
            if (hour >= 6 && hour < 20) return false;
        }
        return true;
    }

    public static void Maintain(GameWorld? world)
    {
        if (world is null) return;
        foreach (var player in world.AllAlivePlayersList)
        {
            if (!PlayerRefresh.CanObserve(player)) continue;
            var held = MeleeRuntime.Held(player);
            var reduced = held?.Effect.WeightMultiplier < 1f;
            if (reduced != WeightReduced.Contains(player))
            {
                if (reduced) WeightReduced.Add(player); else WeightReduced.Remove(player);
                PlayerRefresh.Weight(player, "melee weight");
            }
            if (Flights.TryGetValue(player, out var flight))
            {
                if (!player.MovementContext.IsGrounded) flight.WasAirborne = true;
                else if (flight.WasAirborne) Flights.Remove(player);
                if (held?.Effect.ExtraJumpHeightMultiplier <= 0f || held is null) flight.Available = false;
            }
            if (held is null) continue;
            var s = held.Advanced; var effect = held.Effect; var now = Time.time;
            var dt = Math.Max(0f, Math.Min(.25f, now - s.LastUpdate));
            var moved = player.Position - s.LastPosition; moved.y = 0f;
            var still = player.InputDirection.sqrMagnitude < .0001f && moved.sqrMagnitude < .000001f;
            if (!still) s.StationarySince = now;
            var recoveryTime = still ? Math.Max(0f, Math.Min(dt, now - s.StationarySince - effect.StationarySeconds)) : 0f;
            s.StationaryRecoveryActive = effect.StationarySeconds > 0f && recoveryTime > 0f;
            if (effect.StationarySeconds > 0f && recoveryTime > 0f)
            {
                if (effect.StationaryLegStaminaPerSecond > 0f)
                    Restore(player.Physical.Stamina, effect.StationaryLegStaminaPerSecond * recoveryTime);
                MeleeRuntime.Heal(player, effect.StationaryHealPerSecond * recoveryTime);
            }
            if (effect.Ability == "sprint_charge")
            {
                var sprinting = player.MovementContext.IsSprintEnabled && player.InputDirection.sqrMagnitude > .0001f && moved.sqrMagnitude > .000001f;
                if (!sprinting) s.SprintSince = -1f;
                else
                {
                    if (s.SprintSince < 0f) s.SprintSince = now;
                    if (now - s.SprintSince >= effect.ChargeSeconds) s.Charged = true;
                }
            }
            if (effect.HoldPainkiller)
            {
                if (s.HeldPainkiller?.Existing != true) s.HeldPainkiller = Painkiller(player, 2f);
                else s.HeldPainkiller.AddWorkTime(2f, true);
            }
            s.LastPosition = player.Position; s.LastUpdate = now;
        }
        foreach (var player in WeightReduced.Where(p => p.HealthController?.IsAlive != true).ToArray()) WeightReduced.Remove(player);
        foreach (var player in Flights.Keys.Where(p => p.HealthController?.IsAlive != true).ToArray()) Flights.Remove(player);
        foreach (var player in KillPainkillers.Keys.Where(p => p.HealthController?.IsAlive != true || !KillPainkillers[p].Existing).ToArray())
        { KillPainkillers.Remove(player); KillPainkillerSources.Remove(player); }
    }

    public static void EndHold(MeleeRuntime.HeldState held)
    {
        held.Advanced.HeldPainkiller?.ForceRemove();
        if (Flights.TryGetValue(held.Player, out var flight)) flight.Available = false;
    }

    public static void BeginAttack(MeleeRuntime.HeldState held, bool stab)
    {
        var s = held.Advanced; var e = held.Effect;
        if (!s.PreviousAttackConnected) { s.ChainTarget = string.Empty; s.ChainPenetration = 0f; }
        s.PreviousAttackConnected = false; s.Connected.Clear(); s.BleedingBeforeHit.Clear();
        s.Stab = stab;
        s.AttackCharged = e.Ability == "draw_charge" ? s.FirstAttack :
            e.Ability == "counter_charge" ? s.Charged : stab && s.Charged;
        s.FirstAttack = false;
        if (s.AttackCharged) { s.Charged = false; s.SprintSince = Time.time; }
        if (stab) s.SlashSpeed = 0f;
        s.AttackSlashSpeed = s.SlashSpeed;
        if (!stab) s.SlashSpeed = Math.Min(e.SlashSpeedMaximum, s.SlashSpeed + e.SlashSpeedPerAttack);
        s.AttackChainPenetration = s.ChainPenetration;
    }

    public static void Connect(MeleeRuntime.HeldState held, Player? target)
    {
        if (target is null) return;
        var s = held.Advanced; var e = held.Effect;
        if (!s.Connected.Add(target.ProfileId)) return;
        s.BleedingBeforeHit[target.ProfileId] = Bleeding(target);
        s.PreviousAttackConnected = true;
        if (e.ChainPenetrationPerHit > 0f)
        {
            if (s.ChainTarget != target.ProfileId) { s.ChainPenetration = 0f; s.AttackChainPenetration = 0f; }
            s.ChainTarget = target.ProfileId;
            s.ChainPenetration = Math.Min(e.ChainPenetrationMaximum, s.ChainPenetration + e.ChainPenetrationPerHit);
        }
    }

    public static float Damage(MeleeRuntime.HeldState held, bool stab, Player? target, EBodyPart part)
    {
        var e = held.Effect; var s = held.Advanced; var factor = s.AttackCharged ? e.ChargedDamageMultiplier : 1f;
        if (!stab && part == EBodyPart.Head) factor *= e.HeadSlashDamageMultiplier;
        if (target != null)
        {
            if (e.TargetLowHealthFraction > 0f && MeleeRuntime.HealthFraction(target) <= e.TargetLowHealthFraction) factor *= e.TargetLowHealthDamageMultiplier;
            if (stab && e.FirstPartStabDamageMultiplier != 1f && !s.FirstParts.Contains(target.ProfileId + ":" + part)) factor *= e.FirstPartStabDamageMultiplier;
            var distance = target.Position - held.Player.Position; distance.y = 0f;
            if (stab && e.LongStabDistance > 0f && distance.magnitude >= e.LongStabDistance) factor *= e.LongStabDamageMultiplier;
        }
        return factor;
    }

    public static float Penetration(MeleeRuntime.HeldState held, bool stab, EBodyPart part) =>
        (held.Advanced.AttackCharged ? held.Effect.ChargedPenetrationBonus : 0f) +
        (!stab && part == EBodyPart.Head ? held.Effect.HeadSlashPenetrationBonus : 0f) + held.Advanced.AttackChainPenetration;

    public static float Reach(MeleeRuntime.HeldState held) => held.Effect.ReachMultiplier *
        (held.Advanced.AttackCharged && held.Advanced.Stab ? held.Effect.ChargedReachMultiplier : 1f);

    public static float Speed(MeleeRuntime.HeldState held)
    {
        var e = held.Effect; var bonus = held.Advanced.Stab ? 0f : held.Advanced.AttackSlashSpeed;
        bonus += NearbySpeedBonus(held);
        return e.AttackSpeedMultiplier * (1f + bonus);
    }
    internal static float NearbySpeedBonus(MeleeRuntime.HeldState held)
    {
        var e = held.Effect;
        if (e.NearbyEnemyRadius <= 0f || !Singleton<GameWorld>.Instantiated) return 0f;
        var count = Singleton<GameWorld>.Instance.AllAlivePlayersList.Count(p => p != held.Player &&
            p.HealthController?.IsAlive == true && Vector3.Distance(p.Position, held.Player.Position) <= e.NearbyEnemyRadius && Enemy(held.Player, p));
        return Math.Min(e.NearbyEnemySpeedMaximum, count * e.NearbyEnemySpeedBonus);
    }

    public static bool Enemy(Player owner, Player other)
    {
        if (owner == other || !string.IsNullOrEmpty(owner.GroupId) && owner.GroupId == other.GroupId) return false;
        if (owner.AIData?.BotOwner?.BotsGroup is { } ownerGroup) return ownerGroup.IsEnemy((IPlayer)(object)other);
        if (other.AIData?.BotOwner?.BotsGroup is { } otherGroup) return otherGroup.IsEnemy((IPlayer)(object)owner);
        return owner.Profile.Info.Side != other.Profile.Info.Side;
    }

    public static float Movement(Player player)
    {
        var held = MeleeRuntime.Held(player);
        if (held is null || !MeleeRuntime.Enabled(player, held.Effect)) return 1f;
        return held.Effect.MovementMultiplier * (Crouched(player) ? held.Effect.CrouchMovementMultiplier : 1f);
    }

    public static float Incoming(Player player, Player? attacker, EDamageType type)
    {
        var held = MeleeRuntime.Held(player);
        if (held is null || !MeleeRuntime.Enabled(player, held.Effect)) return 1f;
        var factor = attacker != null && attacker != player ? held.Effect.IncomingDamageMultiplier : 1f;
        if (attacker != null && held.Advanced.MarkedEnemies.Contains(attacker.ProfileId)) factor *= held.Effect.MarkedAttackerDamageMultiplier;
        if (Crouched(player) && (type is EDamageType.Explosion or EDamageType.GrenadeFragment or EDamageType.ThermobaricExplosion)) factor *= held.Effect.CrouchExplosionMultiplier;
        return factor;
    }

    public static void IncomingHit(Player target, DamageInfoStruct info, float actualDamage)
    {
        var attacker = MeleeRuntime.OwnerOfDamage(info);
        var held = MeleeRuntime.Held(target);
        if (actualDamage <= 0f || held?.Effect.Ability != "counter_charge" || attacker is null || attacker == target ||
            info.DamageType is not (EDamageType.Bullet or EDamageType.Melee or EDamageType.Explosion or EDamageType.GrenadeFragment or EDamageType.ThermobaricExplosion)) return;
        held.Advanced.Charged = true;
    }

    public static void FleshHit(MeleeRuntime.HeldState held, Player target, EBodyPart part, DamageInfoStruct original)
    {
        var e = held.Effect; var s = held.Advanced;
        if (s.Stab) s.FirstParts.Add(target.ProfileId + ":" + part);
        if (e.Ability == "combo" && !s.Stab) s.Charged = true;
        if (s.BleedingBeforeHit.TryGetValue(target.ProfileId, out var bleeding) && bleeding) Restore(held.Player.Physical.Stamina, e.BleedingHitLegStaminaFraction);
        if (e.MarkedAttackerDamageMultiplier < 1f) s.MarkedEnemies.Add(target.ProfileId);
        if (!s.Stab && part == EBodyPart.Chest && e.ChestSlashStomachDamage > 0f && target.HealthController.IsAlive)
            {
                var extra = original; extra.Weapon = null; extra.Damage = e.ChestSlashStomachDamage; extra.BleedBlock = true;
                target.ActiveHealthController.ApplyDamage(EBodyPart.Stomach, e.ChestSlashStomachDamage, extra);
            }
    }

    public static void Kill(MeleeRuntime.HeldState held)
    {
        var player = held.Player; var e = held.Effect;
        if (e.KillArmStaminaFraction > 0f) Restore(player.Physical.HandsStamina, e.KillArmStaminaFraction);
        if (e.KillClearsTremor || e.KillStopsBleeding)
            foreach (var effect in player.ActiveHealthController.GetAllEffects().OfType<ActiveHealthController.GClass3008>().ToArray())
                if (e.KillClearsTremor && effect.GetType().Name == "Tremor" || e.KillStopsBleeding && effect is ActiveHealthController.Bleeding) effect.ForceRemove();
        if (e.KillPainkillerSeconds > 0f)
        {
            KillPainkillerSources[player] = held.Roll.Definition;
            if (KillPainkillers.TryGetValue(player, out var painkiller) && painkiller.Existing) painkiller.AddWorkTime(e.KillPainkillerSeconds, true);
            else KillPainkillers[player] = Painkiller(player, e.KillPainkillerSeconds);
        }
    }

    private static ActiveHealthController.GClass3008 Painkiller(Player player, float seconds) =>
        (ActiveHealthController.GClass3008)PainkillerFactory.Invoke(player.ActiveHealthController, new object?[] { EBodyPart.Head, 0f, seconds, 0f, 1f, null })!;

    public static void Restore(GClass774 stamina, float fraction)
    {
        if (fraction <= 0f) return;
        stamina.Current = MeleeRules.RestoreStamina(stamina.Current, stamina.TotalCapacity, fraction); stamina.InvokeChangedAction();
    }

    public static void JumpEntered(JumpStateClass state)
    {
        var player = MeleeRuntime.Owner(state.MovementContext);
        if (player is null) return;
        Flights[player] = new Flight { State = state, LaunchVelocity = state.Vector3_1.y * state.Float_5,
            Available = MeleeRuntime.Effect(player)?.ExtraJumpHeightMultiplier > 0f };
    }

    public static bool ExtraJump(Player player)
    {
        if (!Flights.TryGetValue(player, out var flight) || !flight.Available || player.MovementContext.IsGrounded ||
            player.MovementContext.CurrentState != flight.State || flight.State.EjumpState_0 != JumpStateClass.EJumpState.Jump) return false;
        var effect = MeleeRuntime.Effect(player);
        if (player.Physical is not PlayerPhysicalClass physical) return false;
        if (effect is null || effect.ExtraJumpHeightMultiplier <= 0f || !player.Physical.CanJump || !player.MovementContext.CanJump) return false;
        flight.Available = false;
        physical.Stamina.Consume(physical.Consumptions[PlayerPhysicalClass.EConsumptionType.Jump]);
        var state = flight.State;
        state.Float_2 = state.Float_3;
        state.Vector3_1 = Vector3.up * (flight.LaunchVelocity * MeleeRules.JumpVelocityMultiplier(effect.ExtraJumpHeightMultiplier) / Math.Max(.01f, state.Float_5));
        state.Float_9 = player.MovementContext.TransformPosition.y;
        state.Float_10 = 0f; state.Bool_1 = false; state.Int_0 = 0;
        return true;
    }
    internal static bool ExtraJumpReady(Player player) => Flights.TryGetValue(player, out var flight) && flight.Available &&
        !player.MovementContext.IsGrounded && player.MovementContext.CurrentState == flight.State &&
        flight.State.EjumpState_0 == JumpStateClass.EJumpState.Jump && MeleeRuntime.Effect(player)?.ExtraJumpHeightMultiplier > 0f &&
        player.Physical is PlayerPhysicalClass && player.Physical.CanJump && player.MovementContext.CanJump;
}
