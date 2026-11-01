namespace EscapeFromBorderlands.Server;

/// <summary>Pure pool arithmetic shared by standalone/APBS validation.</summary>
internal static class CategorySpawnPolicy
{
    public static Dictionary<string, double> Weights(
        IReadOnlyDictionary<string, double> originals,
        IReadOnlyDictionary<string, string[]> clonesBySource, double fraction,
        IReadOnlyDictionary<string, double>? cloneWeights = null)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (fraction <= 0d) return result;
        fraction = Math.Clamp(fraction, 0d, .99d);
        var eligible = originals.Where(p => p.Value > 0d && clonesBySource.TryGetValue(p.Key, out var c) && c.Length > 0).ToArray();
        var baseWeight = originals.Where(p => p.Value > 0d).Sum(p => p.Value);
        var eligibleWeight = eligible.Sum(p => p.Value);
        if (baseWeight <= 0d || eligibleWeight <= 0d) return result;
        var total = baseWeight * fraction / (1d - fraction);
        double Weight(string id) => cloneWeights is not null && cloneWeights.TryGetValue(id, out var weight) && double.IsFinite(weight) && weight > 0d ? weight : 1d;
        foreach (var source in eligible)
        {
            var clones = clonesBySource[source.Key].Distinct(StringComparer.Ordinal).ToArray();
            var sum = clones.Sum(Weight);
            foreach (var clone in clones) result[clone] = total * source.Value / eligibleWeight * Weight(clone) / sum;
        }
        return result;
    }
}
