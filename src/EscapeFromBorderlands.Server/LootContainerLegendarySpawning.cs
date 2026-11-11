using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace EscapeFromBorderlands.Server;

// Convert only native-generated roots inside static raid containers. This hook
// does not run for loose loot, bot inventories, airdrops or player storage.
internal sealed class LootContainerLegendaryRolls
{
    private readonly Dictionary<MongoId, (VariantDefinition[] Variants, string Category)> BySource;
    private readonly IReadOnlyDictionary<MongoId, TemplateItem> Templates;
    private readonly ConditionalWeakTable<SpawnpointTemplate, Attempt> Attempts = new();
    private sealed class Attempt { internal bool Done; }

    internal LootContainerLegendaryRolls(IEnumerable<VariantDefinition> definitions,
        IReadOnlyDictionary<MongoId, TemplateItem> templates)
    {
        Templates = templates;
        BySource = definitions.Where(v => v.SpawnEnabled && !v.IsMysteryFleaOffer &&
                double.IsFinite(v.SpawnWeight) && v.SpawnWeight > 0d &&
                v.WeaponClass is not "grenadeLauncher" and not "specialWeapon")
            .DistinctBy(v => v.TemplateId).GroupBy(v => new MongoId(v.SourceTemplateId))
            .ToDictionary(g => g.Key, g =>
            {
                var variants = g.OrderBy(v => v.TemplateId, StringComparer.Ordinal).ToArray();
                return (variants, LegendarySpawnCategories.For(variants[0]));
            });
    }

    internal int Roll(StaticContainerData? container, IEnumerable<StaticForced>? forced,
        RarityConfiguration config, Func<double> random)
    {
        if (!config.Enabled || container?.Template is not { IsContainer: true, Items: not null } template ||
            string.IsNullOrEmpty(template.Root)) return 0;
        var attempt = Attempts.GetValue(template, _ => new Attempt());
        lock (attempt)
        {
            if (attempt.Done) return 0;
            attempt.Done = true;
            // Native loot uses deferred ToLootItem projections. Own the actual
            // returned records so later serialization keeps the changed tpl.
            if (!RarityConfiguration.Categories.Any(c => config.LootContainerFraction(c) > 0d)) return 0;
            var items = Materialize(template.Items);
            template.Items = items;
            var containerItem = items.FirstOrDefault(i => i.Id.ToString() == template.Root);
            if (containerItem is null || !Templates.TryGetValue(containerItem.Template, out var containerDb)) return 0;
            HashSet<MongoId>? forcedTpls = null;
            if (forced is not null)
                foreach (var item in forced)
                    if (item.ContainerId == template.Id)
                        (forcedTpls ??= new()).Add(item.ItemTpl);

            // Match and cache within this generated container only. Optional
            // mods can change the live template database between requests.
            Dictionary<(MongoId Source, string? Slot, string Mode), WeightedSpawnChoices>? choices = null;
            Dictionary<object, string>? filters = null;
            HashSet<MongoId>? ancestry = null;
            var converted = 0;
            foreach (var item in items)
            {
                if (item.ParentId != template.Root || (item.Upd?.StackObjectsCount ?? 1d) != 1d ||
                    forcedTpls?.Contains(item.Template) == true || !BySource.TryGetValue(item.Template, out var candidates)) continue;
                var fraction = config.LootContainerFraction(candidates.Category);
                if (fraction <= 0d || !Templates.TryGetValue(item.Template, out var source) || source.Properties?.QuestItem == true) continue;
                // Preserve 4.0.13's chance draw before compatibility checks,
                // including incompatible items. Misses need no template work.
                if (fraction < 1d && random() >= fraction) continue;
                var key = (item.Template, item.SlotId, item.Upd?.FireMode?.FireMode ?? "");
                if (choices is not null && choices.TryGetValue(key, out var cached))
                {
                    if (cached.IsEmpty) continue;
                    var selected = cached.Pick(random());
                    if (selected.Variant.IsHeadwear) HeadwearTemplates.Convert(item, selected.Id, items);
                    else item.Template = selected.Id;
                    converted++;
                    continue;
                }

                var first = 0;
                while (first < candidates.Variants.Length && !Compatible(candidates.Variants[first], item, source, containerDb, ref filters, ref ancestry)) first++;
                if (first == candidates.Variants.Length)
                {
                    (choices ??= new())[key] = WeightedSpawnChoices.Empty;
                    continue;
                }
                var eligible = new List<VariantDefinition> { candidates.Variants[first] };
                for (var i = first + 1; i < candidates.Variants.Length; i++)
                    if (Compatible(candidates.Variants[i], item, source, containerDb, ref filters, ref ancestry)) eligible.Add(candidates.Variants[i]);
                var table = new WeightedSpawnChoices(eligible.ToArray());
                (choices ??= new())[key] = table;
                var chosen = table.Pick(random());
                if (chosen.Variant.IsHeadwear) HeadwearTemplates.Convert(item, chosen.Id, items);
                else item.Template = chosen.Id;
                converted++;
            }
            return converted;
        }
    }

    private static IEnumerable<T> Materialize<T>(IEnumerable<T> items) => items is T[] or List<T> ? items : items.ToArray();

    private static string FilterKey<T>(T value, ref Dictionary<object, string>? cache)
    {
        if (value is null) return "null";
        cache ??= new(ReferenceEqualityComparer.Instance);
        if (cache.TryGetValue(value, out var key)) return key;
        return cache[value] = JsonSerializer.Serialize(value);
    }

    private bool Compatible(VariantDefinition variant, Item item, TemplateItem source, TemplateItem container,
        ref Dictionary<object, string>? filters, ref HashSet<MongoId>? ancestry)
    {
        if (variant.ChestRigEffect?.SoftArmorClass > 0 ||
            !Templates.TryGetValue(new MongoId(variant.TemplateId), out var target) ||
            source.Properties is not { } oldProps || target.Properties is not { } nextProps ||
            oldProps.Width != nextProps.Width || oldProps.Height != nextProps.Height) return false;
        var mode = item.Upd?.FireMode?.FireMode;
        if (!string.IsNullOrEmpty(mode) && variant.FireModes.Count > 0 && !variant.FireModes.Contains(mode, StringComparer.OrdinalIgnoreCase)) return false;
        var targetGrids = nextProps.Grids?.ToArray() ?? [];
        foreach (var old in oldProps.Grids ?? [])
        {
            var next = targetGrids.FirstOrDefault(g => g.Name == old.Name)?.Properties;
            if (old.Properties is not { } original || next is null ||
                next.CellsH.GetValueOrDefault() < original.CellsH.GetValueOrDefault() ||
                next.CellsV.GetValueOrDefault() < original.CellsV.GetValueOrDefault() ||
                FilterKey(next.Filters, ref filters) != FilterKey(original.Filters, ref filters)) return false;
        }
        var grid = container.Properties?.Grids?.FirstOrDefault(g => g.Name == item.SlotId)?.Properties;
        if (grid?.Filters is null) return true;
        var indexed = false;
        foreach (var filter in grid.Filters)
        {
            if (!indexed)
            {
                (ancestry ??= new()).Clear();
                var id = new MongoId(variant.TemplateId);
                while (ancestry.Add(id) && Templates.TryGetValue(id, out var current)) id = current.Parent;
                indexed = true;
            }
            if (filter.ExcludedFilter?.Overlaps(ancestry!) == true) return false;
            if (filter.Filter is { Count: > 0 } allowed && !allowed.Overlaps(ancestry!)) return false;
        }
        return true;
    }
}

internal static class LegendarySpawnCategories
{
    internal static string For(VariantDefinition v) => v.WeaponClass is "armoredHeadwear" or "unarmoredHeadwear" ? v.WeaponClass :
        v.GrenadeChargesPerRaid > 0 ? "grenades" : v.IsEarpiece ? "earpieces" : v.MeleeEffect is not null ? "melee" :
        v.BackpackEffect is not null ? "backpacks" : v.ChestRigEffect is not null ? "chestRigs" : "guns";
}

internal static class LootContainerLegendarySpawning
{
    private static LootContainerLegendaryRolls? Rolls;
    private static RarityConfiguration Configuration = new();
    private static Action<string> Warning = _ => { };
    private static bool Installed, ReportedFailure;

    internal static void SelectConfiguration(RarityConfiguration config) => Configuration = config;

    internal static void Install(IEnumerable<VariantDefinition> definitions, IReadOnlyDictionary<MongoId, TemplateItem> templates,
        RarityConfiguration config, Action<string> warning)
    {
        Rolls = new LootContainerLegendaryRolls(definitions, templates); Configuration = config; Warning = warning;
        if (Installed) return;
        new Harmony("com.tKwNw.escapefromborderlands.server.lootcontainers").Patch(
            AccessTools.Method(typeof(LocationLootGenerator), "AddLootToContainer"),
            postfix: new HarmonyMethod(typeof(LootContainerLegendarySpawning), nameof(AfterLoot)) { priority = Priority.Last });
        Installed = true;
    }

    private static void AfterLoot(StaticContainerData __result, IEnumerable<StaticForced> staticForced)
    {
        if (Rolls is not { } rolls || !Configuration.Enabled) return;
        var timing = ServerPerformanceTrace.Measure("static container conversion");
        try { timing.Complete(1, rolls.Roll(__result, staticForced, Configuration, Random.Shared.NextDouble)); }
        catch (Exception error)
        {
            if (ReportedFailure) return;
            ReportedFailure = true;
            Warning("[Escape From Borderlands] A loot-container conversion failed; remaining generated loot is retained. " + error.Message);
        }
    }
}
