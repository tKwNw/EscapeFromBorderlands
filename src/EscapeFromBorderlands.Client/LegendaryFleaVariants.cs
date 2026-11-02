using System;
using System.Collections.Generic;
using System.Linq;

namespace EscapeFromBorderlands.Client;

// Purchase previews have separate identities. Existing owned copies keep their
// original templates and deterministic roll seeds, including after resale.
internal static class LegendaryFleaVariants
{
    internal static void Expand(List<VariantDefinition> variants)
    {
        var ids = new HashSet<string>(variants.Select(v => v.TemplateId), StringComparer.Ordinal);
        foreach (var original in variants.Where(v => v.SpawnEnabled && !v.IsMysteryFleaOffer &&
            v.BackpackEffect is null && v.ChestRigEffect is null).ToArray())
        {
            var id = BackpackStorageVariants.MysteryId(original.TemplateId);
            if (!ids.Add(id)) continue;
            var preview = original.Copy();
            preview.TemplateId = id;
            preview.PresetId = BackpackStorageVariants.Id(id, 0, 0);
            preview.FleaRevealTemplateId = original.TemplateId;
            preview.IsMysteryFleaOffer = true;
            preview.SpawnEnabled = false;
            preview.SpawnWeight = 0d;
            variants.Add(preview);
        }
    }
}
