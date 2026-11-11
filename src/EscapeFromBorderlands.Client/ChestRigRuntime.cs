using Shot = EftBulletClass;
using Ammo = AmmoItemClass;
using Physical = PlayerPhysicalClass;
using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ChestRigRuntime
{
    internal sealed class WeaponState
    {
        internal int Shots, BurstShots, Consecutive, ReloadGeneration, Charges, NextOutcome = 1;
        internal readonly SortedDictionary<int,bool?> Outcomes = new();
        internal bool Chamber, Selector, Earned, HalfCrossed, Dressing;
        internal float Damage = 1f, Recoil = 1f, ReloadBonus = 1f, DrawCharge = 1f;
        internal string PreviousAmmo = "", Mode = "";
        internal void Reload() { Shots = BurstShots = Consecutive = Charges = 0; Chamber = Selector = Earned = HalfCrossed = Dressing = false; Damage = Recoil = DrawCharge = 1f; PreviousAmmo = ""; Outcomes.Clear(); NextOutcome=1; ReloadGeneration++; }
    }
    internal sealed class State
    {
        internal Player Player = null!;
        internal Item Rig = null!;
        internal RarityResolver.ShotRoll Roll = null!;
        internal BackpackRuntime.Ledger Ledger = null!;
        internal string Key => Roll.Definition.ChestRigEffect!.Key;
        internal readonly Dictionary<string,bool> Flashlights=new(StringComparer.Ordinal);
        internal readonly Dictionary<string, WeaponState> Weapons = new(StringComparer.Ordinal);
        internal readonly Dictionary<object, float> Bleeds = new();
        internal Vector3 Previous;
        internal float LastTick, SprintSeconds, StillSeconds, StationarySeconds, AdsSeconds, BreathSeconds, WalkDistance;
        internal float ReloadBonus = 1f, LastReloadSpeed = float.NaN, DrawCharge = 1f, CycleCharge = 1f, HitDamage = 1f;
        internal StatFactors LastStats;
        internal int GlobalShots, Packing, HandloadShots;
        internal bool WasVaulting, WasProne, Reloading, EmergencyReloading, Emergency, EmptyPrimary, DressingArmed, PatrolCharged, GlassReady, BreathArmed;
        internal string ReloadWeapon = "", LastWeapon = "", LastPrimary = "", ArmedPrimary = "";
        internal WeaponState Weapon(Weapon w) => Weapons.TryGetValue(w.Id, out var value) ? value : Weapons[w.Id] = new WeaponState();
        internal bool Active(string key) => Ledger.Active(Key + ":" + key);
        internal void Arm(string key, float seconds) => Ledger.Arm(Key + ":" + key, seconds);
        internal bool Ready => Ledger.Ready(Key);
        internal void Cooldown(float seconds) => Ledger.Cooldown(Key, seconds);
    }
    internal struct ShotBonuses
    {
        internal float Damage, Penetration, Recoil, Velocity, Accuracy, FireRate, Hip;
        internal bool DoubleExplosive;
        internal static ShotBonuses Normal => new() { Damage = 1f, Penetration = 1f, Recoil = 1f, Velocity = 1f, Accuracy = 1f, FireRate = 1f, Hip = 1f };
    }
    internal sealed class ShotScope
    {
        internal State State = null!;
        internal Weapon Weapon = null!;
        internal Ammo Ammo = null!;
        internal ShotBonuses Bonuses;
        internal int Generation, Ordinal;
        internal ShotScope? Previous;
        internal bool Hit, Finalized, FullAutoAtFire;
        internal readonly HashSet<Shot> Pending = new();
        internal float Created, FinishedAt = -1f;
    }
    internal static readonly Dictionary<Player, State> States = new();
    private static readonly Dictionary<string,string> Names=new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BackpackRuntime.Ledger> Records = new(StringComparer.Ordinal);
    private static readonly Dictionary<Shot, ShotScope> Shots = new();
    private static readonly Dictionary<(string Weapon,int FireIndex),ShotScope> ImpactScopes=new();
    private static readonly HashSet<ShotScope> Volleys = new();
    private static readonly List<ShotScope> FinishedVolleys = new();
    private static readonly List<Player> StalePlayers = new();
    private static readonly EquipmentSlot[] WeaponSlots = { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };
    private static readonly EBodyPart[] CountedParts = { EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg, EBodyPart.Stomach };
    [ThreadStatic] internal static ShotScope? CurrentShot;
    private static GameWorld? World;
    private static float NextScan;
    internal static Item? Worn(Player? p) => p?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
    internal static State? Get(Player? p, bool observed = false)
    {
        if (p is null || !observed && !PlayerRefresh.CanObserve(p)) { if (p is not null) Forget(p); return null; }
        var rig = Worn(p);
        if (States.TryGetValue(p, out var old) && old.Rig.Id == rig?.Id) return old;
        if (old is not null) { States.Remove(p); ChestRigMedical.Unequip(old); }
        if (rig is null || RarityResolver.Resolve(rig) is not { } roll || roll.Definition.ChestRigEffect is null) {if(old is not null)Refresh(p);return null;}
        var recordKey = p.ProfileId;
        if (!Records.TryGetValue(recordKey, out var ledger)) Records[recordKey] = ledger = new BackpackRuntime.Ledger();
        var s = new State { Player = p, Rig = rig, Roll = roll, Ledger = ledger, Previous = p.Position, LastTick = Time.time };
        States[p] = s;Names[s.Key]=roll.Definition.ShortName;
        Refresh(p);
        return s;
    }
    internal static bool Is(Player? p, string key) => Get(p)?.Key == key;
    private static void Forget(Player p)
    {
        if (!States.TryGetValue(p, out var state)) return;
        States.Remove(p);
        ChestRigMedical.Unequip(state);
    }
    internal static void Reset() { States.Clear(); Names.Clear(); Records.Clear(); Shots.Clear(); ImpactScopes.Clear(); Volleys.Clear(); CurrentShot = null; World = null; NextScan = 0f; ChestRigMedical.Reset(); ChestRigInventory.Reset(); ChestRigCombat.Reset(); }
    internal static void Maintain(GameWorld? world, RaidPlayerSnapshot snapshot)
    {
        if (World != world) { Reset(); World = world; }
        if (Volleys.Count > 0)
        {
            var now = Time.time;
            try
            {
                foreach (var volley in Volleys)
                    if (volley.FinishedAt >= 0 && now - volley.FinishedAt >= .1f || now - volley.Created > 30f)
                        FinishedVolleys.Add(volley);
                foreach (var volley in FinishedVolleys) FinishVolley(volley);
            }
            finally { FinishedVolleys.Clear(); }
        }
        if (world is null || Time.time < NextScan) return;
        NextScan = Time.time + .05f;
        ChestRigCombat.Maintain();
        snapshot.Capture(world);
        try
        {
            foreach (var old in States.Keys)
                if (!snapshot.Contains(old) || !PlayerRefresh.CanObserve(old)) StalePlayers.Add(old);
            foreach (var old in StalePlayers) Forget(old);
        }
        finally { StalePlayers.Clear(); }
        foreach (var p in snapshot)
        {
            var s = Get(p);
            if (s is null) continue;
            var dt = Math.Max(0f, Math.Min(.25f, Time.time - s.LastTick)); s.LastTick = Time.time;
            TickSignature(s, p, dt);
            if (p.HandsController is Player.FirearmController firearm)
            {
                var w = firearm.Weapon;
                s.Reloading=firearm.IsInReloadOperation();
                if (s.LastWeapon != w.Id) Draw(s, w);
                ObserveMode(s,w);
                var reloadSpeed = ReloadSpeed(s, w);
                if (reloadSpeed != s.LastReloadSpeed)
                {
                    s.LastReloadSpeed = reloadSpeed;
                    // EFT caches reload speed on its animators. Expiry must
                    // also update a reload which is already in progress.
                    if (s.Reloading) firearm.SetAnimatorAndProceduralValues();
                }
                if (s.Key == "TR11-2")
                {
                    var light = HasActiveLight(w);
                    if (light && s.Flashlights.TryGetValue(w.Id, out var wasOn) && !wasOn && s.Ready)
                    { s.Weapon(w).Charges = 3; s.Cooldown(20f); }
                    s.Flashlights[w.Id] = light;
                }
                if (s.Key == "TR30-1" && Primary(p, w)) s.LastPrimary = w.Id;

                var factors = Stats(s, w);
                if (!factors.Equals(s.LastStats)) { s.LastStats = factors; Refresh(p); }
            }

            if (s.Key == "TR23-2") ChestRigInventory.AutoReloadHolsteredPistol(s);
            ChestRigMedical.Maintain(s, dt);
            ChestRigInventory.Maintain(s, dt);
        }
    }
    private static bool HasActiveLight(Weapon weapon)
    {
        foreach (var item in weapon.GetAllItems())
            if (Native.Component<LightComponent>(item) is { } lamp && Native.Get(lamp, "IsActive") is true) return true;
        return false;
    }
    private static bool Moving(Player p) => p.InputDirection.sqrMagnitude > .0001f;
    private static void TickSignature(State s, Player p, float dt)
    {
        switch (s.Key)
        {
            case "TR02-2":
                var prone = p.MovementContext.IsInPronePose;
                if (prone && !s.WasProne) s.Arm("workout", 15f);
                s.WasProne = prone;
                break;
            case "TR24-1":
            case "AR08-2":
                var vaulting = p.MovementContext.PlayerAnimatorGetIsVaulting();
                if (s.WasVaulting && !vaulting)
                {
                    if (s.Key == "TR24-1") { s.Arm("vault", 5f); s.GlobalShots = 3; }
                    else { s.Arm("quiet", 5f); s.Arm("ergo", 10f); }
                }
                s.WasVaulting = vaulting;
                break;
            case "AR01-2":
                s.StillSeconds = Moving(p) ? 0f : s.StillSeconds + dt;
                break;
            case "TR08-2":
                s.StationarySeconds = !Moving(p) && AdvancedMeleeRuntime.Crouched(p) &&
                    p.HandsController?.IsAiming == true ? s.StationarySeconds + dt : 0f;
                break;
            case "TR29-1":
                if (p.HandsController?.IsAiming != true) { s.AdsSeconds = 0f; s.GlassReady = false; }
                else if (!Moving(p) && Magnified(p)) s.AdsSeconds += dt;
                else if (!s.GlassReady) s.AdsSeconds = 0f;
                if (s.AdsSeconds >= 4f && Magnified(p)) s.GlassReady = true;
                break;
            case "TR29-2":
                const string spent = "TR29-2:breathSpent";
                if (p.HandsController?.IsAiming == true &&
                    (Native.Get(p.Physical, "HoldingBreath") is true || Native.Get(p.Physical, "HoldBreath") is true))
                    s.BreathSeconds += dt;
                else { s.BreathSeconds = 0f; if (!s.BreathArmed) s.Ledger.Checklist.Remove(spent); }
                if (s.BreathSeconds >= 2f && !s.Ledger.Checklist.ContainsKey(spent)) s.BreathArmed = true;
                break;
            case "TR11-1":
            case "TR26-1":
                s.SprintSeconds = p.MovementContext.IsSprintEnabled && Moving(p) ? s.SprintSeconds + dt : 0f;
                if (s.SprintSeconds >= 5f)
                {
                    if (s.Key == "TR26-1") { ArmReload(s, 1.5f); s.SprintSeconds = 0f; }
                    else if (!s.Active("static")) s.Arm("static", float.MaxValue / 4f);
                }
                break;
            case "TR26-2":
                var position = p.Position;
                var motion = position - s.Previous;
                s.Previous = position;
                var moving = Moving(p);
                if (moving && p.MovementContext.IsSprintEnabled) s.WalkDistance = 0f;
                else if (moving && motion.magnitude < 15f) s.WalkDistance += new Vector2(motion.x, motion.z).magnitude;
                if (s.WalkDistance >= 30f) s.PatrolCharged = true;
                break;
            case "AR22-2":
                if (p.Physical.Stamina.Current <= .01f && s.Ready) { s.Arm("stamina", 20f); s.Cooldown(30f); }
                break;
        }
    }
    internal static void Refresh(Player p)
    { PlayerRefresh.Equipment(p); }
    internal static bool Primary(Player p, Weapon w)
    {
        var equipment = p.InventoryController.Inventory.Equipment;
        return equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem?.Id == w.Id ||
            equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem?.Id == w.Id;
    }
    internal static bool Secondary(Player p, Weapon w) => p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem?.Id == w.Id;
    internal static bool Pistol(Weapon w) => w.WeapClass == "pistol";
    internal static bool ThreeFirearms(Player p)
    {
        var equipment = p.InventoryController.Inventory.Equipment;
        foreach (var slot in WeaponSlots) if (equipment.GetSlot(slot).ContainedItem is not Weapon) return false;
        return true;
    }
    internal static bool Magnified(Player p) => p.HandsController?.IsAiming == true && Native.Get(Native.Get(p.ProceduralWeaponAnimation, "CurrentScope"),"IsOptic") is true;
    internal static int BlackParts(Player p)
    {
        var count = 0;
        foreach (var part in CountedParts) if (p.ActiveHealthController.IsBodyPartDestroyed(part)) count++;
        return count;
    }
    internal static bool BlackArms(Player p) => p.ActiveHealthController.IsBodyPartDestroyed(EBodyPart.LeftArm) || p.ActiveHealthController.IsBodyPartDestroyed(EBodyPart.RightArm);
    internal static bool Knight(State s) => s.Key == "AR15-1" && s.Player.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FaceCover).ContainedItem?.StringTemplateId == "62963c18dbc8ab5f0d382d0b";
    internal static float Health(Player p, EBodyPart part = EBodyPart.Common)
    { var h = p.ActiveHealthController.GetBodyPartHealth(part, false); return h.Maximum > 0 ? h.Current / h.Maximum : 1f; }
    internal static void RestoreArms(Player p, float fraction)
    { var st = p.Physical.HandsStamina; st.UpdateStamina(Math.Min(st.TotalCapacity, st.Current + st.TotalCapacity * fraction)); }
    internal struct StatFactors { internal float Recoil, Ergonomics, Accuracy, FireRate, Aim, Hip, Sway, Penetration, Velocity, FlatErgonomics; }
    internal static StatFactors Stats(State s, Weapon w)
    {
        if (CurrentShot is { } current && current.Weapon == w) return ReadStats(s, w, current.Bonuses);
        if (WeaponStatReadBatch.TryRigStats(w, s, out var cached)) return cached;
        var stats = ReadStats(s, w, Preview(s, w, null));
        WeaponStatReadBatch.RememberRigStats(w, s, stats);
        return stats;
    }
    private static StatFactors ReadStats(State s, Weapon w, ShotBonuses shot)
    {
        var p = s.Player;
        var result = new StatFactors { Recoil = s.Roll.ChestRig.Recoil * shot.Recoil, Ergonomics = s.Roll.ChestRig.Ergonomics, Accuracy = shot.Accuracy, FireRate = shot.FireRate, Aim = s.Roll.ChestRig.Aim, Hip = shot.Hip, Sway = 1f, Penetration = shot.Penetration, Velocity = shot.Velocity };
        if (s.Key == "TR08-2" && s.StationarySeconds >= 3f) result.Recoil *= .7f;
        if (s.Key == "TR16-2") { if (Magnified(p)) result.Sway *= .5f; }
        if (s.Key == "TR18-1" && LegendaryRuntime.Capacity(w) >= 30 && LegendaryFireModePatch.LogicalMode(w) == "fullauto") result.Recoil *= .75f;
        if (s.Key == "TR29-1" && s.GlassReady && Magnified(p)) { result.Accuracy *= .6f; result.Sway *= .4f; }
        if (s.Key == "AR01-1" && s.Active("aim")) result.Aim *= 1.4f;
        if(s.Key=="AR08-2"&&s.Active("ergo"))result.FlatErgonomics=15f;
        if(s.Key=="TR06-1"&&s.Active("clutch"))result.Ergonomics*=1.2f;
        if (Knight(s)) result.Ergonomics *= 1.1f;
        if (s.Key == "AR16-1" && p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem is null) result.Aim *= 1.6f;
        if (s.Key == "AR24-2" && Health(p,EBodyPart.Chest) < .35f) result.Aim *= 1.4f;
        if (s.Key == "AR19-2" && s.Active("breach")) result.Hip *= .5f;
        return result;
    }
    internal static ShotBonuses Preview(State s, Weapon w, Ammo? ammo, int? remainingOverride = null)
    {
        var a = ShotBonuses.Normal; var ws = s.Weapon(w); var p = s.Player;
        var ordinal = ws.Shots + 1;
        switch (s.Key)
        {
            case "TR21-1" when Pistol(w): a.Damage = 1.25f; a.FireRate = 1.25f; a.Recoil = .75f; break;
            case "TR03-1" when ws.Chamber: a.Velocity = 1.4f; break;
            case "TR10-1" when ws.Charges > 0 && Secondary(p,w): a.Damage = 1.5f; break;
            case "TR11-2" when ws.Charges>0&&p.HandsController?.IsAiming!=true: a.Hip=.4f;break;
            case "TR24-1" when s.GlobalShots>0: a.Recoil=.5f;break;
            case "TR11-1" when s.Active("static"): a.Damage = 1.5f; a.Velocity = 1.6f; break;
            case "TR15-1" when s.HandloadShots > 0: a.Damage = 1.25f; break;
            case "TR15-2" when ws.Selector: a.Damage = 1.4f; break;
            case "TR21-2" when Pistol(w) && ordinal % 4 == 0 && (ammo?.AmmoTemplate.ProjectileCount ?? 1) == 1: a.DoubleExplosive = true; break;
            case "TR22-2" when Pistol(w) && (remainingOverride ?? LegendaryRuntime.RemainingAmmo(w)) <= 1: a.Damage = 2f; break;
            case "TR27-2" when ws.Chamber && (w.WeapClass is "assaultRifle" or "assaultCarbine" or "marksmanRifle" or "sniperRifle") && w.GetCurrentMagazine() is not null && w.ReloadMode.ToString().IndexOf("Internal",StringComparison.OrdinalIgnoreCase)>=0: a.Penetration = 1.4f; break;
            case "TR28-1" when w.WeapClass == "shotgun" && ammo is not null && ws.PreviousAmmo.Length > 0 && ws.PreviousAmmo != ammo.StringTemplateId: a.Damage = 1.5f; break;
            case "TR29-2" when s.BreathArmed: a.Damage = 1.4f; break;
            case "TR30-1" when ws.Charges > 0: a.Damage = 1.35f; break;
            case "TR31-1" when LegendaryFireModePatch.LogicalFireMode(w) == Weapon.EFireMode.burst && (ws.BurstShots+1)%3 == 0: a.Damage = 1.75f; break;
            case "TR31-2" when ws.HalfCrossed || CompareHalf(w, remainingOverride, above: false): a.FireRate = 1.3f; break;
            case "TR32-1": if (CompareHalf(w, remainingOverride, above: true)) a.Recoil = .6f; else a.Damage = 1.35f; break;
            case "AR03-2" when s.HitDamage > 1f: a.Damage = s.HitDamage; break;
            case "AR04-1" when ws.Earned: a.Damage = 1.25f; break;
            case "AR07-2":
                switch (LegendaryFireModePatch.LogicalFireMode(w))
                {
                    case Weapon.EFireMode.single: a.Damage = 1.25f; break;
                    case Weapon.EFireMode.burst: a.Velocity = 1.3f; break;
                    case Weapon.EFireMode.fullauto: a.Recoil = .7f; break;
                }
                break;
            case "AR10-1" when ws.Earned: a.Recoil = .6f; a.FireRate = 1.2f; break;
            case "AR17-1" when ws.Chamber && LegendaryRuntime.Capacity(w) <= 10: a.Damage = 1.4f; break;
            case "AR17-2" when ws.Charges > 0: a.Recoil = .1f; break;
            case "AR20-2" when ordinal > 40 && (ordinal-40)%3 == 0: a.Damage = 2f; break;
            case "AR27-2" when ws.Selector: a.Damage = 2f; break;
            case "AR28-2" when ws.Earned: a.Damage = 1.75f; a.FireRate = .5f; break;
        }
        return a;
    }
    private static bool CompareHalf(Weapon weapon, int? remainingOverride, bool above)
    {
        var magazine = weapon.GetCurrentMagazine();
        var remaining = remainingOverride ?? ((magazine?.Count ?? 0) + weapon.ChamberAmmoCount);
        var capacity = Math.Max(1, (magazine?.MaxCount ?? weapon.GetMaxMagazineCount()) + weapon.Chambers.Length);
        return above ? remaining > capacity * .5f : remaining < capacity * .5f;
    }
    internal static ShotScope Begin(Weapon w, Ammo ammo, State s, int? remainingOverride = null)
    {
        var p = s.Player;
        var ws = s.Weapon(w);
        ObserveMode(s,w);
        var mode = LegendaryFireModePatch.LogicalMode(w);
        int? nativeRemaining = null;
        int Remaining() => nativeRemaining ??= LegendaryRuntime.RemainingAmmo(w);
        // Native FirearmController.Shot has already consumed this cartridge. Snapshot the
        // pre-shot ammunition window before previewing last-round conditions.
        var scope = new ShotScope { Created=Time.time, State=s, Weapon=w, Ammo=ammo, FullAutoAtFire=mode=="fullauto", Generation=ws.ReloadGeneration, Ordinal=ws.Shots+1, Previous=CurrentShot, Bonuses=Preview(s,w,ammo,remainingOverride??Remaining()+1) };
        if (s.Key == "TR22-2" && Pistol(w) && Remaining() == 0) scope.Bonuses.Damage = 2f;
        CurrentShot = scope; ws.Shots++; ws.Outcomes[scope.Ordinal]=null;
        if (mode == "burst") ws.BurstShots++;
        if (s.Key == "TR31-2" && Remaining() < LegendaryRuntime.Capacity(w)*.5f) ws.HalfCrossed = true;
        if (s.Key == "AR28-2") { if(mode=="single") ws.Consecutive |= 1; if(mode=="fullauto")ws.Consecutive |=2; if(ws.Consecutive==3)ws.Earned=true; }
        ws.PreviousAmmo=ammo.StringTemplateId;
        if (ws.Charges>0)ws.Charges--;
        if(s.HandloadShots>0)s.HandloadShots--;
        if(s.GlobalShots>0)s.GlobalShots--;
        if(s.Key is "TR15-2" or "AR27-2")ws.Selector=false;
        if(s.Key=="TR11-1"){s.Ledger.Until.Remove(s.Key+":static");s.SprintSeconds=0;}
        if(s.Key=="TR29-2"&&s.BreathArmed){s.BreathArmed=false;s.Ledger.Checklist[s.Key+":breathSpent"]=1;}
        if(s.Key=="AR03-2")s.HitDamage=1f;
        if(s.Key=="TR25-1"&&ws.Shots==20) ArmReload(s,1.8f);
        if(s.Key=="TR18-2"&&w.WeapClass=="machinegun"&&ws.Shots%20==0&&s.Ready){BackpackRuntime.RestoreLeg(p!, .3f);s.Cooldown(5f);}
        if(s.Key=="TR10-1"&&Primary(p!,w)&&Remaining()==0)s.EmptyPrimary=true;
        if(s.Key=="TR22-2"&&Pistol(w)&&Remaining()==0)ArmReload(s,1.75f,w);
        return scope;
    }
    internal static void End(ShotScope? scope) { if(scope is not null&&ReferenceEquals(CurrentShot,scope))CurrentShot=scope.Previous; }
    internal static bool TryShot(Shot shot,out ShotScope scope)=>Shots.TryGetValue(shot,out scope!);
    internal static ShotScope? FindScope(Weapon w,int fireIndex)=>ImpactScopes.TryGetValue((w.Id,fireIndex),out var scope)?scope:null;
    internal static void Associate(Shot shot)
    { if(CurrentShot is { } scope){Shots[shot]=scope;ImpactScopes[(scope.Weapon.Id,shot.FireIndex)]=scope;scope.Pending.Add(shot);scope.FinishedAt=-1;Volleys.Add(scope);} }
    internal static void AssociateSibling(Shot shot,Shot parent)
    { if(Shots.TryGetValue(parent,out var scope)){Shots[shot]=scope;ImpactScopes[(scope.Weapon.Id,shot.FireIndex)]=scope;scope.Pending.Add(shot);scope.FinishedAt=-1;Volleys.Add(scope);} }
    internal static void BulletFinished(Shot shot)
    { if(!Shots.TryGetValue(shot,out var s))return; Shots.Remove(shot);s.Pending.Remove(shot);if(s.Pending.Count==0)s.FinishedAt=Time.time; }
    internal static void FleshHit(ShotScope v,Player target,EBodyPart part,bool killed)
    {
        if(target==v.State.Player)return;
        var s=v.State;var p=s.Player;var w=v.Weapon;var ws=s.Weapon(w);
        if(Get(p)!=s)return;
        if(!v.Hit&&v.Generation==ws.ReloadGeneration)
        {
            v.Hit=true;
            ws.Outcomes[v.Ordinal]=s.Key!="AR10-1"||v.FullAutoAtFire;FlushOutcomes(s,w);
            if(s.Key=="AR11-2"&&ChestRigCombat.BlindedBy(target,p))RestoreArms(p,.1f);
            if(s.Key=="AR17-2"&&v.Ordinal<=10&&s.Emergency) { /* recoil is snapshotted at fire time */ }
            if(s.Key=="TR24-2"&&Vector3.Distance(p.Position,target.Position)>=75){ArmReload(s,1.6f);s.CycleCharge=1.75f;}
        }
        if(!killed)return;
        if(s.Key=="TR16-1"&&w.Mods.Any(m=>m is SilencerItemClass)&&s.Ready){RestoreArms(p,.3f);BackpackRuntime.RestoreLeg(p,.3f);s.Cooldown(10f);}
        if(s.Key=="TR28-2"&&w.WeapClass=="shotgun"){s.Arm("medical",15f);if(s.Ready){BackpackRuntime.RestoreLeg(p,.25f);s.Cooldown(10f);}}
    }
    private static void FinishVolley(ShotScope v)
    {
        if(v.Finalized)return;v.Finalized=true;Volleys.Remove(v);
        var ws=v.State.Weapon(v.Weapon);
        if(!v.Hit&&v.Generation==ws.ReloadGeneration){ws.Outcomes[v.Ordinal]=false;FlushOutcomes(v.State,v.Weapon);}
        foreach(var shot in Shots.Where(kv=>kv.Value==v).Select(kv=>kv.Key).ToArray())Shots.Remove(shot);
        foreach(var key in ImpactScopes.Where(kv=>kv.Value==v).Select(kv=>kv.Key).ToArray())ImpactScopes.Remove(key);
    }
    private static void FlushOutcomes(State s,Weapon w)
    {
        var ws=s.Weapon(w);
        while(ws.Outcomes.TryGetValue(ws.NextOutcome,out var hit)&&hit.HasValue)
        {
            ws.Outcomes.Remove(ws.NextOutcome++);
            if(s.Key=="AR04-1"||s.Key=="AR10-1")
            {ws.Consecutive=hit.Value?ws.Consecutive+1:0;
             if(s.Key=="AR04-1"&&ws.Consecutive>=4||s.Key=="AR10-1"&&ws.Consecutive>=3)ws.Earned=true;}
        }
    }
    internal static void ObserveMode(State s,Weapon w)
    {var ws=s.Weapon(w);var mode=LegendaryFireModePatch.LogicalMode(w);if(ws.Mode.Length>0&&ws.Mode!=mode&&s.Key=="AR27-2")ws.Selector=true;ws.Mode=mode;}
    internal static void Draw(State s,Weapon w)
    {
        var previous=s.LastWeapon;s.LastWeapon=w.Id;var ws=s.Weapon(w);
        if(s.Key=="TR15-2"&&previous.Length>0&&previous!=w.Id)ws.Selector=true;
        s.DrawCharge=1f;
        if(s.Key=="TR10-1"&&s.EmptyPrimary&&Secondary(s.Player,w)){s.EmptyPrimary=false;ws.Charges=3;ws.DrawCharge=2f;}
    }
    internal static void Check(Player p,Weapon w,int bit)
    {
        var s=Get(p);if(s is null)return;var ws=s.Weapon(w);
        if(bit==1)
        {
            if(s.Key is "TR03-1" or "TR27-2" or "AR17-1")ws.Chamber=true;
            if(s.Key=="AR24-1"&&ChestRigMedical.HasDressing(s))s.DressingArmed=true;
        }
        if(bit==2)
        {
            if(s.Key=="TR04-2"&&!BackpackRuntime.Daytime()&&s.Ready){ArmReload(s,1.75f);s.Cooldown(30f);}
            if(s.Key=="TR14-2"&&s.Ready){RestoreArms(p,.4f);s.Cooldown(20f);}
            if(s.Key=="TR26-2"&&s.PatrolCharged){BackpackRuntime.RestoreLeg(p,.5f);s.PatrolCharged=false;s.WalkDistance=0;EffectStatusModel.Notify(p.ProfileId,"rig:"+s.Key,s.Roll.Definition.ShortName,Time.time,"Leg Stamina recovery");}
        }
        Refresh(p);
    }
    internal static void ArmReload(State s,float factor,Weapon? weapon=null)
    {
        if(weapon is null)s.ReloadBonus=factor;else s.Weapon(weapon).ReloadBonus=factor;
        s.Arm(weapon is null?"reload":"reload:"+weapon.Id,5f);
    }
    internal static float ReloadSpeed(State s,Weapon w)
    {
        var factor=s.Roll.ChestRig.Reload*Math.Max(s.Active("reload")?s.ReloadBonus:1f,s.Active("reload:"+w.Id)?s.Weapon(w).ReloadBonus:1f);
        if(s.Key=="TR06-1"&&s.Reloading&&s.EmergencyReloading)factor*=2f;
        if(s.Key=="AR05-1"&&LegendaryRuntime.RemainingAmmo(w)<=LegendaryRuntime.Capacity(w)*.25f)factor*=1.8f;
        if(s.Key=="AR06-2"&&BlackArms(s.Player))factor*=1.35f;
        if(Knight(s))factor*=1.1f;
        if(s.Key=="AR26-2"&&Health(s.Player)<.5f)factor*=1.5f;
        if(s.Key=="TR18-1"&&LegendaryRuntime.Capacity(w)>=30)factor*=1.4f;
        return factor;
    }
    internal static void ReloadCompleted(Player p,Weapon w,bool emergency)
    {
        var s=Get(p);if(s is null)return;
        var carriedCharges=s.Weapon(w).Charges;var carriedShot=s.Weapon(w).Selector;
        s.Weapon(w).Reload();
        if(s.Key is "TR10-1" or "TR30-1" or "TR11-2")s.Weapon(w).Charges=carriedCharges;
        if(s.Key is "TR15-2" or "AR27-2")s.Weapon(w).Selector=carriedShot;
        s.DressingArmed=false;s.Reloading=false;s.EmergencyReloading=false;s.Emergency=emergency;
        if(s.Key=="TR06-1"&&emergency)s.Arm("clutch",7f);
        if(s.Key=="AR17-2"&&emergency)s.Weapon(w).Charges=10;
        if(s.Key=="TR30-1")
        {
            var other=Primary(p,w)?p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem as Weapon:
                p.InventoryController.Inventory.Equipment.GetAllItems().OfType<Weapon>().FirstOrDefault(x=>x.Id==s.LastPrimary);
            if(other is not null)s.Weapon(other).Charges=3;
        }
        Refresh(p);
    }
    internal static float Value(Player p,string kind,bool observed=false)
    {
        var s=Get(p,observed);if(s is null)return 1f;
        if(kind=="aimCost"&&s.Key=="AR24-2"&&Health(p,EBodyPart.Chest)<.35f&&p.HandsController is Player.FirearmController{IsTriggerPressed:true})return 0f;
        if(kind=="aimCost")return s.Roll.ChestRig.AimCost*(s.Key=="TR08-2"&&s.StationarySeconds>=3f?.5f:1f)*(s.Key=="AR22-2"&&s.Active("stamina")?.5f:1f);
        if(kind=="armRecovery")return s.Key=="AR20-1"?1.25f:1f;
        if(kind=="sprintCost")return s.Key=="AR16-2"&&ChestRigLegSprintPatch.Fractured(p)?.7f:1f;
        if(kind=="legRecovery")return (s.Key=="AR20-1"?1.25f:1f)*(s.Key=="AR16-1"&&p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem is null?1.7f:1f)*(s.Key=="AR22-2"&&s.Active("stamina")?1.6f:1f);
        return 1f;
    }
    internal static float Movement(Player p)
    {
        var s=Get(p);if(s is null)return 1f;var factor=s.Roll.ChestRig.Movement;var sprint=p.MovementContext.IsSprintEnabled;var aim=p.HandsController?.IsAiming==true;
        if(Knight(s))factor*=1.1f;
        if(s.Key=="TR02-2"&&s.Active("workout")&&sprint)factor*=1.25f;
        if(s.Key=="TR13-1"&&ThreeFirearms(p))factor*=1.1f;
        if(s.Key=="TR07-1"&&s.Active("grenade")&&sprint)factor*=1.2f;
        if(s.Key=="TR22-1"&&s.Active("loot")&&sprint)factor*=1.3f;
        if(s.Key=="TR33-1"&&aim&&AdvancedMeleeRuntime.Crouched(p))factor*=1.2f;
        if(s.Key=="AR15-2"&&s.Active("door")&&aim)factor*=1.35f;
        if(s.Key=="AR16-1"&&sprint&&p.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem is null)factor*=1.1f;
        if(s.Key=="AR23-2"&&s.Active("escape")&&sprint)factor*=1.3f;
        if(s.Key=="AR26-2"&&Health(p)<.5f)factor*=.75f;
        return factor;
    }
    internal static float JumpHeight(Player p)
    {
        var s=Get(p);
        if(s?.Key=="TR02-2"&&s.Active("workout"))return 1.25f;
        return s?.Key=="AR25-2"?1.35f:1f;
    }
    internal static float Noise(Player p,string kind)
    {
        var s=Get(p);if(s is null)return 1f;
        if(s.Key=="TR19-2")return .5f;
        if(s.Key=="TR03-1"&&p.HandsController?.Item is Weapon w&&s.Weapon(w).Chamber&&kind is "movement" or "item" or "inventory")return kind=="movement"&&p.MovementContext.IsSprintEnabled?1f:.6f;
        if(s.Key=="TR14-1"&&AdvancedMeleeRuntime.Crouched(p)&&kind=="item"&&(s.Reloading||Native.Get(Native.Get(p.HandsController,"CurrentOperation"),"EutilityType_0")?.ToString() is "CheckMagazine" or "CheckChamber"))return .3f;
        if(s.Key=="TR33-2"&&p.HandsController?.Item is Weapon suppressed&&suppressed.Mods.Any(m=>m is SilencerItemClass))return kind=="movement"&&!p.MovementContext.IsSprintEnabled?.6f:kind=="item"&&s.Reloading?.4f:1f;
        if(s.Key=="AR08-2"&&s.Active("quiet")&&kind=="movement")return .25f;
        return 1f;
    }
    internal static IEnumerable<EffectStatusModel.Status> Statuses(Player? player)
    {
        if(player?.HealthController?.IsAlive!=true)yield break;
        var s=Get(player);
        if(Records.TryGetValue(player.ProfileId,out var ledger))
            foreach(var pair in ledger.Until.Where(kv=>kv.Key.StartsWith("cd:",StringComparison.Ordinal)&&kv.Value>Time.time))
            {
                var key=pair.Key.Substring(3);
                if(s?.Key!=key&&Names.TryGetValue(key,out var name))
                    yield return new EffectStatusModel.Status("rig:"+key,name,false,0f,pair.Value-Time.time);
            }
        if(s is null)yield break;
        var remaining=s.Ledger.Until.Where(kv=>kv.Key.StartsWith(s.Key+":",StringComparison.Ordinal)&&kv.Value>Time.time).Select(kv=>kv.Value-Time.time).Where(v=>v<100000f).DefaultIfEmpty(0f).Max();
        var cooldown=EffectStatusModel.Remaining(s.Ledger.Until,"cd:"+s.Key,Time.time);
        var held=s.Player.HandsController?.Item as Weapon;
        var ws=held is null?null:s.Weapon(held);
        var charges=(s.DressingArmed?1:0)+s.HandloadShots+s.GlobalShots+(ws?.Charges??0)+(ws?.Selector==true?1:0);
        var active=remaining>0||charges>0||s.DrawCharge>1||s.CycleCharge>1||s.HitDamage>1||s.PatrolCharged||s.BreathArmed||s.Active("static");
        if(held is not null)
        {
            var ammo=held.GetCurrentMagazine()?.Cartridges.Items.FirstOrDefault() as Ammo;
            var a=Preview(s,held,ammo);
            if(LegendaryRuntime.RemainingAmmo(held)>0)active|=a.Damage!=1||a.Penetration!=1||a.Recoil!=1||a.FireRate!=1||a.Velocity!=1||a.Hip!=1||a.DoubleExplosive;
            active|=s.Key=="TR08-2"&&s.StationarySeconds>=3f||s.Key=="TR29-1"&&s.GlassReady&&Magnified(player)||s.Key=="TR18-1"&&LegendaryRuntime.Capacity(held)>=30;
            active|=s.Key=="TR16-2"&&Magnified(player)||s.Key=="TR33-2"&&held.Mods.Any(m=>m is SilencerItemClass);
            active|=s.Key=="AR05-1"&&s.Reloading&&LegendaryRuntime.RemainingAmmo(held)<=LegendaryRuntime.Capacity(held)*.25f;
            active|=s.Key=="TR06-1"&&s.Reloading&&s.EmergencyReloading;
        }
        active|=Knight(s)||s.Key=="AR18-2"&&BlackParts(s.Player)>0||s.Key=="AR24-2"&&Health(s.Player,EBodyPart.Chest)<.35f||s.Key=="AR26-2"&&Health(s.Player)<.5f||s.Key=="AR06-2"&&BlackArms(player)||s.Key=="AR11-1"&&s.Reloading;
        active|=s.Key=="TR13-1"&&ThreeFirearms(player)||s.Key=="TR14-1"&&AdvancedMeleeRuntime.Crouched(player);
        active|=ServiceWindow.Active(player);
        active|=s.Key=="TR33-1"&&held is not null&&player.HandsController?.IsAiming==true&&AdvancedMeleeRuntime.Crouched(player);
        active|=s.Key=="AR01-2"&&s.StillSeconds>=3f||s.Key=="AR14-2"&&player.HandsController is Player.BaseKnifeController;
        active|=s.Key=="AR16-1"&&player.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem is null;
        active|=s.Key=="AR16-2"&&ChestRigLegSprintPatch.Fractured(player);
        if(s.Key=="AR05-2")
        {var bleed=s.Bleeds.Where(b=>b.Value>Time.time&&Native.Get(b.Key,"Existing") is true).Select(b=>b.Value-Time.time).DefaultIfEmpty(0f).Max();remaining=Math.Max(remaining,bleed);active|=bleed>0;}
        if(s.Key=="AR04-2")active|=ChestRigMedical.Uses.Values.Any(u=>u.State==s&&u.Bleeds.Any(b=>Native.Get(b,"Existing") is true&&ChestRigMedical.PauseBleed(s.Player.ActiveHealthController,u.Part,b.GetType().Name=="HeavyBleeding"?EDamageType.HeavyBleeding:EDamageType.LightBleeding)));
        if(s.Key=="AR11-2")
        {var flash=ChestRigCombat.FlashRemaining(player);remaining=Math.Max(remaining,flash);active|=flash>0;}
        if(active||cooldown>0)yield return new EffectStatusModel.Status("rig:"+s.Key,s.Roll.Definition.ShortName,active,remaining,cooldown,charges>0?charges+" charges":s.CycleCharge>1?"Next bolt cycle ready":s.HitDamage>1?"Next shot ready":"");
    }
}
