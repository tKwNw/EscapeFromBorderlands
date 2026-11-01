using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EscapeFromBorderlands.Client;

// Persistent template layouts let native inventory/save code validate the same
// capacity on both sides. Original IDs keep their original four-boon algorithm.
internal static class BackpackStorageVariants
{
    internal const int CurrentLayoutVersion = 2;
    internal static void Expand(List<VariantDefinition> variants)
    {
        var ids = new HashSet<string>(variants.Select(v => v.TemplateId), StringComparer.Ordinal);
        foreach (var original in variants.Where(v => v.BackpackEffect is not null && v.StorageRollVersion == 0).ToArray())
        {
            original.SpawnEnabled = false;
            // Offers have their own identity: an old, owned bag must never be
            // mistaken for unrevealed stock and rerolled on resale or transfer.
            var offerId = MysteryId(original.TemplateId);
            if (ids.Add(offerId))
            {
                var offer = original.Copy();
                offer.TemplateId = offerId;
                offer.PresetId = Id(offerId, 0, 0);
                offer.IsMysteryFleaOffer = true;
                offer.SpawnEnabled = false;
                offer.SpawnWeight = 0d;
                offer.StorageRollVersion = 2;
                offer.StorageFamilyId = original.TemplateId;
                variants.Add(offer);
            }
            // Keep v1 templates available to saved inventories. Their smaller
            // pockets may contain items in the added cells; never shrink them.
            for (var version = 1; version <= CurrentLayoutVersion; version++)
            for (var rows = 0; rows <= 2; rows++)
            for (var columns = 0; columns <= 2; columns++)
            {
                var id = version == 1 ? Id(original.TemplateId, rows, columns) : CurrentId(original.TemplateId, rows, columns);
                if (ids.Contains(id)) continue;
                var copy = original.Copy();
                copy.TemplateId = id;
                copy.PresetId = Id(id, 0, 0);
                copy.SpawnEnabled = version == CurrentLayoutVersion && columns <= MaximumColumnRoll(original);
                copy.StorageRollVersion = version;
                copy.StorageFamilyId = original.TemplateId;
                copy.StorageRows = rows;
                copy.StorageColumns = columns;
                // C(8,4), C(8,3)/2 and C(8,2)/4, divided by seven:
                // exactly the layout marginal of choosing four of ten boons.
                copy.SpawnWeight = LayoutWeight(rows, columns);
                if (version == CurrentLayoutVersion)
                {
                    var maximum = MaximumColumnRoll(original);
                    if (columns > maximum) copy.SpawnWeight = 0d;
                    else if (columns == maximum)
                        for (var overflow = maximum + 1; overflow <= 2; overflow++) copy.SpawnWeight += LayoutWeight(rows, overflow);
                }
                copy.BackpackEffect = new BackpackEffectDefinition
                {
                    Key = original.BackpackEffect!.Key,
                    ExtraRows = original.BackpackEffect.ExtraRows + rows,
                    ExtraColumns = original.BackpackEffect.ExtraColumns + columns,
                    ExtraGrids = original.BackpackEffect.ExtraGrids,
                };
                variants.Add(copy);
                ids.Add(id);
            }
        }
    }

    internal static int MaximumColumnRoll(VariantDefinition definition) => definition.BackpackBaseColumns <= 0 ? 2 :
        Math.Max(0, Math.Min(2, 7 - definition.BackpackBaseColumns - Math.Max(0, (definition.BackpackEffect?.ExtraColumns ?? 0) - definition.StorageColumns)));
    private static double LayoutWeight(int rows, int columns) => rows == 0 && columns == 0 ? 10d : rows == 0 || columns == 0 ? 4d : 1d;

    internal static string MysteryId(string family) => Id("flea-mystery:" + family, 0, 0);

    internal static string CurrentId(string family, int rows, int columns) => HashId("v2", family, rows, columns);

    internal static string Id(string family, int rows, int columns) => HashId("v1", family, rows, columns);

    private static string HashId(string version, string family, int rows, int columns)
    {
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes("efb:backpack-storage:" + version + ":" + family + ":" + rows + ":" + columns));
        return BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant();
    }
}
