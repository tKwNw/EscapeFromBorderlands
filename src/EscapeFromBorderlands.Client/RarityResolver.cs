using Magazine = MagazineItemClass;
using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

internal static class RarityResolver
{
    private static readonly object Sync = new();
    private static readonly LegendaryCatalog Catalog = LegendaryCatalog.Load();
    private static readonly Dictionary<string, VariantDefinition> Definitions = Catalog.Variants
        .ToDictionary(definition => definition.TemplateId, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> MasterySources = Catalog.Variants
        .Where(definition => !definition.IsMysteryFleaOffer)
        .Concat(Catalog.LegacyAliases)
        .ToDictionary(definition => definition.TemplateId, definition => definition.SourceTemplateId, StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ShotRoll> RollsByInstance = new(StringComparer.Ordinal);
    private static readonly List<(string Id, ShotRoll Roll)> PendingRollLogs = new(32);
    private static readonly HashSet<string> LoggedInstances = new(StringComparer.Ordinal);

    public static int DefinitionCount => Definitions.Values.Select(d => !string.IsNullOrEmpty(d.FleaRevealTemplateId)
        ? d.FleaRevealTemplateId : string.IsNullOrEmpty(d.StorageFamilyId) ? d.TemplateId : d.StorageFamilyId).Distinct(StringComparer.Ordinal).Count();
    public static int CatalogRevision => Catalog.CatalogRevision;

    internal static string SourceTemplateForMastery(string templateId) =>
        MasterySources.TryGetValue(templateId, out var sourceId) ? sourceId : templateId;

    public static void ResetSession()
    {
        lock (Sync)
        {
            try
            {
                foreach (var log in PendingRollLogs)
                    Plugin.LogSource.LogInfo($"{log.Roll.Tier} roll for {log.Id}: {string.Join(", ", log.Roll.AffixDescriptions)}");
            }
            finally { RollsByInstance.Clear(); LoggedInstances.Clear(); PendingRollLogs.Clear(); }
        }
    }

    public static ShotRoll? Resolve(Item? weapon)
    {
        if (weapon is null || !Definitions.TryGetValue(weapon.StringTemplateId, out var definition))
        {
            return null;
        }
        return Resolve(weapon, definition);
    }

    internal static ShotRoll Resolve(Item weapon, VariantDefinition definition)
    {
        if (RollsByInstance.TryGetValue(weapon.Id, out var existing)) return existing;
        lock (Sync)
        {
            if (RollsByInstance.TryGetValue(weapon.Id, out var cached))
            {
                return cached;
            }

            var roll = RollProcedural(weapon.Id, definition);
            if (definition.EffectKind != "procedural")
            {
                roll.AffixDescriptions.Insert(0, definition.EffectText);
            }
            RollsByInstance[weapon.Id] = roll;
            return roll;
        }
    }

    public static bool TryGetDefinition(Item? weapon, out VariantDefinition definition)
    {
        if (weapon is not null && Definitions.TryGetValue(weapon.StringTemplateId, out definition!))
        {
            return true;
        }

        definition = null!;
        return false;
    }

    public static void LogRollOnce(Item weapon, ShotRoll roll)
    {
        lock (Sync)
        {
            if (PendingRollLogs.Count < 32 && LoggedInstances.Add(weapon.Id)) PendingRollLogs.Add((weapon.Id, roll));
        }
    }

    private static ShotRoll RollProcedural(string instanceId, VariantDefinition definition)
    {
        if (definition.IsMysteryFleaOffer)
        {
            var preview = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
            for (var i = 0; i < 4; i++) preview.AffixDescriptions.Add("????");
            if (definition.GrenadeChargesPerRaid > 0) ApplyGrenadeSignature(preview);
            return preview;
        }
        var rng = new StableRandom(Fnv1a(instanceId + ":" + definition.TemplateId));
        if (definition.GrenadeChargesPerRaid > 0) return RollGrenade(rng, definition);
        if (definition.IsEarpiece) return RollEarpiece(rng, definition);
        if (definition.IsHeadwear) return RollHeadwear(rng, definition);
        if (definition.ChestRigEffect is not null) return RollChestRig(rng, definition);
        if (definition.BackpackEffect is not null) return RollBackpack(rng, definition);
        if (definition.MeleeEffect is not null) return RollMelee(rng, definition);
        var available = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        var tierHeading = $"Legendary — {definition.DisplayName}";
        var roll = new ShotRoll(definition, tierHeading, "#FF9D28");
        float NextFivePercent(int minimum, int maximum)
        {
            var first = (int)Math.Ceiling(minimum / 5d) * 5;
            var last = (int)Math.Floor(maximum / 5d) * 5;
            var steps = Math.Max(1, (last - first) / 5 + 1);
            return (first + rng.Next(steps) * 5) / 100f;
        }

        for (var index = 0; index < definition.AffixCount && available.Count > 0; index++)
        {
            var selection = rng.Next(available.Count);
            var affix = available[selection];
            available.RemoveAt(selection);
            switch (affix)
            {
                case 0:
                {
                    var bonus = NextFivePercent(15, 30);
                    roll.DamageMultiplier *= 1f + bonus;
                    roll.AffixDescriptions.Add($"Heavy-Hitting +{bonus:P0} damage");
                    break;
                }
                case 1:
                {
                    var bonus = NextFivePercent(15, 25);
                    roll.VelocityMultiplier *= 1f + bonus;
                    roll.AffixDescriptions.Add($"Accelerated +{bonus:P0} projectile velocity");
                    break;
                }
                case 2:
                {
                    var bonus = NextFivePercent(10, 20);
                    roll.PenetrationMultiplier *= 1f + bonus;
                    roll.AffixDescriptions.Add($"Piercing +{bonus:P0} penetration");
                    break;
                }
                case 3:
                {
                    var bonus = NextFivePercent(20, 35);
                    roll.RicochetChanceBonus += bonus;
                    roll.AffixDescriptions.Add($"Trickshot +{bonus:P0} natural ricochet chance");
                    break;
                }
                case 4:
                {
                    var bonus = NextFivePercent(15, 30);
                    roll.FragmentationChanceBonus += bonus;
                    roll.AffixDescriptions.Add($"Splintering +{bonus:P0} fragmentation chance");
                    break;
                }
                case 5:
                {
                    var reduction = NextFivePercent(15, 25);
                    roll.RecoilDeltaModifier -= reduction;
                    roll.AffixDescriptions.Add($"Stabilized −{reduction:P0} vertical and horizontal recoil");
                    break;
                }
                case 6:
                {
                    var bonus = NextFivePercent(15, 25);
                    roll.ErgonomicsDeltaModifier += bonus;
                    roll.AffixDescriptions.Add($"Responsive +{bonus:P0} ergonomics");
                    break;
                }
                case 7:
                {
                    var bonus = NextFivePercent(15, 25);
                    roll.CenterOfImpactDeltaModifier -= bonus;
                    roll.AffixDescriptions.Add($"Precise −{bonus:P0} shot dispersion");
                    break;
                }
                case 8:
                {
                    var bonus = NextFivePercent(10, 20);
                    roll.FireRateMultiplier *= 1f + bonus;
                    roll.AffixDescriptions.Add($"Rapid +{bonus:P0} fire rate");
                    break;
                }
            }
        }

        return roll;
    }

    private static ShotRoll RollGrenade(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        float Pick(int lo, int hi) => (lo + rng.Next((hi - lo) / 5 + 1) * 5) / 100f;
        // Preserve Encore's existing seed sequence and 4 bonus types for saved copies.
        if (definition.RuntimeKey == "GR26")
        {
            var damage = Pick(15, 30); roll.GrenadeDamageMultiplier = 1f + damage;
            roll.AffixDescriptions.Add($"Heavy Payload +{damage:P0} blast damage");
            var radius = Pick(10, 20); roll.GrenadeRadiusMultiplier = 1f + radius;
            roll.AffixDescriptions.Add($"Wide Blast +{radius:P0} explosion radius");
            var fragments = Pick(20, 40); roll.GrenadeFragmentMultiplier = 1f + fragments;
            roll.AffixDescriptions.Add($"Packed Shrapnel +{fragments:P0} fragments");
            var distance = Pick(15, 30); roll.GrenadeThrowDistanceMultiplier = 1f + distance;
            roll.AffixDescriptions.Add($"Long Toss +{distance:P0} throwing distance");
        }
        else
        {
            var pool = GrenadeEffectRules.BonusPool(definition.GrenadeCategory);
            var replacement = -1;
            for (var i = 0; i < 4; i++)
            {
                var selected = rng.Next(pool.Count); var bonus = pool[selected]; pool.RemoveAt(selected);
                switch (bonus)
                {
                    case 0: var damage = Pick(15, 30); roll.GrenadeDamageMultiplier *= 1f + damage; roll.AffixDescriptions.Add($"Heavy Payload +{damage:P0} blast damage"); break;
                    case 1: var radius = Pick(10, 20); roll.GrenadeRadiusMultiplier *= 1f + radius; roll.AffixDescriptions.Add($"Wide Blast +{radius:P0} explosion radius"); break;
                    case 2: var fragments = Pick(20, 40); roll.GrenadeFragmentMultiplier *= 1f + fragments; roll.AffixDescriptions.Add($"Packed Shrapnel +{fragments:P0} fragments"); break;
                    case 3: var distance = Pick(15, 30); roll.GrenadeThrowDistanceMultiplier *= 1f + distance; roll.AffixDescriptions.Add($"Long Toss +{distance:P0} throwing distance"); break;
                    case 4: var flesh = Pick(10, 25); roll.GrenadeFragmentDamageMultiplier *= 1f + flesh; roll.AffixDescriptions.Add($"Sharp Fragments +{flesh:P0} fragment damage"); break;
                    case 5: var draw = Pick(20, 40); roll.GrenadeDrawSpeedMultiplier *= 1f + draw; roll.AffixDescriptions.Add($"Quick Draw +{draw:P0} grenade draw speed"); break;
                    case 6: var speed = Pick(15, 30); roll.GrenadeThrowSpeedMultiplier *= 1f + speed; roll.AffixDescriptions.Add($"Fast Release +{speed:P0} throw animation speed"); break;
                    case 7: var duration = Pick(15, 30); roll.GrenadeFlashDurationMultiplier *= 1f + duration; roll.AffixDescriptions.Add($"Lingering Flash +{duration:P0} flash duration"); break;
                    case 8: var flash = Pick(10, 25); roll.GrenadeFlashRadiusMultiplier *= 1f + flash; roll.AffixDescriptions.Add($"Wide Flash +{flash:P0} effective flash radius"); break;
                    case 9: var smokeTime = Pick(20, 40); roll.GrenadeSmokeDurationMultiplier *= 1f + smokeTime; roll.AffixDescriptions.Add($"Lasting Screen +{smokeTime:P0} smoke duration"); break;
                    case 10: var size = Pick(15, 30); roll.GrenadeSmokeSizeMultiplier *= 1f + size; roll.AffixDescriptions.Add($"Broad Screen +{size:P0} smoke cloud size"); break;
                    case 11:
                        var deploy = Pick(20, 40);
                        if (definition.RuntimeKey == "GR59")
                        { replacement = roll.AffixDescriptions.Count; roll.AffixDescriptions.Add(""); }
                        else { roll.GrenadeSmokeBuildTimeMultiplier *= 1f / (1f + deploy); roll.AffixDescriptions.Add($"Rapid Screen +{deploy:P0} smoke deployment speed"); }
                        break;
                    case 12: roll.GrenadeDoorBreach = true; roll.AffixDescriptions.Add("Door Breaching opens closed, unlocked swing doors within 3 metres"); break;
                }
            }
            if (replacement >= 0)
            {
                // Consume the original 4 selections and values first. Upgrade
                // only Pop Smoke's obsolete growth affix; keep its other rolls.
                var compatible = pool.Where(b => b != 11).ToArray();
                var selected = compatible[rng.Next(compatible.Length)];
                string text;
                switch (selected)
                {
                    case 3: var distance = Pick(15, 30); roll.GrenadeThrowDistanceMultiplier *= 1f + distance; text = $"Long Toss +{distance:P0} throwing distance"; break;
                    case 5: var draw = Pick(20, 40); roll.GrenadeDrawSpeedMultiplier *= 1f + draw; text = $"Quick Draw +{draw:P0} grenade draw speed"; break;
                    case 6: var speed = Pick(15, 30); roll.GrenadeThrowSpeedMultiplier *= 1f + speed; text = $"Fast Release +{speed:P0} throw animation speed"; break;
                    case 9: var duration = Pick(20, 40); roll.GrenadeSmokeDurationMultiplier *= 1f + duration; text = $"Lasting Screen +{duration:P0} smoke duration"; break;
                    case 10: var size = Pick(15, 30); roll.GrenadeSmokeSizeMultiplier *= 1f + size; text = $"Broad Screen +{size:P0} smoke cloud size"; break;
                    default: throw new InvalidOperationException("Missing compatible Pop Smoke replacement.");
                }
                roll.AffixDescriptions[replacement] = text;
            }
        }
        ApplyGrenadeSignature(roll);
        return roll;
    }

    private static void ApplyGrenadeSignature(ShotRoll roll)
    {
        switch (roll.Definition.RuntimeKey)
        {
            case "GR02": roll.GrenadeFragmentMultiplier *= 1.75f; break;
            case "GR06": roll.GrenadeFragmentMultiplier *= 2f; break;
            case "GR14": roll.GrenadeThrowDistanceMultiplier *= 2f; roll.GrenadeRadiusMultiplier *= 1.5f; break;
            case "GR15": roll.GrenadeRadiusMultiplier *= 4f; break;
            case "GR35": roll.GrenadeRadiusMultiplier *= 1.3f; break;
            case "GR40": roll.GrenadeThrowDistanceMultiplier *= 1.5f; break;
            case "GR51": roll.GrenadeFlashRadiusMultiplier *= 2f; break;
        }
    }

    private static ShotRoll RollEarpiece(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        var pool = Enumerable.Range(0, 8).Where(i => i != 3 || definition.RuntimeKey != "EP09-2").ToList();
        float Pick(int lo, int hi) => (lo + rng.Next((hi - lo) / 5 + 1) * 5) / 100f;
        for (var i = 0; i < 4; i++)
        {
            var selected = rng.Next(pool.Count); var type = pool[selected]; pool.RemoveAt(selected);
            var b = roll.Earpiece;
            switch (type)
            {
                case 0: var range = Pick(5, 15); b.Hearing *= 1f + range; roll.AffixDescriptions.Add($"Far Signal +{range:P0} hearing distance"); break;
                case 1: var ambient = Pick(10, 25); b.Ambient *= 1f - ambient; roll.AffixDescriptions.Add($"Noise Gate −{ambient:P0} environmental ambient volume to you"); break;
                case 2: var self = Pick(10, 25); b.SelfNoise *= 1f - self; roll.AffixDescriptions.Add($"Self Muffle −{self:P0} your own footstep and equipment volume to you"); break;
                case 3: var contusion = Pick(15, 30); b.Contusion *= 1f - contusion; roll.AffixDescriptions.Add($"Clear Head −{contusion:P0} grenade-induced contusion duration"); break;
                case 4: var cost = Pick(10, 20); b.AimCost *= 1f - cost; roll.AffixDescriptions.Add($"Steady Aim −{cost:P0} aiming Arm Stamina cost"); break;
                case 5: var draw = Pick(10, 20); b.Draw *= 1f + draw; roll.AffixDescriptions.Add($"Fast Hands +{draw:P0} firearm draw speed"); break;
                case 6: var legs = Pick(10, 25); b.LegRecovery *= 1f + legs; roll.AffixDescriptions.Add($"2nd Wind +{legs:P0} Leg Stamina regeneration"); break;
                case 7: var aim = Pick(10, 20); b.Aim *= 1f + aim; roll.AffixDescriptions.Add($"Quick Focus +{aim:P0} aiming speed"); break;
            }
        }
        return roll;
    }

    private static ShotRoll RollHeadwear(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        var choices = Enumerable.Range(0, definition.WeaponClass == "armoredHeadwear" ? 9 : 6).ToList();
        var bonus = new HeadwearRoll();
        float Pick(int min, int max) => (min + rng.Next((max - min) / 5 + 1) * 5) / 100f;
        for (var i = 0; i < 4; ++i)
        {
            var index = rng.Next(choices.Count); var kind = choices[index]; choices.RemoveAt(index);
            switch (kind)
            {
                case 0: bonus.Camera = Pick(20, 35); roll.AffixDescriptions.Add($"Braced Neck −{bonus.Camera:P0} damage camera kick"); break;
                case 1: bonus.Freelook = .20f; roll.AffixDescriptions.Add("Peripheral Vision +20% freelook angle"); break;
                case 2: bonus.Search = Pick(20, 40); roll.AffixDescriptions.Add($"Quick Study +{bonus.Search:P0} search speed"); break;
                case 3: bonus.Lean = Pick(15, 25); roll.AffixDescriptions.Add($"Quick Lean +{bonus.Lean:P0} lean speed"); break;
                case 4: bonus.Stance = Pick(15, 25); roll.AffixDescriptions.Add($"Quick Duck +{bonus.Stance:P0} stance transition speed"); break;
                case 5: bonus.Sprint = Pick(15, 25); roll.AffixDescriptions.Add($"Snap Ready +{bonus.Sprint:P0} sprint-to-ready speed"); break;
                case 6: bonus.Blunt = Pick(10, 20); roll.AffixDescriptions.Add($"Impact Liner −{bonus.Blunt:P0} blunt HP damage from bullets stopped by this helmet"); break;
                case 7: bonus.Durability = Pick(15, 30); roll.AffixDescriptions.Add($"Reinforced Shell −{bonus.Durability:P0} helmet durability loss from impacts"); break;
                case 8: bonus.Ricochet = Pick(5, 15); roll.AffixDescriptions.Add($"Glancing Blow +{bonus.Ricochet:P0} to this helmet’s existing ricochet chance"); break;
            }
        }
        roll.Headwear = bonus;
        return roll;
    }

    private static ShotRoll RollMelee(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        var available = new List<int> { 0, 1, 2, 3, 4, 5 };
        for (var i = 0; i < definition.AffixCount && available.Count > 0; i++)
        {
            var selected = rng.Next(available.Count);
            var affix = available[selected];
            available.RemoveAt(selected);
            var bonus = (affix < 2 ? 15 + rng.Next(4) * 5 : 10 + rng.Next(3) * 5) / 100f;
            switch (affix)
            {
                case 0: roll.MeleeSlashMultiplier *= 1f + bonus; roll.AffixDescriptions.Add($"+{bonus:P0} slash damage"); break;
                case 1: roll.MeleeStabMultiplier *= 1f + bonus; roll.AffixDescriptions.Add($"+{bonus:P0} stab damage"); break;
                case 2: roll.MeleeAttackSpeedMultiplier *= 1f + bonus; roll.AffixDescriptions.Add($"+{bonus:P0} melee attack speed"); break;
                case 3: roll.MeleeReachMultiplier *= 1f + bonus; roll.AffixDescriptions.Add($"+{bonus:P0} melee reach"); break;
                case 4: roll.PenetrationMultiplier *= 1f + bonus; roll.AffixDescriptions.Add($"+{bonus:P0} melee penetration"); break;
                case 5: roll.MeleeSwapMultiplier *= 1f + bonus; roll.AffixDescriptions.Add($"+{bonus:P0} draw and holster speed for this melee weapon"); break;
            }
        }
        return roll;
    }

    private static uint Fnv1a(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash == 0 ? offset : hash;
    }

    private static ShotRoll RollChestRig(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        var pool = Enumerable.Range(0, 11).Where(i => i != 10 || definition.ChestRigEffect!.HasTurningPenalty).ToList();
        var count = 4;
        if (definition.StorageRows > 0) { roll.AffixDescriptions.Add("Expanded Pouch +1 row in the largest original compartment"); count--; }
        if (definition.StorageColumns > 0) { roll.AffixDescriptions.Add("Widened Pouch +1 column in the largest original compartment"); count--; }
        float Pick(int lo, int hi) => (lo + rng.Next((hi - lo) / 5 + 1) * 5) / 100f;
        for (var i = 0; i < count; i++)
        {
            var selected = rng.Next(pool.Count); var boon = pool[selected]; pool.RemoveAt(selected);
            var b = roll.ChestRig;
            switch (boon)
            {
                case 0: var reload = Pick(10, 20); b.Reload += reload; roll.AffixDescriptions.Add($"Quick Reload +{reload:P0} reload speed"); break;
                case 1: var swap = Pick(10, 20); b.Swap += swap; roll.AffixDescriptions.Add($"Fast Hands +{swap:P0} firearm switching speed"); break;
                case 2: var aim = Pick(5, 10); b.Aim += aim; roll.AffixDescriptions.Add($"Ready Position +{aim:P0} aiming speed"); break;
                case 3: var ergo = Pick(10, 20); b.Ergonomics += ergo; roll.AffixDescriptions.Add($"Comfortable Fit +{ergo:P0} ergonomics"); break;
                case 4: var recoil = Pick(5, 10); b.Recoil -= recoil; roll.AffixDescriptions.Add($"Braced Shooting −{recoil:P0} recoil"); break;
                case 5: var mag = Pick(20, 35); b.MagazineCheck += mag; roll.AffixDescriptions.Add($"Magazine Drill +{mag:P0} magazine-check speed"); break;
                case 6: var chamber = Pick(20, 35); b.ChamberCheck += chamber; roll.AffixDescriptions.Add($"Chamber Drill +{chamber:P0} chamber-check speed"); break;
                case 7: var pack = Pick(20, 35); b.Packing += pack; roll.AffixDescriptions.Add($"Efficient Packing +{pack:P0} manual ammunition packing speed"); break;
                case 8: var arms = Pick(10, 20); b.AimCost -= arms; roll.AffixDescriptions.Add($"Supported Arms −{arms:P0} aiming Arm Stamina drain"); break;
                // Retain the old random draw so existing items keep their other boon selections and values.
                case 9: _ = Pick(15, 30); b.Movement = 1.1f; roll.AffixDescriptions.Add("Lightweight Construction +10% movement speed"); break;
                case 10: b.RemoveTurningPenalty = true; roll.AffixDescriptions.Add("Flexible Harness removes this chest piece's turning penalty"); break;
            }
        }
        return roll;
    }

    private static ShotRoll RollBackpack(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        var pool = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        var count = 4;
        // Layout choices are weighted like a four-of-ten draw. Conditional on
        // that layout, choose the remaining distinct boons from the original
        // eight; the server template already owns the permanent dimensions.
        const string compartment = "largest compartment";
        if (definition.StorageRollVersion > 0)
        {
            if (definition.StorageRows > 0)
            {
                roll.Backpack.ExtraRows = definition.StorageRows;
                roll.AffixDescriptions.Add($"Expanded Storage +{definition.StorageRows} {(definition.StorageRows == 1 ? "row" : "rows")} in the {compartment}");
                count--;
            }
            if (definition.StorageColumns > 0)
            {
                roll.Backpack.ExtraColumns = definition.StorageColumns;
                roll.AffixDescriptions.Add($"Widened Storage +{definition.StorageColumns} {(definition.StorageColumns == 1 ? "column" : "columns")} in the {compartment}");
                count--;
            }
        }
        float Pick(int lo, int hi) => (lo + rng.Next((hi - lo) / 5 + 1) * 5) / 100f;
        for (var n = 0; n < count; n++)
        {
            var ix = rng.Next(pool.Count); var boon = pool[ix]; pool.RemoveAt(ix);
            var b = roll.Backpack;
            switch (boon)
            {
                // Keep one RNG draw per replaced boon: unrelated rolls remain stable on existing items.
                case 0: _ = Pick(15, 30); b.Movement = 1.1f; roll.AffixDescriptions.Add("Light Frame +10% movement speed"); break;
                case 1: var cargo = Pick(10, 15); b.Ergonomics = 1f + cargo; roll.AffixDescriptions.Add($"Balanced Cargo +{cargo:P0} ergonomics"); break;
                case 2: var load = Pick(10, 20); b.StaminaCapacity = 1f + load; roll.AffixDescriptions.Add($"Load Harness +{load:P0} maximum Arm and Leg Stamina"); break;
                case 3: var search = Pick(15, 30); b.SearchTime = 1f - search; roll.AffixDescriptions.Add($"Quick Search −{search:P0} item reveal time while searching"); break;
                case 4: var sprint = Pick(10, 20); b.SprintCost = 1f - sprint; roll.AffixDescriptions.Add($"Long Haul −{sprint:P0} sprint Leg Stamina cost"); break;
                case 5: var recover = Pick(15, 30); b.LegRecovery = 1f + recover; roll.AffixDescriptions.Add($"Second Breath +{recover:P0} Leg Stamina recovery"); break;
                case 6: var aim = Pick(10, 20); b.AimCost = 1f - aim; roll.AffixDescriptions.Add($"Steady Shoulders −{aim:P0} aiming Arm Stamina cost"); break;
                case 7: var noise = Pick(10, 20); b.MovementNoise = 1f - noise; roll.AffixDescriptions.Add($"Soft Step −{noise:P0} movement noise"); break;
            }
        }
        return roll;
    }

    internal sealed class ShotRoll
    {
        public ShotRoll(VariantDefinition definition, string tier, string colorHex)
        {
            Definition = definition;
            Tier = tier;
            ColorHex = colorHex;
        }

        public VariantDefinition Definition { get; }
        public string Tier { get; }
        public string ColorHex { get; }
        public BackpackBoons Backpack { get; } = new();
        public ChestRigBoons ChestRig { get; } = new();
        public EarpieceBoons Earpiece { get; } = new();
        internal HeadwearRoll Headwear { get; set; }
        public float GrenadeDamageMultiplier { get; set; } = 1f;
        public float GrenadeRadiusMultiplier { get; set; } = 1f;
        public float GrenadeFragmentMultiplier { get; set; } = 1f;
        public float GrenadeThrowDistanceMultiplier { get; set; } = 1f;
        public float GrenadeFragmentDamageMultiplier { get; set; } = 1f;
        public float GrenadeDrawSpeedMultiplier { get; set; } = 1f;
        public float GrenadeThrowSpeedMultiplier { get; set; } = 1f;
        public float GrenadeFlashDurationMultiplier { get; set; } = 1f;
        public float GrenadeFlashRadiusMultiplier { get; set; } = 1f;
        public float GrenadeSmokeDurationMultiplier { get; set; } = 1f;
        public float GrenadeSmokeSizeMultiplier { get; set; } = 1f;
        public float GrenadeSmokeBuildTimeMultiplier { get; set; } = 1f;
        public bool GrenadeDoorBreach { get; set; }

        public float DamageMultiplier { get; set; } = 1f;
        public float VelocityMultiplier { get; set; } = 1f;
        public float PenetrationMultiplier { get; set; } = 1f;
        public float RicochetChanceBonus { get; set; }
        public float FragmentationChanceBonus { get; set; }
        public float RecoilDeltaModifier { get; set; }
        public float ErgonomicsDeltaModifier { get; set; }
        public float CenterOfImpactDeltaModifier { get; set; }
        public float FireRateMultiplier { get; set; } = 1f;
        public float MeleeSlashMultiplier { get; set; } = 1f;
        public float MeleeStabMultiplier { get; set; } = 1f;
        public float MeleeAttackSpeedMultiplier { get; set; } = 1f;
        public float MeleeReachMultiplier { get; set; } = 1f;
        public float MeleeSwapMultiplier { get; set; } = 1f;
        public List<string> AffixDescriptions { get; } = new();
        internal ShotRoll GrenadeSnapshot() => (ShotRoll)MemberwiseClone();

        private string? _descriptionBlock;
        private string? _headwearBinding;
        public string BuildDescriptionBlock()
        {
            if (Definition.IsHeadwear && _headwearBinding != HeadwearSettings.Label)
            { _headwearBinding = HeadwearSettings.Label; _descriptionBlock = null; }
            return _descriptionBlock ??= $"<b><color={ColorHex}>{Tier.ToUpperInvariant()}</color></b>\n" +
                "• " + string.Join("\n• ", AffixDescriptions.Select(line => line == Definition.EffectText ? "<color=#66D9EF>" + (Definition.IsHeadwear ? HeadwearSettings.Describe(line) : line) + "</color>" : "<color=#E6E6E6>" + line + "</color>")) +
                (Definition.IsMysteryFleaOffer ? "\n4 random bonuses are revealed when purchased." : string.Empty);
        }

    }

    private sealed class StableRandom
    {
        private uint _state;
        public StableRandom(uint seed) => _state = seed;
        public int Next(int exclusiveMaximum) => exclusiveMaximum <= 1 ? 0 : (int)(NextUInt() % (uint)exclusiveMaximum);
        public float NextFloat(float minimum, float maximum)
        {
            var unit = (NextUInt() & 0x00FFFFFFu) / 16777216f;
            return minimum + (maximum - minimum) * unit;
        }

        private uint NextUInt()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }
    }
}
