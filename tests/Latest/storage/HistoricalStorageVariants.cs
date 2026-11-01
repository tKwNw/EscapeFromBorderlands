using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using EscapeFromBorderlands.Client;
namespace EscapeFromBorderlands.PreLargest;

// Persistent template layouts let native inventory/save code validate the same
// capacity on both sides. Original IDs keep their original four-boon algorithm.
internal static class HistoricalStorageVariants
{
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
            for (var rows = 0; rows <= 2; rows++)
            for (var columns = 0; columns <= 2; columns++)
            {
                var id = Id(original.TemplateId, rows, columns);
                if (ids.Contains(id)) continue;
                var copy = original.Copy();
                copy.TemplateId = id;
                copy.PresetId = Id(id, 0, 0);
                copy.SpawnEnabled = true;
                copy.StorageRollVersion = 1;
                copy.StorageFamilyId = original.TemplateId;
                copy.StorageRows = rows;
                copy.StorageColumns = columns;
                // C(8,4), C(8,3)/2 and C(8,2)/4, divided by seven:
                // exactly the layout marginal of choosing four of ten boons.
                copy.SpawnWeight = rows == 0 && columns == 0 ? 10d : rows == 0 || columns == 0 ? 4d : 1d;
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

    internal static string MysteryId(string family) => Id("flea-mystery:" + family, 0, 0);

    internal static string Id(string family, int rows, int columns)
    {
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes("efb:backpack-storage:v1:" + family + ":" + rows + ":" + columns));
        return BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant();
    }
}
