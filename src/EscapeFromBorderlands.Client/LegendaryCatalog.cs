using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace EscapeFromBorderlands.Client;

internal sealed class LegendaryCatalog
{
    public int SchemaVersion { get; set; }
    public int CatalogRevision { get; set; }
    public int VanillaWeaponCount { get; set; }
    public List<string> VanillaMeleeTemplateIds { get; set; } = new();
    public List<string> UnlootableMeleeTemplateIds { get; set; } = new();
    public int LegendaryCount { get; set; }
    public List<VariantDefinition> Variants { get; set; } = new();
    public List<VariantDefinition> LegacyAliases { get; set; } = new();

    public static LegendaryCatalog Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("LegendaryDefinitions.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded catalog {resourceName} could not be opened.");
        using var reader = new StreamReader(stream);
        var catalog = JsonConvert.DeserializeObject<LegendaryCatalog>(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Embedded Legendary catalog was empty.");
        BackpackStorageVariants.Expand(catalog.Variants);
        LegendaryFleaVariants.Expand(catalog.Variants);
        return catalog;
    }
}

internal sealed class VariantDefinition
{
    public bool IsEarpiece { get; set; }
    public bool SpawnEnabled { get; set; } = true;
    public bool IsMysteryFleaOffer { get; set; }
    public string FleaRevealTemplateId { get; set; } = string.Empty;
    public double SpawnWeight { get; set; } = 1d;
    public int StorageRollVersion { get; set; }
    public int StorageRows { get; set; }
    public int StorageColumns { get; set; }
    public int BackpackBaseColumns { get; set; }
    public string StorageFamilyId { get; set; } = string.Empty;
    internal VariantDefinition Copy() => (VariantDefinition)MemberwiseClone();
    public string SourceTemplateId { get; set; } = string.Empty;
    public string SourceShortName { get; set; } = string.Empty;
    public string SourceFullName { get; set; } = string.Empty;
    public string WeaponClass { get; set; } = string.Empty;
    public bool Automatic { get; set; }
    public string CaliberKey { get; set; } = string.Empty;
    public int CaliberAverageVelocity { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string PresetId { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public int AffixCount { get; set; }
    public int GrenadeChargesPerRaid { get; set; }
    public int GrenadeRechargeSeconds { get; set; } = 180;
    public string GrenadeCategory { get; set; } = "fragmentation";
    public string BackgroundColor { get; set; } = string.Empty;
    public double PriceMultiplier { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EffectKind { get; set; } = string.Empty;
    public string EffectText { get; set; } = string.Empty;
    public string RuntimeKey { get; set; } = string.Empty;
    public int TriggerIndex { get; set; } = -1;
    public int PayoffIndex { get; set; } = -1;
    public int SecondaryPayoffIndex { get; set; } = -1;
    public string SpecialId { get; set; } = string.Empty;
    public MeleeEffectDefinition? MeleeEffect { get; set; }
    public BackpackEffectDefinition? BackpackEffect { get; set; }
    public ChestRigEffectDefinition? ChestRigEffect { get; set; }
    public CustomEffectDefinition? CustomEffect { get; set; }
    public List<string> FireModes { get; set; } = new();
    public int? BurstShotsCount { get; set; }
    public int? BaseFireRate { get; set; }
    public int? SingleFireRate { get; set; }
    public string Tone { get; set; } = string.Empty;
    public bool IsMeme { get; set; }

    public int Number(string key, int minimum, int maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        return minimum + (int)(Fnv1a(SourceTemplateId + ":" + key) % (uint)(maximum - minimum + 1));
    }

    public float Decimal(string key, int minimum, int maximum) => Number(key, minimum, maximum) / 10f;

    public int StepNumber(string key, int minimum, int maximum, int step = 5)
    {
        var first = (int)Math.Ceiling(minimum / (double)step) * step;
        var last = (int)Math.Floor(maximum / (double)step) * step;
        if (last <= first)
        {
            return first;
        }

        var count = (last - first) / step + 1;
        return first + (int)(Fnv1a(SourceTemplateId + ":" + key) % (uint)count) * step;
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
}

internal sealed class CustomEffectDefinition
{
    public string Trigger { get; set; } = "always";
    public int Count { get; set; }
    public float Seconds { get; set; }
    public float Threshold { get; set; }
    public bool RequireTriggerHeld { get; set; }
    public bool RampUsesTriggerHeldShots { get; set; }
    public float DamageMultiplier { get; set; } = 1f;
    public float HeadDamageMultiplier { get; set; } = 1f;
    public float BodyDamageMultiplier { get; set; } = 1f;
    public float VelocityMultiplier { get; set; } = 1f;
    public float PenetrationMultiplier { get; set; } = 1f;
    public float ArmorDamageMultiplier { get; set; } = 1f;
    public float RicochetChanceBonus { get; set; }
    public float FragmentationChanceBonus { get; set; }
    public float RicochetRetention { get; set; }
    public float RecoilDelta { get; set; }
    public float ErgonomicsDelta { get; set; }
    public float AccuracyDelta { get; set; }
    public float FireRateMultiplier { get; set; } = 1f;
    public float ReloadSpeedMultiplier { get; set; } = 1f;
    public float AlwaysRecoilDelta { get; set; }
    public float FireRatePerShot { get; set; }
    public float FireRateMaxBonus { get; set; }
    public float RecoilReductionPerShot { get; set; }
    public float RecoilReductionMax { get; set; }
    public int ProjectileCount { get; set; } = 1;
    public float ProjectileDamageMultiplier { get; set; } = 1f;
    public float ProjectileSpreadDegrees { get; set; }
    public string ProjectilePattern { get; set; } = "horizontal";
    public float ExplosionDamageFactor { get; set; }
    public float ExplosionRadius { get; set; }
}
