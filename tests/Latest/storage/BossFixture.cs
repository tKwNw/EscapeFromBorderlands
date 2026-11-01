using System.Collections;
using System.Globalization;
namespace EscapeFromBorderlands.Server;
internal static class BossFixture {
private static bool AddSourceRestrictedBossVariants(
        IDictionary pool,
        IReadOnlyDictionary<string, VariantDefinition[]> variantsBySource,
        double targetFraction,
        out int entriesAdded)
    {
        entriesAdded = 0;
        var eligibleSources = new List<(object Key, double Weight, VariantDefinition[] Variants)>();
        var baseWeight = 0d;

        foreach (DictionaryEntry entry in pool)
        {
            var weight = Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture);
            if (weight <= 0d)
            {
                continue;
            }

            baseWeight += weight;
            var sourceId = entry.Key.ToString() ?? string.Empty;
            if (variantsBySource.TryGetValue(sourceId, out var sourceVariants))
            {
                eligibleSources.Add((entry.Key, weight, sourceVariants));
            }
        }

        var eligibleSourceWeight = eligibleSources.Sum(entry => entry.Weight);
        if (baseWeight <= 0d || eligibleSourceWeight <= 0d)
        {
            return false;
        }

        var totalLegendaryWeight = baseWeight * targetFraction / (1d - targetFraction);
        foreach (var source in eligibleSources)
        {
            var sourceLegendaryWeight = totalLegendaryWeight * source.Weight / eligibleSourceWeight;
            var variantWeightSum = source.Variants.Sum(v => v.SpawnWeight);
            foreach (var variant in source.Variants)
            {
                var variantKey = CreateCompatiblePoolKey(source.Key, variant.TemplateId);
                if (variantKey is null)
                {
                    continue;
                }

                pool[variantKey] = sourceLegendaryWeight * variant.SpawnWeight / variantWeightSum;
                entriesAdded++;
            }
        }

        return entriesAdded > 0;
    }
private static object? CreateCompatiblePoolKey(object exampleKey, string templateId)
    {
        if (exampleKey is string)
        {
            return templateId;
        }

        try
        {
            return Activator.CreateInstance(exampleKey.GetType(), new object?[] { templateId });
        }
        catch
        {
            return null;
        }
    }
internal static bool Apply(IDictionary pool, IReadOnlyDictionary<string, VariantDefinition[]> variants, double fraction, out int added)=>AddSourceRestrictedBossVariants(pool, variants, fraction, out added);
}
