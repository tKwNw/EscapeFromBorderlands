namespace EscapeFromBorderlands.Server;

internal sealed class GrenadeSpawnPolicy
{
    private readonly HashSet<string> AllIds;
    private readonly Dictionary<string, string[]> BySource;
    private readonly Dictionary<string, double> VariantWeights;

    internal GrenadeSpawnPolicy(IEnumerable<VariantDefinition> definitions)
    {
        var grenades = definitions.Where(v => v.GrenadeChargesPerRaid > 0).ToArray();
        AllIds = grenades.Select(v => v.TemplateId).ToHashSet(StringComparer.Ordinal);
        var active = grenades.Where(v => v.SpawnEnabled).ToArray();
        BySource = active.GroupBy(v => v.SourceTemplateId).ToDictionary(g => g.Key, g => g.Select(v => v.TemplateId).ToArray(), StringComparer.Ordinal);
        VariantWeights = active.ToDictionary(v => v.TemplateId, v => v.SpawnWeight, StringComparer.Ordinal);
    }

    internal Dictionary<string, double> Pool(IReadOnlyDictionary<string, double> original, RarityConfiguration config, string pmcMethod, string role, bool isPmc)
    {
        // Never modify the native/APBS cache or unrelated item weights. A source
        // removed by its loot filters cannot produce a legendary replacement.
        var result = original.Where(p => !AllIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var fraction = CustomBotSpawning.UsesPostGeneration(role) ? 0d : config.BotFraction(pmcMethod, role, "grenades");
        foreach (var pair in CategorySpawnPolicy.Weights(result, BySource, fraction, VariantWeights)) result[pair.Key] = pair.Value;
        return result;
    }
}
