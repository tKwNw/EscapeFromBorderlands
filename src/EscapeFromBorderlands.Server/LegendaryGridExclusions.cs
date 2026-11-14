using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace EscapeFromBorderlands.Server;

internal static class LegendaryGridExclusions
{
    private static Dictionary<string, string> Sources = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Options = new() { Converters = { new StringToMongoIdConverter() } };

    // Run once after clone creation, before spawn/identification tables cache
    // compatibility. Only EFB-owned containers change; vanilla and other mods
    // keep their definitions. Copy before editing even if a caller shares grids.
    internal static void Apply(LegendaryCatalog catalog, Dictionary<MongoId, TemplateItem> items, ICloner cloner)
    {
        var definitions = catalog.Variants.Select(v => (v.TemplateId, v.SourceTemplateId))
            .Concat(catalog.LegacyAliases.Select(v => (v.TemplateId, v.SourceTemplateId)))
            .Where(v => v.TemplateId != v.SourceTemplateId && items.ContainsKey(new MongoId(v.TemplateId)))
            .DistinctBy(v => v.TemplateId).ToArray();
        Sources = definitions.ToDictionary(v => v.TemplateId, v => v.SourceTemplateId, StringComparer.Ordinal);
        var equivalents = definitions.GroupBy(v => new MongoId(v.SourceTemplateId))
            .ToDictionary(g => g.Key, g => g.Select(v => new MongoId(v.TemplateId)).ToArray());
        foreach (var definition in definitions)
        {
            var item = items[new MongoId(definition.TemplateId)];
            if (item.Properties?.Grids is not { } grids || !grids.Any(g => g.Properties?.Filters?.Any(f =>
                f.ExcludedFilter?.Any(id => equivalents.TryGetValue(id, out var clones) && clones.Any(c => !f.ExcludedFilter.Contains(c))) == true) == true)) continue;
            var copy = cloner.Clone(item.Properties)!;
            foreach (var grid in copy.Grids!)
                foreach (var filter in grid.Properties?.Filters ?? [])
                    if (filter.ExcludedFilter is { } excluded)
                        foreach (var id in excluded.ToArray())
                            if (equivalents.TryGetValue(id, out var clones)) excluded.UnionWith(clones);
            item.Properties = copy;
        }
    }

    // Existing conversion guards compare restrictions, not just dimensions.
    // Ignore only EFB exclusions redundant with a vanilla exclusion in this
    // SAME filter. Unknown IDs, standalone clone bans and all other fields stay
    // significant, so unrelated third-party restrictions still block conversion.
    internal static string FilterKey<T>(T filters)
    {
        if (Sources.Count == 0) return JsonSerializer.Serialize(filters, Options);
        var node = JsonSerializer.SerializeToNode(filters, Options);
        if (node is JsonArray list)
            foreach (var filter in list)
                if (filter?["ExcludedFilter"] is JsonArray excluded)
                {
                    var ids = excluded.Select(id => id!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    for (var index = excluded.Count - 1; index >= 0; index--)
                        if (Sources.TryGetValue(excluded[index]!.GetValue<string>(), out var source) && ids.Contains(source))
                            excluded.RemoveAt(index);
                }
        return node?.ToJsonString() ?? "null";
    }
}
