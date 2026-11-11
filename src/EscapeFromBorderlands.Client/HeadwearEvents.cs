using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Headwear
{
    private static readonly HashSet<string> Deaths = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Corpses = new(StringComparer.Ordinal);
    private static readonly HashSet<SmokeGrenade> Smokes = new();
    private static readonly Dictionary<Player, ExfiltrationPoint> Zone = new(), IndividualExit = new();
    private static readonly Dictionary<Player, ActiveHealthController.Effect> Pain = new();
    private static readonly MethodInfo CreatePain = typeof(ActiveHealthController.Effect)
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 8)
        .MakeGenericMethod(typeof(ActiveHealthController).GetNestedType("PainKiller", BindingFlags.Public | BindingFlags.NonPublic)!);

    private static void ResetEvents() { Deaths.Clear(); Corpses.Clear(); Smokes.Clear(); Zone.Clear(); IndividualExit.Clear(); Pain.Clear(); }
    private static void ClearPlayerEvents(Player p) { Zone.Remove(p); IndividualExit.Remove(p); }
    private static void Notice(State s, string message) =>
        EffectStatusModel.Notify(s.Player.ProfileId, s.Key, s.Roll.Definition.ShortName, Time.time, message);
    private static void Painkiller(State s, float seconds)
    {
        var p = s.Player;
        if (Pain.TryGetValue(p, out var effect) && effect.Existing) { effect.AddWorkTime(seconds, true); return; }
        Action<ActiveHealthController.Effect> init = e => Native.Call(e, "StoreValues", "575146b724597720a27126d5", seconds);
        var h = p.ActiveHealthController;
        if (CreatePain.Invoke(null, new object?[] { h, EBodyPart.Head, 0f, seconds, 0f, 1f, Native.Get(h, "UpdateTime"), init }) is ActiveHealthController.Effect created)
            Pain[p] = created;
    }
    private static void RemovePain(Player p)
    {
        if (!Pain.TryGetValue(p, out var effect)) return;
        Pain.Remove(p);
        if (p.HealthController?.IsAlive == true && effect.Existing) effect.ForceRemove();
    }
    internal static void SmokeChanged(SmokeGrenade g, bool started) { if (started) Smokes.Add(g); else Smokes.Remove(g); }
    private static bool InSmoke(Player p)
    {
        foreach (var g in Smokes)
        {
            if (g == null || Native.Get(g, "Area") is not Component area || Native.Get(area, "enabled") is false) continue;
            var center = area.transform.TransformPoint(Native.Get(area, "center") is Vector3 v ? v : Vector3.zero);
            var radius = Native.Number(area, "radius") * Math.Abs(area.transform.lossyScale.x);
            if ((p.Position + Vector3.up - center).sqrMagnitude <= radius * radius) return true;
        }
        return false;
    }
    internal static void ExfilChanged(Player p, ExfiltrationPoint point, bool entered, bool countdown = false)
    {
        if (entered) { Zone[p] = point; if (countdown) IndividualExit[p] = point; }
        else
        {
            if (Zone.GetValueOrDefault(p) == point) Zone.Remove(p);
            if (IndividualExit.GetValueOrDefault(p) == point) IndividualExit.Remove(p);
        }
        if (Get(p) is { Key: "HW089-1" } state) state.Exfil = Extracting(p);
    }
    private static bool Extracting(Player p) => Zone.TryGetValue(p, out var point) && point != null && point.Entered.Contains(p) &&
        (point.Status == EExfiltrationStatus.RegularMode && IndividualExit.GetValueOrDefault(p) == point ||
         point.Status == EExfiltrationStatus.Countdown && point.ExfiltrationStartTime >= 0f && !point.UnmetRequirements(p).Any());

    private static bool Repair(RepairableComponent? r, float value)
    {
        if (r is null || r.Durability >= r.MaxDurability) return false;
        r.Durability = Math.Min(r.MaxDurability, r.Durability + value);
        r.Item.RaiseRefreshEvent(); return true;
    }
    private static void RepairHat(State s, float amount, bool all = false, bool visor = false)
    {
        RepairableComponent? worst = null; float lowest = 1; bool repaired = false;
        foreach (var i in s.Hat.GetAllItems())
        {
            if (!Section(s, i, visor) || Native.Component<RepairableComponent>(i) is not { } r) continue;
            if (all) repaired |= Repair(r, amount);
            else if (r.MaxDurability > 0 && r.Durability / r.MaxDurability < lowest) { worst = r; lowest = r.Durability / r.MaxDurability; }
        }
        if (Repair(worst, amount) || repaired) s.Player.RecalculateEquipmentParams();
    }
    private static void RepairWorn(State s)
    {
        var p = s.Player;
        var gear = p.InventoryController.Inventory.Equipment;
        var repaired = false;
        foreach (var slot in new[] { EquipmentSlot.Headwear, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear, EquipmentSlot.Earpiece, EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Backpack })
            if (gear.GetSlot(slot)?.ContainedItem is { } item)
                foreach (var i in item.GetAllItems())
                    if (i == item || Native.Component<ArmorComponent>(i) is not null && i.CurrentAddress is SlotItemAddress)
                        repaired |= Repair(Native.Component<RepairableComponent>(i), 10);
        if (repaired) p.RecalculateEquipmentParams();
    }
    private static bool Boss(Player p) => p.Profile?.Info?.Settings?.Role.ToString() is { } role &&
        (role.StartsWith("boss", StringComparison.OrdinalIgnoreCase) || role is "sectantPriest" or "sectantOni");
    internal static void Dead(Player victim)
    {
        if (victim.InventoryController?.Inventory?.Equipment is { } gear) Corpses[gear.Id] = victim.ProfileId;
        try { Killed(victim); } finally { Forget(victim); }
    }
    private static void Killed(Player victim)
    {
        if (Native.Get(victim, "LastAggressor") is not Player p || !AdvancedMeleeRuntime.Enemy(p, victim) ||
            Get(p) is not { } s || Native.Get(victim, "LastDamageInfo") is not DamageInfo hit || !Deaths.Add(victim.ProfileId)) return;
        var head = victim.LastDamagedBodyPart == EBodyPart.Head;
        var firearm = hit.DamageType == EDamageType.Bullet && hit.Weapon is Weapon;
        var explosive = hit.DamageType is EDamageType.Explosion or EDamageType.GrenadeFragment or EDamageType.ThermobaricExplosion || hit.Weapon is ThrowWeap;
        bool NATO() => firearm && (Singleton<ItemFactory>.Instance?.ItemTemplates.TryGetValue(hit.SourceId, out var ammo) == true && ammo is AmmoTemplate round
            ? round.Caliber == "Caliber556x45NATO" : (hit.Weapon as Weapon)?.AmmoCaliber == "Caliber556x45NATO");
        switch (s.Key)
        {
            case "HW003-2" when Math.Abs(p.MovementContext.Tilt) > .05f && s.Ready: s.Buff(10, 20); break;
            case "HW014-1" when s.Ready: s.Window = Time.time + 5; break;
            case "HW018-2" when explosive && s.Ready: s.Buff(25, 30); break;
            case "HW019-2" when s.Ready:
                var hp = p.ActiveHealthController.GetBodyPartHealth(EBodyPart.Common, false);
                if (hp.Current < hp.Maximum * .5f) s.Buff(15, 30);
                break;
            case "HW035-2": s.Charge(3); break;
            case "HW039-1" when NATO(): s.Buff(120); break;
            case "HW041-2": p.ActiveHealthController.ChangeEnergy(10); p.ActiveHealthController.ChangeHydration(10); Heal(s, 10); Notice(s, "Energy, Hydration and health restored"); break;
            case "HW050-3" when TimeCondition(s) && firearm && hit.Weapon is Weapon gun && gun.Mods.Any(m => m is Silencer): s.Buff(120); break;
            case "HW051-3" when !s.Permanent:
                if (victim.Side is EPlayerSide.Usec or EPlayerSide.Bear) s.LastPmc = Time.time;
                else if (victim.Side == EPlayerSide.Savage && !Boss(victim)) s.LastScav = Time.time;
                if (Time.time - s.LastPmc <= 60 && Time.time - s.LastScav <= 60) s.Permanent = true;
                break;
            case "HW056-2" when hit.DamageType == EDamageType.Melee: RepairWorn(s); Heal(s, 10, true); Notice(s, "Health and durability restored"); break;
            case "HW057-3" when TimeCondition(s): Heal(s, 30); Notice(s, "Health restored"); break;
            case "HW058-3" when head: s.Buff(25); break;
            case "HW060-3":
                if (++s.Counter % 3 == 0) { RepairHat(s, 10); Notice(s, "Durability restored"); }
                break;
            case "HW064-3" when TimeCondition(s) && s.Ready: s.Charges = 1; s.Window = Time.time + 30; s.Cool(20); break;
            case "HW069-3" when Boss(victim): s.Permanent = true; break;
            case "HW074-3" when p.HandsController?.Item is Weapon { WeapClass: "smg" }: s.Buff(10); break;
            case "HW082-3" when ((hit.HitPoint == Vector3.zero ? victim.Position : hit.HitPoint) -
                (hit.MasterOrigin == Vector3.zero ? p.Position : hit.MasterOrigin)).sqrMagnitude > 10000: s.Buff(45); break;
            case "HW092-3" when head && NATO() && s.Ready: s.Charges = 1; s.Cool(35); break;
            case "HW094-2" when head && TimeCondition(s) && s.Ready: RepairHat(s, 6); s.Cool(35); Notice(s, "Durability restored"); break;
        }
    }
    internal static void Hit(Player victim, DamageInfo info, EBodyPart part)
    {
        if (info.DamageType != EDamageType.Bullet || info.Weapon is not Weapon w || info.Player?.iPlayer is not Player p ||
            Get(p) is not { Key: "HW008-1" or "HW037-1" } s || !AdvancedMeleeRuntime.Enemy(p, victim)) return;
        if (s.Key == "HW008-1" && s.Ready && part == EBodyPart.Head &&
            ((info.HitPoint == Vector3.zero ? victim.Position : info.HitPoint) - (info.MasterOrigin == Vector3.zero ? p.Position : info.MasterOrigin)).sqrMagnitude > 900)
            s.Charges = 1;
        if (s.Key == "HW037-1" && !s.Permanent && s.Classes.Add(w.WeapClass) && s.Classes.Count >= 3) s.Permanent = true;
    }
    internal static void Fired(Player? p)
    {
        if (p is null) return;
        FiredTemporary(p);
        if (Get(p) is not { } s) return;
        s.LastShot = Time.time;
        if (s.Key == "HW090-1") s.Distance = 0;
    }
    internal static void Checked(Player p, int bit)
    {
        if (Get(p) is not { } s || bit is not (1 or 2)) return;
        if (bit == 1)
        {
            s.Chamber = Time.time;
            if (s.Key == "HW007-2" && s.Ready) { CastSmoke(s, 60); s.Buff(60, 60); }
            if (s.Key == "HW008-1" && s.Charges > 0 && s.Ready) { s.Charges--; s.Buff(120, 20); }
        }
        else { s.Magazine = Time.time; if (s.Key == "HW055-3") s.Buff(5); }
        if (s.Key == "HW042-1" && s.Ready && Math.Abs(s.Chamber - s.Magazine) <= 10)
        { s.Charges = s.DrawCharges = 1; s.Cool(35); s.Chamber = s.Magazine = -999; }
    }
    internal static void Breach(Player p)
    {
        if (Get(p) is not { } s || !s.Ready) return;
        if (s.Key == "HW021-1") s.Buff(12, 25);
        if (s.Key == "HW072-2") s.Buff(30, 45);
        if (s.Key == "HW085-3") s.Charge(3);
    }
    internal static void Packed(Player? p)
    {
        if (Get(p) is { Key: "HW022-1", Charges: 0 } s && ++s.Counter >= 30) { s.Counter = 0; s.Charge(1); }
    }
    internal static void Ate(State s, bool drink)
    {
        if (drink && s.Key == "HW040-1") { s.Buff(60); Painkiller(s, 60); }
        if (s.Key == "HW081-2") { s.Buff(120); Painkiller(s, 120); }
        if (!drink && s.Key == "HW090-2" && s.Ready) { s.Buff(25, 45); s.Charges = 1; }
        if (drink && s.Key == "HW091-3" && s.Ready) s.Buff(30, 30);
    }
    internal static void Thrown(Player? p, Grenade grenade, ThrowWeap item)
    {
        if (Get(p) is not { } s || !s.Ready) return;
        if (s.Key == "HW018-1" && grenade is SmokeGrenade) { s.Buff(10, 30); s.Second = Time.time + 5; }
        if (s.Key == "HW066-3" && s.Charges > 0 && item is IExplosiveItem { MaxExplosionDistance: > 0 } && grenade is not SmokeGrenade and not StunGrenade)
        { Enlarge(item.Id); s.Charges--; s.Cool(60); }
    }
    internal static float Wear(State s, ArmorComponent armor, DamageInfo info)
    {
        var regular = Section(s, armor.Item);
        var factor = regular ? Factor(s, HatStat.Durability) : 1;
        if (s is { Key: "HW071-1", Visor: true }) factor *= .25f;
        if (regular && s is { Key: "HW090-2", Charges: > 0 } &&
            (info.BlockedBy == armor.Item.Id || info.DeflectedBy == armor.Item.Id)) { factor *= .5f; s.Charges--; }
        return factor;
    }
    internal static void Stopped(State s, ArmorComponent armor, ref DamageInfo info)
    {
        var regular = Section(s, armor.Item);
        var ricochet = info.DeflectedBy == armor.Item.Id;
        if (regular)
        {
            info.Damage *= Factor(s, HatStat.Blunt);
            if (s.Key == "HW067-1" && s.Melee || s.Key == "HW093-2") info.Damage *= .30f;
            if (s.Key == "HW080-1" && s.Lmg) info.Damage *= .35f;
        }
        if (s.Key == "HW053-1" && s.Visor && info.IsForwardHit) info.Damage *= .40f;
        if (!regular) return;
        switch (s.Key)
        {
            case "HW049-1" when s.Ready && info.Player?.iPlayer is Player enemy && AdvancedMeleeRuntime.Enemy(s.Player, enemy):
                if (Flash(s.Player)) s.Cool(60); break;
            case "HW049-3": Heal(s, 5, true); break;
            case "HW052-1" when ricochet: s.Buff(60); Painkiller(s, 60); break;
            case "HW064-2" when s.Ready && armor.Item.CurrentAddress is SlotItemAddress a && a.Slot.ID.Equals("Helmet_back", StringComparison.OrdinalIgnoreCase):
                s.Buff(12, 45); s.Second = Time.time + 6; break;
            case "HW077-2" when ricochet: s.Buff(10); break;
            case "HW084-1" when s.Visor: s.Charges = 1; s.Window = Time.time + 20; break;
            case "HW087-1" when ricochet: Heal(s, 20); Notice(s, "Health restored"); break;
            case "HW088-1" when ricochet && s.Ready: s.Buff(6, 60); s.Second = Time.time + 10; break;
            case "HW090-1" when s.Charges > 0: s.Charges--; BackpackRuntime.RestoreLeg(s.Player, .50f); Notice(s, "Leg Stamina restored"); break;
        }
    }
    internal static bool Incoming(Player p, ref DamageInfo hit, EBodyPart part)
    {
        if (Immune(p, hit)) return false;
        if (Get(p) is not { } s || hit.Damage <= 0) return true;
        if (s.Key == "HW038-2" && hit.DamageType == EDamageType.Bullet && hit.Weapon is Weapon && hit.Player?.iPlayer is Player enemy &&
            AdvancedMeleeRuntime.Enemy(p, enemy))
        {
            if (s.DodgedSource == enemy.ProfileId && s.DodgedIndex == hit.FireIndex && Time.time - s.DodgedAt < .2f) return false;
            if (s.Ready && Time.time - s.Quiet >= 15)
            { s.Cool(30); s.DodgedSource = enemy.ProfileId; s.DodgedIndex = hit.FireIndex; s.DodgedAt = Time.time; Notice(s, "Shot dodged"); return false; }
        }
        if (!DamageHelper.IsEnemyDamage(hit.DamageType)) return true;
        hit.Damage *= Factor(s, HatStat.Resist);
        if (part == EBodyPart.Head && s.Key == "HW024-1" && hit.DamageType == EDamageType.Bullet && hit.Weapon is Weapon) hit.Damage *= .25f;
        if (part == EBodyPart.Head && s.Key == "HW085-2" && s.Smoke && hit.DamageType == EDamageType.Melee) hit.Damage *= .50f;
        return true;
    }
    internal static void BulletImpact(Player victim, DamageInfo hit, ShotId shotId)
    {
        if (victim.HealthController?.IsAlive != true || hit.DamageType != EDamageType.Bullet || hit.Weapon is not Weapon ||
            hit.Player?.iPlayer is not Player attacker || Get(victim) is not { Key: "HW019-1" } s ||
            !AdvancedMeleeRuntime.Enemy(victim, attacker)) return;
        var source = string.IsNullOrEmpty(shotId._ammoId) ? attacker.ProfileId : shotId._ammoId;
        var index = string.IsNullOrEmpty(shotId._ammoId) ? hit.FireIndex : shotId._fragmentIndex;
        if (s.Impacts.Hit(source, index, Time.time, s.Ready)) s.Buff(6, 45);
        HeadwearDiagnostics.Damage(s, hit, "Confirmed enemy impact outcome", hit.Damage);
    }
    internal static void Damaged(State s, DamageInfo hit, EBodyPart part)
    {
        var enemy = hit.Player?.iPlayer is Player attacker && AdvancedMeleeRuntime.Enemy(s.Player, attacker);
        var firearm = hit.DamageType == EDamageType.Bullet && hit.Weapon is Weapon;
        if (DamageHelper.IsEnemyDamage(hit.DamageType))
        {
            s.Quiet = Time.time;
            if (s.Key == "HW073-3" && enemy) s.Attackers.Add(hit.Player!.iPlayer.ProfileId);
        }
        if (s.Key == "HW005-1") s.Distance = 0;
        HeadwearDiagnostics.Damage(s, hit, "Damaged outcome", hit.Damage);
        if (s.Key == "HW085-1" && s.Ready && (hit.DamageType is EDamageType.Explosion or EDamageType.ThermobaricExplosion ||
            hit.Weapon is ThrowWeap g && hit.SourceId == g.StringTemplateId)) s.Buff(20, 45);
        if (s.Key == "HW067-3" && part == EBodyPart.Head && firearm && s.Charges > 0)
        { s.Charges--; Heal(s, 25); Notice(s, "Health restored"); }
    }
    internal static void Searched(Player p, InventoryEquipment root)
    {
        if (Get(p) is { Key: "HW073-3" } s && Corpses.TryGetValue(root.Id, out var id) && s.Attackers.Contains(id) && s.Searched.Add(id))
        { Heal(s, 15, true); Notice(s, "Health restored"); }
    }

    internal static IEnumerable<EffectStatusModel.Status> Status(Player? p)
    {
        if (p is null || Get(p) is not { } s) yield break;
        var now = Time.time;
        var time = Math.Max(0, Math.Max(s.Until, s.Second) - now);
        // Sprint-ready signatures and bonuses retain their mechanics without a transient recovery status.
        var active = time > 0 || s.Permanent || s.Drawing || s.Key switch
        {
            "HW004-2" => Map == "woods", "HW006-2" or "HW078-1" => s.Dry, "HW011-2" => s.NoArmor,
            "HW012-1" => s.Still && now - s.Rest >= 8, "HW013-1" or "HW020-1" or "HW065-2" => TimeCondition(s),
            "HW024-2" => s.NoRig, "HW025-1" or "HW067-1" => s.Melee, "HW026-1" => s.Still && s.Prone,
            "HW009-2" or "HW027-1" => TemporaryHeld(p), "HW030-1" => s.Seal, "HW031-2" => s.Still && now - s.Rest >= 2,
            "HW032-1" => s.Cal762, "HW036-2" => s.NoFace, "HW038-2" => s.Ready && now - s.Quiet >= 15,
            "HW044-1" => s.Nvg, "HW045-1" => s.Bolt, "HW047-3" => MapCondition, "HW048-2" => s.Forward,
            "HW049-2" => s.Visor && s.AimSince > 0 && now - s.AimSince >= 4, "HW053-1" or "HW071-1" => s.Visor,
            "HW059-2" => s.NoEarpiece, "HW061-1" => s.Armed, "HW062-3" => s.Suppressed && TimeCondition(s),
            "HW063-1" => s.Marksman, "HW080-1" => s.Lmg, "HW083-2" => s.Baton,
            "HW085-2" => s.Smoke, "HW089-1" => s.Exfil,
            "HW092-2" => s.Still && p.HandsController?.IsAiming != true, "HW095-3" => s.Green, _ => false
        };
        var charges = s.Key is "HW064-3" or "HW084-1" && !s.InWindow ? 0 : s.Charges + s.DrawCharges;
        if (s.Key == "HW014-1" && s.InWindow) { active = true; time = s.Window - now; }
        var detail = charges > 0 ? charges + (charges == 1 ? " charge" : " charges") : "";
        if (s.Drawing && detail.Length == 0) detail = "Draw speed";
        if (HatMedical.Active(s) && detail.Length == 0) { active = true; detail = "Medical use speed"; }
        var ready = Math.Max(0, s.Cooldown - now);
        if (active || charges > 0 || ready > 0) yield return new EffectStatusModel.Status(s.Key, s.Roll.Definition.ShortName, active || charges > 0, time, ready, detail);
    }
}
