using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace EscapeFromBorderlands.Server;

// Stat Rewards selects templates uniformly, so exposing every EFB clone there
// would overwhelm ordinary rewards. Filter only its random equipment pools,
// then roll once on each successfully generated supported reward root.
internal sealed class StatRewardsLegendaryRolls
{
    private readonly HashSet<MongoId> Known;
    private readonly Dictionary<MongoId, WeightedSpawnChoices> Variants;
    internal StatRewardsLegendaryRolls(IEnumerable<VariantDefinition> definitions,
        IEnumerable<LegacyAliasDefinition> aliases, IReadOnlySet<string> registered)
    {
        var all = definitions.ToArray();
        Known = all.Select(v => new MongoId(v.TemplateId)).Concat(aliases.Select(v => new MongoId(v.TemplateId))).ToHashSet();
        Variants = all.Where(v => registered.Contains(v.TemplateId) && v.SpawnEnabled && !v.IsMysteryFleaOffer &&
                double.IsFinite(v.SpawnWeight) && v.SpawnWeight > 0 &&
                v.WeaponClass is not "grenadeLauncher" and not "specialWeapon")
            .DistinctBy(v => v.TemplateId).GroupBy(v => new MongoId(v.SourceTemplateId))
            .ToDictionary(g => g.Key, g => new WeightedSpawnChoices(g.OrderBy(v => v.TemplateId, StringComparer.Ordinal).ToArray()));
    }
    internal List<MongoId> Filter(string category, List<MongoId> pool) => category is "gear" or "_weapons_with_presets" &&
        pool.Exists(Known.Contains) ? pool.FindAll(id => !Known.Contains(id)) : pool;

    internal void Convert(List<Item> items, int before, RarityConfiguration config, Func<double> randomUnit)
    {
        var fraction = config.StatRewardsFraction;
        if (!config.Enabled || fraction <= 0 || before < 0 || before >= items.Count) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = before; i < items.Count; i++) ids.Add(items[i].Id.ToString());
        Item? root = null;
        for (var i = before; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ParentId is { } parent && ids.Contains(parent)) continue;
            if (root is not null) return;
            root = item;
        }
        if (root is null || (root.Upd?.StackObjectsCount ?? 1d) != 1d || !Variants.TryGetValue(root.Template, out var variants)) return;
        if (fraction < 1d && randomUnit() >= fraction) return;
        var (selected, id) = variants.Pick(Math.Clamp(randomUnit(), 0d, 1d));
        // Change only the root template and an incompatible saved fire mode.
        // Native IDs, attachments, armor inserts, durability and FIR stay intact.
        var state = root.Upd;
        if (selected.FireModes.Count > 0 && state is { FireMode: { } mode } &&
            !selected.FireModes.Contains(mode.FireMode ?? "", StringComparer.OrdinalIgnoreCase))
            state = state with { FireMode = mode with { FireMode = selected.FireModes[0] } };
        root.Template = id;
        root.Upd = state;
    }
}

[Injectable(InjectionType.Singleton, int.MaxValue)]
public sealed class StatRewardsCompatibility(ISptLogger<StatRewardsCompatibility> logger, TemplateTable templates) : IOnLoad
{
    private readonly ReleaseLog releaseLog = new(logger);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = RarityConfiguration.Load();
        if (!config.Enabled) return Task.CompletedTask;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("StatRewards.Services.RewardGenerator", false)).FirstOrDefault(t => t is not null);
        if (type is null) return Task.CompletedTask;
        var catalog = LegendaryCatalog.Load();
        var registered = templates.Items.Keys.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
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
