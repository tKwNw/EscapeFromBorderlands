using System.Collections;
using System.Globalization;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;

namespace EscapeFromBorderlands.Server;

/// <summary>
/// APBS imports every non-vanilla weapon template with a fixed per-template
/// weight. EFB has many intentionally unique Legendary templates, so those fixed
/// weights would swamp APBS's pools. This optional, reflection-only integration
/// runs after APBS's importer, removes retired aliases/non-PMC registrations, and
/// normalizes the combined active Legendary share in APBS PMC tier pools and
/// adds source-restricted variants to named-boss pools.
/// EFB retains no compile-time or runtime dependency on APBS.
/// </summary>
[Injectable(InjectionType.Singleton, int.MaxValue)]
public sealed class ApbsCompatibility(
    ISptLogger<ApbsCompatibility> logger,
    IServiceProvider serviceProvider,
    RarityMod rarityMod) : IOnLoad
{
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

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = RarityConfiguration.Load();
        var dataLoaderType = FindLoadedType(ApbsDataLoaderTypeNames);
        var legacyMethod = config.AutoDetectApbs && (dataLoaderType is not null || IsApbsInstalledOnDisk()) ? "apbs" : "standalone";
        config = RarityConfiguration.Load(legacyMethod);
        if (config.MigratedLegacySpawnSettings)
            logger.Info($"[Escape From Borderlands] Consolidated spawn settings using the active {legacyMethod} rates. The original config was backed up next to config.json.");
        if (!config.Enabled)
        {
            return Task.CompletedTask;
        }

        rarityMod.FinalizeMeleeLooting();
        rarityMod.ConfigureCustomBotSpawning(config, serviceProvider);

        if (!config.AutoDetectApbs)
        {
            if (dataLoaderType is not null)
            {
                try
                {
                    var dataLoader = ResolveLiveDataLoader(dataLoaderType, out _);
                    var removed = dataLoader is null ? -1 : RemoveImportedEfb(dataLoaderType, dataLoader);
                    if (removed < 0) logger.Warning("[Escape From Borderlands] APBS's live tier datasets were unavailable; imported EFB entries could not be cleared for standalone mode.");
                    else logger.Info($"[Escape From Borderlands] Cleared {removed} imported EFB entries from APBS dirty/clean datasets before selecting standalone spawning. Other mods' weights were preserved.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    logger.Warning($"[Escape From Borderlands] APBS cleanup for standalone mode failed: {exception.Message}");
                }
            }
            logger.Info("[Escape From Borderlands] APBS auto-detection is disabled; selecting standalone SPT bot pools.");
            rarityMod.ApplyStandalonePmcSpawning(config);
            return Task.CompletedTask;
        }

        if (dataLoaderType is null)
        {
            if (IsApbsInstalledOnDisk())
            {
                logger.Warning(
                    "[Escape From Borderlands] APBS is installed but its DataLoader was unavailable at the final spawn stage. " +
                    "Standalone registration was intentionally skipped to prevent double-injection. APBS weighting could not be reconciled.");
                rarityMod.SelectApbsPmcSpawning(config);
            }
            else
            {
                logger.Info("[Escape From Borderlands] APBS was not found at the final spawn stage; selecting standalone SPT bot pools.");
                rarityMod.ApplyStandalonePmcSpawning(config);
            }
            return Task.CompletedTask;
        }

        rarityMod.SelectApbsPmcSpawning(config);
        try
        {
            var equipmentHelper = FindLoadedType(ApbsBotEquipmentHelperTypeNames);
            if (equipmentHelper is not null) FactionEquipmentSpawning.InstallApbs(equipmentHelper);
            foreach (var name in new[] { "ProgressiveBotSystem.Services.CustomBotLootCacheService", "_progressiveBotSystem.Services.CustomBotLootCacheService" })
                if (FindLoadedType(new[] { name }) is { } cacheType) ReusableGrenadeSpawning.InstallApbsCache(cacheType);
        }
        catch (Exception exception) { logger.Warning($"[Escape From Borderlands] APBS role/loot adapters could not be installed: {exception.Message}"); }
        logger.Info(
            $"[Escape From Borderlands] APBS bridge found {dataLoaderType.FullName} in " +
            $"{dataLoaderType.Assembly.GetName().Name} {dataLoaderType.Assembly.GetName().Version}.");

        try
        {
            var dataLoader = ResolveLiveDataLoader(dataLoaderType, out var resolutionSource);
            if (dataLoader is null)
            {
                logger.Warning("[Escape From Borderlands] APBS was detected, but its data service could not be resolved; APBS may override EFB's PMC spawn weighting.");
                return Task.CompletedTask;
            }

            logger.Info($"[Escape From Borderlands] APBS live data source: {resolutionSource}.");

            var catalog = LegendaryCatalog.Load();
            // Grenades use the native grenade-loot pool, never firearm equipment slots.
            var activeVariants = catalog.Variants.Where(v => v.SpawnEnabled && v.GrenadeChargesPerRaid == 0).ToList();
            var variantWeights = activeVariants.ToDictionary(v => v.TemplateId, v => v.SpawnWeight, StringComparer.Ordinal);
            var activeIds = activeVariants
                .Select(variant => variant.TemplateId)
                .ToHashSet(StringComparer.Ordinal);
            var variantsBySource = activeVariants
                .GroupBy(variant => variant.SourceTemplateId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var retiredIds = catalog.LegacyAliases
                .Select(alias => alias.TemplateId)
                .Concat(catalog.Variants.Where(v => !v.SpawnEnabled || v.GrenadeChargesPerRaid > 0).Select(v => v.TemplateId))
                .ToHashSet(StringComparer.Ordinal);
            var adjustedPmcPools = 0;
            var pmcPoolsContainingEfb = 0;
            var weightedPmcEntries = 0;
            var removedPmcEntries = 0;
            var removedNonPmcEntries = 0;
            var removedImportedBossEntries = 0;
            var removedRetiredEntries = 0;
            var adjustedBossPools = 0;
            var weightedBossEntries = 0;
            var bossRolesAdjusted = new HashSet<string>(StringComparer.Ordinal);
            var processedDataSets = 0;
            var processedTierSets = new List<IDictionary>();
            var allTierDataPropertyNames = new[] { "AllTierDataDirty", "AllTierDataClean" };

            foreach (var propertyName in allTierDataPropertyNames)
            {
                var allTierData = dataLoaderType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(dataLoader);
                var tiers = allTierData?.GetType().GetProperty("Tiers", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(allTierData) as IDictionary;
                if (tiers is null || processedTierSets.Any(existing => ReferenceEquals(existing, tiers)))
                {
                    continue;
                }

                processedDataSets++;
                processedTierSets.Add(tiers);

                foreach (DictionaryEntry tierEntry in tiers)
                {
                    var equipmentData = tierEntry.Value?.GetType()
                        .GetProperty("EquipmentData", BindingFlags.Instance | BindingFlags.Public)
                        ?.GetValue(tierEntry.Value);
                    if (equipmentData is null)
                    {
                        continue;
                    }

                    foreach (var botProperty in equipmentData.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        var botEquipment = botProperty.GetValue(equipmentData);
                        var equipment = botEquipment?.GetType()
                            .GetProperty("Equipment", BindingFlags.Instance | BindingFlags.Public)
                            ?.GetValue(botEquipment) as IDictionary;
                        if (equipment is null)
                        {
                            continue;
                        }

                        var isPmcFaction = IsPmcProperty(botProperty.Name);
                        var isBoss = IsBossProperty(botProperty.Name);
                        foreach (DictionaryEntry slotEntry in equipment)
                        {
                            if (slotEntry.Value is not IDictionary pool)
                            {
                                continue;
                            }

                            removedRetiredEntries += RemoveMatching(pool, retiredIds);
                            var category = RarityConfiguration.Category(slotEntry.Key.ToString() ?? "");
                            var slotVariants = activeVariants.Where(v => category == "earpieces" ? v.IsEarpiece : category == "chestRigs" ? v.ChestRigEffect is not null : category == "backpacks" ? v.BackpackEffect is not null : category == "melee" ? v.MeleeEffect is not null : !v.IsEarpiece && v.BackpackEffect is null && v.MeleeEffect is null && v.ChestRigEffect is null).ToArray();
                            var categoryIds = slotVariants.Select(v => v.TemplateId).ToHashSet(StringComparer.Ordinal);
                            if (propertyName == "AllTierDataDirty" && isPmcFaction)
                                FactionEquipmentSpawning.RememberImported(Convert.ToInt32(tierEntry.Key, CultureInfo.InvariantCulture), category, MatchingKeys(pool, categoryIds).Select(k => k.ToString()!));
                            var slotSources = slotVariants.GroupBy(v => v.SourceTemplateId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
                            if (!IsWeaponSlot(slotEntry.Key))
                            {
                                removedNonPmcEntries += RemoveMatching(pool, activeIds);
                                continue;
                            }
                            var wrongCategory = activeIds.Except(categoryIds).ToHashSet(StringComparer.Ordinal);
                            removedNonPmcEntries += RemoveMatching(pool, wrongCategory);
                            var bossFraction = config.AddToBossWeapons ? config.Fraction("bosses", category) : 0d;
                            var pmcFraction = config.BotFraction("apbs", botProperty.Name, category);
                            if (isBoss)
                            {
                                removedImportedBossEntries += RemoveMatching(pool, categoryIds);
                                if (bossFraction > 0d && AddSourceRestrictedBossVariants(pool, slotSources, bossFraction, out var bossEntries))
                                {
                                    adjustedBossPools++; weightedBossEntries += bossEntries; bossRolesAdjusted.Add(botProperty.Name);
                                }
                                continue;
                            }
                            if (!isPmcFaction)
                            {
                                removedNonPmcEntries += RemoveMatching(pool, activeIds);
                                continue;
                            }
                            // Preserve APBS import/filter decisions for guns and bags. Its weapon
                            // importer excludes melee, so scabbards retain the existing source-based bridge.
                            var imported = MatchingKeys(pool, categoryIds);
                            var sourcesAllowed = imported.Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);
                            if (category == "melee") sourcesAllowed = categoryIds;
                            if (imported.Count > 0) pmcPoolsContainingEfb++;
                            removedPmcEntries += RemoveMatching(pool, categoryIds);
                            if (pmcFraction <= 0d) continue;
                            var allowedSources = slotSources.ToDictionary(p => p.Key, p => p.Value.Where(v => sourcesAllowed.Contains(v.TemplateId)).Select(v => v.TemplateId).ToArray());
                            var originals = new Dictionary<string, double>(StringComparer.Ordinal);
                            object? example = null;
                            foreach (DictionaryEntry entry in pool)
                            {
                                example ??= entry.Key;
                                originals[entry.Key.ToString()!] = Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture);
                            }
                            if (example is null) continue;
                            var weights = CategorySpawnPolicy.Weights(originals, allowedSources, pmcFraction, variantWeights);
                            foreach (var pair in weights)
                            {
                                var key = CreateCompatiblePoolKey(example, pair.Key);
                                if (key is not null) { pool[key] = pair.Value; weightedPmcEntries++; }
                            }
                            if (weights.Count > 0) adjustedPmcPools++;
                        }
                    }
                }
            }

            if (processedDataSets == 0)
            {
                logger.Warning("[Escape From Borderlands] APBS was detected, but no live tier datasets could be resolved; APBS may override EFB's PMC spawn weighting.");
                return Task.CompletedTask;
            }

            foreach (var category in new[] { "guns", "melee", "backpacks", "chestRigs", "earpieces" })
            {
                var ids = activeVariants.Where(v => category == "earpieces" ? v.IsEarpiece : category == "chestRigs" ? v.ChestRigEffect is not null : category == "backpacks" ? v.BackpackEffect is not null : category == "melee" ? v.MeleeEffect is not null : !v.IsEarpiece && v.BackpackEffect is null && v.MeleeEffect is null && v.ChestRigEffect is null)
                    .Select(v => v.TemplateId).ToHashSet(StringComparer.Ordinal);
                var pmcRate = config.AddToPmcWeapons ? config.Chance("apbs", category) : 0d;
                var bossRate = config.AddToBossWeapons ? config.Chance("bosses", category) : 0d;
                var unexpected = processedTierSets.Sum(t => CountUnexpectedMatching(t, ids, config, category));
                if (unexpected > 0) logger.Error($"[Escape From Borderlands] APBS {category} verification failed: {unexpected} disallowed registrations remain.");
                else logger.Success($"[Escape From Borderlands] APBS {category} verified: PMC target={pmcRate:0.###}%, boss target={bossRate:0.###}%; disabled categories contain zero EFB registrations. Other mods' weights and APBS filters were preserved.");
            }
            logger.Info($"[Escape From Borderlands] APBS reconciliation: {weightedPmcEntries} PMC entries in {adjustedPmcPools} pools; {weightedBossEntries} source-restricted boss entries in {adjustedBossPools} pools; removed {removedNonPmcEntries} unintended and {removedRetiredEntries} retired registrations across {processedDataSets} live datasets.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.Warning($"[Escape From Borderlands] APBS compatibility could not reconcile progressive bot pools: {exception.Message}");
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

    private static bool AddSourceRestrictedBossVariants(
        IDictionary pool,
        IReadOnlyDictionary<string, VariantDefinition[]> variantsBySource,
        double targetFraction,
        out int entriesAdded)
    {
        entriesAdded = 0;
        var eligibleSources = new List<(object Key, double Weight, VariantDefinition[] Variants)>();
        var baseWeight = 0d;

        foreach (DictionaryEntry entry in pool)
        {
            var weight = Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture);
            if (weight <= 0d)
            {
                continue;
            }

            baseWeight += weight;
            var sourceId = entry.Key.ToString() ?? string.Empty;
            if (variantsBySource.TryGetValue(sourceId, out var sourceVariants))
            {
                eligibleSources.Add((entry.Key, weight, sourceVariants));
            }
        }

        var eligibleSourceWeight = eligibleSources.Sum(entry => entry.Weight);
        if (baseWeight <= 0d || eligibleSourceWeight <= 0d)
        {
            return false;
        }

        var totalLegendaryWeight = baseWeight * targetFraction / (1d - targetFraction);
        foreach (var source in eligibleSources)
        {
            var sourceLegendaryWeight = totalLegendaryWeight * source.Weight / eligibleSourceWeight;
            var variantWeightSum = source.Variants.Sum(v => v.SpawnWeight);
            foreach (var variant in source.Variants)
            {
                var variantKey = CreateCompatiblePoolKey(source.Key, variant.TemplateId);
                if (variantKey is null)
                {
                    continue;
                }

                pool[variantKey] = sourceLegendaryWeight * variant.SpawnWeight / variantWeightSum;
                entriesAdded++;
            }
        }

        return entriesAdded > 0;
    }

    private static object? CreateCompatiblePoolKey(object exampleKey, string templateId)
    {
        if (exampleKey is string)
        {
            return templateId;
        }

        try
        {
            return Activator.CreateInstance(exampleKey.GetType(), new object?[] { templateId });
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPmcProperty(string propertyName) =>
        propertyName is "PmcUsec" or "PmcBear";

    private static bool IsBossProperty(string propertyName) =>
        propertyName.StartsWith("Boss", StringComparison.Ordinal);

    private static bool IsWeaponSlot(object slotKey)
    {
        var slotName = slotKey.ToString() ?? string.Empty;
        return slotName.Contains("PrimaryWeapon", StringComparison.Ordinal) || slotName is "Holster" or "Scabbard" or "Backpack" or "TacticalVest" or "ArmorVest" or "Earpiece";
    }

    private static int CountUnexpectedMatching(
        IDictionary tiers,
        IReadOnlySet<string> ids,
        RarityConfiguration config,
        string category)
    {
        return CountMatching(tiers, ids, propertyName =>
            IsPmcProperty(propertyName) || IsBossProperty(propertyName) ? config.BotFraction("apbs", propertyName, category) <= 0d :
            true);
    }

    private static int CountMatching(
        IDictionary tiers,
        IReadOnlySet<string> ids,
        Func<string, bool> includeBotProperty)
    {
        var count = 0;
        foreach (DictionaryEntry tierEntry in tiers)
        {
            var equipmentData = tierEntry.Value?.GetType()
                .GetProperty("EquipmentData", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(tierEntry.Value);
            if (equipmentData is null)
            {
                continue;
            }

            foreach (var botProperty in equipmentData.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!includeBotProperty(botProperty.Name))
                {
                    continue;
                }

                var botEquipment = botProperty.GetValue(equipmentData);
                var equipment = botEquipment?.GetType()
                    .GetProperty("Equipment", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(botEquipment) as IDictionary;
                if (equipment is null)
                {
                    continue;
                }

                foreach (DictionaryEntry slotEntry in equipment)
                {
                    if (slotEntry.Value is IDictionary pool)
                    {
                        count += MatchingKeys(pool, ids).Count;
                    }
                }
            }
        }

        return count;
    }
}
