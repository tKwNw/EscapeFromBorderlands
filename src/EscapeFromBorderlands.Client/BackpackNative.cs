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
internal static class BackpackNative
{
    internal sealed class Use
    {
        public Player Player = null!; public Item Item = null!; public EBodyPart Part;
        public string? SourceBagId;
        public string StatusKey = "", StatusName = "";
        public bool HeavyBefore;
        public float BeforeHp, Resource, Factor = 1f;
        public float EnergyBonus, HydrationBonus;
        // The item may have been consumed or moved by completion. Match its
        // captured source bag, rather than checking its current address.
        public bool From(BackpackRuntime.State s) => Player == s.Player && SourceBagId is not null && SourceBagId == s.Bag.Id;
    }
    internal static readonly Dictionary<object, Use> Uses = new();
    internal static readonly Dictionary<string, Use> ItemUses = new(StringComparer.Ordinal);
    internal sealed class Drop
    {
        public Player Player = null!; public Item Item = null!; public BackpackRuntime.State State = null!; public Vector3 Position; public float Expires;
    }
    internal static readonly List<Drop> Drops = new();
    internal static void Reset() { Uses.Clear(); ItemUses.Clear(); BackpackMedicalRestoration.Reset(); BackpackLootTransfers.Reset(); Drops.Clear(); BackpackTripwire.Recover.Clear(); BackpackAudio.Reset(); BackpackPresentation.Reset(); BackpackGrenadeAction.Reset(); }
    internal static bool InBag(Item? item, Item bag) => item is not null && bag.GetAllItems().Any(i => i.Id == item.Id);
    internal static Player? ItemOwner(Item item) => Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => p.InventoryController.Inventory.Equipment.GetAllItems().Any(i => i.Id == item.Id));
    internal static Player? WeaponOwner(Weapon weapon) => Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => p.HandsController?.Item?.Id == weapon.Id);
    internal static float ErgonomicsFactor(BackpackRuntime.State? s) => s is null ? 1f : s.Roll.Backpack.Ergonomics * BackpackRules.Ergonomics(s.Key, s.Key == "BP24-1" && BackpackRuntime.Daytime(), s.Key == "BP31-2" && BackpackRuntime.Milestone(s));
    internal static void Maintain(BackpackRuntime.State s, float dt)
    {
        var p = s.Player;
        BackpackPresentation.Maintain(s);
        if (p.HandsController is Player.FirearmController controller)
        {
            var current = ErgonomicsFactor(s);
            if (s.Record.Until.TryGetValue("ergoFactor", out var old) ? old != current : current != 1f)
            { s.Record.Until["ergoFactor"] = current; controller.RecalculateErgonomic(); p.ProceduralWeaponAnimation.UpdateWeaponVariables(); }
        }
    }
    internal static void MaintainWorld()
    {
        foreach (var drop in Drops.ToArray())
        {
            if (drop.Expires < Time.time || drop.Player.HealthController?.IsAlive != true) { Drops.Remove(drop); continue; }
            if (drop.Player.InventoryController.Inventory.Equipment.GetAllItems().Any(i => i.Id == drop.Item.Id)) continue;
            Drops.Remove(drop); Dropped(drop);
        }
    }

    internal static Use? Begin(object effect)
    {
        var p = BackpackRuntime.Actor(Native.Get(effect, "HealthController"));
        var item = Native.Get(effect, "MedItem") as Item;
        var s = BackpackRuntime.Get(p);
        if (s is null || item is null || p is null) return null;
        var part = Native.Get(effect, "BodyPart") is EBodyPart body ? body : EBodyPart.Chest;
        var u = new Use { Player = p, Item = item, Part = part, SourceBagId = InBag(item, s.Bag) ? s.Bag.Id : null, BeforeHp = p.ActiveHealthController.GetBodyPartHealth(part, false).Current,
            HeavyBefore = p.ActiveHealthController.GetAllEffects().Any(e => e.GetType().Name == "HeavyBleeding" && e.Existing && e.BodyPart == part), Resource = Remaining(item), StatusKey = s.Key, StatusName = s.Roll.Definition.ShortName };
        u.Factor = UseFactor(p, item);
        Uses[effect] = u; ItemUses[item.Id] = u;
        if (s.Key == "BP14-3" && s.ColdCharge) { s.ColdCharge = false; s.ProneSeconds = 0f; }
        return u;
    }
    internal static float Remaining(Item item)
    {
        var food = Native.Component<FoodDrinkComponent>(item);
        if (food is not null) return food.HpPercent;
        var med = Native.Component<MedKitComponent>(item);
        return med?.HpResource ?? 0f;
    }
    internal static float UseFactor(Player p, Item item)
    {
        if (ItemUses.TryGetValue(item.Id, out var active)) return active.Factor;
        var s = BackpackRuntime.Get(p); if (s is null) return 1f;
        var factor = 1f;
        if (s.Key == "BP14-3" && s.ColdCharge) factor /= 1.4f;
        if (s.Key == "BP23-1" && InBag(item, s.Bag) && Native.Component<MedKitComponent>(item)?.HpResourceRate > 0f) factor *= .6f;
        if (s.Key == "BP42-1" && Native.Component<FoodDrinkComponent>(item) is not null) factor *= .5f;
        return factor;
    }
    internal static IEnumerable<EffectStatusModel.Status> UseStatuses(Player player)
    {
        foreach (var pair in Uses.Where(p => p.Value.Player == player && p.Value.Factor < 1f && p.Value.StatusKey.Length > 0 && Native.Get(p.Key, "Bool_2") is not true))
            yield return new EffectStatusModel.Status(pair.Value.StatusKey, pair.Value.StatusName, true, detail: "Item use speed active");
    }
    internal static void End(object effect)
    {
        if (!Uses.TryGetValue(effect, out var u)) return;
        Uses.Remove(effect); ItemUses.Remove(u.Item.Id);
        var s = BackpackRuntime.Get(u.Player);
        if (s is null || Native.Get(effect, "Bool_2") is true) return;
        var fromBag = u.From(s);
        var p = u.Player; var h = p.ActiveHealthController;
        var food = Native.Component<FoodDrinkComponent>(u.Item);
        if (food is not null && Remaining(u.Item) <= .01f)
        {
            if (s.Key == "BP37-2" && u.Item.StringTemplateId == "575146b724597720a27126d5")
            { s.Record.Arm("milk", 180f); BackpackRuntime.Painkiller(p, 180f); }
            if (s.Key == "BP29-2" && fromBag)
            {
                var energy = Native.Get(Native.Component<HealthEffectsComponent>(u.Item), "HealthEffects");
                var isDrink = u.Item.Template.ParentId == "5448e8d64bdc2dce718b4568";
                if (isDrink) s.Record.LastDrink = Time.time; else s.Record.LastFood = Time.time;
                if (Time.time - s.Record.LastDrink <= 60f && Time.time - s.Record.LastFood <= 60f && s.Record.Ready(s.Key))
                { MeleeRuntime.Heal(p, 20f); s.Record.Cooldown(s.Key, 120f); s.Record.LastFood = s.Record.LastDrink = -1000f; }
            }
        }
        if (s.Key == "BP34-2" && fromBag && food is not null && u.Item.Template.ParentId != "5448e8d64bdc2dce718b4568" && p.InputDirection.sqrMagnitude < .0001f && AdvancedMeleeRuntime.Crouched(p) && s.Record.Ready(s.Key))
        { BackpackRuntime.RestoreLeg(p, .3f); s.Record.Cooldown(s.Key, 45f); }
        var healed = h.GetBodyPartHealth(u.Part, false).Current - u.BeforeHp;
        if (s.Key == "BP36-1" && fromBag && healed > 0f && Native.Component<MedKitComponent>(u.Item) is { } med)
        {
            var extra = Math.Min(10f, med.HpResource);
            var missing = Enum.GetValues(typeof(EBodyPart)).Cast<EBodyPart>().Where(b => b != EBodyPart.Common && b != u.Part && !h.IsBodyPartDestroyed(b)).Sum(b => Math.Max(0f, h.GetBodyPartHealth(b, false).Maximum - h.GetBodyPartHealth(b, false).Current));
            extra = Math.Min(extra, missing);
            if (extra > 0f) { med.HpResource -= extra; med.Item.RaiseRefreshEvent(); HealOther(p, u.Part, extra); EffectStatusModel.Notify(p.ProfileId, s.Key, s.Roll.Definition.ShortName, Time.time, "additional healing"); }
        }
        var heavyStopped = u.HeavyBefore && !h.GetAllEffects().Any(e => e.GetType().Name == "HeavyBleeding" && e.Existing && e.BodyPart == u.Part);
        if (heavyStopped && fromBag && s.Key == "BP18-2")
            if (h.GetAllEffects().FirstOrDefault(e => e.GetType().Name == "LightBleeding" && e.Existing) is { } light) { Native.Call(light, "ForceRemove"); EffectStatusModel.Notify(p.ProfileId, s.Key, s.Roll.Definition.ShortName, Time.time, "additional Light Bleed removed"); }
        if (heavyStopped && fromBag && s.Key == "BP23-3") s.Record.Arm("bleed:" + u.Part, 120f);
    }
    private static void HealOther(Player p, EBodyPart excluded, float amount)
    {
        var h = p.ActiveHealthController;
        var parts = Enum.GetValues(typeof(EBodyPart)).Cast<EBodyPart>().Where(b => b != EBodyPart.Common && b != excluded && !h.IsBodyPartDestroyed(b)).Select(b => (Part: b, Missing: Math.Max(0f, h.GetBodyPartHealth(b, false).Maximum - h.GetBodyPartHealth(b, false).Current))).ToArray();
        var total = parts.Sum(v => v.Missing); if (total <= 0f) return;
        foreach (var part in parts) h.ChangeHealth(part.Part, amount * part.Missing / total, GClass3051.MedKitUse);
    }
    internal static void QueueDrop(Player p, Item item)
    {
        var s = BackpackRuntime.Get(p); if (s is null || !InBag(item, s.Bag)) return;
        Drops.Add(new Drop { Player = p, Item = item, State = s, Position = p.Position, Expires = Time.time + 10f });
    }
    private static void Dropped(Drop d)
    {
        var s = d.State; var p = d.Player;
        if (d.Item.Id == s.Bag.Id)
        {
            if (s.Key == "BP10-3" && s.Record.Ready(s.Key)) { s.Record.Arm("throwDistance", 15f); s.Record.Cooldown(s.Key, 60f); }
            if (s.Key == "BP20-3" && s.Record.Ready(s.Key))
            { AdvancedLegendaryActions.DeploySmokeAt(p, d.Position + Vector3.up * .15f, 60f, s.Roll.Definition.ShortName, s.Key); s.Record.Cooldown(s.Key, 300f); }
            if (s.Key == "BP41-3") s.Record.Checklist.Clear();
            return;
        }
        if (s.Key == "BP37-3" && d.Item.SpawnedInSession && Barter(d.Item) && s.Record.Rewards.Add(d.Item.Id))
        { s.Record.Gifts++; BackpackRuntime.RestoreLeg(p, .25f); EffectStatusModel.Notify(p.ProfileId, s.Key, s.Roll.Definition.ShortName, Time.time, "Leg Stamina recovery"); }
    }
    internal static bool Barter(Item item) => BarterCategory(item) is not null;
    internal static string? BarterCategory(Item item)
    {
        const string barter = "5448eb774bdc2d0a728b4567";
        for (var category = item.Template.Parent; category is not null; category = category.Parent)
        {
            if (category.StringId == barter) return barter;
            if (category.Parent?.StringId == barter) return category.StringId;
        }
        return null;
    }
}
