using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace EscapeFromBorderlands.Server;

internal static class ChestRigStorage
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new StringToMongoIdConverter() } };
    internal static TemplateItemProperties Apply(TemplateItemProperties original, ChestRigEffectDefinition effect, string templateId)
    {
        var node = JsonSerializer.SerializeToNode(original, Options)!.AsObject();
        var grids = node["Grids"]?.AsArray() ?? new JsonArray();
        var largest = grids.Select((g, i) => (Index: i, Area: g!["_props"]!["cellsH"]!.GetValue<int>() * g["_props"]!["cellsV"]!.GetValue<int>()))
            .OrderByDescending(x => x.Area).Select(x => x.Index).DefaultIfEmpty(-1).First();
        for (var i = 0; i < grids.Count; i++)
        {
            var props = grids[i]!["_props"]!;
            if (effect.ExpandEveryGrid)
            {
                props["cellsH"] = props["cellsH"]!.GetValue<int>() + effect.EveryGridColumns;
                props["cellsV"] = props["cellsV"]!.GetValue<int>() + effect.EveryGridRows;
            }
            if (i != largest) continue;
            props["cellsH"] = props["cellsH"]!.GetValue<int>() + effect.ExtraColumns;
            props["cellsV"] = props["cellsV"]!.GetValue<int>() + effect.ExtraRows;
        }
        foreach (var (grid, index) in effect.ExtraGrids.Select((g, i) => (g, i)))
        {
            grids.Add(new JsonObject {
                ["_id"] = templateId[..20] + index.ToString("x4"), ["_name"] = "efb_rig_extra_" + index, ["_parent"] = templateId,
                ["_props"] = new JsonObject {
                    ["cellsH"] = grid.Width, ["cellsV"] = grid.Height, ["minCount"] = 0, ["maxCount"] = 0,
                    ["filters"] = new JsonArray(new JsonObject {
                        ["Filter"] = new JsonArray(grid.Filter.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["ExcludedFilter"] = new JsonArray() })
                }
            });
        }
        node["Grids"] = grids;
        // Every dimensional change uses the native automatic layout on the client.
        // It must not reuse a prefab whose fixed sub-grid positions now overlap.
        if (effect.ExpandEveryGrid || effect.ExtraRows > 0 || effect.ExtraColumns > 0 || effect.ExtraGrids.Count > 0) node["GridLayoutName"] = null;
        return node.Deserialize<TemplateItemProperties>(Options)!;
    }
}
