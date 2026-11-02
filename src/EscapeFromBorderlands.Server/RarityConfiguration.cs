using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace EscapeFromBorderlands.Server;

public sealed class RarityConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool AddToPmcWeapons { get; set; } = true;
    public bool CreateDefaultPresets { get; set; } = true;
    public bool AllowFleaMarketTrading { get; set; } = false;
    public double AirdropLegendaryChancePercent { get; set; } = 1d;
    public double AirdropFraction => (double.IsFinite(AirdropLegendaryChancePercent) ? Math.Clamp(AirdropLegendaryChancePercent, 0d, 100d) : 1d) / 100d;
    public double StatRewardsLegendaryChancePercent { get; set; } = 1d;
    public double StatRewardsFraction => (double.IsFinite(StatRewardsLegendaryChancePercent) ? Math.Clamp(StatRewardsLegendaryChancePercent, 0d, 100d) : 1d) / 100d;
    public Dictionary<string, double> PriceMultipliers { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["Legendary"] = 10d };
    // Compatibility inputs only. Canonical config files omit these old scalars.
    public double StandalonePmcLegendaryChancePercent { get; set; } = 1d;
    public double ApbsPmcLegendaryChancePercent { get; set; } = 1d;
    public bool AddToBossWeapons { get; set; } = true;
    public bool AddToNonScavFactions { get; set; } = true;
    public double BossLegendaryChancePercent { get; set; } = 10d;
    public bool AutoDetectApbs { get; set; } = true;
    public Dictionary<string, CategorySpawnChances>? SpawnChances { get; set; }
    // The shared wrapper is an internal representation; config.json has a flat
    // factionSpawnChances object. Old backend wrappers remain readable until
    // the final detection stage has chosen which saved rates to migrate.
    public Dictionary<string, Dictionary<string, CategorySpawnChances>>? FactionSpawnChances { get; set; }
    internal bool LegacySpawnSettingsPending { get; private set; }
    internal bool MigratedLegacySpawnSettings { get; private set; }

    [JsonPropertyName("pmcLegendaryWeaponChancePercent")]
    public double? LegacyPmcLegendaryWeaponChancePercent { get; set; }
    public double EffectiveStandalonePmcChancePercent => LegacyPmcLegendaryWeaponChancePercent ?? StandalonePmcLegendaryChancePercent;

    private static readonly string[] Categories = ["guns", "melee", "backpacks", "chestRigs", "grenades", "earpieces"];
    private static readonly string[] LegacyScalars = ["standalonePmcLegendaryChancePercent", "apbsPmcLegendaryChancePercent", "pmcLegendaryWeaponChancePercent", "bossLegendaryChancePercent", "effectiveStandalonePmcChancePercent"];
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, IgnoreReadOnlyProperties = true, WriteIndented = true };
    private static T? Find<T>(Dictionary<string, T>? groups, string name) where T : class => groups?.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static double? Value(CategorySpawnChances? group, string category) => category switch { "guns" => group?.Guns, "melee" => group?.Melee, "backpacks" => group?.Backpacks, "chestRigs" => group?.ChestRigs, "grenades" => group?.Grenades, "earpieces" => group?.Earpieces, _ => null };
    private static double Valid(double? value, double fallback) => Math.Clamp(value is { } configured && double.IsFinite(configured) ? configured : fallback, 0d, 99d);

    public double Chance(string method, string category)
    {
        var boss = method.Equals("bosses", StringComparison.OrdinalIgnoreCase);
        var fallback = category is "backpacks" or "chestRigs" or "grenades" or "earpieces" ? (boss ? 10d : 1d)
            : boss ? BossLegendaryChancePercent : method.Equals("apbs", StringComparison.OrdinalIgnoreCase) ? ApbsPmcLegendaryChancePercent : EffectiveStandalonePmcChancePercent;
        var configured = Value(Find(SpawnChances, boss ? "bosses" : "pmcs"), category);
        if (!boss) configured ??= Value(Find(SpawnChances, method), category);
        return Valid(configured, fallback);
    }

    public double FactionChance(string method, string group, string category)
    {
        var fallback = group is "pmcUsec" or "pmcBear" ? Chance(method, category) : 1d;
        var configured = Value(Find(Find(FactionSpawnChances, "shared"), group), category)
            ?? Value(Find(Find(FactionSpawnChances, method), group), category);
        return Valid(configured, fallback);
    }

    public double BotFraction(string method, string role, string category)
    {
        var group = BotSpawnGroups.Group(role);
        if (!Enabled || group is null) return 0d;
        if (group == "bosses") return AddToBossWeapons ? Fraction("bosses", category) : 0d;
        if (group is "pmcUsec" or "pmcBear") return AddToPmcWeapons ? FactionChance(method, group, category) / 100d : 0d;
        return AddToNonScavFactions ? FactionChance(method, group, category) / 100d : 0d;
    }

    public double Fraction(string method, string category) => Chance(method, category) / 100d;
    public string Describe(string method) => string.Join(", ", Categories.Select(category => $"{category}={Chance(method, category):0.###}%"));
    public static string Category(string slot) => slot == "Earpiece" ? "earpieces" : slot == "Scabbard" ? "melee" : slot == "Backpack" ? "backpacks" : slot is "TacticalVest" or "ArmorVest" ? "chestRigs" : "guns";

    public static RarityConfiguration Load(string? legacyMethod = null)
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        var path = Path.Combine(directory, "config", "config.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var defaults = Path.Combine(directory, "config", "config.default.json");
            if (File.Exists(defaults)) File.Copy(defaults, path, false);
            else File.WriteAllText(path, DefaultJson());
        }
        var original = File.ReadAllText(path);
        var config = Parse(original, out var migrated, legacyMethod);
        if (migrated is not null)
        {
            if (config.MigratedLegacySpawnSettings)
            {
                var backup = path + ".before-shared-spawns.bak";
                for (var index = 1; File.Exists(backup); index++) backup = path + ".before-shared-spawns.bak." + index;
                File.Copy(path, backup, false);
            }
            var temporary = path + ".efb-migration.tmp";
            File.WriteAllText(temporary, migrated);
            File.Move(temporary, path, true);
        }
        return config;
    }

    private static JsonObject Node(string json) => JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new();
    private static bool Backend(string key) => key.Equals("standalone", StringComparison.OrdinalIgnoreCase) || key.Equals("apbs", StringComparison.OrdinalIgnoreCase);
    private static bool HasLegacy(JsonObject node) => LegacyScalars.Any(key => node[key] is not null)
        || (node["spawnChances"] is JsonObject spawn && spawn.Any(p => Backend(p.Key)))
        || (node["factionSpawnChances"] is JsonObject factions && factions.Any(p => Backend(p.Key)));

    private static RarityConfiguration Read(JsonObject node)
    {
        var copy = (JsonObject)node.DeepClone();
        if (copy["factionSpawnChances"] is JsonObject factions)
        {
            var shared = new JsonObject();
            foreach (var pair in factions.Where(p => !Backend(p.Key)).ToArray())
            { shared[pair.Key] = pair.Value?.DeepClone(); factions.Remove(pair.Key); }
            factions["shared"] = shared;
        }
        var config = JsonSerializer.Deserialize<RarityConfiguration>(copy.ToJsonString(), ReadOptions) ?? new();
        config.PriceMultipliers = new(config.PriceMultipliers ?? [], StringComparer.OrdinalIgnoreCase);
        return config;
    }

    internal static RarityConfiguration Parse(string json, out string? migrated, string? legacyMethod = null)
    {
        migrated = null;
        var node = Node(json);
        var config = Read(node);
        var legacy = HasLegacy(node);
        if (legacy && legacyMethod is null)
        {
            config.LegacySpawnSettingsPending = true;
            return config; // No write until APBS detection finishes.
        }
        var method = legacyMethod?.Equals("apbs", StringComparison.OrdinalIgnoreCase) == true ? "apbs" : "standalone";
        var before = node.ToJsonString();
        var groups = node["spawnChances"] as JsonObject ?? new JsonObject();
        if (node["spawnChances"] is null) node["spawnChances"] = groups;
        foreach (var name in new[] { "pmcs", "bosses" })
        {
            var rates = groups[name] as JsonObject ?? new JsonObject();
            if (groups[name] is null) groups[name] = rates;
            foreach (var category in Categories)
                if (rates[category] is null) rates[category] = config.Chance(name == "bosses" ? "bosses" : method, category);
        }
        groups.Remove("standalone"); groups.Remove("apbs");
        var factions = node["factionSpawnChances"] as JsonObject ?? new JsonObject();
        if (node["factionSpawnChances"] is null) node["factionSpawnChances"] = factions;
        if (factions[method] is JsonObject selected)
            foreach (var pair in selected)
                if (factions[pair.Key] is null) factions[pair.Key] = pair.Value?.DeepClone();
        factions.Remove("standalone"); factions.Remove("apbs");
        foreach (var group in BotSpawnGroups.Expanded)
            if (factions[group] is null) factions[group] = new JsonObject();
        foreach (var name in BotSpawnGroups.Expanded)
            if (factions[name] is JsonObject rates)
                foreach (var category in Categories)
                    if (rates[category] is null) rates[category] = config.FactionChance(method, name, category);
        if (node["addToNonScavFactions"] is null) node["addToNonScavFactions"] = config.AddToNonScavFactions;
        node["airdropLegendaryChancePercent"] = config.AirdropFraction * 100d;
        node["statRewardsLegendaryChancePercent"] = config.StatRewardsFraction * 100d;
        foreach (var key in LegacyScalars) node.Remove(key);
        node.Remove("enableReusableGrenadeTestOffer");
        config = Read(node);
        config.MigratedLegacySpawnSettings = legacy;
        if (node.ToJsonString() != before) migrated = node.ToJsonString(WriteOptions);
        return config;
    }

    internal static string DefaultJson()
    {
        var node = JsonSerializer.SerializeToNode(new RarityConfiguration(), WriteOptions)!.AsObject();
        foreach (var key in LegacyScalars) node.Remove(key);
        Parse(node.ToJsonString(), out var canonical, "standalone");
        return canonical ?? node.ToJsonString(WriteOptions);
    }

    public double PriceMultiplierFor(string rarity, double catalogDefault)
    {
        var multiplier = PriceMultipliers.TryGetValue(rarity, out var configured) ? configured : catalogDefault;
        return double.IsNaN(multiplier) || double.IsInfinity(multiplier) ? Math.Max(0.01d, catalogDefault) : Math.Max(0.01d, multiplier);
    }
}

public sealed class CategorySpawnChances
{
    public double? Guns { get; set; }
    public double? Melee { get; set; }
    public double? Backpacks { get; set; }
    public double? ChestRigs { get; set; }
    public double? Grenades { get; set; }
    public double? Earpieces { get; set; }
}
