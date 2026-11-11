using System.Security.Cryptography;
using System.Text;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace EscapeFromBorderlands.Server;

internal static class HeadwearTemplates
{
    private readonly record struct Section(MongoId Source, MongoId Target);
    private static readonly Dictionary<MongoId, Dictionary<string, Section>> Sections = [];
    private static readonly HashSet<MongoId> DoubleDurability = [];
    private static readonly HashSet<string> Internal = new(StringComparer.Ordinal);
    private readonly record struct RequiredSection(string Slot, MongoId Template, double Durability);
    private static readonly Dictionary<MongoId, RequiredSection[]> Required = [];

    internal static bool IsSection(string id) => Internal.Contains(id);

    internal static void Apply(TemplateItem helmet, VariantDefinition variant, Dictionary<MongoId, TemplateItem> items,
        ICloner cloner, Action<MongoId, MongoId> copyLocale)
    {
        var key = variant.RuntimeKey;
        if (helmet.Properties is not { } props) return;
        Adjust(props, key);
        if (key == "HW084-3") props.ConflictingItems = [];
        if (key is "HW068-3" or "HW084-3" or "HW086-1" or "HW087-1" or "HW093-1")
        {
            if (key is "HW084-3" or "HW086-1") DoubleDurability.Add(helmet.Id);
            var family = variant.IsMysteryFleaOffer ? variant.FleaRevealTemplateId : variant.TemplateId;
            var map = Sections[helmet.Id] = new(StringComparer.Ordinal);
            foreach (var slot in props.Slots ?? [])
                foreach (var filter in slot.Properties?.Filters ?? [])
                {
                    if (filter.Plate is not { } source || string.IsNullOrEmpty(slot.Name) ||
                        !items.TryGetValue(source, out var plate) || plate.Properties?.ArmorClass is not > 0) continue;
                    var target = new MongoId(System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"efb:headwear-section:{family}:{slot.Name}:{source}")))[..24].ToLowerInvariant());
                    if (!items.ContainsKey(target))
                    {
                        var clone = cloner.Clone(plate)!;
                        clone.Id = target;
                        Adjust(clone.Properties!, key);
                        clone.Properties!.CanSellOnRagfair = clone.Properties.CanRequireOnRagfair = false;
                        items.Add(target, clone);
                        Internal.Add(target.ToString());
                        copyLocale(source, target);
                    }
                    filter.Plate = target;
                    filter.Filter = [.. (filter.Filter ?? []).Select(x => x == source ? target : x)];
                    filter.Filter.Add(target);
                    map[slot.Name] = new(source, target);
                }
            }
        // Read defaults after signature-specific section cloning. Only required
        // armor sections belong here, never optional visors, NVGs or attachments.
        Required[helmet.Id] = (props.Slots ?? []).Where(s => s.Required == true && !string.IsNullOrEmpty(s.Name))
            .SelectMany(s => (s.Properties?.Filters ?? []).Where(f => f.Plate.HasValue)
                .Select(f => (Slot: s.Name!, Template: f.Plate!.Value)))
            .Where(s => items.TryGetValue(s.Template, out var plate) && plate.Properties?.ArmorClass > 0)
            .DistinctBy(s => s.Slot)
            .Select(s => new RequiredSection(s.Slot, s.Template,
                items[s.Template].Properties!.MaxDurability ?? items[s.Template].Properties!.Durability ?? 0))
            .ToArray();
    }

    internal static Preset? FallbackPreset(VariantDefinition variant)
    {
        if (!Required.TryGetValue(new MongoId(variant.TemplateId), out var sections) || sections.Length == 0) return null;
        var root = new Item { Id = new MongoId(), Template = new MongoId(variant.SourceTemplateId) };
        return new Preset { Parent = root.Id, Items = [root] };
    }

    // Generation-only completion. Never scan/repair saved player inventory,
    // replace an existing plate, or restore its lost durability.
    internal static int Complete(List<Item> items) => Complete(items, static item => item);
    internal static int Complete(List<SptLootItem> items) => Complete(items, static item => new SptLootItem {
        Id = item.Id, Template = item.Template, ParentId = item.ParentId, SlotId = item.SlotId, Upd = item.Upd
    });
    private static int Complete<T>(List<T> items, Func<Item, T> toItem) where T : Item
    {
        var added = 0;
        var originalCount = items.Count;
        for (var i = 0; i < originalCount; i++)
        {
            var root = items[i];
            if (!Required.TryGetValue(root.Template, out var sections) || sections.Length == 0) continue;
            var parent = root.Id.ToString();
            foreach (var section in sections)
            {
                if (items.Any(item => item.ParentId == parent && item.SlotId == section.Slot)) continue;
                items.Add(toItem(new Item {
                    Id = new MongoId(), Template = section.Template, ParentId = parent, SlotId = section.Slot,
                    Upd = new Upd { StackObjectsCount = 1, Repairable = new UpdRepairable {
                        Durability = section.Durability, MaxDurability = section.Durability } }
                }));
                added++;
            }
        }
        return added;
    }
    private static void Adjust(TemplateItemProperties p, string key)
    {
        if (key is "HW068-3" or "HW084-3")
        {
            if (p.SpeedPenaltyPercent.HasValue) p.SpeedPenaltyPercent = 0;
            if (p.MousePenalty.HasValue) p.MousePenalty = 0;
            // Armor uses weaponErgonomicPenalty, not the weapon/mod Ergonomics stat.
            if (p.WeaponErgonomicPenalty.HasValue) p.WeaponErgonomicPenalty = 0;
            if (p.DeafStrength is not null) p.DeafStrength = "None";
        }
        if (key == "HW068-3") p.Weight = 0;
        if (key is "HW084-3" or "HW086-1") { p.MaxDurability *= 2; p.Durability *= 2; }
        if (p.ArmorClass is > 0 && key is "HW086-1" or "HW093-1") p.ArmorClass = key == "HW093-1" ? 6 : 4;
        if (key == "HW087-1" && p.RicochetParams is { } r)
            p.RicochetParams = r with { Y = Math.Max(.35f, r.Y) }; // High begins above 0.30.
    }

    internal static void Convert(Item root, MongoId target, IEnumerable<Item> contents)
    {
        var old = root.Template;
        if (old == target) return;
        root.Template = target;
        var scale = DoubleDurability.Contains(target) && !DoubleDurability.Contains(old);
        if (scale) Scale(root);
        if (!Sections.TryGetValue(target, out var map) || map.Count == 0) return;
        var parent = root.Id.ToString();
        foreach (var child in contents)
            if (child.ParentId == parent && child.SlotId is { } slot && map.TryGetValue(slot, out var section) && child.Template == section.Source)
            {
                child.Template = section.Target;
                if (scale) Scale(child);
            }
    }
    private static void Scale(Item item)
    {
        if (item.Upd is { Repairable: { } repair } upd)
            item.Upd = upd with { Repairable = repair with { Durability = repair.Durability * 2, MaxDurability = repair.MaxDurability * 2 } };
    }
}
