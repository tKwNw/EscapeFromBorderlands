using System.Collections;
using System.Globalization;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace EscapeFromBorderlands.Server;

// Optional APBS integration removes pre-generation EFB entries. Final bot
// inventory conversion owns the configured chance for every eligible faction.
[Injectable(InjectionType.Singleton, null, int.MaxValue)]
public sealed class ApbsCompatibility(
    ISptLogger<ApbsCompatibility> logger,
    IServiceProvider serviceProvider,
    RarityMod rarityMod) : IOnLoad
{
    private readonly ReleaseLog releaseLog = new(logger);

    // APBS 2.2 moved out of the legacy underscore-prefixed root namespace.
    // Keep both exact names so the optional bridge remains compatible with
    // current releases and older installations without taking a hard APBS
    // assembly dependency.
    private static readonly string[] ApbsDataLoaderTypeNames =
    [
        "ProgressiveBotSystem.Helpers.DataLoader",
        "_progressiveBotSystem.Helpers.DataLoader"
    ];

    private static readonly string[] ApbsBotEquipmentHelperTypeNames =
    [
        "ProgressiveBotSystem.Helpers.BotEquipmentHelper",
        "_progressiveBotSystem.Helpers.BotEquipmentHelper"
    ];

    public Task OnLoad()
    {
        var config = RarityConfiguration.Load();
        var dataLoaderType = FindLoadedType(ApbsDataLoaderTypeNames);
        var legacyMethod = config.AutoDetectApbs && (dataLoaderType is not null || IsApbsInstalledOnDisk()) ? "apbs" : "standalone";
        config = RarityConfiguration.Load(legacyMethod);
        if (config.MigratedLegacySpawnSettings)
            releaseLog.Info($"[Escape From Borderlands] Consolidated spawn settings using the active {legacyMethod} rates. The original config was backed up next to config.json.");
        if (!config.Enabled)
        {
            return Task.CompletedTask;
        }

        rarityMod.FinalizeMeleeLooting();
        rarityMod.ConfigureCustomBotSpawning(config, serviceProvider);

        try
        {
            var equipmentHelper = FindLoadedType(ApbsBotEquipmentHelperTypeNames);
            if (equipmentHelper is not null) FactionEquipmentSpawning.InstallApbs(equipmentHelper);
            foreach (var name in new[] { "ProgressiveBotSystem.Services.CustomBotLootCacheService", "_progressiveBotSystem.Services.CustomBotLootCacheService" })
                if (FindLoadedType(new[] { name }) is { } cacheType) ReusableGrenadeSpawning.InstallApbsCache(cacheType);
        }
        catch (Exception exception) { releaseLog.Warning($"[Escape From Borderlands] APBS role/loot adapters could not be installed: {exception.Message}"); }

        if (!config.AutoDetectApbs)
        {
            if (dataLoaderType is not null)
            {
                try
                {
                    var dataLoader = ResolveLiveDataLoader(dataLoaderType, out _);
                    var removed = dataLoader is null ? -1 : RemoveImportedEfb(dataLoaderType, dataLoader);
                    if (removed < 0) releaseLog.Warning("[Escape From Borderlands] APBS's live tier datasets were unavailable; imported EFB entries could not be cleared for standalone mode.");
                    else releaseLog.Info($"[Escape From Borderlands] Cleared {removed} imported EFB entries from APBS dirty/clean datasets before selecting standalone spawning. Other mods' weights were preserved.");
                }
                catch (Exception exception)
                {
                    releaseLog.Warning($"[Escape From Borderlands] APBS cleanup for standalone mode failed: {exception.Message}");
                }
            }
            releaseLog.Info("[Escape From Borderlands] APBS auto-detection is disabled; selecting standalone SPT bot pools.");
            rarityMod.ApplyStandalonePmcSpawning(config);
            return Task.CompletedTask;
        }

        if (dataLoaderType is null)
        {
            if (IsApbsInstalledOnDisk())
            {
                releaseLog.Warning(
                    "[Escape From Borderlands] APBS is installed but its DataLoader was unavailable at the final spawn stage. " +
                    "Final conversion remains authoritative; optional APBS pool cleanup could not be confirmed.");
                rarityMod.SelectApbsPmcSpawning(config);
            }
            else
            {
                releaseLog.Info("[Escape From Borderlands] APBS was not found at the final spawn stage; selecting standalone SPT bot pools.");
                rarityMod.ApplyStandalonePmcSpawning(config);
            }
            return Task.CompletedTask;
        }

        rarityMod.SelectApbsPmcSpawning(config);
        releaseLog.Info(
            $"[Escape From Borderlands] APBS bridge found {dataLoaderType.FullName} in " +
            $"{dataLoaderType.Assembly.GetName().Name} {dataLoaderType.Assembly.GetName().Version}.");

        try
        {
            var dataLoader = ResolveLiveDataLoader(dataLoaderType, out var resolutionSource);
            if (dataLoader is null)
            {
                releaseLog.Warning("[Escape From Borderlands] APBS was detected, but its data service could not be resolved; equipment-copy cleanup and final conversion remain installed.");
                return Task.CompletedTask;
            }

            releaseLog.Info($"[Escape From Borderlands] APBS live data source: {resolutionSource}.");

            var removed = RemoveImportedEfb(dataLoaderType, dataLoader);
            if (removed < 0) releaseLog.Warning("[Escape From Borderlands] APBS tier data unavailable; returned equipment copies are still cleaned before generation.");
            else releaseLog.Info($"[Escape From Borderlands] Removed {removed} imported EFB pool entries. Final bot conversion owns all configured category chances, independently of APBS modded-item settings.");
        }
        catch (Exception exception)
        {
            releaseLog.Warning($"[Escape From Borderlands] APBS compatibility could not reconcile progressive bot pools: {exception.Message}");
        }

        return Task.CompletedTask;
    }

    private static int RemoveImportedEfb(Type dataLoaderType, object dataLoader)
    {
        var catalog = LegendaryCatalog.Load();
        var ids = catalog.Variants.Select(v => v.TemplateId).Concat(catalog.LegacyAliases.Select(v => v.TemplateId)).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        var found = false;
        foreach (var name in new[] { "AllTierDataDirty", "AllTierDataClean" })
        {
            var data = dataLoaderType.GetProperty(name)?.GetValue(dataLoader);
            if (data?.GetType().GetProperty("Tiers")?.GetValue(data) is not IDictionary tiers) continue;
            found = true;
            foreach (DictionaryEntry tier in tiers)
            {
                var equipmentData = tier.Value?.GetType().GetProperty("EquipmentData")?.GetValue(tier.Value);
                if (equipmentData is null) continue;
                foreach (var botProperty in equipmentData.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var bot = botProperty.GetValue(equipmentData);
                    if (bot?.GetType().GetProperty("Equipment")?.GetValue(bot) is not IDictionary equipment) continue;
                    foreach (DictionaryEntry slot in equipment)
                        if (slot.Value is IDictionary pool) removed += RemoveMatching(pool, ids);
                }
            }
        }
        return found ? removed : -1;
    }

    private object? ResolveLiveDataLoader(Type dataLoaderType, out string resolutionSource)
    {
        var helperType = FindLoadedType(ApbsBotEquipmentHelperTypeNames);
        if (helperType is not null)
        {
            var helper = serviceProvider.GetService(helperType);
            var dataLoaderField = helperType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(field => dataLoaderType.IsAssignableFrom(field.FieldType));
            var capturedDataLoader = helper is null ? null : dataLoaderField?.GetValue(helper);
            if (capturedDataLoader is not null)
            {
                resolutionSource = $"{helperType.FullName}.{dataLoaderField!.Name}";
                return capturedDataLoader;
            }
        }

        resolutionSource = dataLoaderType.FullName ?? dataLoaderType.Name;
        return serviceProvider.GetService(dataLoaderType);
    }

    private static Type? FindLoadedType(IEnumerable<string> typeNames)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => typeNames.Select(typeName => assembly.GetType(typeName, false)))
            .FirstOrDefault(type => type is not null);
    }

    private static bool IsApbsInstalledOnDisk()
    {
        var efbDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var modsDirectory = efbDirectory is null ? null : Directory.GetParent(efbDirectory)?.FullName;
        if (modsDirectory is null || !Directory.Exists(modsDirectory))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateDirectories(modsDirectory)
                .Select(Path.GetFileName)
                .Any(name => name?.Contains("progressivebotsystem", StringComparison.OrdinalIgnoreCase) == true) ||
                Directory.EnumerateFiles(modsDirectory, "*.dll", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Any(name => name?.Contains("progressivebotsystem", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch
        {
            return false;
        }
    }

    private static List<object> MatchingKeys(IDictionary pool, IReadOnlySet<string> ids)
    {
        var matches = new List<object>();
        foreach (DictionaryEntry entry in pool)
        {
            if (ids.Contains(entry.Key.ToString() ?? string.Empty))
            {
                matches.Add(entry.Key);
            }
        }

        return matches;
    }

    private static int RemoveMatching(IDictionary pool, IReadOnlySet<string> ids)
    {
        var keys = MatchingKeys(pool, ids);
        foreach (var key in keys)
        {
            pool.Remove(key);
        }

        return keys.Count;
    }

}
