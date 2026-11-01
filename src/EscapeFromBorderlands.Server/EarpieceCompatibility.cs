using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace EscapeFromBorderlands.Server;

// Preserve every original headgear/headset conflict for additive clones. This
// includes one-sided entries on masks or helmet attachments, not just headsets.
internal static class EarpieceCompatibility
{
    internal static void Apply(IEnumerable<VariantDefinition> variants, Dictionary<MongoId, TemplateItem> items)
    {
        var clones = variants.Where(v => v.IsEarpiece && items.ContainsKey(new MongoId(v.TemplateId)))
            .GroupBy(v => v.SourceTemplateId).ToDictionary(g => g.Key, g => g.Select(v => new MongoId(v.TemplateId)).ToArray());
        foreach (var item in items.Values)
        {
            if (item.Properties?.ConflictingItems is not { } conflicts) continue;
            var extra = conflicts.SelectMany(id => clones.GetValueOrDefault(id.ToString()) ?? []).ToArray();
            foreach (var id in extra) conflicts.Add(id);
        }
    }
}
