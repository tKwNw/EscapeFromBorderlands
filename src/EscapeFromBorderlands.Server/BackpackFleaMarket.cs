using HarmonyLib;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;

namespace EscapeFromBorderlands.Server;

internal sealed class BackpackFleaRolls
{
    private readonly Dictionary<string, VariantDefinition> _variants;
    private readonly Dictionary<string, WeightedSpawnChoices> _layouts;
    private readonly Dictionary<string, string> _revealedTemplates;
    private readonly HashSet<MongoId> _legendaryIds;
    private readonly HashSet<MongoId> _offerIds;
    private readonly Dictionary<MongoId, MongoId> _fixedById;
    private readonly Dictionary<MongoId, WeightedSpawnChoices> _layoutById;
    private readonly bool _tradingEnabled;

    internal BackpackFleaRolls(IEnumerable<VariantDefinition> definitions, bool tradingEnabled)
    {
        _tradingEnabled = tradingEnabled;
        _variants = definitions.ToDictionary(v => v.TemplateId, StringComparer.Ordinal);
        _legendaryIds = _variants.Keys.Select(id => new MongoId(id)).ToHashSet();
        _layouts = _variants.Values.Where(v => (v.BackpackEffect is not null || v.ChestRigEffect is not null) &&
                v.StorageRollVersion == BackpackStorageVariants.CurrentLayoutVersion && !v.IsMysteryFleaOffer && v.SpawnEnabled)
            .GroupBy(v => v.StorageFamilyId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new WeightedSpawnChoices(g.ToArray()), StringComparer.Ordinal);
        _revealedTemplates = _variants.Values.Where(v => v.IsMysteryFleaOffer &&
                _variants.TryGetValue(v.FleaRevealTemplateId, out var target) && !target.IsMysteryFleaOffer && target.SpawnEnabled)
            .ToDictionary(v => v.TemplateId, v => v.FleaRevealTemplateId, StringComparer.Ordinal);
        _fixedById = _revealedTemplates.ToDictionary(p => new MongoId(p.Key), p => new MongoId(p.Value));
        _layoutById = _variants.Values.Where(v => v.IsMysteryFleaOffer && _layouts.ContainsKey(v.StorageFamilyId))
            .ToDictionary(v => new MongoId(v.TemplateId), v => _layouts[v.StorageFamilyId]);
        _offerIds = _tradingEnabled ? _fixedById.Keys.Concat(_layoutById.Keys).ToHashSet() : [];
    }

    // Only the mystery identity generates NPC stock. Known player-owned copies
    // can still be listed normally; this does not change their flea flags.
    internal bool AllowGeneratedOffer(string templateId) =>
        !HeadwearTemplates.IsSection(templateId) && (!_variants.TryGetValue(templateId, out var variant) ||
        _tradingEnabled && variant.IsMysteryFleaOffer &&
            (_revealedTemplates.ContainsKey(templateId) || _layouts.ContainsKey(variant.StorageFamilyId)));
    internal bool AllowGeneratedOffer(MongoId templateId) =>
        !HeadwearTemplates.IsSection(templateId.ToString()) && (!_legendaryIds.Contains(templateId) || _offerIds.Contains(templateId));

    internal string PurchasedTemplate(string templateId, Func<double> randomUnit)
    {
        if (_revealedTemplates.TryGetValue(templateId, out var revealed)) return revealed;
        if (!_variants.TryGetValue(templateId, out var offer) || !offer.IsMysteryFleaOffer ||
            !_layouts.TryGetValue(offer.StorageFamilyId, out var layouts)) return templateId;
        return layouts.Pick(Math.Clamp(randomUnit(), 0d, 1d)).Variant.TemplateId;
    }

    internal void Reveal(AddItemsDirectRequest request, Func<double> randomUnit)
    {
        // TradeHelper has already cloned stock, given each purchased bag its
        // own root ID and split multi-buys. Keep the native IDs, children,
        // callback, payment and failed-purchase handling intact. Convert before
        // InventoryHelper checks stash placement, so it validates the real bag.
        if (request.ItemsWithModsToAdd is not { } groups) return;
        // Concrete collections already own the records. Materialize a
        // deferred projection exactly once before changing any roots.
        var items = groups is List<List<Item>> or List<Item>[] ? groups : groups.ToList();
        foreach (var group in items)
        {
            if (group.Count == 0) continue;
            var root = group[0];
            if (_fixedById.TryGetValue(root.Template, out var fixedId)) root.Template = fixedId;
            else if (_layoutById.TryGetValue(root.Template, out var choices))
                root.Template = choices.Pick(Math.Clamp(randomUnit(), 0d, 1d)).Id;
        }
        if (!ReferenceEquals(items, groups)) request.ItemsWithModsToAdd = items;
    }
}

internal static class BackpackFleaMarket
{
    private static BackpackFleaRolls _rolls = null!;
    private static bool _installed;

    internal static void Install(IEnumerable<VariantDefinition> definitions, bool tradingEnabled)
    {
        _rolls = new BackpackFleaRolls(definitions, tradingEnabled);
        if (_installed) return;
        var harmony = new Harmony("com.tkw.escapefromborderlands.server.backpack-flea");
        harmony.Patch(AccessTools.Method(typeof(RagfairOfferGenerator), "CreateOffersFromAssort"),
            prefix: new HarmonyMethod(typeof(BackpackFleaMarket), nameof(Generate)));
        harmony.Patch(AccessTools.Method(typeof(InventoryHelper), nameof(InventoryHelper.AddItemsToStash)),
            prefix: new HarmonyMethod(typeof(BackpackFleaMarket), nameof(BeforeAdd)));
        _installed = true;
    }

    private static bool Generate(List<Item> assortItemWithChildren)
    {
        if (assortItemWithChildren.Count == 0) return true;
        if (!_rolls.AllowGeneratedOffer(assortItemWithChildren[0].Template)) return false;
        HeadwearTemplates.Complete(assortItemWithChildren);
        return true;
    }

    private static void BeforeAdd(AddItemsDirectRequest request)
    {
        using var timing = ServerPerformanceTrace.Measure("flea purchase reveal");
        _rolls.Reveal(request, Random.Shared.NextDouble);
    }
}
