using EFT.Ballistics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Equipment identity is resolved on the existing equipment event/roster route.
// Only humans with movement-sensitive effects enter the bounded sampler.
internal static partial class Eyewear
{
    private sealed class Entry
    {
        internal bool Observed;
        internal State? Current;
        internal Item? ObservedItem;
        internal string? ObservedTemplate;
        internal ProceduralWeaponAnimation? Animation;
        internal Dictionary<string, State>? Records;
        internal Dictionary<string, float>? Cooldowns;
        internal Dictionary<string, HashSet<string>>? Seen;
    }
    internal sealed partial class State
    {
        internal Player Player = null!;
        internal Item Item = null!;
        internal RarityResolver.ShotRoll Roll = null!;
        internal string Key => Roll.Definition.RuntimeKey;
        internal bool Human => ActorCapabilities.IsHuman(Player);
        internal float Until, Second, Cooldown, Window, LastShot, ChargeAt, EquippedAt, Distance, AimSince;
        internal float RemainingRecharge, RechargePeriod, LastSample, NextError;
        internal int Charges, Counter, Lean, LastLean, RewardMeters;
        internal bool Still, Sprint, Aiming, Moving, PositionKnown;
        internal Vector3 Position;
        internal string? AttackerId;
        internal Weapon? Weapon;
        internal string StatusText = "", StatusKey = "", SmokeKey = "", AmmoId = "", AmmoName = "";
        internal int AmmoCount = -1, AmmoCapacity = -1, AmmoChamber = -1;
        internal HashSet<string> Seen = new(StringComparer.Ordinal);
        internal bool Ready => Time.time >= Cooldown;
        internal bool Active => Time.time < Until;
        internal bool InWindow => Time.time < Window;
        internal void Cool(float seconds) => Cooldown = Math.Max(Cooldown, Time.time + seconds);
        internal void Buff(float seconds, float cooldown = 0)
        { Until = Time.time + seconds; if (cooldown > 0) Cool(cooldown); }
    }
    private static ConditionalWeakTable<Player, Entry> Players = new();
    private static readonly Dictionary<Player, State> Worn = new(), Sampled = new();
    private static readonly Dictionary<ProceduralWeaponAnimation, Player> Animations = new();
    private static readonly List<State> Work = new();
    private static float NextSample;
    internal static int Generation { get; private set; }

    internal static bool BotCanUse(string key) => EyewearRules.BotCanUse(key);

    internal static State? Get(Player? player)
    {
        if (ReferenceEquals(player, null) || PlayerRefresh.Resetting) return null;
        if (Players.TryGetValue(player, out var entry) && entry.Observed) return entry.Current;
        return PlayerRefresh.CanObserve(player) ? Observe(player) : null;
    }
    internal static State? Get(ProceduralWeaponAnimation animation) => Animations.TryGetValue(animation, out var p) ? Get(p) : null;
    internal static State? Observe(Player p)
    {
        if (!PlayerRefresh.CanObserve(p)) return null;
        var entry = Players.GetOrCreateValue(p);
        var item = p.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Eyewear)?.ContainedItem;
        var template = item?.StringTemplateId;
        if (entry.Observed && ReferenceEquals(entry.ObservedItem, item) && entry.ObservedTemplate == template)
        {
            if (entry.Current is not null) BindAnimation(p, entry);
            return entry.Current;
        }
        entry.Observed = true; entry.ObservedItem = item; entry.ObservedTemplate = template;
        var previous = entry.Current;
        if (previous is not null) Detach(previous, entry);
        entry.Current = null;
        if (item is null || !RarityResolver.TryGetDefinition(item, out var definition) || !definition.IsEyewear || definition.IsMysteryFleaOffer)
            return null;
        // A bot carrying unsupported eyewear has no runtime, roll resolution,
        // sampler, or HUD state for its effects.
        if (!ActorCapabilities.IsHuman(p) && !BotCanUse(definition.RuntimeKey)) return null;
        var records = entry.Records ??= new(StringComparer.Ordinal);
        var known = entry.Seen ??= new(StringComparer.Ordinal);
        if (!records.TryGetValue(item.Id, out var s))
        {
            s = new State { Player = p, Item = item, LastShot = Time.time, StatusKey = "eyewear:" + item.Id, SmokeKey = "eyewear-smoke:" + item.Id };
            if (!known.TryGetValue(definition.RuntimeKey, out var seen)) known[definition.RuntimeKey] = seen = new(StringComparer.Ordinal);
            s.Seen = seen;
            s.RechargePeriod = definition.RuntimeKey == "EW001" ? 600 : definition.RuntimeKey == "EW030" ? 120 : 0;
            s.Charges = s.RechargePeriod > 0 ? 1 : 0;
            records.Add(item.Id, s);
        }
        s.Roll = RarityResolver.Resolve(item)!;
        s.Cooldown = Math.Max(s.Cooldown, entry.Cooldowns?.GetValueOrDefault(s.Key) ?? 0);
        s.EquippedAt = Time.time;
        if (s.RechargePeriod > 0 && s.Charges == 0) s.ChargeAt = Time.time + Math.Max(0, s.RemainingRecharge);
        entry.Current = Worn[p] = s;
        CombatEquipped(s);
        BindAnimation(p, entry);
        if (s.Human && NeedsSampling(s))
        {
            Sampled[p] = s;
            Sample(s, Time.time, true);
        }
        PlayerRefresh.Firearm(p, "eyewear equipment");
        return s;
    }
    private static bool NeedsSampling(State s) => s.Key is "EW005" or "EW009" or "EW020" or "EW021" or "EW034" ||
        s.Roll.Eyewear.Hip > 0 || s.Roll.Eyewear.MovingSway > 0;
    private static void BindAnimation(Player player, Entry entry)
    {
        var animation = player.ProceduralWeaponAnimation;
        if (ReferenceEquals(entry.Animation, animation)) return;
        if (entry.Animation is not null) Animations.Remove(entry.Animation);
        entry.Animation = animation;
        if (animation is not null) Animations[animation] = player;
    }
    private static void Detach(State s, Entry entry)
    {
        var cooldowns = entry.Cooldowns ??= new(StringComparer.Ordinal);
        cooldowns[s.Key] = Math.Max(cooldowns.GetValueOrDefault(s.Key), s.Cooldown);
        if (s.RechargePeriod > 0)
        {
            RefreshCharge(s);
            s.RemainingRecharge = EyewearRules.PauseRecharge(s.Charges, s.ChargeAt, Time.time, s.RechargePeriod);
        }
        s.Charges = 0; s.Until = s.Second = s.Window = s.Distance = s.AimSince = 0;
        s.Counter = s.RewardMeters = s.LastLean = 0; s.AttackerId = null; s.PositionKnown = false;
        CombatForget(s);
        EyewearLegDrain.Forget(s);
        Worn.Remove(s.Player); Sampled.Remove(s.Player);
        if (entry.Animation is { } animation) Animations.Remove(animation);
        entry.Animation = null;
        PlayerRefresh.Firearm(s.Player, "eyewear removed");
    }
    internal static void Forget(Player p)
    {
        if (Players.TryGetValue(p, out var entry) && entry.Current is { } s) Detach(s, entry);
        Players.Remove(p); Worn.Remove(p); Sampled.Remove(p);
        if (p.ProceduralWeaponAnimation is { } animation) Animations.Remove(animation);
    }
    internal static void Reset()
    {
        Generation++;
        try { CombatReset(); }
        finally
        {
            Players = new(); Worn.Clear(); Sampled.Clear(); Animations.Clear(); Work.Clear(); NextSample = 0;
            EyewearLegDrain.Reset(); ResetEvents();
        }
    }
    internal static void Maintain(GameWorld? world)
    {
        if (world is null || Sampled.Count == 0 || Time.time < NextSample) return;
        var now = Time.time; NextSample = now + .1f;
        Work.AddRange(Sampled.Values);
        try
        {
            foreach (var s in Work)
            {
                if (!PlayerRefresh.CanObserve(s.Player)) { Forget(s.Player); continue; }
                try { Sample(s, now, false); }
                catch (Exception error)
                {
                    if (now < s.NextError) continue;
                    s.NextError = now + 30;
                    Plugin.LogSource.LogWarning($"EFB eyewear {s.Key}: {error.Message}");
                }
            }
        }
        finally { Work.Clear(); }
    }
    internal static void PrepareShot(State s)
    { if (s.Human && NeedsSampling(s)) Sample(s, Time.time, false); }
    private static void Sample(State s, float now, bool initial)
    {
        if (!s.Human) return;
        if (!EyewearRules.NeedsMovementRead(s.Key, s.Roll.Eyewear.Hip, s.Roll.Eyewear.MovingSway,
            s.Active && s.RewardMeters < 50, s.InWindow && s.Charges > 0))
        {
            // Inactive rewards need no native position, velocity, aim or
            // stamina reads. Restart distance tracking when a window opens.
            s.PositionKnown = false; s.LastSample = now;
            return;
        }
        var p = s.Player;
        var oldStill = s.Still; var oldMoving = s.Moving; var oldSprint = s.Sprint; var oldAim = s.Aiming;
        s.Weapon = p.HandsController?.Item as Weapon;
        s.Aiming = p.HandsController?.IsAiming == true;
        s.Still = p.Velocity.sqrMagnitude < .01f && p.InputDirection.sqrMagnitude < .001f;
        s.Sprint = p.MovementContext.IsSprintEnabled && p.Velocity.sqrMagnitude > .1f;
        s.Moving = !s.Still && !s.Sprint;
        if (s.Key == "EW021") s.AimSince = s.Still && s.Aiming ? s.AimSince > 0 ? s.AimSince : now : 0;
        if (s.Key == "EW009")
        {
            // Native tilt magnitude 5 is the full lean endpoint; a partial
            // lean or a return through center cannot fabricate a transition.
            var tilt = p.MovementContext.Tilt;
            var lean = tilt >= 4.9f ? 1 : tilt <= -4.9f ? -1 : 0;
            if (lean != 0 && lean != s.LastLean)
            {
                if (!initial && s.LastLean != 0 && s.Ready) s.Charges = 1;
                s.LastLean = lean;
            }
            s.Lean = lean;
        }
        if (s.Key is "EW005" or "EW034")
        {
            var position = p.Position;
            var elapsed = now - s.LastSample;
            var length = s.PositionKnown ? Vector3.Distance(position, s.Position) : 0;
            var valid = EyewearRules.TravelValid(length, elapsed);
            if (!valid) s.Distance = 0;
            if (!initial && valid && s.Sprint)
            {
                if (s.Key == "EW005" && s.Active && s.RewardMeters < 50)
                {
                    var meters = EyewearRules.VictoryMeters(ref s.Distance, ref s.RewardMeters, length);
                    if (meters > 0)
                    {
                        HealDistributed(s, meters);
                        var stamina = p.Physical.Stamina;
                        stamina.UpdateStamina(Math.Min(stamina.TotalCapacity, stamina.Current + meters));
                    }
                }
                else if (s.Key == "EW034") s.Distance += length;
            }
            if (s.Key == "EW034")
            {
                if (s.Sprint && !oldSprint) s.Distance = 0;
                if (!s.Sprint && oldSprint)
                {
                    if (s.Distance >= 20 && s.Ready) { s.Buff(6, 30); Changed(s); }
                    s.Distance = 0;
                }
            }
            s.Position = position; s.PositionKnown = true;
        }
        if (s.Key == "EW020" && s.InWindow && s.Charges > 0 && p.Physical.Stamina.Current < p.Physical.Stamina.TotalCapacity * .25f)
        { s.Charges = 0; s.Window = 0; s.Cool(45); BackpackRuntime.RestoreLeg(p, .4f); Notice(s, "Stamina restored"); }
        s.LastSample = now;
        if (!initial && ((oldStill != s.Still && s.Roll.Eyewear.Hip > 0) || ((oldMoving != s.Moving || oldAim != s.Aiming) && s.Roll.Eyewear.MovingSway > 0))) Changed(s);
    }
    internal static void RefreshCharge(State s)
    {
        if (s.RechargePeriod <= 0) return;
        s.Charges = EyewearRules.Charged(s.Charges, s.ChargeAt, Time.time);
        if (s.Charges > 0) s.RemainingRecharge = 0;
    }
    internal static bool ConsumeCharge(State s, float rechargeSeconds)
    {
        RefreshCharge(s);
        if (s.Charges <= 0) return false;
        s.Charges = 0; s.RechargePeriod = rechargeSeconds;
        s.RemainingRecharge = rechargeSeconds; s.ChargeAt = Time.time + rechargeSeconds;
        return true;
    }
    private static void Changed(State s) => PlayerRefresh.Firearm(s.Player, "eyewear state");
    private static void Notice(State s, string detail)
    { if (s.Human) EffectStatusModel.Notify(s.Player.ProfileId, s.Key, s.Roll.Definition.ShortName, Time.time, detail); }
}
