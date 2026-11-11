using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
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
    private static readonly HashSet<Player> Stale = new();
    private static readonly List<string> StaleCooldowns = new();
    private static readonly EquipmentSlot[] RechargeSlots = { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest };
    private static readonly HashSet<string> Deaths = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);
    [ThreadStatic] internal static Shot? CurrentShot;
    private static bool Resetting;
    private static float NextTick;
    internal static State? Get(Player? player, bool observed = false)
    {
        if (Resetting || player is null || !observed && !PlayerRefresh.CanObserve(player))
        {
            if (player is not null) Forget(player);
            return null;
        }
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
    // Only this group can require a firearm stat or animation refresh. The
    // cartridge, audio and movement hooks still see every worn earpiece.
    internal static bool ChangesFirearm(Player? p, bool observed = false) => Get(p, observed) is { } s &&
        (s.Roll.Earpiece.Aim != 1f || s.Roll.Earpiece.Draw != 1f ||
         s.Key is "EP01-4" or "EP02-5" or "EP03-3" or "EP04-1" or "EP04-2" or "EP04-3" or
             "EP06-3" or "EP06-5" or "EP08-1" or "EP08-3" or "EP09-3" or "EP10-1" or "EP10-2" or "EP11-3");
    internal static State? Listener => Get(Singleton<GameWorld>.Instance?.MainPlayer);
    internal static bool Moving(Player p) => p.InputDirection.sqrMagnitude > .0001f;
    private static string Kind(Item? item) => item is Weapon ? "firearm" : item is Knife ? "melee" : "other";
    internal static void Suspend() => Resetting = true;
    private static void Forget(Player player)
    {
        States.Remove(player);
        Changed.Remove(player);
        Cooldowns.Remove(player.ProfileId);
    }
    internal static void Reset()
    {
        Resetting = true;
        try
        {
            SessionCleanup.Run(nameof(EarpieceAudio), EarpieceAudio.Reset);
            SessionCleanup.Run(nameof(EarpieceFoodUse), EarpieceFoodUse.Reset);
            SessionCleanup.Run(nameof(EarpieceSwitching), EarpieceSwitching.Reset);
            SessionCleanup.Run(nameof(EarpieceAimWalkingPatch), EarpieceAimWalkingPatch.Reset);
            SessionCleanup.Run(nameof(EarpieceMedicalAnimation), EarpieceMedicalAnimation.Reset);
        }
        finally
        {
            States.Clear(); Changed.Clear(); Deaths.Clear(); Cooldowns.Clear(); Names.Clear();
            CurrentShot = null; NextTick = 0f; Resetting = false;
        }
    }
    internal static void Maintain(GameWorld? world, RaidPlayerSnapshot snapshot)
    {
        if (PlayerRefresh.ActiveWorld(world) is null || Time.time < NextTick) return;
        NextTick = Time.time + .05f;
        snapshot.Capture(world!);
        try
        {
            foreach (var p in States.Keys) if (!snapshot.Contains(p) || !PlayerRefresh.CanObserve(p)) Stale.Add(p);
            foreach (var p in Changed) if (!snapshot.Contains(p) || !PlayerRefresh.CanObserve(p)) Stale.Add(p);
            foreach (var p in Stale) Forget(p);
            foreach (var id in Cooldowns.Keys) if (!snapshot.ContainsProfile(id)) StaleCooldowns.Add(id);
            foreach (var id in StaleCooldowns) Cooldowns.Remove(id);
        }
        finally { Stale.Clear(); StaleCooldowns.Clear(); }
        foreach (var p in snapshot)
        {
            var s = Get(p);
            var changed = Changed.Remove(p);
            if (s is not null)
            {
                // Only these signatures consume the sampled movement/timer
                // inputs. Other signatures respond to discrete events.
                var restore = TickSignature(s, p);
                if (restore.Legs > 0f) { BackpackRuntime.RestoreLeg(p, restore.Legs); s.StatusNoticeUntil = Time.time + 2f; s.StatusNotice = "Triggered · Leg Stamina recovery"; }
                if (restore.Arms > 0f) { ChestRigRuntime.RestoreArms(p, restore.Arms); s.StatusNoticeUntil = Time.time + 2f; s.StatusNotice = "Triggered · Arm Stamina recovery"; }
                var held = p.HandsController?.Item; var id = held?.Id ?? "";
                if (id != s.HeldId)
                { s.HeldId = id; s.PreviousKind = Kind(held); changed = true; }
                if (held is Weapon)
                {
                    var aim = s.Roll.Earpiece.Aim * s.Effect.Aim;
                    var reload = s.Effect.Reload;
                    var recoil = Recoil(s);
                    var accuracy = HighFidelity(s) ? .75f : 1f;
                    var rate = s.Effect.FireRate;
                    changed |= aim != s.LastAim || reload != s.LastReload || recoil != s.LastRecoil || accuracy != s.LastAccuracy || rate != s.LastRate;
                    s.LastAim = aim; s.LastReload = reload; s.LastRecoil = recoil; s.LastAccuracy = accuracy; s.LastRate = rate;
                }
            }
            if (changed) PlayerRefresh.Firearm(p, "headset conditions");
        }
        EarpieceAudio.Maintain(snapshot); EarpieceMedicalAnimation.Maintain();
    }
    private static (float Legs, float Arms) TickSignature(State s, Player p)
    {
        switch (s.Key)
        {
            case "EP01-1":
            case "EP04-4": return s.Effect.Tick(Moving(p), false, false, false, 0f, 0f);
            case "EP02-5": return s.Effect.Tick(false, false, p.MovementContext.IsInPronePose, false, 0f, 0f);
            case "EP10-1": return s.Effect.Tick(false, false, false, p.HandsController?.IsAiming == true, 0f, 0f);
            case "EP03-2": return s.Effect.Tick(false, false, false, false, 0f, p.Physical.HandsStamina.Current);
            case "EP04-1":
                return s.Effect.Tick(false, Moving(p) && p.MovementContext.IsSprintEnabled, false, false, 0f, 0f);
            case "EP02-3":
                var position = p.Position;
                var motion = position - s.Previous; s.Previous = position;
                var moving = Moving(p); var sprint = moving && p.MovementContext.IsSprintEnabled;
                var elapsed = Math.Max(.001f, Time.time - s.LastTick); s.LastTick = Time.time;
                var distance = new Vector2(motion.x, motion.z).magnitude;
                var walked = moving && !sprint && p.MovementContext.IsGrounded && distance <= Math.Min(3f, elapsed * 12f) ? distance : 0f;
                return s.Effect.Tick(false, sprint, false, false, walked, 0f);
            default: return default;
        }
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
        if (!Deaths.Add(victim.ProfileId)) return;
        if (Native.Get(victim, "LastAggressor") is not Player killer ||
            Get(killer) is not { Key: "EP05-3" or "EP10-2" or "EP06-4" } s ||
            !AdvancedMeleeRuntime.Enemy(killer, victim)) return;
        if (Native.Get(victim, "LastDamageInfo") is not DamageInfo info) return;
        var firearm = info.DamageType == EDamageType.Bullet && info.Weapon is Weapon;
        if (s.Effect.Killed(firearm, s.Key == "EP05-3" ? Vector3.Distance(killer.Position, victim.Position) : 0f)) AccelerateGrenade(killer);
    }
    private static void AccelerateGrenade(Player p)
    {
        var equipment = p.InventoryController.Inventory.Equipment;
        Item? chosen = null;
        VariantDefinition? chosenDefinition = null;
        var best = double.PositiveInfinity;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in RechargeSlots)
        {
            if (equipment.GetSlot(slot).ContainedItem is not { } root) continue;
            foreach (var item in root.GetAllItems())
            {
                if (!ReusableGrenadeRuntime.IsReusable(item) || !seen.Add(item.Id)) continue;
                var definition = RarityResolver.Resolve(item)!.Definition;
                var seconds = ReusableGrenadeRuntime.Charges.SecondsUntil(item.Id, definition.GrenadeChargesPerRaid,
                    LegendaryGrenadeSettings.Interval(definition));
                if (!(seconds > 0d) || chosen is not null &&
                    (seconds > best || seconds == best && string.CompareOrdinal(item.Id, chosen.Id) >= 0)) continue;
                best = seconds; chosen = item; chosenDefinition = definition;
            }
        }
        if (chosen is not null && chosenDefinition is { } d)
            ReusableGrenadeRuntime.Charges.ReduceRecharge(chosen.Id, d.GrenadeChargesPerRaid, 15, LegendaryGrenadeSettings.Interval(d));
    }
    internal static void Damaged(Player target, DamageInfo info)
    {
        if (info.DamageType != EDamageType.Bullet || info.Weapon is not Weapon || info.Player?.iPlayer is not Player attacker ||
            Get(target) is not { Key: "EP03-3" or "EP04-5" } s || !AdvancedMeleeRuntime.Enemy(target, attacker)) return;
        s.Effect.Damaged(s.Key == "EP04-5" ? ChestRigRuntime.Health(target) : 1f);
    }
    internal static float StatRecoil(Weapon weapon) => WeaponStatReadBatch.ResolveStatEarpiece(weapon) is { } s ? Recoil(s) : 1f;
    private static float Recoil(State s) => s.Key switch
    {
        "EP04-2" => EarpieceEffectRules.Recoil(s.Key, false, Math.Abs(s.Player.InputDirection.x) > .01f, false, false),
        "EP08-1" => EarpieceEffectRules.Recoil(s.Key, false, false, AdvancedMeleeRuntime.Crouched(s.Player), Moving(s.Player)),
        "EP06-5" => EarpieceEffectRules.Recoil(s.Key, s.Effect.Active, false, false, false),
        _ => 1f
    };
    private static bool HighFidelity(State s) => s.Key == "EP10-1" &&
        EarpieceEffectRules.HighFidelity(s.Key, s.Player.HandsController?.IsAiming == true, s.Effect.AdsSeconds);
    private static bool HighFidelity(Player? p) => Get(p) is { } s && HighFidelity(s);
    internal static float StatAccuracy(Weapon weapon) =>
        WeaponStatReadBatch.ResolveStatEarpiece(weapon) is { } s && HighFidelity(s) ? .75f : 1f;
    internal static float Velocity(Player? p) => HighFidelity(p) ? 1.2f : 1f;
    internal static float FireRate(Player? p) => Get(p)?.Effect.FireRate ?? 1f;
    internal static float Aim(Player? p) => Get(p) is { } s ? s.Roll.Earpiece.Aim * s.Effect.Aim : 1f;
    internal static float Reload(Player? p) => Get(p)?.Effect.Reload ?? 1f;
    internal static float Draw(Player? p) => Get(p)?.Roll.Earpiece.Draw ?? 1f;
    internal static float Sway(Player? p)
    {
        return Get(p) is { } s ? s.Key switch
        {
            "EP02-5" => EarpieceEffectRules.Sway(s.Key, s.Player.MovementContext.IsInPronePose, s.Effect.ProneSeconds, false),
            "EP08-3" => EarpieceEffectRules.Sway(s.Key, false, 0f, Night()),
            _ => 1f
        } : 1f;
    }
    internal static bool Night()
    {
        var clock = Singleton<GameWorld>.Instance?.GameDateTime;
        if (clock is null || Native.Call(clock, "Calculate") is not DateTime time) return false;
        return time.Hour >= 20 || time.Hour < 6;
    }
    internal static float Value(Player p, string kind, bool observed = false)
    {
        var s = Get(p, observed); if (s is null) return 1f;
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
    internal static float Incoming(Player p, DamageInfo info) => info.DamageType == EDamageType.Bullet && info.Weapon is Weapon && Is(p, "EP09-5") && UsingMedicine(p) ? .7f : 1f;
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
