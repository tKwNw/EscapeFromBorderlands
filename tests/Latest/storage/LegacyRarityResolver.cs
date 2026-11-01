using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;

using EscapeFromBorderlands.Client;
namespace EscapeFromBorderlands.Legacy;

internal static class LegacyRarityResolver
{
    private static readonly object Sync = new();
    private static readonly LegendaryCatalog Catalog = LegendaryCatalog.Load();
    private static readonly Dictionary<string, VariantDefinition> Definitions = Catalog.Variants
        .ToDictionary(definition => definition.TemplateId, StringComparer.Ordinal);
    private static readonly Dictionary<string, ShotRoll> RollsByInstance = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedInstances = new(StringComparer.Ordinal);

    public static int DefinitionCount => Definitions.Count;
    public static int CatalogRevision => Catalog.CatalogRevision;

    public static void ResetSession()
    {
        lock (Sync) { RollsByInstance.Clear(); LoggedInstances.Clear(); }
    }

    public static ShotRoll? Resolve(Item? weapon)
    {
        if (weapon is null || !Definitions.TryGetValue(weapon.StringTemplateId, out var definition))
        {
            return null;
        }

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
            if (!LoggedInstances.Add(weapon.Id))
            {
                return;
            }
        }

        Plugin.LogSource.LogInfo($"{roll.Tier} roll for {weapon.Id}: {string.Join(", ", roll.AffixDescriptions)}");
    }

    private static ShotRoll RollProcedural(string instanceId, VariantDefinition definition)
    {
        var rng = new StableRandom(Fnv1a(instanceId + ":" + definition.TemplateId));
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

    private static ShotRoll RollBackpack(StableRandom rng, VariantDefinition definition)
    {
        var roll = new ShotRoll(definition, $"Legendary — {definition.DisplayName}", "#FF9D28");
        var pool = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        float Pick(int lo, int hi) => (lo + rng.Next((hi - lo) / 5 + 1) * 5) / 100f;
        for (var n = 0; n < 4; n++)
        {
            var ix = rng.Next(pool.Count); var boon = pool[ix]; pool.RemoveAt(ix);
            var b = roll.Backpack;
            switch (boon)
            {
                case 0: var frame = Pick(15, 30); b.FrameWeight = 1f - frame; roll.AffixDescriptions.Add($"Light Frame −{frame:P0} empty backpack weight"); break;
                case 1: var cargo = Pick(10, 20); b.ContentsWeight = 1f - cargo; roll.AffixDescriptions.Add($"Balanced Cargo −{cargo:P0} backpack contents weight"); break;
                case 2: var load = Pick(10, 20); b.OverweightThreshold = 1f + load; roll.AffixDescriptions.Add($"Load Harness +{load:P0} overweight thresholds"); break;
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

        public string BuildDescriptionBlock()
        {
            return $"<b><color={ColorHex}>{Tier.ToUpperInvariant()}</color></b>\n" +
                "• " + string.Join("\n• ", AffixDescriptions.Select(line => line == Definition.EffectText ? "<color=#66D9EF>" + line + "</color>" : "<color=#E6E6E6>" + line + "</color>")) +
                (Definition.BackpackEffect is not null && (Backpack.FrameWeight < 1f || Backpack.ContentsWeight < 1f)
                    ? "\nWeight reductions affect movement and stamina; the inventory weight readout stays unchanged." : string.Empty);
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
