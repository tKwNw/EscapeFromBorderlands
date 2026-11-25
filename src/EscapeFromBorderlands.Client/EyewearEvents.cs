using System;
using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Eyewear
{
    private static readonly EBodyPart[] Parts = { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };
    private static readonly HashSet<string> Deaths = new(StringComparer.Ordinal);
    private static readonly Dictionary<Player, HashSet<string>> CarriedLoot = new();
    private sealed class DrinkUse
    {
        internal State State = null!;
        internal float Hydration;
        internal int Generation;
    }
    private static readonly Dictionary<object, DrinkUse> Drinks = new();
    private static void ResetEvents() { Deaths.Clear(); CarriedLoot.Clear(); Drinks.Clear(); }

    internal static void Dead(Player victim, IPlayer? aggressor, in DamageInfoStruct hit)
    {
        try
        {
            if (aggressor is not Player player || !AdvancedMeleeRuntime.Enemy(player, victim) || Get(player) is not { } s ||
                !Deaths.Add(victim.ProfileId)) return;
            var firearm = hit.DamageType == EDamageType.Bullet && hit.Weapon is Weapon;
            var origin = hit.MasterOrigin == Vector3.zero ? player.Position : hit.MasterOrigin;
            var impact = hit.HitPoint == Vector3.zero ? victim.Position : hit.HitPoint;
            var distance = (impact - origin).sqrMagnitude;
            switch (s.Key)
            {
                case "EW005" when s.Human:
                    s.Buff(10); s.Distance = 0; s.RewardMeters = 0; break;
                case "EW016":
                    s.Counter++; s.StatusText = "+" + (s.Counter * 2L).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
                    Changed(s); break;
                case "EW024" when firearm && distance >= 900:
                    RemoveOneBleed(s); break;
                case "EW027" when firearm && distance >= 2500 && s.Ready:
                    s.Charges = 1; break;
            }
        }
        finally { Forget(victim); }
    }
    private static void RemoveOneBleed(State s)
    {
        var health = s.Player.ActiveHealthController;
        object? light = null;
        foreach (var effect in health.GetAllEffects())
        {
            if (!effect.Existing) continue;
            var name = effect.GetType().Name;
            if (name == "HeavyBleeding") { Native.Call(effect, "ForceRemove"); Notice(s, "Heavy bleed removed"); return; }
            if (name == "LightBleeding") light = effect;
        }
        if (light is not null) { Native.Call(light, "ForceRemove"); Notice(s, "Light bleed removed"); }
    }
    internal static void HealDistributed(State s, float amount)
    {
        var health = s.Player.ActiveHealthController;
        if (health?.IsAlive != true || amount <= 0) return;
        var missing = 0f;
        foreach (var part in Parts)
        {
            if (health.IsBodyPartDestroyed(part)) continue;
            var hp = health.GetBodyPartHealth(part, false);
            if (hp.Current > 0) missing += Math.Max(0, hp.Maximum - hp.Current);
        }
        if (missing <= 0) return;
        var budget = Math.Min(amount, missing);
        foreach (var part in Parts)
        {
            if (health.IsBodyPartDestroyed(part)) continue;
            var hp = health.GetBodyPartHealth(part, false);
            if (hp.Current <= 0 || hp.Current >= hp.Maximum) continue;
            health.ChangeHealth(part, EyewearRules.HealShare(budget, missing, hp.Maximum - hp.Current), GClass3051.MedKitUse);
        }
    }
    internal static void HealMostInjured(State s, float amount)
    {
        var health = s.Player.ActiveHealthController;
        if (health?.IsAlive != true || amount <= 0) return;
        EBodyPart? selected = null; var fraction = 1f;
        foreach (var part in Parts)
        {
            if (health.IsBodyPartDestroyed(part)) continue;
            var hp = health.GetBodyPartHealth(part, false);
            if (hp.Current > 0 && hp.Maximum > 0 && hp.Current / hp.Maximum < fraction)
            { selected = part; fraction = hp.Current / hp.Maximum; }
        }
        if (selected is { } chosen)
        {
            var hp = health.GetBodyPartHealth(chosen, false);
            health.ChangeHealth(chosen, Math.Min(amount, hp.Maximum - hp.Current), GClass3051.MedKitUse);
        }
    }
    internal static void Breach(Player p)
    {
        if (!ActorCapabilities.IsHuman(p) || Get(p) is not { Key: "EW037", Ready: true } s) return;
        s.Cool(45);
        FlashAt(s, p.Position + Vector3.up * 1.5f, 8, true, p.LookDirection, .7071068f);
    }
    internal static void SearchStarted(State s, InventoryEquipment corpse)
    {
        if (!s.Human || s.Key != "EW015" || !s.Ready || !s.Seen.Add(corpse.Id)) return;
        s.Cool(40);
        AdvancedLegendaryActions.DeploySmokeAt(s.Player, s.Player.Position, 30, s.Roll.Definition.ShortName,
            s.SmokeKey, instant: true);
    }
    internal static void SearchCompleted(State s, Item item)
    {
        if (!s.Human || s.Key != "EW033" || !s.Ready || !s.Seen.Add(item.Id)) return;
        s.Buff(12, 30);
    }
    internal static void UseStart(object effect)
    {
        if (Native.Get(effect, "MedItem") is not Item item || item.Template.ParentId != "5448e8d64bdc2dce718b4568" ||
            Get(BackpackRuntime.Actor(Native.Get(effect, "HealthController"))) is not { Key: "EW020", Human: true, Ready: true } s || s.InWindow) return;
        Drinks[effect] = new DrinkUse { State = s, Hydration = s.Player.ActiveHealthController.Hydration.Current, Generation = Generation };
    }
    internal static void UseEnd(object effect)
    {
        if (!Drinks.TryGetValue(effect, out var use)) return;
        Drinks.Remove(effect);
        var s = use.State;
        if (use.Generation != Generation || Native.Get(effect, "Bool_2") is true || Get(s.Player) != s || !s.Ready || s.InWindow ||
            s.Player.ActiveHealthController.Hydration.Current <= use.Hydration) return;
        s.Charges = 1; s.Window = Time.time + 30;
    }
    internal static bool TracksLoot(Player p) => ActorCapabilities.IsHuman(p) && Get(p)?.Key == "EW035";
    internal static void Dropped(Player p, Item item)
    {
        if (!ActorCapabilities.IsHuman(p)) return;
        if (!CarriedLoot.TryGetValue(p, out var carried)) CarriedLoot[p] = carried = new(StringComparer.Ordinal);
        carried.Add(item.Id);
    }
    // Called from successful concrete transfers, with origin captured BEFORE
    // native mutation. A loose item has no container parent; corpse/container
    // transfers and splits of player-owned stacks are ineligible.
    internal static void Acquired(Player p, Item source, Item received, bool looseWorld, bool external)
    {
        if (!ActorCapabilities.IsHuman(p)) return;
        if (Get(p) is not { Key: "EW035" } s) return;
        if (!CarriedLoot.TryGetValue(p, out var carried)) CarriedLoot[p] = carried = new(StringComparer.Ordinal);
        var wasCarried = carried.Contains(source.Id);
        carried.Add(source.Id); carried.Add(received.Id);
        if (!external || !looseWorld || wasCarried ||
            !BackpackNative.Barter(source) || !s.Seen.Add(source.Id)) return;
        if (++s.Counter < 3) return;
        s.Counter = 0;
        s.Player.ActiveHealthController.ChangeEnergy(10);
        s.Player.ActiveHealthController.ChangeHydration(10);
        HealDistributed(s, 5);
        Notice(s, "Health, energy and hydration restored");
    }
}
