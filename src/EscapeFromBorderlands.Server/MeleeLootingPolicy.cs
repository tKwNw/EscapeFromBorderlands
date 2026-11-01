using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace EscapeFromBorderlands.Server;

internal static class MeleeLootingPolicy
{
    public static void Apply(LegendaryCatalog catalog, IDictionary<MongoId, TemplateItem> items)
    {
        var protectedSources = catalog.UnlootableMeleeTemplateIds.ToHashSet(StringComparer.Ordinal);
        var lootableIds = catalog.VanillaMeleeTemplateIds
            .Where(id => !protectedSources.Contains(id))
            .Concat(catalog.Variants.Where(variant => variant.MeleeEffect is not null &&
                !protectedSources.Contains(variant.SourceTemplateId)).Select(variant => variant.TemplateId));
        foreach (var id in lootableIds)
        {
            if (!items.TryGetValue(new MongoId(id), out var item) || item.Properties is null) continue;
            // Native ItemFactory omits the Scabbard protection component when this flag is false.
            item.Properties.Unlootable = false;
            item.Properties.UnlootableFromSide = [];
        }

        var protectedIds = protectedSources.Concat(catalog.LegacyAliases
            .Where(alias => protectedSources.Contains(alias.SourceTemplateId)).Select(alias => alias.TemplateId));
        foreach (var id in protectedIds)
        {
            if (!items.TryGetValue(new MongoId(id), out var item) || item.Properties is null) continue;
            // Restore the exact vanilla protection for these two unsupported player weapon models.
            item.Properties.Unlootable = true;
            item.Properties.UnlootableFromSlot = "Scabbard";
            item.Properties.UnlootableFromSide = [PlayerSideMask.Bear, PlayerSideMask.Usec, PlayerSideMask.Savage];
        }
    }
}
