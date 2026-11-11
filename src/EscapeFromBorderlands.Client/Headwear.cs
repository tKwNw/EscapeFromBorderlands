using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Animations;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal enum HatStat
{
    Move, Reload, Draw, Aim, Bolt, MeleeDamage, MeleeSwing, Resist, Recoil, Dispersion, Hip, Penetration, FireRate,
    Sway, Recovery, Leg, Arm, Pack, Unpack, Search, Medical, Noise, Hearing, ExternalSteps, Skill, Gunshot,
    Camera, Freelook, Lean, Stance, SprintReady, Blunt, Durability, Ricochet, Ergo
}
internal struct HeadwearRoll
{
    internal float Camera, Freelook, Search, Lean, Stance, Sprint, Blunt, Durability, Ricochet;
}

internal static partial class Headwear
{
    private sealed class Entry
    {
        internal bool Observed;
        internal State? Current;
        internal readonly Dictionary<string, State> Records = new(StringComparer.Ordinal);
    }
    internal sealed class State
    {
        internal Player Player = null!;
        internal Item Hat = null!;
        internal RarityResolver.ShotRoll Roll = null!;
        internal string Key => Roll.Definition.RuntimeKey;
        internal float Until, Second, Cooldown, Window, Quiet, Rest, Run, MeleeSince, AimSince, NvgSince, LastShot;
        internal float LastScav = -999, LastPmc = -999, Chamber = -999, Magazine = -999, LastLean, Distance;
        internal readonly HeadwearImpactWindow Impacts = new();
        internal float HealAt, ChargeAt, RepairAt, ErrorAt;
        internal int Charges, DrawCharges, Counter, Lean, LastLeanDirection, Motion;
        internal bool Permanent, Armed, Standing, Crouched, Prone, Sprint, Nvg, Visor, Still, NoRig, NoArmor, NoFace, NoEarpiece;
        internal bool Dry, Smoke, Exfil, Green, Forward, Melee, Bolt, Marksman, Lmg, Smg, Suppressed, Cal762, Seal, Baton;
        internal float LastAim = 1, LastHip = 1;
        internal bool Drawing, HandsSprint;
        internal float DrawSpeed, SnapUntil, SnapSpeed, SnapApplied;
        internal FirearmsAnimator? Snap;
        internal Vector3 Position;
        internal Weapon? Weapon, PreviousWeapon;
        internal readonly HashSet<string> Classes = new(StringComparer.Ordinal), Attackers = new(StringComparer.Ordinal), Searched = new(StringComparer.Ordinal);
        internal int DodgedIndex;
        internal string? DodgedSource;
        internal float DodgedAt;
        internal bool Ready => Time.time >= Cooldown;
        internal bool Active => Time.time < Until;
        internal bool InWindow => Time.time < Window;
        internal void Buff(float seconds, float cooldown = 0) { Until = Time.time + seconds; if (cooldown > 0) Cool(cooldown); }
        internal void Cool(float seconds) => Cooldown = Time.time + seconds;
        internal void Charge(int max) { if (Charges < max) Charges++; }
    }

    private static readonly EBodyPart[] Parts = (EBodyPart[])Enum.GetValues(typeof(EBodyPart));
    private static ConditionalWeakTable<Player, Entry> Players = new();
    private static readonly Dictionary<Player, State> Worn = new();
    private static readonly Dictionary<Item, State> Hats = new();
    private static readonly Dictionary<ProceduralWeaponAnimation, Player> Animations = new();
    private static readonly Dictionary<SkillManager, Player> Skills = new();
    private static readonly List<State> Work = new();
    private static float NextTick, NextClock, Hour = -1;
    private static string Map = "";
    internal static Player? Local;
    internal static State? Listener => Get(Local);

    internal static void Reset()
    {
        HeadwearDiagnostics.Reset();
        foreach (var s in Worn.Values) RestoreSnap(s);
        Players = new(); Worn.Clear(); Hats.Clear(); Animations.Clear(); Skills.Clear(); Work.Clear();
        Local = null; NextTick = NextClock = 0; Hour = -1; Map = "";
        ResetEvents(); ResetAbilities(); HatMedical.Reset();
    }
    internal static State? Get(Player? player)
    {
        if (ReferenceEquals(player, null) || PlayerRefresh.Resetting) return null;
        if (Players.TryGetValue(player, out var entry) && entry.Observed) return entry.Current;
        return PlayerRefresh.CanObserve(player) ? Observe(player, Players.GetOrCreateValue(player)) : null;
    }
    internal static State? Get(ProceduralWeaponAnimation animation) => Animations.TryGetValue(animation, out var p) ? Get(p) : null;
    internal static State? Get(SkillManager skills) => Skills.TryGetValue(skills, out var p) ? Get(p) : null;
    internal static bool Is(Player? p, string key) => Get(p)?.Key == key;
    internal static void EquipmentChanged(Player p)
    {
        if (!PlayerRefresh.CanObserve(p)) return;
        var e = Players.GetOrCreateValue(p);
        var old = e.Current; var current = Observe(p, e);
        if (old is not null || current is not null) PlayerRefresh.Firearm(p, "headwear equipment");
    }
    private static State? Observe(Player p, Entry entry)
    {
        entry.Observed = true;
        var gear = p.InventoryController.Inventory.Equipment;
        var hat = gear.GetSlot(EquipmentSlot.Headwear)?.ContainedItem;
        var previous = entry.Current;
        if (hat is null || !RarityResolver.TryGetDefinition(hat, out var d) || !d.IsHeadwear || d.IsMysteryFleaOffer)
        {
            if (previous is not null) Forget(p, false);
            return entry.Current = null;
        }
        if (previous?.Hat == hat) { Gear(previous, gear); return previous; }
        if (previous is not null) { Hats.Remove(previous.Hat); RemovePain(p); RestoreSnap(previous); }
        if (!entry.Records.TryGetValue(d.RuntimeKey, out var state))
        {
            state = new State { Player = p, LastShot = Time.time, ChargeAt = Time.time + 60, RepairAt = Time.time + 5 };
            entry.Records[d.RuntimeKey] = state;
        }
        state.Hat = hat; state.Roll = RarityResolver.Resolve(hat)!;
        entry.Current = Worn[p] = Hats[hat] = state;
        if (p.ProceduralWeaponAnimation is { } a) Animations[a] = p;
        if (p.Skills is { } skills) Skills[skills] = p;
        Gear(state, gear);
        Sample(state, Time.time, true);
        return state;
    }
    internal static void Forget(Player p, bool discard = true)
    {
        if (Players.TryGetValue(p, out var e)) e.Current = null;
        if (discard) { Players.Remove(p); ClearPlayerEvents(p); }
        if (Worn.TryGetValue(p, out var s)) { Hats.Remove(s.Hat); RestoreSnap(s); }
        Worn.Remove(p); RemovePain(p);
        if (p.ProceduralWeaponAnimation is { } a) Animations.Remove(a);
        if (p.Skills is { } skills) Skills.Remove(skills);
    }
    private static void Gear(State s, InventoryEquipment gear)
    {
        Item? Equip(EquipmentSlot slot) => gear.GetSlot(slot)?.ContainedItem;
        var rig = Equip(EquipmentSlot.TacticalVest);
        s.NoRig = rig is null;
        s.NoArmor = s.Key == "HW011-2" && Equip(EquipmentSlot.ArmorVest) is null &&
            (rig is null || !rig.GetAllItems().Any(i => Native.Component<ArmorComponent>(i)?.ArmorClass > 0));
        s.NoFace = Equip(EquipmentSlot.FaceCover) is null && Equip(EquipmentSlot.Eyewear) is null;
        s.NoEarpiece = Equip(EquipmentSlot.Earpiece) is null;
    }
    internal static void Maintain(GameWorld? world)
    {
        if (world is null) return;
        Local = world.MainPlayer;
        if (Local != null) _ = Get(Local);
        MaintainAbilities();
        var now = Time.time;
        if (Worn.Count == 0 || now < NextTick) return;
        NextTick = now + .10f;
        if (now >= NextClock)
        {
            NextClock = now + 1;
            var clock = world.GameDateTime;
            Hour = clock is null ? -1 : clock.Calculate().Hour;
            Map = world.LocationId ?? "";
        }
        Work.Clear(); Work.AddRange(Worn.Values);
        foreach (var s in Work)
        {
            if (!PlayerRefresh.CanObserve(s.Player)) { Forget(s.Player); continue; }
            try { Sample(s, now, false); }
            catch (Exception e) { if (now >= s.ErrorAt) { s.ErrorAt = now + 30; Plugin.LogSource.LogWarning($"EFB {s.Key}: {e.Message}"); } }
        }
        Work.Clear();
    }
    private static bool Between(int start, int end) => Hour >= 0 && (start < end ? Hour >= start && Hour < end : Hour >= start || Hour < end);
    private static bool TimeCondition(State s) => s.Key switch
    {
        "HW013-1" or "HW050-3" or "HW094-2" => Between(20, 6),
        "HW020-1" => Between(6, 12), "HW057-3" => Between(18, 4), "HW062-3" => Between(20, 5),
        "HW064-3" => Between(0, 6), "HW065-2" => Between(10, 15), _ => false
    };
    private static bool MapCondition => Map is "woods" or "bigmap" or "shoreline";
    private static void Sample(State s, float now, bool initial)
    {
        var p = s.Player;
        var old = s.Weapon; var prone = s.Prone; var crouch = s.Crouched; var nvg = s.Nvg; var position = s.Position; var marksman = s.Marksman;
        s.Weapon = p.HandsController?.Item as Weapon;
        if (old is not null && old != s.Weapon) s.PreviousWeapon = old;
        s.Position = p.Position;
        s.Still = p.Velocity.sqrMagnitude < .01f && p.InputDirection.sqrMagnitude < .001f;
        s.Sprint = p.MovementContext.IsSprintEnabled && p.Velocity.sqrMagnitude > .1f;
        s.Prone = p.IsInPronePose; s.Crouched = !s.Prone && AdvancedMeleeRuntime.Crouched(p); s.Standing = !s.Prone && !s.Crouched;
        s.Melee = p.HandsController is Player.BaseKnifeController;
        s.Forward = p.IsForwardInputDirection && Math.Abs(p.InputDirection.x) < .10f && p.HandsController?.IsAiming != true;
        if (!s.Still) { s.Rest = now; s.Motion++; }
        if (!s.Sprint) s.Run = now;
        if (!s.Melee) s.MeleeSince = now;
        s.Lean = p.MovementContext.Tilt > .05f ? 1 : p.MovementContext.Tilt < -.05f ? -1 : 0;
        if (s.Key is "HW001-2" or "HW044-1")
        {
            s.Nvg = Native.Get(Native.Get(p.NightVisionObserver, "Component"), "IsOn") is true;
            if (s.Nvg && !nvg) s.NvgSince = now;
        }
        if (s.Key is "HW049-2" or "HW053-1" or "HW071-1" or "HW084-1")
            s.Visor = p.FaceShieldObserver?.Component?.Togglable.On == true;
        s.AimSince = p.HandsController?.IsAiming == true && (s.Key != "HW049-2" || s.Visor) ? s.AimSince > 0 ? s.AimSince : now : 0;
        if (s.Key is "HW006-2" or "HW078-1") s.Dry = p.ActiveHealthController.Hydration.Current <= 0;
        if (s.Key == "HW062-3") s.Suppressed = s.Weapon?.Mods.Any(m => m is Silencer) == true;
        if (s.Key == "HW085-2") s.Smoke = InSmoke(p);
        if (s.Key == "HW089-1") s.Exfil = Extracting(p);
        if (s.Key == "HW095-3") s.Green = Healthy(p);
        s.Bolt = s.Weapon?.BoltAction == true && s.Weapon.WeapClass == "sniperRifle";
        s.Marksman = s.Weapon?.WeapClass == "marksmanRifle";
        s.Lmg = s.Weapon?.WeapClass == "machinegun";
        s.Smg = s.Weapon?.WeapClass == "smg";
        s.Cal762 = s.Weapon?.AmmoCaliber == "Caliber762x39";
        s.Seal = s.Weapon is { } w && Seal(w.StringTemplateId);
        s.Baton = s.Melee && p.HandsController?.Item is { } baton && Baton(baton.StringTemplateId);
        if (initial)
        {
            s.Quiet = s.Rest = s.Run = s.MeleeSince = s.NvgSince = now;
            s.HealAt = now + 1; s.RepairAt = now + 5; s.ChargeAt = now + 60;
        }
        else switch (s.Key)
        {
            case "HW001-2" when nvg && !s.Nvg && now - s.NvgSince >= 10 && s.Ready: s.Buff(10, 30); break;
            case "HW005-1":
                if (s.Sprint) s.Distance = 0;
                else if (!s.Still && (s.Distance += Travel(position, s.Position)) >= 40) { s.Distance -= 40; Heal(s, 20); Notice(s, "Health restored"); }
                break;
            case "HW012-1" when s.Still && now - s.Rest >= 8: Regen(s, 2); break;
            case "HW014-1" when !crouch && s.Crouched && s.InWindow && s.Ready:
                s.Window = 0; s.Cool(25); ChestRigRuntime.RestoreArms(p, .35f); Heal(s, 25); Notice(s, "Arm Stamina and health restored"); break;
            case "HW015-1" when s.Lean != 0 && s.Lean != s.LastLeanDirection:
                if (s.LastLeanDirection != 0 && now - s.LastLean <= 6) s.Buff(10);
                s.LastLean = now; s.LastLeanDirection = s.Lean; break;
            case "HW026-1" when s.Still && s.Prone: Regen(s, 1, true); break;
            case "HW061-1": s.Armed = s.Still && (s.Armed || now - Math.Max(s.Rest, s.LastShot) >= 5); break;
            case "HW065-2" when TimeCondition(s): Regen(s, 1, true, true); break;
            case "HW066-3" when s.Melee && now - s.MeleeSince >= 10 && s.Ready: s.Charge(1); break;
            case "HW067-3" when now >= s.ChargeAt: s.Charge(5); s.ChargeAt = now + 60; break;
            case "HW070-2" when s.Sprint && now - s.Run >= 4 && s.Weapon?.SelectedFireMode == Weapon.EFireMode.fullauto && s.Ready: s.Buff(12, 15); s.Run = now; break;
            case "HW071-1":
                if (!s.Visor) s.RepairAt = now + 5;
                else if (now >= s.RepairAt) { RepairHat(s, 1, true, true); s.RepairAt = now + 5; }
                break;
            case "HW076-3" when prone && s.Standing && s.Ready: s.Buff(10, 30); break;
            case "HW078-1" when s.Dry: Regen(s, 0, false, true); break;
            case "HW081-2" when s.Active: Regen(s, 2); break;
            case "HW090-1" when s.Sprint && s.Charges == 0:
                if ((s.Distance += Travel(position, s.Position)) >= 40) { s.Distance = 0; s.Charge(1); }
                break;
        }
        if (marksman != s.Marksman && s.Key == "HW063-1") p.RecalculateEquipmentParams();
        var aim = Factor(s, HatStat.Aim); var hip = Factor(s, HatStat.Hip);
        if (!initial && (old != s.Weapon || aim != s.LastAim || hip != s.LastHip)) PlayerRefresh.Firearm(p, "headwear conditions");
        s.LastAim = aim; s.LastHip = hip;
        if (s.Snap is not null && (now >= s.SnapUntil || old != s.Weapon)) RestoreSnap(s);
    }

    private static float Travel(Vector3 from, Vector3 to)
    { var v = to - from; v.y = 0; var n = v.magnitude; return n <= 3 ? n : 0; }
    private static bool Healthy(Player p)
    {
        var h = p.ActiveHealthController;
        foreach (var part in Parts)
        {
            if (part == EBodyPart.Common || h.IsBodyPartDestroyed(part)) continue;
            var hp = h.GetBodyPartHealth(part, false);
            if (hp.Current <= hp.Maximum * .75f) return false;
        }
        return true;
    }
    private static void Regen(State s, float amount, bool all = false, bool pain = false)
    {
        if (Time.time < s.HealAt) return;
        s.HealAt = Time.time + 1;
        if (amount > 0) Heal(s, amount, all);
        if (pain) Painkiller(s, 1.2f);
    }
    internal static void Heal(State s, float amount, bool all = false, EBodyPart? exact = null)
    {
        var h = s.Player.ActiveHealthController;
        if (h?.IsAlive != true) return;
        EBodyPart? most = null; var lowest = 1f;
        foreach (var part in Parts)
        {
            if (part == EBodyPart.Common || exact is { } target && part != target || h.IsBodyPartDestroyed(part)) continue;
            var hp = h.GetBodyPartHealth(part, false);
            if (hp.Current <= 0 || hp.Current >= hp.Maximum) continue;
            if (all || exact.HasValue) h.ChangeHealth(part, Math.Min(amount, hp.Maximum - hp.Current), DamageHelper.MedKitUse);
            else if (hp.Current / hp.Maximum < lowest) { most = part; lowest = hp.Current / hp.Maximum; }
        }
        if (most is { } b) { var hp = h.GetBodyPartHealth(b, false); h.ChangeHealth(b, Math.Min(amount, hp.Maximum - hp.Current), DamageHelper.MedKitUse); }
    }
    internal static State? ForArmor(ArmorComponent a)
    {
        if (Hats.Count == 0) return null;
        if (Hats.TryGetValue(a.Item, out var s)) return s;
        var root = a.Item.CurrentAddress?.Container.ParentItem;
        return root is not null && Hats.TryGetValue(root, out s) && Section(s, a.Item, true) ? s : null;
    }
    internal static bool Section(State s, Item item, bool visor = false) => item == s.Hat ||
        item.CurrentAddress is SlotItemAddress a && a.Container.ParentItem == s.Hat &&
        (a.Slot.ID.StartsWith("Helmet_", StringComparison.OrdinalIgnoreCase) || visor && Native.Component<FaceShieldComponent>(item) is not null);
    internal static bool Attached(State s, Item item)
    {
        var visited = new HashSet<Item>(AttachmentItemComparer.Instance);
        for (Item? i = item; i is not null; i = i.CurrentAddress?.Container.ParentItem)
        {
            if (ReferenceEquals(i, s.Hat)) return true;
            // The inventory controller can return its own root as ParentItem.
            // Other repeated ancestors must also terminate without hiding valid attachments.
            if (i is InventoryEquipment || !visited.Add(i)) return false;
        }
        return false;
    }
    private sealed class AttachmentItemComparer : IEqualityComparer<Item>
    {
        internal static readonly AttachmentItemComparer Instance = new();
        public bool Equals(Item? x, Item? y) => ReferenceEquals(x, y);
        public int GetHashCode(Item item) => RuntimeHelpers.GetHashCode(item);
    }
    internal static bool Seal(string id) => id is "3a7743400a6a6c23b6f3dd09" or "c2bd53402f8c5cc527b78a28";
    internal static bool Baton(string id) => id is "3ef13605279d6076cc9d9433" or "9a82e88c9f27c9bf40f21b5b";
    internal static bool Amplifies(MeleeRuntime.HeldState held) => Baton(held.Item.StringTemplateId) && Is(held.Player, "HW083-2");
    internal static Player? Owner(Weapon weapon) => TemporaryOwner(weapon) ?? BackpackNative.WeaponOwner(weapon);
    internal static float Factor(Player? p, HatStat stat) => Factor(Get(p), stat);
    internal static float Factor(State? s, HatStat stat)
    {
        if (s is null) return 1;
        var roll = s.Roll.Headwear;
        var bonus = stat switch
        {
            HatStat.Camera => 1 - roll.Camera, HatStat.Freelook => 1 + roll.Freelook, HatStat.Search => 1 + roll.Search,
            HatStat.Lean => 1 + roll.Lean, HatStat.Stance => 1 + roll.Stance, HatStat.SprintReady => 1 + roll.Sprint,
            HatStat.Blunt => 1 - roll.Blunt, HatStat.Durability => 1 - roll.Durability, HatStat.Ricochet => 1 + roll.Ricochet, _ => 1
        };
        return bonus * Signature(s, stat);
    }
    internal static float Recovery(Player p, string stat) => stat == "legRecovery" ? Factor(p, HatStat.Leg) : stat == "armRecovery" ? Factor(p, HatStat.Arm) : 1f;
    private static float Signature(State s, HatStat stat)
    {
        var on = s.Active;
        return (s.Key, stat) switch
        {
            ("HW001-2", HatStat.Aim) when on => 1.60f, ("HW001-2", HatStat.Draw) when on => 1.40f,
            ("HW002-1", HatStat.Move) when !s.Player.MovementContext.IsSprintEnabled => 1.30f, ("HW002-1", HatStat.Leg) => 1.30f,
            ("HW003-2", HatStat.Reload) when on => 1.50f, ("HW003-2", HatStat.Move) when on => 1.25f,
            ("HW004-2", HatStat.Move) when Map == "woods" && s.Player.MovementContext.IsSprintEnabled => 1.25f, ("HW004-2", HatStat.Reload) when Map == "woods" => 1.50f,
            ("HW006-2", HatStat.Move) when s.Dry => 1.15f, ("HW006-2", HatStat.Reload) when s.Dry => 1.25f, ("HW006-2", HatStat.Recoil) when s.Dry => .70f,
            ("HW008-1", HatStat.Bolt) when on && s.Bolt => 1.40f,
            ("HW010-1", HatStat.MeleeDamage) => 2f, ("HW010-1", HatStat.MeleeSwing) => .60f,
            ("HW011-2", HatStat.Resist) when s.NoArmor => .75f,
            ("HW013-1", HatStat.Draw) when TimeCondition(s) => 1.50f, ("HW013-1", HatStat.Reload) when TimeCondition(s) => 1.40f,
            ("HW015-1", HatStat.Penetration or HatStat.Reload) when on => 1.30f,
            ("HW016-1", HatStat.Move) when on => 1.30f, ("HW017-1", HatStat.Resist) => .90f,
            ("HW017-1", HatStat.Move or HatStat.Reload or HatStat.Hearing or HatStat.Leg or HatStat.Arm) => 1.10f,
            ("HW018-1", HatStat.Move) when on => 1.35f, ("HW018-1", HatStat.Reload) when Time.time < s.Second => 1.50f,
            ("HW018-2", HatStat.Pack or HatStat.Unpack) when on => 1.60f, ("HW018-2", HatStat.Search) when on => 1.40f,
            ("HW019-1", HatStat.Move) when on => 1.50f, ("HW019-2", HatStat.Reload or HatStat.Medical) when on => 1.40f,
            ("HW020-1", HatStat.Move) when TimeCondition(s) => 1.20f, ("HW020-1", HatStat.Resist) when TimeCondition(s) => .90f,
            ("HW021-1", HatStat.Hip) when on => .50f, ("HW021-1", HatStat.Draw) when on => 1.30f,
            ("HW023-1", HatStat.Medical) when on => 2f,
            ("HW024-2", HatStat.Medical) when s.NoRig => 1.40f, ("HW024-2", HatStat.Pack) when s.NoRig => 1.60f,
            ("HW025-1", HatStat.Move) when s.Melee => 1.10f, ("HW025-1", HatStat.Resist) when s.Melee => .90f, ("HW025-1", HatStat.MeleeSwing) when s.Melee => 1.30f,
            ("HW027-1", HatStat.Aim or HatStat.Penetration) when TemporaryHeld(s.Player) => 1.50f,
            ("HW028-1", HatStat.Noise) => .10f, ("HW029-2", HatStat.Skill) => 2f, ("HW030-1", HatStat.Resist) when s.Seal => .67f,
            ("HW031-2", HatStat.Recoil) when s.Still && Time.time - s.Rest >= 2 => .60f, ("HW031-2", HatStat.Dispersion) when s.Still && Time.time - s.Rest >= 2 => .70f,
            ("HW032-1", HatStat.Recoil) when s.Cal762 => .70f, ("HW032-1", HatStat.Reload) when s.Cal762 => 1.50f,
            ("HW036-2", HatStat.Noise) when s.NoFace => .50f, ("HW036-2", HatStat.Search) when s.NoFace => 1.50f, ("HW036-2", HatStat.Medical) when s.NoFace => 1.30f,
            ("HW037-1", HatStat.Recoil) when s.Permanent => .65f, ("HW037-1", HatStat.Dispersion) when s.Permanent => .25f, ("HW037-1", HatStat.Move) when s.Permanent => 1.10f,
            ("HW039-1", HatStat.Aim) when on => 1.50f, ("HW039-1", HatStat.Reload) when on => 1.30f, ("HW040-1", HatStat.Pack) when on => 1.60f,
            ("HW044-1", HatStat.Recoil) when s.Nvg => .70f, ("HW044-1", HatStat.Move) when s.Nvg => 1.10f, ("HW044-1", HatStat.Medical) when s.Nvg => 1.50f,
            ("HW045-1", HatStat.Sway) when s.Bolt => 0f, ("HW045-1", HatStat.Recoil) when s.Bolt => .25f, ("HW045-1", HatStat.Bolt) when s.Bolt => 2f,
            ("HW047-3", HatStat.Noise) when MapCondition => .40f, ("HW047-3", HatStat.Recoil) when MapCondition => .80f, ("HW047-3", HatStat.Move) when MapCondition => 1.10f,
            ("HW048-2", HatStat.Resist) when s.Forward => .75f,
            ("HW049-2", HatStat.Recoil) when s.Visor && s.AimSince > 0 && Time.time - s.AimSince >= 4 => .55f,
            ("HW049-2", HatStat.Camera) when s.Visor && s.AimSince > 0 && Time.time - s.AimSince >= 4 => .50f,
            ("HW050-3", HatStat.Reload) when on => 1.50f, ("HW050-3", HatStat.Recoil) when on => .75f,
            ("HW051-3", HatStat.Reload or HatStat.Pack) when s.Permanent => 1.60f,
            ("HW052-1", HatStat.Move) when on => 1.10f, ("HW052-1", HatStat.Resist) when on => .85f,
            ("HW058-3", HatStat.Draw) when on => 1.60f, ("HW058-3", HatStat.Medical) when on => 1.40f,
            ("HW059-2", HatStat.ExternalSteps) when s.NoEarpiece => 1.40f,
            ("HW061-1", HatStat.Recoil) when s.Armed => .50f, ("HW061-1", HatStat.Dispersion) when s.Armed => .10f,
            ("HW062-3", HatStat.Reload or HatStat.Bolt) when s.Suppressed && TimeCondition(s) => 1.50f,
            ("HW062-3", HatStat.Gunshot) when s.Suppressed && TimeCondition(s) => .50f,
            ("HW063-1", HatStat.Sway) when s.Marksman => .20f, ("HW063-1", HatStat.Recoil) when s.Marksman => .80f,
            ("HW064-2", HatStat.Move) when Time.time < s.Second => 1.50f, ("HW064-2", HatStat.Draw) when on => 1.70f,
            ("HW067-1", HatStat.Camera) when s.Melee => .50f,
            ("HW069-3", HatStat.Medical or HatStat.Draw) when s.Permanent => 1.50f, ("HW070-2", HatStat.Hip) when on => .20f,
            ("HW072-2", HatStat.Resist) when on => .80f, ("HW074-3", HatStat.Move) when on && !s.Player.MovementContext.IsSprintEnabled => 1.25f, ("HW074-3", HatStat.Draw) when on => 1.50f,
            ("HW075-2", HatStat.Ergo) => 2f, ("HW076-3", HatStat.Move) when on => 1.50f,
            ("HW077-2", HatStat.Reload or HatStat.FireRate) when on => 1.50f,
            ("HW078-1", HatStat.Resist) when s.Dry => .90f, ("HW078-1", HatStat.Move) when s.Dry => 1.10f,
            ("HW079-1", HatStat.Move) => 1.15f, ("HW079-1", HatStat.Noise) => .50f, ("HW079-1", HatStat.Hearing) => 1.30f,
            ("HW080-1", HatStat.Recoil) when s.Lmg => .70f, ("HW081-2", HatStat.Resist) when on => .90f,
            ("HW082-3", HatStat.Pack) when on => 1.60f, ("HW082-3", HatStat.Reload or HatStat.Medical) when on => 1.50f,
            ("HW083-2", HatStat.Resist) when s.Baton => .75f, ("HW085-1", HatStat.Medical) when on => 1.80f,
            ("HW085-2", HatStat.Search) when s.Smoke => 1.75f, ("HW088-1", HatStat.Move) when on => 1.60f,
            ("HW089-1", HatStat.Resist) when s.Exfil => .40f, ("HW090-2", HatStat.Medical) when on => 1.50f,
            ("HW091-3", HatStat.Recoil) when on => .30f, ("HW091-3", HatStat.Recovery) when on => 1.70f,
            ("HW092-2", HatStat.Ricochet) when s.Still && s.Player.HandsController?.IsAiming != true => 1.50f,
            ("HW093-2", HatStat.Move) when !s.Player.MovementContext.IsSprintEnabled => .90f,
            ("HW095-3", HatStat.Durability) when s.Green => .55f, _ => 1f
        };
    }
}
