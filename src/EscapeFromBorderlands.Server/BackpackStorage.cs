using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace EscapeFromBorderlands.Server;
internal static class BackpackStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new StringToMongoIdConverter() },
    };
    public static TemplateItemProperties Apply(TemplateItemProperties properties, VariantDefinition variant)
    {
        if (variant.BackpackEffect is null) return properties;
        var legacy = !variant.IsMysteryFleaOffer && variant.StorageRollVersion == 1;
        return Apply(properties, variant.BackpackEffect, variant.TemplateId,
            variant.StorageRows > 0 || variant.StorageColumns > 0,
            !variant.IsMysteryFleaOffer && (legacy || variant.StorageRollVersion >= BackpackStorageVariants.CurrentLayoutVersion), legacy);
    }

    public static TemplateItemProperties Apply(TemplateItemProperties properties, BackpackEffectDefinition effect, string templateId, bool randomizedLayout = false, bool expandLargestGrid = false, bool preserveLegacyCells = false)
    {
        var featherLight = effect.Key == "BP03-2";
        if (!featherLight && effect.ExtraRows == 0 && effect.ExtraColumns == 0 && effect.ExtraGrids.Count == 0) return properties;
        var node = JsonSerializer.SerializeToNode(properties, Options)!.AsObject();
        if (featherLight)
        {
            // Only this EFB clone changes. Native item weights keep all contents fully weighted.
            node["Weight"] = 0;
            node["mousePenalty"] = 0;
            node["speedPenaltyPercent"] = 0;
            node["weaponErgonomicPenalty"] = 0;
        }
        var grids = node["Grids"]!.AsArray();
        // Select before adding signature compartments, so a restricted gun
        // sleeve cannot displace the backpack's normal main compartment.
        var target = 0;
        if (expandLargestGrid)
        {
            long largestArea = -1;
            for (var i = 0; i < grids.Count; i++)
            {
                var props = grids[i]!["_props"]!;
                var area = (long)props["cellsH"]!.GetValue<int>() * props["cellsV"]!.GetValue<int>();
                if (area <= largestArea) continue;
                largestArea = area; target = i;
            }
        }
        void Grow(int index)
        {
            if (effect.ExtraRows > 0)
            {
                var height = grids[index]!["_props"]!["cellsV"]!.GetValue<int>();
                grids[index]!["_props"]!["cellsV"] = height + effect.ExtraRows;
            }
            if (effect.ExtraColumns > 0)
            {
                var width = grids[index]!["_props"]!["cellsH"]!.GetValue<int>();
                grids[index]!["_props"]!["cellsH"] = width + effect.ExtraColumns;
            }
        }
        // Correct owned v1 bags additively: the first pocket's old added cells
        // can already hold saved loot. Retain them while also granting the
        // promised bonus to the actual largest original compartment.
        if (preserveLegacyCells && target != 0) Grow(0);
        Grow(target);
        foreach (var (grid, index) in effect.ExtraGrids.Select((g, i) => (g, i)))
        {
            grids.Add(new JsonObject
            {
                ["_id"] = templateId[..20] + index.ToString("x4"),
                ["_name"] = "efb_extra_" + index,
                ["_parent"] = templateId,
                ["_props"] = new JsonObject
                {
                    ["cellsH"] = grid.Width, ["cellsV"] = grid.Height,
                    ["minCount"] = 0, ["maxCount"] = 0,
                    ["filters"] = grid.FirearmsOnly ? new JsonArray(new JsonObject { ["Filter"] = new JsonArray("5422acb9af1c889c16000029"), ["ExcludedFilter"] = new JsonArray() }) : new JsonArray()
                }
            });
        }
        // The client builds a layout for added grids; it does not reuse a prefab
        // which only has views for the original number of compartments.
        if (effect.ExtraGrids.Count > 0 || effect.ExtraColumns > 0 || randomizedLayout) node["GridLayoutName"] = null;
        return node.Deserialize<TemplateItemProperties>(Options)!;
    }
}
