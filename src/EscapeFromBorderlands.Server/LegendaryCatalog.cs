using System.Reflection;
using System.Text.Json;

namespace EscapeFromBorderlands.Server;

internal sealed class LegendaryCatalog
{
    public int SchemaVersion { get; set; }
    public int CatalogRevision { get; set; }
    public int VanillaWeaponCount { get; set; }
    public List<string> VanillaMeleeTemplateIds { get; set; } = new();
    public List<string> UnlootableMeleeTemplateIds { get; set; } = new();
    public int LegendaryCount { get; set; }
    public List<LegacyAliasDefinition> LegacyAliases { get; set; } = [];
    public List<VariantDefinition> Variants { get; set; } = [];

    public static LegendaryCatalog Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("LegendaryDefinitions.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded catalog {resourceName} could not be opened.");
        var catalog = JsonSerializer.Deserialize<LegendaryCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Embedded Legendary catalog was empty.");
        BackpackStorageVariants.Expand(catalog.Variants);
        return catalog;
    }
}

internal sealed class LegacyAliasDefinition
{
    public string SourceTemplateId { get; set; } = string.Empty;
    public string SourceShortName { get; set; } = string.Empty;
    public string SourceFullName { get; set; } = string.Empty;
    public string WeaponClass { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

internal sealed class VariantDefinition
{
    public bool IsEarpiece { get; set; }
    public bool SpawnEnabled { get; set; } = true;
    public bool IsMysteryFleaOffer { get; set; }
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
    public string LocaleDescription => BackpackEffect is null && ChestRigEffect is null && GrenadeChargesPerRaid == 0 && !IsEarpiece ? Description
        : "<color=#66D9EF>" + EffectText + "</color>" + (string.IsNullOrEmpty(Description) ? "" : "\n\n" + Description);
    public List<string> FireModes { get; set; } = [];
    public int? BurstShotsCount { get; set; }
    public int? BaseFireRate { get; set; }
    public int? SingleFireRate { get; set; }
    public string Tone { get; set; } = string.Empty;
    public bool IsMeme { get; set; }
}
