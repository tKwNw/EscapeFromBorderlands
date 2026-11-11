using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace EscapeFromBorderlands.Server;

// Stat Rewards selects templates uniformly, so exposing every EFB clone there
// would overwhelm ordinary rewards. Filter only its random equipment pools,
// then roll once on each successfully generated supported reward root.
internal sealed class StatRewardsLegendaryRolls
{
    private readonly HashSet<string> Known;
    private readonly Dictionary<string, VariantDefinition[]> Variants;
    internal StatRewardsLegendaryRolls(IEnumerable<VariantDefinition> definitions,
        IEnumerable<LegacyAliasDefinition> aliases, IReadOnlySet<string> registered)
    {
        var all = definitions.ToArray();
        Known = all.Select(v => v.TemplateId).Concat(aliases.Select(v => v.TemplateId)).ToHashSet(StringComparer.Ordinal);
        Variants = all.Where(v => registered.Contains(v.TemplateId) && v.SpawnEnabled && !v.IsMysteryFleaOffer &&
                double.IsFinite(v.SpawnWeight) && v.SpawnWeight > 0 &&
                v.WeaponClass is not "grenadeLauncher" and not "specialWeapon")
            .DistinctBy(v => v.TemplateId).GroupBy(v => v.SourceTemplateId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.TemplateId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }
    internal List<MongoId> Filter(string category, List<MongoId> pool) => category is "gear" or "_weapons_with_presets" &&
        pool.Any(id => Known.Contains(id.ToString())) ? pool.Where(id => !Known.Contains(id.ToString())).ToList() : pool;

    internal void Convert(List<Item> items, int before, RarityConfiguration config, Func<double> randomUnit)
    {
        if (!config.Enabled || config.StatRewardsFraction <= 0 || before < 0 || before >= items.Count) return;
        var added = items.Skip(before).ToArray();
        var ids = added.Select(i => i.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        var roots = added.Where(i => i.ParentId is null || !ids.Contains(i.ParentId)).ToArray();
        if (roots.Length != 1) return;
        var root = roots[0];
        if ((root.Upd?.StackObjectsCount ?? 1d) != 1d || !Variants.TryGetValue(root.Template.ToString(), out var variants)) return;
        if (config.StatRewardsFraction < 1d && randomUnit() >= config.StatRewardsFraction) return;
        var pick = Math.Clamp(randomUnit(), 0d, 1d) * variants.Sum(v => v.SpawnWeight);
        var selected = variants[^1];
        foreach (var variant in variants)
        {
            pick -= variant.SpawnWeight;
            if (pick < 0d) { selected = variant; break; }
        }
        // Change only the root template and an incompatible saved fire mode.
        // Native IDs, attachments, armor inserts, durability and FIR stay intact.
        var state = root.Upd;
        if (selected.FireModes.Count > 0 && state is { FireMode: { } mode } &&
            !selected.FireModes.Contains(mode.FireMode ?? "", StringComparer.OrdinalIgnoreCase))
            state = state with { FireMode = mode with { FireMode = selected.FireModes[0] } };
        root.Template = new MongoId(selected.TemplateId);
        root.Upd = state;
    }
}

[Injectable(InjectionType.Singleton, null, int.MaxValue)]
public sealed class StatRewardsCompatibility(ISptLogger<StatRewardsCompatibility> logger, DatabaseService database) : IOnLoad
{
    private readonly ReleaseLog releaseLog = new(logger);

    public Task OnLoad()
    {
        var config = RarityConfiguration.Load();
        if (!config.Enabled) return Task.CompletedTask;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("StatRewards.Services.RewardGenerator", false)).FirstOrDefault(t => t is not null);
        if (type is null) return Task.CompletedTask;
        var catalog = LegendaryCatalog.Load();
        var registered = database.GetItems().Keys.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        StatRewardsIntegration.Configure(catalog, registered, config, message => releaseLog.Warning(message));
        if (StatRewardsIntegration.Install(type))
            releaseLog.Info($"[Escape From Borderlands] Stat Rewards compatibility enabled: {config.StatRewardsFraction * 100d:0.###}% per supported random equipment reward.");
        return Task.CompletedTask;
    }
}

internal static class StatRewardsIntegration
{
    private const string HarmonyId = "com.tKwNw.escapefromborderlands.server.statrewards";
    private static StatRewardsLegendaryRolls? Rolls;
    private static RarityConfiguration Config = new();
    private static Action<string>? Warning;
    private static bool Installed, ReportedFailure;
    internal static void Configure(LegendaryCatalog catalog, IReadOnlySet<string> registered, RarityConfiguration config, Action<string> warning)
    { Rolls = new(catalog.Variants, catalog.LegacyAliases, registered); Config = config; Warning = warning; ReportedFailure = false; }
    internal static bool Install(Type generator)
    {
        if (Installed) return true;
        var pool = AccessTools.DeclaredMethod(generator, "GetCategoryPool", [typeof(string)]);
        var weapon = AccessTools.DeclaredMethod(generator, "GenerateWeaponPresetReward", [typeof(List<Item>)]);
        var gear = AccessTools.DeclaredMethod(generator, "GenerateGearReward", [typeof(List<Item>)]);
        if (pool?.ReturnType != typeof(List<MongoId>) || weapon?.ReturnType != typeof(void) || gear?.ReturnType != typeof(void))
        { Report(new MissingMethodException("Unsupported Stat Rewards random equipment methods; no compatibility hooks installed.")); return false; }
        var harmony = new Harmony(HarmonyId);
        try
        {
            harmony.Patch(pool, postfix: new HarmonyMethod(typeof(StatRewardsIntegration), nameof(Filter)) { priority = Priority.Last });
            foreach (var method in new[] { weapon, gear })
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(StatRewardsIntegration), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(StatRewardsIntegration), nameof(After)) { priority = Priority.Last });
            Installed = true;
            return true;
        }
        catch (Exception e)
        {
            harmony.UnpatchSelf();
            Report(e); return false;
        }
    }
    private static void Filter(string __0, ref List<MongoId> __result)
    { if (Config.Enabled && Rolls is not null) __result = Rolls.Filter(__0, __result); }
    private static void Before(List<Item> __0, out int __state) => __state = __0.Count;
    private static void After(List<Item> __0, int __state)
    {
        try { Rolls?.Convert(__0, __state, Config, Random.Shared.NextDouble); }
        catch (Exception e) { Report(e); }
    }
    private static void Report(Exception e)
    {
        if (ReportedFailure) return;
        ReportedFailure = true;
        Warning?.Invoke("[Escape From Borderlands] Stat Rewards compatibility unavailable: " + (e.InnerException ?? e).Message);
    }
}
