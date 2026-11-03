using System;
using System.Collections;
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

internal static class BackpackRuntime
{
    internal sealed class Ledger
    {
        public readonly Dictionary<string, float> Until = new(StringComparer.Ordinal);
        public readonly HashSet<string> Containers = new(StringComparer.Ordinal), Rewards = new(StringComparer.Ordinal), Templates = new(StringComparer.Ordinal), Categories = new(StringComparer.Ordinal), NewLoot = new(StringComparer.Ordinal), Collected = new(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Distance = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Checklist = new(StringComparer.Ordinal);
        public float LastFood = -1000, LastDrink = -1000, LastTraversal = -1000;
        public string Traversal = "";
        public int Gifts;
        public bool Ready(string key) => !Until.TryGetValue("cd:" + key, out var t) || Time.time >= t;
        public bool Active(string key) => Until.TryGetValue(key, out var t) && Time.time < t;
        public void Arm(string key, float seconds) => Until[key] = Time.time + seconds;
        public void Cooldown(string key, float seconds) => Arm("cd:" + key, seconds);
    }
    internal sealed class State
    {
        public Player Player = null!;
        public Item Bag = null!;
        public RarityResolver.ShotRoll Roll = null!;
        public Ledger Record = null!;
        public string Key => Roll.Definition.BackpackEffect!.Key;
        public Vector3 Previous;
        public float LastTick, SprintSeconds, SprintDistance, ProneSeconds;
        public float JumpHorizontal, LastJump;
        public float LastPhysicalWeight = float.NaN;
        public bool PreviousSprint, WasAirborne, Jumped, JumpSafe, JumpCharged, ColdCharge, SprintCostObserved;
        public readonly HashSet<string> Contents = new(StringComparer.Ordinal);
    }
    internal static readonly Dictionary<Player, State> States = new();
    private static readonly HashSet<Player> EquipmentChanges = new();
    internal static readonly Dictionary<string, Ledger> Records = new(StringComparer.Ordinal);
    internal static readonly Dictionary<Compute<float>, (Player Player, string Kind)> Values = new();
    internal static readonly Dictionary<Stamina, Player> StaminaOwners = new();
    private static GameWorld? World;
    private static float ScanTime;
    internal static bool Resetting;
    private static readonly Dictionary<Player, ActiveHealthController.Effect> MilkPainkillers = new();
    private static readonly MethodInfo PainkillerFactory = typeof(ActiveHealthController.Effect).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 8)
        .MakeGenericMethod(typeof(ActiveHealthController).GetNestedType("PainKiller", BindingFlags.Public | BindingFlags.NonPublic)!);
    internal static Item? Worn(Player? p) => p?.InventoryController?.Inventory?.Equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
    internal static State? Get(Player? p)
    {
        if (Resetting || !PlayerRefresh.CanObserve(p)) return null;
        var bag = Worn(p);
        if (States.TryGetValue(p, out var previous) && previous.Bag.Id != bag?.Id)
        {
            // Observe removal even when another Legendary bag immediately replaces it.
            // Keep raid-wide cooldowns/progress, but discard this equip's charges and steps.
            previous.Record.Checklist.Clear();
            States.Remove(p);
            EquipmentChanges.Add(p);
        }
        if (bag is null || !RarityResolver.TryGetDefinition(bag, out var d) || d.BackpackEffect is null) return null;
        if (States.TryGetValue(p, out var s) && s.Bag.Id == bag.Id) return s;
        var record = Records.TryGetValue(p.ProfileId, out var r) ? r : Records[p.ProfileId] = new Ledger();
        s = new State { Player = p, Bag = bag, Roll = RarityResolver.Resolve(bag)!, Record = record, Previous = p.Position, LastTick = Time.time };
        foreach (var item in bag.GetAllItems()) s.Contents.Add(item.Id);
        States[p] = s;
        return s;
    }
    internal static bool Is(Player? p, string key) => Get(p)?.Key == key;
    internal static Player? Actor(object? value)
    {
        if (value is Player p) return p;
        if (value is ActiveHealthController h) return h.Player;
        if (value is MovementContext m) return MeleeRuntime.Owner(m);
        if (value is Stamina stamina) return StaminaOwners.TryGetValue(stamina, out var owner) ? owner : null;
        if (value is null) return null;
        var direct = Native.Get(value, "Player") ?? Native.Get(value, "_player") ?? Native.Get(value, "Player_0") ?? Native.Get(value, "player_0");
        if (direct is Player player) return player;
        var profile = Native.Get(value, "ProfileId")?.ToString() ?? Native.Get(value, "Id")?.ToString();
        return World?.RegisteredPlayers.OfType<Player>().FirstOrDefault(x => x.ProfileId == profile);
    }
    internal static void Reset()
    {
        Resetting = true;
        try
        {
        // The old world owns its native health/physical objects. Discard our
        // references without calling back into players during world destruction.
        MilkPainkillers.Clear();
        States.Clear(); EquipmentChanges.Clear(); Records.Clear(); Values.Clear(); StaminaOwners.Clear(); World = null; ScanTime = 0;
        BackpackNative.Reset();
        }
        finally { Resetting = false; }
    }
    internal static void Maintain(GameWorld? world)
    {
        if (World != world) { Reset(); World = world; if (world is not null) BackpackRaidReport.BeginRaid(); }
        // Free-grenade hand cleanup must survive unequipping Fireball.
        BackpackGrenadeAction.Maintain(world);
        if (world is null || Time.time < ScanTime) return;
        ScanTime = Time.time + .05f;
        BackpackNative.MaintainWorld();
        var players = world.RegisteredPlayers.OfType<Player>().ToArray();
        foreach (var old in States.Keys.Where(p => !PlayerRefresh.CanObserve(p) || Array.IndexOf(players, p) < 0).ToArray())
        { States.Remove(old); EquipmentChanges.Remove(old); MilkPainkillers.Remove(old); }
        foreach (var player in players)
        {
            if (!PlayerRefresh.CanObserve(player)) continue;
            Register(player);
            var s = Get(player);
            var equipmentChanged = EquipmentChanges.Remove(player);
            if (s?.Key != "BP37-2" && MilkPainkillers.TryGetValue(player, out var milkPainkiller))
            { milkPainkiller.ForceRemove(); MilkPainkillers.Remove(player); }
            if (s is null)
            {
                if (equipmentChanged)
                {
                    BackpackPresentation.Restore(player); PlayerRefresh.Weight(player, "backpack removal");
                    if (player.HandsController is Player.FirearmController firearm) { firearm.RecalculateErgonomic(); player.ProceduralWeaponAnimation.UpdateWeaponVariables(); }
                }
                if (world.MainPlayer == player) BackpackRaidReport.Capture(player);
                continue;
            }
            if (equipmentChanged && player.HandsController is Player.FirearmController equippedFirearm)
            { equippedFirearm.RecalculateErgonomic(); player.ProceduralWeaponAnimation.UpdateWeaponVariables(); }
            if (s.Key == "BP07-1") BackpackGrenadeAction.Preload(world);
            var dt = Math.Min(.25f, Math.Max(0f, Time.time - s.LastTick)); s.LastTick = Time.time;
            var motion = player.Position - s.Previous; s.Previous = player.Position;
            var distance = new Vector2(motion.x, motion.z).magnitude;
            if (distance > 15f || player.InputDirection.sqrMagnitude < .0001f) distance = 0f;
            var moving = player.InputDirection.sqrMagnitude > .0001f;
            var sprint = player.MovementContext.IsSprintEnabled && moving && player.Physical.Stamina.Current > 0f;
            var previousSprintSeconds = s.SprintSeconds;
            s.SprintSeconds = sprint ? s.SprintSeconds + dt : 0f;
            s.SprintDistance = sprint ? s.SprintDistance + distance : 0f;
            if (s.Key == "BP16-1" && s.SprintDistance >= 50f) { s.JumpCharged = true; s.SprintDistance -= 50f; }
            if (s.Key == "BP34-1" && sprint) s.Record.Arm("walkLockout", 10f);
            if (s.Key == "BP31-2") s.Record.Distance[s.Key] = (s.Record.Distance.TryGetValue(s.Key, out var d) ? d : 0f) + distance;
            s.ProneSeconds = player.MovementContext.IsInPronePose ? s.ProneSeconds + dt : 0f;
            if (s.Key == "BP14-3" && s.ProneSeconds >= 5f) s.ColdCharge = true;
            if (s.Key == "BP03-3" && BackpackRules.FreightBrake(s.PreviousSprint, previousSprintSeconds, sprint) && s.Record.Ready(s.Key))
            { RestoreLeg(player, .2f); s.Record.Cooldown(s.Key, 30f); }
            if (!sprint) s.SprintCostObserved = false;
            s.PreviousSprint = sprint;
            // The native ground-check hook observes flight/landing in order with
            // HandleFall. Polling here could clear protection before that call.
            if (s.Key == "BP43-3" && player.Physical.Stamina.Current <= .01f && s.Record.Ready(s.Key))
            { RestoreLeg(player, .75f); s.Record.Cooldown(s.Key, 180f); }
            if (s.Key == "BP37-2" && s.Record.Active("milk"))
            {
                if (!MilkPainkillers.TryGetValue(player, out var painkiller) || !painkiller.Existing) Painkiller(player, s.Record.Until["milk"] - Time.time);
                MeleeRuntime.Heal(player, dt); player.ActiveHealthController.ChangeEnergy(dt); player.ActiveHealthController.ChangeHydration(dt);
            }
            ObserveContents(s);
            if (world.MainPlayer == player) BackpackRaidReport.Capture(player);
            BackpackNative.Maintain(s, dt);
            // Physical limits and cache-backed values must follow equipment and conditions,
            // including the end of a timer, rather than updating after the next shot.
            var effectiveWeight = Weight(player, player.InventoryController.Inventory.TotalWeight.Value);
            if (!Mathf.Approximately(effectiveWeight, s.LastPhysicalWeight) || float.IsNaN(s.LastPhysicalWeight))
            { s.LastPhysicalWeight = effectiveWeight; PlayerRefresh.Weight(player, "backpack weight"); }
        }
    }
    private static void Register(Player p)
    {
        var physical = p.Physical;
        if (physical is null) return;
        StaminaOwners[physical.Stamina] = p; StaminaOwners[physical.HandsStamina] = p;
        Values[physical.Stamina.TotalCapacity] = (p, "legCapacity"); Values[physical.HandsStamina.TotalCapacity] = (p, "armCapacity");
        Values[physical.Stamina.SelfRestoration] = (p, "legRecovery");
        Values[physical.HandsStamina.SelfRestoration] = (p, "armRecovery");
        Values[physical.Oxygen.TotalCapacity] = (p, "oxygenCapacity");
        if (physical is Physical playerPhysical)
        {
            Values[playerPhysical.Consumptions[Physical.EConsumptionType.Aim].Delta] = (p, "aimCost");
            Values[playerPhysical.Consumptions[Physical.EConsumptionType.Sprint].Delta] = (p, "sprintCost");
        }
    }
    private static void ObserveContents(State s)
    {
        var current = s.Bag.GetAllItems().Where(i => i.Id != s.Bag.Id).ToArray();
        foreach (var item in current)
        {
            if (s.Contents.Contains(item.Id)) continue;
            // Inventory moves within the player's own equipment do not count as new loot.
            if (!s.Record.NewLoot.Contains(item.Id) || !s.Record.Collected.Add(s.Key + ":" + item.Id)) continue;
            if (s.Key == "BP26-1") s.Record.Templates.Add(item.StringTemplateId);
            if (s.Key == "BP24-2" && item.SpawnedInSession && BackpackNative.BarterCategory(item) is { } category) s.Record.Categories.Add(category);
        }
        s.Contents.Clear(); foreach (var item in current) s.Contents.Add(item.Id);
    }
    internal static float Movement(Player p)
    {
        var s = Get(p); if (s is null) return 1f;
        var sprint = p.MovementContext.IsSprintEnabled;
        switch (s.Key)
        {
            case "BP02-1": return BackpackRules.RushHour(sprint);
            case "BP07-2": return 1f;
            case "BP07-3": return sprint && BlackLeg(p) ? 1.2f : 1f;
            case "BP17-1": return s.Record.Active("fallSpeed") ? 1.15f : 1f;
            case "BP22-3": return Woods(p) ? 1.2f : 1f;
            case "BP24-1": return Daytime() ? 1.1f : 1f;
            case "BP28-3": return p.MovementContext.IsInPronePose ? 1.5f : 1f;
            case "BP31-2": return Milestone(s) ? 1.15f : 1f;
            case "BP34-1": return BackpackRules.Pilgrimage(sprint, EffectStatusModel.Remaining(s.Record.Until, "walkLockout", Time.time));
            case "BP44-3": return sprint ? 1.4f : 1.3f;
            case "BP45-2": return MainGridFull(s.Bag) ? 1.15f : 1f;
            default: return 1f;
        }
    }
    internal static float MotionFactor(Player p)
    {
        return Movement(p) * (Get(p)?.Roll.Backpack.Movement ?? 1f);
    }
    internal static float Value(Player p, string kind)
    {
        var s = Get(p); if (s is null) return 1f;
        if (kind is "legCapacity" or "armCapacity") return s.Roll.Backpack.StaminaCapacity * (s.Key == "BP22-3" && Woods(p) ? 1.25f : 1f) * (kind == "legCapacity" ? BackpackRules.LegCapacity(s.Key) : 1f);
        if (kind == "oxygenCapacity") return s.Key == "BP22-3" && Woods(p) ? 1.5f : 1f;
        if (kind == "legRecovery") return s.Roll.Backpack.LegRecovery;
        if (kind == "aimCost") return s.Roll.Backpack.AimCost * (s.Key == "BP30-1" && AimingLoadedPrimary(p) ? .6f : 1f);
        if (kind == "sprintCost")
        {
            var factor = s.Roll.Backpack.SprintCost;
            factor *= BackpackRules.SprintCost(s.Key);
            if (s.Key == "BP12-1" && s.Record.Active("lootSprint")) factor *= .5f;
            if (s.Key == "BP40-2" && (s.Record.Active("traversal:sprint") || TraversalFactor(p, "sprint") < 1f)) factor *= .8f;
            return factor;
        }
        return 1f;
    }
    internal static float Weight(Player p,float original) => ChestRigRuntime.Weight(p,BagWeight(p,original));
    internal static float Noise(Player p,string kind) => BagNoise(p,kind)*ChestRigRuntime.Noise(p,kind);
    internal static float CarriedWeightFactor(Player p,Item item)
    {
        var s=Get(p);if(s is null||!BackpackNative.InBag(item,s.Bag))return 1f;
        return item.Id==s.Bag.Id?s.Roll.Backpack.FrameWeight:s.Roll.Backpack.ContentsWeight;
    }
    private static float BagWeight(Player p, float original)
    {
        var s = Get(p); if (s is null) return original;
        var empty = s.Bag.Weight;
        var contents = Math.Max(0f, s.Bag.TotalWeight - empty);
        var cargo = s.Roll.Backpack.ContentsWeight;
        var adjusted = original - empty * (1f - s.Roll.Backpack.FrameWeight) - contents * (1f - cargo);
        return Math.Max(0f, adjusted);
    }
    private static float BagNoise(Player p, string kind)
    {
        var s = Get(p); if (s is null) return 1f;
        var factor = kind is "movement" or "foliage" ? s.Roll.Backpack.MovementNoise : 1f;
        if (s.Key == "BP37-1") factor *= 1.75f;
        factor *= BackpackRules.SprintCost(s.Key);
        if (s.Key == "BP27-2" && p.HandsController is Player.FirearmController f && f.Weapon.WeapClass == "sniperRifle" && kind is "movement" or "foliage") factor *= .25f;
        if (s.Key == "BP10-2" && kind == "foliage") factor *= .3f;
        if (s.Key == "BP13-1" && p.InputDirection.sqrMagnitude < .0001f && kind == "medical") factor *= .3f;
        if (s.Key == "BP45-1" && kind == "inventory") factor *= .5f;
        return factor;
    }
    internal static void RestoreLeg(Player p, float fraction) { var st = p.Physical.Stamina; st.UpdateStamina(Math.Min(st.TotalCapacity, st.Current + st.TotalCapacity * fraction)); }
    internal static bool BlackLeg(Player p) => p.ActiveHealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg) || p.ActiveHealthController.IsBodyPartDestroyed(EBodyPart.RightLeg);
    internal static bool Woods(Player p)
    {
        var id = Singleton<GameWorld>.Instance?.LocationId;
        if (string.IsNullOrEmpty(id)) id = p.Location;
        return BackpackRules.IsWoods(id);
    }
    internal static bool Daytime()
    {
        var clock = Singleton<GameWorld>.Instance?.GameDateTime;
        var date = clock is null ? DateTime.MinValue : (DateTime)(Native.Call(clock, "Calculate") ?? DateTime.MinValue);
        return date.Hour >= 6 && date.Hour < 20;
    }
    internal static bool Milestone(State s) => s.Record.Distance.TryGetValue(s.Key, out var d) && d >= 1000f;
    internal static bool LoadedPrimaries(Player p) => new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon }.All(slot => p.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem is Weapon w && (w.GetCurrentMagazineCount() > 0 || w.Chambers.Any(c => c.ContainedItem is Ammo a && !a.IsUsed)));
    internal static bool AimingLoadedPrimary(Player p) => LoadedPrimaries(p) && p.HandsController is Player.FirearmController held &&
        new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon }.Any(slot => p.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem?.Id == held.Weapon.Id);
    internal static void Acquired(Player p, Item source, Item received, bool external)
    {
        var s = Get(p); if (s is null) return;
        if (external || s.Record.NewLoot.Contains(source.Id))
            foreach (var item in received.GetAllItems()) s.Record.NewLoot.Add(item.Id);
        if (external && s.Key == "BP12-1") s.Record.Arm("lootSprint", 15f);
        // Count direct-to-bag loot even if equipping this very bag created State
        // with its contents already present. Other moves are handled by the poll.
        foreach (var item in received.GetAllItems())
        {
            if (!s.Record.NewLoot.Contains(item.Id) || !BackpackNative.InBag(item, s.Bag) || !s.Record.Collected.Add(s.Key + ":" + item.Id)) continue;
            if (s.Key == "BP26-1") s.Record.Templates.Add(item.StringTemplateId);
            if (s.Key == "BP24-2" && item.SpawnedInSession && BackpackNative.BarterCategory(item) is { } category) s.Record.Categories.Add(category);
        }
    }
    internal static bool MainGridFull(Item bag)
    {
        var grid = Native.Items(Native.Get(bag, "Grids")).FirstOrDefault();
        if (grid is null) return false;
        var free = Native.Call(grid, "GetFreeSpace") ?? Native.Get(grid, "FreeSpace");
        if (free is not null) return Convert.ToInt32(free) == 0;
        var capacity = Native.Number(grid, "GridWidth") * Native.Number(grid, "GridHeight");
        var cells = Native.Items(Native.Get(grid, "Items")).OfType<Item>().Sum(i => { var size = i.CalculateCellSize(); return size.X * size.Y; });
        return capacity > 0f && cells >= capacity;
    }
    internal static void Searched(Player p, Item item)
    {
        var s = Get(p); if (s?.Key == "BP01-3") s.Record.Containers.Add(item.Id);
    }
    internal static void Check(Player p, string weaponId, int bit)
    {
        var s = Get(p); if (s?.Key != "BP41-3") return;
        var value = s.Record.Checklist.TryGetValue(weaponId, out var old) ? old | bit : bit;
        if (value == 7) { s.Record.Arm("firstItem", 180f); s.Record.Checklist.Clear(); }
        else s.Record.Checklist[weaponId] = value;
    }
    internal static void Traversal(Player p, string action)
    {
        var s = Get(p); if (s?.Key != "BP40-2") return;
        if (s.Record.Traversal != action && Time.time - s.Record.LastTraversal <= 5f) s.Record.Arm("traversal:" + action, action == "sprint" ? 5f : .5f);
        s.Record.Traversal = action; s.Record.LastTraversal = Time.time;
    }
    internal static float TraversalFactor(Player p, string action)
    {
        var s = Get(p);
        return s?.Key == "BP40-2" && s.Record.Traversal.Length > 0 && s.Record.Traversal != action &&
            Time.time - s.Record.LastTraversal <= 5f ? .8f : 1f;
    }
    internal static void Jump(Player p)
    {
        var s = Get(p); if (s is null || !p.MovementContext.IsGrounded) return;
        s.Jumped = true; s.LastJump = Time.time;
        if (s.Key == "BP16-1" && s.JumpCharged) { s.JumpSafe = true; s.JumpCharged = false; s.JumpHorizontal = .9f; }
        if (s.Key == "BP39-1" && p.InputDirection.y > .1f && p.HandsController is Player.FirearmController f && f.Weapon.WeapClass == "pistol" && new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon }.All(slot => p.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem is null)) s.JumpHorizontal = 1.4f;
    }
    internal static State? GroundCheck(MovementContext movement)
    {
        var s = Get(Actor(movement)); if (s is null) return null;
        if (!movement.IsGrounded) { s.WasAirborne = true; return null; }
        return s.WasAirborne ? s : null;
    }
    internal static void Landed(State? s)
    {
        if (s is null) return;
        s.WasAirborne = false; s.JumpSafe = false; s.Jumped = false; s.JumpHorizontal = 0f;
    }
    internal static void Painkiller(Player p, float seconds)
    {
        if (MilkPainkillers.TryGetValue(p, out var effect) && effect.Existing) { effect.AddWorkTime(seconds, true); return; }
        Action<ActiveHealthController.Effect> initialize = e => Native.Call(e, "StoreValues", "575146b724597720a27126d5", seconds);
        effect = (ActiveHealthController.Effect)PainkillerFactory.Invoke(null, new object?[] { p.ActiveHealthController, EBodyPart.Head, 0f, seconds, 0f, 1f, Native.Get(p.ActiveHealthController, "UpdateTime"), initialize })!;
        MilkPainkillers[p] = effect;
    }
}

internal static class Native
{
    private static readonly Dictionary<(Type, string), MemberInfo?> Members = new();
    internal static object? Get(object? obj, string name)
    {
        if (obj is null) return null;
        var key = (obj.GetType(), name);
        if (!Members.TryGetValue(key, out var m)) Members[key] = m = (MemberInfo?)AccessTools.Property(key.Item1, name) ?? AccessTools.Field(key.Item1, name);
        return m is PropertyInfo p ? p.GetValue(obj) : m is FieldInfo f ? f.GetValue(obj) : null;
    }
    internal static void Set(object obj, string name, object? value)
    {
        var p = AccessTools.Property(obj.GetType(), name);
        if (p?.SetMethod is not null) p.SetValue(obj, value); else AccessTools.Field(obj.GetType(), name)?.SetValue(obj, value);
    }
    internal static object? Call(object obj, string name, params object?[] args)
    {
        var method = AccessTools.GetDeclaredMethods(obj.GetType()).Concat(obj.GetType().GetMethods()).FirstOrDefault(m => m.Name == name && !m.ContainsGenericParameters && m.GetParameters().Length == args.Length);
        return method?.Invoke(obj, args);
    }
    internal static T? Component<T>(Item item) where T : class
    {
        var method = typeof(Item).GetMethods().First(m => m.Name == "GetItemComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        return method.MakeGenericMethod(typeof(T)).Invoke(item, null) as T;
    }
    internal static float Number(object obj, string name) => Get(obj, name) is { } n ? Convert.ToSingle(n) : 0f;
    internal static IEnumerable<object> Items(object? value) => value is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();
}
