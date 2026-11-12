using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services;

namespace EscapeFromBorderlands.Server;

// Only explicit EFB preview identities qualify. Concrete/legacy items never
// reroll. Instance IDs and all inventory records stay intact, so the client
// derives the normal four deterministic bonuses from the revealed template.
internal sealed class OwnedLegendaryRolls
{
    private readonly Dictionary<MongoId, WeightedSpawnChoices> _choices = [];

    internal OwnedLegendaryRolls(IEnumerable<VariantDefinition> definitions, Dictionary<MongoId, TemplateItem> templates)
    {
        var variants = definitions.ToArray();
        var concrete = variants.Where(v => !v.IsMysteryFleaOffer && v.SpawnEnabled).ToArray();
        foreach (var preview in variants.Where(v => v.IsMysteryFleaOffer))
        {
            var id = new MongoId(preview.TemplateId);
            if (!templates.TryGetValue(id, out var source)) continue;
            var candidates = concrete.Where(v =>
                (!string.IsNullOrEmpty(preview.FleaRevealTemplateId)
                    ? v.TemplateId == preview.FleaRevealTemplateId
                    : !string.IsNullOrEmpty(preview.StorageFamilyId) && v.StorageFamilyId == preview.StorageFamilyId &&
                      v.StorageRollVersion == BackpackStorageVariants.CurrentLayoutVersion) &&
                templates.TryGetValue(new MongoId(v.TemplateId), out var target) && Fits(source, target)).ToArray();
            if (candidates.Length > 0) _choices[id] = new(candidates);
        }
    }

    private static bool Fits(TemplateItem source, TemplateItem target)
    {
        if (source.Properties is not { } from || target.Properties is not { } to) return false;
        // A previously acquired preview can already contain items. Never
        // shrink/remove a grid or change its restrictions during identification.
        var grids = to.Grids?.ToArray() ?? [];
        foreach (var grid in from.Grids ?? [])
        {
            var match = grids.FirstOrDefault(g => g.Name == grid.Name);
            if (grid.Properties is not { } old || match?.Properties is not { } next ||
                next.CellsH.GetValueOrDefault() < old.CellsH.GetValueOrDefault() ||
                next.CellsV.GetValueOrDefault() < old.CellsV.GetValueOrDefault() ||
                JsonSerializer.Serialize(next.Filters) != JsonSerializer.Serialize(old.Filters)) return false;
        }
        return true;
    }

    internal int Identify(IEnumerable<Item>? items)
    {
        if (items is null) return 0;
        var changed = 0;
        foreach (var item in items)
        {
            if (item is null || !_choices.TryGetValue(item.Template, out var choices)) continue;
            // Repeat delivery attempts/reloads resolve a given preview instance
            // identically without consuming spawn RNG or retaining a global cache.
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"efb:identify:{item.Id}:{item.Template}"));
            var sample = BinaryPrimitives.ReadUInt32LittleEndian(hash) / 4294967296d;
            item.Template = choices.Pick(sample).Id;
            changed++;
        }
        return changed;
    }
}

internal static class OwnedLegendaryIdentification
{
    private static OwnedLegendaryRolls _rolls = null!;
    private static bool _installed;

    internal static void Install(IEnumerable<VariantDefinition> definitions, Dictionary<MongoId, TemplateItem> templates)
    {
        _rolls = new(definitions, templates);
        if (_installed) return;
        var harmony = new Harmony("com.tkw.escapefromborderlands.server.identification");
        harmony.Patch(AccessTools.Method(typeof(ProfileController), nameof(ProfileController.GetCompleteProfile)),
            postfix: new HarmonyMethod(typeof(OwnedLegendaryIdentification), nameof(ProfileLoaded)));
        harmony.Patch(AccessTools.Method(typeof(InventoryHelper), nameof(InventoryHelper.AddItemsToStash)),
            prefix: new HarmonyMethod(typeof(OwnedLegendaryIdentification), nameof(BeforeAddMany)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(InventoryHelper), "AddItemToStash"),
            prefix: new HarmonyMethod(typeof(OwnedLegendaryIdentification), nameof(BeforeAddOne)));
        harmony.Patch(AccessTools.Method(typeof(CircleOfCultistService), "AddRewardsToCircleContainer"),
            prefix: new HarmonyMethod(typeof(OwnedLegendaryIdentification), nameof(CultistRewards)));
        harmony.Patch(AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.EndLocalRaid)),
            prefix: new HarmonyMethod(typeof(OwnedLegendaryIdentification), nameof(RaidReturned)));
        _installed = true;
    }

    // The profile is repaired before the client constructs its stash. There is
    // no per-frame inventory walk and no change to trader/flea preview stock.
    private static void ProfileLoaded(List<PmcData>? __result)
    {
        if (__result is null) return;
        foreach (var profile in __result) _rolls.Identify(profile?.Inventory?.Items);
    }

    private static void BeforeAddMany(AddItemsDirectRequest request)
    {
        if (request.ItemsWithModsToAdd is not { } groups) return;
        var items = groups is List<List<Item>> or List<Item>[] ? groups : groups.ToList();
        foreach (var group in items) _rolls.Identify(group);
        if (!ReferenceEquals(items, groups)) request.ItemsWithModsToAdd = items;
    }

    private static void BeforeAddOne(AddItemDirectRequest request) => _rolls.Identify(request.ItemWithModsToAdd);
    private static void CultistRewards(List<List<Item>> rewards)
    {
        foreach (var group in rewards) _rolls.Identify(group);
    }
    private static void RaidReturned(EndLocalRaidRequestData request) => _rolls.Identify(request.Results?.Profile?.Inventory?.Items);
}
