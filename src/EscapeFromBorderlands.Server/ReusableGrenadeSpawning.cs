using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Services;

namespace EscapeFromBorderlands.Server;

internal static class ReusableGrenadeSpawning
{
    private static GrenadeSpawnPolicy Policy = new([]);
    private static RarityConfiguration Configuration = new();
    private static string PmcMethod = "standalone";
    private static bool Installed;
    private static readonly HashSet<System.Reflection.MethodBase> ApbsCaches = [];

    internal static void Install(IEnumerable<VariantDefinition> definitions, RarityConfiguration config)
    {
        Policy = new GrenadeSpawnPolicy(definitions); Configuration = config;
        if (Installed) return;
        new Harmony("com.tkw.escapefromborderlands.server.grenades").Patch(
            AccessTools.Method(typeof(BotLootCacheService), nameof(BotLootCacheService.GetLootFromCache)),
            postfix: new HarmonyMethod(typeof(ReusableGrenadeSpawning), nameof(AfterPool)) { priority = Priority.Last });
        Installed = true;
    }
    internal static void SelectMode(RarityConfiguration config, bool apbs)
    { Configuration = config; PmcMethod = apbs ? "apbs" : "standalone"; }

    internal static void InstallApbsCache(Type type)
    {
        var target = type.GetMethods().SingleOrDefault(m => m.Name == "GetLootFromCache" && m.GetParameters().Any(p => p.Name == "botRole") && m.GetParameters().Any(p => p.Name == "lootType"))
            ?? throw new InvalidOperationException("APBS grenade loot-cache method was not found.");
        if (ApbsCaches.Contains(target)) return;
        new Harmony("com.tkw.escapefromborderlands.server.grenades.apbs").Patch(target,
            postfix: new HarmonyMethod(typeof(ReusableGrenadeSpawning), nameof(AfterPool)) { priority = Priority.Last });
        ApbsCaches.Add(target);
    }

    private static void AfterPool(string botRole, bool isPmc, string lootType, ref Dictionary<MongoId, double> __result)
    {
        if (lootType != "GrenadeItems") return;
        var pool = Policy.Pool(__result.ToDictionary(p => p.Key.ToString(), p => p.Value, StringComparer.Ordinal), Configuration, PmcMethod, botRole, isPmc);
        __result = pool.ToDictionary(p => new MongoId(p.Key), p => p.Value);
    }
}
