using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class EarpieceRuntime
{
    internal sealed class State
    {
        internal Player Player = null!;
        internal Item Item = null!;
        internal RarityResolver.ShotRoll Roll = null!;
        internal EarpieceEffects Effect = null!;
        internal Vector3 Previous;
        internal float LastTick, LastAim = float.NaN, LastReload = float.NaN, LastRecoil = float.NaN, LastAccuracy = float.NaN, LastRate = float.NaN;
        internal string HeldId = "", PreviousKind = "";
        internal float StatusNoticeUntil;
        internal string StatusNotice = "";
        internal string Key => Effect.Key;
    }
    internal sealed class Shot
    { internal Shot? Previous; internal Weapon Weapon = null!; internal float Damage; }
    internal static readonly Dictionary<Player, State> States = new();
    private static readonly Dictionary<string, Dictionary<string, float>> Cooldowns = new(StringComparer.Ordinal);
    private static readonly HashSet<Player> Changed = new();
    private static readonly HashSet<Player> Deaths = new();
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);
    [ThreadStatic] internal static Shot? CurrentShot;
    private static bool Resetting;
    private static float NextTick;
    internal static State? Get(Player? player)
    {
        if (Resetting || player?.HealthController?.IsAlive != true) return null;
        var item = player.InventoryController?.Inventory?.Equipment.GetSlot(EquipmentSlot.Earpiece)?.ContainedItem;
        if (States.TryGetValue(player, out var old) && item?.Id == old.Item.Id) return old;
        if (old is not null) { States.Remove(player); Changed.Add(player); }
        if (item is null || RarityResolver.Resolve(item) is not { Definition.IsEarpiece: true } roll) return null;
        if (!Cooldowns.TryGetValue(player.ProfileId, out var cooldowns)) Cooldowns[player.ProfileId] = cooldowns = new(StringComparer.Ordinal);
        var s = new State { Player = player, Item = item, Roll = roll,
            Effect = new EarpieceEffects(roll.Definition.RuntimeKey, () => Time.time, cooldowns), Previous = player.Position, LastTick = Time.time,
            HeldId = player.HandsController?.Item?.Id ?? "", PreviousKind = Kind(player.HandsController?.Item) };
        States[player] = s; Names[s.Key] = roll.Definition.ShortName; Changed.Add(player);
        return s;
    }
    internal static bool Is(Player? p, string key) => Get(p)?.Key == key;
    internal static State? Listener => Get(Singleton<GameWorld>.Instance?.MainPlayer);
    internal static bool Moving(Player p) => p.InputDirection.sqrMagnitude > .0001f;
    private static string Kind(Item? item) => item is Weapon ? "firearm" : item is KnifeItemClass ? "melee" : "other";
    internal static void Suspend() => Resetting = true;
    internal static void Reset()
    {
        Resetting = true;
        EarpieceAudio.Reset();
        States.Clear(); Changed.Clear(); Deaths.Clear(); Cooldowns.Clear(); Names.Clear(); CurrentShot = null; NextTick = 0f;
        EarpieceFoodUse.Reset(); EarpieceSwitching.Reset(); EarpieceAimWalkingPatch.Reset(); EarpieceMedicalAnimation.Reset();
        Resetting = false;
    }
    internal static void Maintain(GameWorld? world)
    {
        if (world is null || Time.time < NextTick) return;
        NextTick = Time.time + .05f;
        foreach (var p in world.RegisteredPlayers.OfType<Player>().ToArray())
        {
            var s = Get(p);
            var changed = Changed.Remove(p);
            if (s is not null)
            {
                var motion = p.Position - s.Previous; s.Previous = p.Position;
                var moving = Moving(p); var sprint = moving && p.MovementContext.IsSprintEnabled;
                var elapsed = Math.Max(.001f, Time.time - s.LastTick); s.LastTick = Time.time;
                var distance = new Vector2(motion.x, motion.z).magnitude;
                var walked = moving && !sprint && p.MovementContext.IsGrounded && distance <= Math.Min(3f, elapsed * 12f) ? distance : 0f;
                var restore = s.Effect.Tick(moving, sprint, p.MovementContext.IsInPronePose, p.HandsController?.IsAiming == true, walked, p.Physical.HandsStamina.Current);
                if (restore.Legs > 0f) { BackpackRuntime.RestoreLeg(p, restore.Legs); s.StatusNoticeUntil = Time.time + 2f; s.StatusNotice = "Triggered · Leg Stamina recovery"; }
                if (restore.Arms > 0f) { ChestRigRuntime.RestoreArms(p, restore.Arms); s.StatusNoticeUntil = Time.time + 2f; s.StatusNotice = "Triggered · Arm Stamina recovery"; }
                var held = p.HandsController?.Item; var id = held?.Id ?? "";
                if (id != s.HeldId)
                { s.HeldId = id; s.PreviousKind = Kind(held); }
                if (held is Weapon weapon)
                {
                    var aim = Aim(p); var reload = Reload(p); var recoil = Recoil(p); var accuracy = Accuracy(p); var rate = FireRate(p);
                    changed |= aim != s.LastAim || reload != s.LastReload || recoil != s.LastRecoil || accuracy != s.LastAccuracy || rate != s.LastRate;
                    s.LastAim = aim; s.LastReload = reload; s.LastRecoil = recoil; s.LastAccuracy = accuracy; s.LastRate = rate;
                }
            }
            if (changed && p.HealthController?.IsAlive == true && p.HandsController is Player.FirearmController f)
            { EarpieceAimWalkingPatch.Refresh(p); f.SetAnimatorAndProceduralValues(); p.ProceduralWeaponAnimation.UpdateWeaponVariables(); WeaponCombatState.Refresh(f); }
        }
        EarpieceAudio.Maintain(); EarpieceMedicalAnimation.Maintain();
    }
    internal static Shot? Begin(Player p, Weapon weapon)
    {
        if (Get(p) is not { } s) return null;
        var shot = new Shot { Previous = CurrentShot, Weapon = weapon, Damage = s.Effect.Fire() }; CurrentShot = shot;
        return shot;
    }
    internal static void End(Shot? shot) { if (shot is not null && ReferenceEquals(CurrentShot, shot)) CurrentShot = shot.Previous; }
    internal static void Killed(Player victim)
    {
        if (!Deaths.Add(victim)) return;
        if (Native.Get(victim, "LastAggressor") is not Player killer || !AdvancedMeleeRuntime.Enemy(killer, victim) || Get(killer) is not { } s) return;
        if (Native.Get(victim, "LastDamageInfo") is not DamageInfoStruct info) return;
        var firearm = info.DamageType == EDamageType.Bullet && info.Weapon is Weapon;
        if (s.Effect.Killed(firearm, Vector3.Distance(killer.Position, victim.Position))) AccelerateGrenade(killer);
    }
    private static void AccelerateGrenade(Player p)
    {
        var equipment = p.InventoryController.Inventory.Equipment;
        var items = new[] { equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem, equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem }
            .Where(i => i is not null).SelectMany(i => i!.GetAllItems()).Where(ReusableGrenadeRuntime.IsReusable).GroupBy(i => i.Id).Select(g => g.First());
        var candidates = items.Select(i => (Item: i, Definition: RarityResolver.Resolve(i)!.Definition))
            .Select(x => (x.Item, x.Definition, Seconds: ReusableGrenadeRuntime.Charges.SecondsUntil(x.Item.Id, x.Definition.GrenadeChargesPerRaid, LegendaryGrenadeSettings.Interval(x.Definition))))
            .Where(x => x.Seconds > 0).OrderBy(x => x.Seconds).ThenBy(x => x.Item.Id, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return;
        var chosen = candidates[0];
        ReusableGrenadeRuntime.Charges.ReduceRecharge(chosen.Item.Id, chosen.Definition.GrenadeChargesPerRaid, 15, LegendaryGrenadeSettings.Interval(chosen.Definition));
    }
    internal static void Damaged(Player target, DamageInfoStruct info)
    {
        if (info.DamageType != EDamageType.Bullet || info.Weapon is not Weapon || info.Player?.iPlayer is not Player attacker || !AdvancedMeleeRuntime.Enemy(target, attacker) || Get(target) is not { } s) return;
        s.Effect.Damaged(ChestRigRuntime.Health(target));
    }
    internal static float Recoil(Player? p)
    {
        var s = Get(p); if (s is null) return 1f;
        return EarpieceEffectRules.Recoil(s.Key, s.Effect.Active, Math.Abs(p!.InputDirection.x) > .01f, AdvancedMeleeRuntime.Crouched(p), Moving(p));
    }
    private static bool HighFidelity(Player? p) => Get(p) is { } s && EarpieceEffectRules.HighFidelity(s.Key, p!.HandsController?.IsAiming == true, s.Effect.AdsSeconds);
    internal static float Accuracy(Player? p) => HighFidelity(p) ? .75f : 1f;
    internal static float Velocity(Player? p) => HighFidelity(p) ? 1.2f : 1f;
    internal static float FireRate(Player? p) => Get(p)?.Effect.FireRate ?? 1f;
    internal static float Aim(Player? p) => Get(p) is { } s ? s.Roll.Earpiece.Aim * s.Effect.Aim : 1f;
    internal static float Reload(Player? p) => Get(p)?.Effect.Reload ?? 1f;
    internal static float Draw(Player? p) => Get(p)?.Roll.Earpiece.Draw ?? 1f;
    internal static float Sway(Player? p)
    {
        var s = Get(p); if (s is null) return 1f;
        return EarpieceEffectRules.Sway(s.Key, p!.MovementContext.IsInPronePose, s.Effect.ProneSeconds, Night());
    }
    internal static bool Night()
    {
        var clock = Singleton<GameWorld>.Instance?.GameDateTime;
        if (clock is null || Native.Call(clock, "Calculate") is not DateTime time) return false;
        return time.Hour >= 20 || time.Hour < 6;
    }
    internal static float Value(Player p, string kind)
    {
        var s = Get(p); if (s is null) return 1f;
        return kind switch
        {
            "aimCost" => s.Roll.Earpiece.AimCost * (s.Key == "EP08-3" && Night() ? .65f : 1f),
            "legRecovery" => s.Roll.Earpiece.LegRecovery * (s.Key == "EP07-5" && s.Effect.Active ? 1.5f : 1f),
            "armRecovery" when s.Key == "EP02-5" && p.MovementContext.IsInPronePose && s.Effect.ProneSeconds >= 3f => 1.5f,
            _ => 1f
        };
    }
    internal static float Movement(Player p)
    {
        var s = Get(p); if (s is null) return 1f;
        return EarpieceEffectRules.Movement(s.Key, s.Effect.Active, p.MovementContext.IsSprintEnabled, p.HandsController is Player.BaseKnifeController);
    }
    internal static float MeleeSpeed(Player? p) => Is(p, "EP05-4") ? 1.25f : 1f;
    internal static float MedicalSpeed(Player p, Item? item = null)
    {
        item ??= (p.HandsController as Player.MedsController)?.Item;
        return item is not null && Native.Component<MedKitComponent>(item) is not null && Is(p, "EP04-4") && !Moving(p) && Get(p)!.Effect.StillSeconds >= 3f ? 1.5f : 1f;
    }
    internal static bool UsingMedicine(Player p) => p.HandsController is Player.MedsController meds && Native.Component<MedKitComponent>(meds.Item) is not null;
    internal static float Incoming(Player p, DamageInfoStruct info) => info.DamageType == EDamageType.Bullet && info.Weapon is Weapon && Is(p, "EP09-5") && UsingMedicine(p) ? .7f : 1f;
    internal static float EmittedStep(Player? p) => Is(p, "EP08-2") && AdvancedMeleeRuntime.Crouched(p!) && Moving(p!) && !p!.MovementContext.IsSprintEnabled ? .6f : 1f;
    internal static float Contusion(Player? p) => Get(p) is { } s ? EarpieceEffectRules.Contusion(s.Key, s.Roll.Earpiece.Contusion) : 1f;
    internal static IEnumerable<EffectStatusModel.Status> Statuses(Player? p)
    {
        if (p?.HealthController?.IsAlive != true) yield break;
        var s = Get(p);
        if (Cooldowns.TryGetValue(p.ProfileId, out var records))
            foreach (var pair in records.Where(x => x.Value > Time.time && x.Key != s?.Key))
                yield return new EffectStatusModel.Status("earpiece:" + pair.Key, Names.GetValueOrDefault(pair.Key) ?? pair.Key, false, ready: pair.Value - Time.time);
        if (s is null) yield break;
        // Only unconditional equipped modifiers are permanent. Stance, ADS,
        // location and resource gates use the same conditions as gameplay.
        var shots = s.Effect.ReadyShots;
        var notice = s.StatusNoticeUntil > Time.time;
        var active = s.Effect.Active || shots > 0 || ConditionalStatus(s) || notice;
        if (active || s.Effect.Cooldown > 0)
            yield return new EffectStatusModel.Status("earpiece:" + s.Key, s.Roll.Definition.ShortName, active,
                Math.Max(0f, s.Effect.ActiveUntil - Time.time), s.Effect.Cooldown, shots > 0 ? shots + " shots ready" : notice ? s.StatusNotice : "");
    }
    private static bool ConditionalStatus(State s)
    {
        var p = s.Player;
        return s.Key switch
        {
            "EP01-1" => !Moving(p) && s.Effect.StillSeconds >= 1f,
            "EP02-1" => Native.Get(Singleton<GameWorld>.Instance, "LocationId")?.ToString()?.ToLowerInvariant() is "woods" or "shoreline" or "rezervbase",
            "EP02-5" => p.MovementContext.IsInPronePose && s.Effect.ProneSeconds >= 3f,
            "EP04-2" => Math.Abs(p.InputDirection.x) > .01f,
            "EP04-3" => p.HandsController?.IsAiming == true,
            "EP04-4" => !Moving(p) && s.Effect.StillSeconds >= 3f,
            "EP05-4" => p.HandsController is Player.BaseKnifeController,
            "EP08-1" => AdvancedMeleeRuntime.Crouched(p) && !Moving(p),
            "EP08-2" => AdvancedMeleeRuntime.Crouched(p) && Moving(p) && !p.MovementContext.IsSprintEnabled,
            "EP08-3" => Night(),
            "EP09-5" => UsingMedicine(p),
            "EP10-1" => HighFidelity(p),
            "EP11-2" => p.MovementContext.IsInPronePose,
            "EP11-3" => p.Physical.HandsStamina.Current >= p.Physical.HandsStamina.TotalCapacity * .8f,
            _ => false
        };
    }
}
