using System.Runtime.CompilerServices;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Services;
using SPTarkov.Server.Core.Services;

namespace EscapeFromBorderlands.Server;

// Keep EFB's many database clones out of native airdrop pools. The shared
// percentage is then rolled once for each supported generated loot root,
// before native crate fitting, without replacing its ID, mods or item state.
internal sealed class AirdropLegendaryRolls
{
    private readonly Dictionary<string, string> Sources;
    private readonly Dictionary<string, WeightedSpawnChoices> Variants;
    private readonly MongoId[] EfbIds;
    private readonly ConditionalWeakTable<List<Item>, Attempt> Attempts = new();
    private sealed class Attempt { internal bool Done; }

    internal AirdropLegendaryRolls(IEnumerable<VariantDefinition> definitions, IEnumerable<LegacyAliasDefinition> aliases,
        IReadOnlySet<string>? excludedSources = null)
    {
        var all = definitions.ToArray();
        Sources = all.Select(v => (v.TemplateId, v.SourceTemplateId))
            .Concat(aliases.Select(v => (v.TemplateId, v.SourceTemplateId)))
            .DistinctBy(v => v.TemplateId).ToDictionary(v => v.TemplateId, v => v.SourceTemplateId, StringComparer.Ordinal);
        EfbIds = Sources.Keys.Select(id => new MongoId(id)).ToArray();
        Variants = all.Where(v => v.SpawnEnabled && !v.IsMysteryFleaOffer &&
                double.IsFinite(v.SpawnWeight) && v.SpawnWeight > 0d &&
                v.WeaponClass is not "grenadeLauncher" and not "specialWeapon" &&
                excludedSources?.Contains(v.SourceTemplateId) != true)
            .DistinctBy(v => v.TemplateId).GroupBy(v => v.SourceTemplateId)
            .ToDictionary(g => g.Key, g => new WeightedSpawnChoices(g.OrderBy(v => v.TemplateId, StringComparer.Ordinal).ToArray()), StringComparer.Ordinal);
    }

    internal AirdropLootRequest Prepare(AirdropLootRequest request)
    {
        // These are request-local copies; native global blacklists, forced
        // loot mappings and unrelated reward generators stay untouched.
        var blacklist = new HashSet<MongoId>(request.ItemBlacklist ?? []);
        blacklist.UnionWith(EfbIds);
        var prepared = request with { ItemBlacklist = blacklist };
        if (request.ForcedLoot is not null)
        {
            var forced = new Dictionary<MongoId, MinMax<int>>();
            foreach (var (id, amount) in request.ForcedLoot)
            {
                var source = Sources.TryGetValue(id.ToString(), out var original) ? new MongoId(original) : id;
                if (forced.TryGetValue(source, out var previous))
                    forced[source] = new MinMax<int> { Min = (int)Math.Clamp((long)previous.Min + amount.Min, 0, int.MaxValue),
                        Max = (int)Math.Clamp((long)previous.Max + amount.Max, 0, int.MaxValue) };
                else forced[source] = amount;
            }
            prepared.ForcedLoot = forced;
        }
        return prepared;
    }

    internal IEnumerable<List<Item>> Roll(IEnumerable<List<Item>> loot, RarityConfiguration config, Func<double> randomUnit)
    {
        if (!config.Enabled) return loot;
        var groups = loot as List<Item>[] ?? loot.ToArray();
        var fraction = config.AirdropFraction;
        // Keep the existing once-only evaluation of a deferred native source.
        // With conversion off there are no item markers to look up or allocate.
        if (fraction <= 0d) return groups;
        var timing = ServerPerformanceTrace.Measure("airdrop conversion");
        var converted = 0;
        foreach (var group in groups)
        {
            if (group.Count == 0) continue;
            var attempt = Attempts.GetValue(group, _ => new Attempt());
            lock (attempt)
            {
                if (attempt.Done) continue;
                attempt.Done = true;
                var root = group[0];
                // Native supported gear is individual. Retain unusual modded
                // stacks rather than creating an invalid max-stack-1 clone.
                if ((root.Upd?.StackObjectsCount ?? 1d) != 1d ||
                    !Variants.TryGetValue(root.Template.ToString(), out var variants)) continue;
                if (fraction < 1d && randomUnit() >= fraction) continue;
                var (selected, id) = variants.Pick(Math.Clamp(randomUnit(), 0d, 1d));
                if (selected.IsHeadwear) HeadwearTemplates.Convert(root, id, group); else root.Template = id;
                converted++;
                if (selected.FireModes.Count > 0 && root.Upd is { FireMode: { } mode } state &&
                    !selected.FireModes.Contains(mode.FireMode ?? "", StringComparer.OrdinalIgnoreCase))
                    root.Upd = state with { FireMode = mode with { FireMode = selected.FireModes[0] } };
            }
        }
        timing.Complete(groups.Length, converted);
        return groups;
    }
}

internal static class AirdropLegendarySpawning
{
    private static AirdropLegendaryRolls? Rolls;
    private static RarityConfiguration Config = new();
    private static bool Installed;

    internal static void Configure(IEnumerable<VariantDefinition> definitions, IEnumerable<LegacyAliasDefinition> aliases,
        RarityConfiguration config, IReadOnlySet<string>? excludedSources = null)
    { Rolls = new AirdropLegendaryRolls(definitions, aliases, excludedSources); Config = config; }

    internal static void Install(IEnumerable<VariantDefinition> definitions, IEnumerable<LegacyAliasDefinition> aliases,
        RarityConfiguration config, IReadOnlySet<string>? excludedSources = null)
    {
        Configure(definitions, aliases, config, excludedSources);
        if (Installed) return;
        var harmony = new Harmony("com.tkw.escapefromborderlands.server.airdrops");
        harmony.Patch(AccessTools.Method(typeof(AirdropService), "GetAirdropLootConfigByType"),
            postfix: new HarmonyMethod(typeof(AirdropConfigPatch), nameof(AirdropConfigPatch.Postfix)));
        harmony.Patch(AccessTools.Method(typeof(AirdropService), "GetLootThatFitsContainer"),
            prefix: new HarmonyMethod(typeof(AirdropLootPatch), nameof(AirdropLootPatch.Prefix)));
        Installed = true;
    }

    internal static AirdropLootRequest Prepare(AirdropLootRequest request) => Config.Enabled && Rolls is not null ? Rolls.Prepare(request) : request;
    internal static IEnumerable<List<Item>> Roll(IEnumerable<List<Item>> loot) => Rolls?.Roll(loot, Config, Random.Shared.NextDouble) ?? loot;
}

internal static class AirdropConfigPatch
{
    internal static void Postfix(ref AirdropLootRequest __result) => __result = AirdropLegendarySpawning.Prepare(__result);
}

internal static class AirdropLootPatch
{
    internal static void Prefix(ref IEnumerable<List<Item>> crateLootPool) => crateLootPool = AirdropLegendarySpawning.Roll(crateLootPool);
}
