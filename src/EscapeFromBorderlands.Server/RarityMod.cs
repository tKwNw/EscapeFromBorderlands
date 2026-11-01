using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace EscapeFromBorderlands.Server;

#pragma warning disable CS0618 // SPT 4.0.13 exposes RagfairConfig through the registered ConfigServer service.
[Injectable(InjectionType.Singleton, null, OnLoadOrder.PostDBModLoader + 100)]
public sealed class RarityMod(
    ISptLogger<RarityMod> logger,
    DatabaseService databaseService,
    CustomItemService customItemService,
    ItemHelper itemHelper,
    LocaleService localeService,
    ICloner cloner,
    ConfigServer configServer,
    SaveServer saveServer) : IOnLoad
{
    private const int MaximumReportedErrors = 25;
    private static readonly MongoId DefaultInventoryTemplateId = new("55d7217a4bdc2d86028b456d");
    private IReadOnlyCollection<SpawnVariant> _spawnVariants = [];
    private bool _spawnRegistrationFinalized;

    public Task OnLoad()
    {
        var config = RarityConfiguration.Load();
        var ragfairConfig = configServer.GetConfig<RagfairConfig>();
        if (!config.Enabled)
        {
            logger.Info("[Escape From Borderlands] Disabled in config/config.json.");
            return Task.CompletedTask;
        }

        logger.Info(config.LegacySpawnSettingsPending
            ? "[Escape From Borderlands] Legacy spawn settings will be consolidated after final APBS detection."
            : $"[Escape From Borderlands] Shared spawn configuration: PMC ({config.Describe("pmcs")}); bosses ({config.Describe("bosses")}); auto-detect={config.AutoDetectApbs}. Config: SPT/user/mods/EscapeFromBorderlands/config/config.json");

        var catalog = LegendaryCatalog.Load();
        if (catalog.SchemaVersion != 1 || catalog.Variants.Count == 0)
        {
            logger.Error($"[Escape From Borderlands] Unsupported or empty catalog schema {catalog.SchemaVersion}.");
            return Task.CompletedTask;
        }

        BackpackRaidRewards.Install(saveServer, databaseService, catalog);

        EnableMeleeLooting(catalog);

        var sourceCache = new Dictionary<string, SourceWeapon>(StringComparer.Ordinal);
        var spawnVariants = new List<SpawnVariant>();
        var created = 0;
        var presetsCreated = 0;
        var aliasesCreated = 0;
        var errors = 0;

        foreach (var alias in catalog.LegacyAliases)
        {
            if (!TryGetSource(alias.SourceTemplateId, alias.SourceFullName, sourceCache, ref errors, out var source))
            {
                continue;
            }

            var result = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
            {
                ItemTplToClone = new MongoId(alias.SourceTemplateId),
                ParentId = source.Item.Parent.ToString(),
                NewId = alias.TemplateId,
                HandbookParentId = source.Handbook.ParentId.ToString(),
                HandbookPriceRoubles = source.Handbook.Price.GetValueOrDefault(50_000d),
                OverrideProperties = new TemplateItemProperties
                {
                    WeapUseType = IsHolsterOnly(source.EquipmentSlots)
                        ? "secondary"
                        : source.Item.Properties?.WeapUseType,
                    CanSellOnRagfair = false,
                    CanRequireOnRagfair = false,
                },
                Locales = new Dictionary<string, LocaleDetails>
                {
                    ["en"] = new()
                    {
                        Name = alias.DisplayName,
                        ShortName = alias.ShortName,
                        Description = alias.Description,
                    },
                },
            });

            if (result.Success != true)
            {
                ReportError($"Could not create legacy compatibility alias for {alias.DisplayName}: {string.Join("; ", result.Errors ?? [])}", ref errors);
                continue;
            }

            aliasesCreated++;
            WhitelistCloneForEquipmentSlots(source.EquipmentSlots, alias.TemplateId);
            ragfairConfig.Dynamic.Blacklist.Custom.Add(new MongoId(alias.TemplateId));
        }

        foreach (var variant in catalog.Variants)
        {
            if (!TryGetSource(variant.SourceTemplateId, variant.SourceFullName, sourceCache, ref errors, out var source))
            {
                continue;
            }

            var priceMultiplier = config.PriceMultiplierFor(variant.Tier, variant.PriceMultiplier);
            var sourceHandbookPrice = Math.Max(1d, source.Handbook.Price.GetValueOrDefault(50_000d));
            var sourceFleaPrice = source.FleaPrice > 0d ? source.FleaPrice : sourceHandbookPrice;
            var handbookPrice = Math.Max(1d, Math.Ceiling(sourceHandbookPrice * priceMultiplier));
            var fleaPrice = Math.Max(1d, Math.Ceiling(sourceFleaPrice * priceMultiplier));
            var result = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
            {
                ItemTplToClone = new MongoId(variant.SourceTemplateId),
                ParentId = source.Item.Parent.ToString(),
                NewId = variant.TemplateId,
                HandbookParentId = source.Handbook.ParentId.ToString(),
                HandbookPriceRoubles = handbookPrice,
                FleaPriceRoubles = fleaPrice,
                OverrideProperties = new TemplateItemProperties
                {
                    WeapUseType = IsHolsterOnly(source.EquipmentSlots)
                        ? "secondary"
                        : source.Item.Properties?.WeapUseType,
                    BackgroundColor = variant.BackgroundColor,
                    CanSellOnRagfair = config.AllowFleaMarketTrading,
                    // Mystery stock is a purchase preview, never an item a
                    // player must hand in for a barter.
                    CanRequireOnRagfair = config.AllowFleaMarketTrading && !variant.IsMysteryFleaOffer,
                    WeapFireType = variant.FireModes.Count > 0
                        ? new HashSet<string>(variant.FireModes, StringComparer.OrdinalIgnoreCase)
                        : null,
                    BurstShotsCount = variant.BurstShotsCount,
                    BFirerate = variant.BaseFireRate,
                    SingleFireRate = variant.SingleFireRate,
                    StackMaxSize = variant.GrenadeChargesPerRaid > 0 ? 1 : null,
                    CanPlantOnGround = variant.GrenadeChargesPerRaid > 0 ? false : null,
                },
                Locales = new Dictionary<string, LocaleDetails>
                {
                    ["en"] = new()
                    {
                        Name = variant.DisplayName,
                        ShortName = variant.ShortName,
                        Description = variant.LocaleDescription,
                    },
                },
            });

            if (result.Success != true)
            {
                ReportError($"Could not create {variant.DisplayName}: {string.Join("; ", result.Errors ?? [])}", ref errors);
                continue;
            }

            created++;
            if (variant.BackpackEffect is not null && databaseService.GetItems().TryGetValue(new MongoId(variant.TemplateId), out var backpack))
                backpack.Properties = BackpackStorage.Apply(backpack.Properties!, variant);
            if (variant.ChestRigEffect is not null && databaseService.GetItems().TryGetValue(new MongoId(variant.TemplateId), out var rig))
            {
                rig.Properties = ChestRigStorage.Apply(rig.Properties!, variant.ChestRigEffect, variant.TemplateId);
                ApplyRigSoftArmor(rig, variant);
            }
            WhitelistCloneForEquipmentSlots(source.EquipmentSlots, variant.TemplateId);
            // Concrete backpack stock is filtered by the generator hook. Keep
            // owned bags eligible for ordinary resale when trading is enabled.
            if (!config.AllowFleaMarketTrading)
            {
                ragfairConfig.Dynamic.Blacklist.Custom.Add(new MongoId(variant.TemplateId));
            }
            if (config.CreateDefaultPresets && source.Preset is not null && CreateDefaultPreset(source.Preset, variant))
            {
                presetsCreated++;
            }

            if (IsSafeForBotPool(variant))
            {
                spawnVariants.Add(new SpawnVariant(variant, source.EquipmentSlots));
            }
        }

        EarpieceCompatibility.Apply(catalog.Variants, databaseService.GetItems());
        _spawnVariants = spawnVariants;
        LegendaryQuestItems.Install(catalog.Variants.Where(v => databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))),
            catalog.LegacyAliases.Where(v => databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))));
        logger.Info("[Escape From Borderlands] Quest compatibility enabled: EFB variants count as their base items for non-consuming objectives and are protected from quest hand-ins and placement.");
        FactionEquipmentSpawning.Configure(catalog.Variants.Where(v => databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))), catalog.LegacyAliases.Select(v => v.TemplateId), config);
        ReusableGrenadeTestOffer.Install(databaseService, catalog);
        ReusableGrenadeSpawning.Install(catalog.Variants.Where(v => databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))), config);
        AirdropLegendarySpawning.Install(catalog.Variants.Where(v => databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))),
            catalog.LegacyAliases.Where(v => databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))), config,
            catalog.UnlootableMeleeTemplateIds.ToHashSet(StringComparer.Ordinal));
        logger.Info($"[Escape From Borderlands] Shared airdrop legendary chance: {config.AirdropFraction * 100d:0.###}% per supported loot item, across all airdrop types.");

        BackpackFleaMarket.Install(catalog.Variants.Where(v =>
            databaseService.GetItems().ContainsKey(new MongoId(v.TemplateId))), config.AllowFleaMarketTrading);
        if (config.AllowFleaMarketTrading)
            logger.Info("[Escape From Borderlands] Legendary backpack flea stock uses one mystery template per signature. Four bonuses, including any extra storage, are revealed separately for each purchased bag. Concrete storage layouts are excluded from generated flea stock.");

        logger.Success(
            $"[Escape From Borderlands] Catalog revision {catalog.CatalogRevision}: created {created}/{catalog.Variants.Count} additive Legendary templates " +
            $"from {catalog.VanillaWeaponCount} recoverable vanilla weapon roots, {presetsCreated} assembled presets, " +
            $"and {aliasesCreated}/{catalog.LegacyAliases.Count} effect-free retired-item compatibility aliases. " +
            (config.AllowFleaMarketTrading
                ? "Active Legendary receivers are enabled for flea purchase/listing under normal SPT flea rules. "
                : "All active Legendary receivers are blocked from flea purchase/listing. ") +
            "PMC, boss and eligible non-Scav faction spawning is waiting for the final APBS/standalone selection stage. " +
            "Retired-weapon compatibility aliases remain flea-blocked and never enter bot pools. Supported vanilla melee weapons and Legendary melee clones are corpse-lootable; Chained Labrys and Superfors DB 2020 remain unlootable; original firearm statistics, third-party weapons, ammunition, attachments, and the zombie attack item were left untouched.");
        if (errors > MaximumReportedErrors)
        {
            logger.Warning($"[Escape From Borderlands] {errors - MaximumReportedErrors} additional catalog errors were suppressed.");
        }

        return Task.CompletedTask;
    }

    public void ApplyStandalonePmcSpawning(RarityConfiguration config)
    {
        if (_spawnRegistrationFinalized)
        {
            return;
        }

        _spawnRegistrationFinalized = true;
        ReusableGrenadeSpawning.SelectMode(config, false);
        FactionEquipmentSpawning.SelectConfiguration(config);
        if (!config.AddToPmcWeapons && !config.AddToBossWeapons && !config.AddToNonScavFactions)
        {
            logger.Info("[Escape From Borderlands] All bot spawning groups are disabled; no standalone bot-pool entries were registered.");
            return;
        }

        var entries = ApplyCategoryPools(config);
        LogFactionRates(config, "standalone");
        logger.Success($"[Escape From Borderlands] Final spawn method: standalone. PMC ({config.Describe("standalone")}); bosses ({config.Describe("bosses")}). Registered {entries} source-restricted equipment entries. Targets are combined per-slot EFB shares, not a weight for every individual variant.");
    }

    public void SelectApbsPmcSpawning(RarityConfiguration config)
    {
        if (_spawnRegistrationFinalized)
        {
            return;
        }

        _spawnRegistrationFinalized = true;
        ReusableGrenadeSpawning.SelectMode(config, true);
        FactionEquipmentSpawning.SelectConfiguration(config);
        // APBS-disabled roles use native templates. Both paths read the same
        // shared rates; progressed roles normalize their returned copies.
        var fallbackEntries = ApplyCategoryPools(config, "apbs", nonPmcOnly: true);
        LogFactionRates(config, "apbs");
        logger.Info(
            "[Escape From Borderlands] Final spawn method: APBS pools. Standalone PMC registration was not performed. " +
            $"Shared PMC rates ({config.Describe("apbs")}); bosses ({config.Describe("bosses")}). {fallbackEntries} source-restricted non-PMC fallback entries use the shared faction rates. Category targets apply independently.");
    }

    private int ApplyCategoryPools(RarityConfiguration config, string method = "standalone", bool nonPmcOnly = false)
    {
        var slots = new[] { EquipmentSlots.FirstPrimaryWeapon, EquipmentSlots.SecondPrimaryWeapon, EquipmentSlots.Holster, EquipmentSlots.Scabbard, EquipmentSlots.Backpack, EquipmentSlots.TacticalVest, EquipmentSlots.ArmorVest, EquipmentSlots.Earpiece };
        var catalog = LegendaryCatalog.Load();
        var allEfb = catalog.Variants.Select(v => new MongoId(v.TemplateId)).Concat(catalog.LegacyAliases.Select(v => new MongoId(v.TemplateId))).ToHashSet();
        var variantWeights = _spawnVariants.ToDictionary(v => v.Variant.TemplateId, v => v.Variant.SpawnWeight);
        var total = 0;
        foreach (var (role, bot) in databaseService.GetBots().Types)
        {
            if (bot is null) continue;
            if (nonPmcOnly && BotSpawnGroups.Group(role) is "pmcUsec" or "pmcBear") continue;
            foreach (var slot in slots)
            {
                if (!bot.BotInventory.Equipment.TryGetValue(slot, out var pool)) continue;
                foreach (var id in allEfb) pool.Remove(id);
                var category = RarityConfiguration.Category(slot.ToString());
                var fraction = config.BotFraction(method, role, category);
                if (fraction <= 0d) continue;
                var sources = _spawnVariants.Where(v => v.EquipmentSlots.Contains(slot))
                    .GroupBy(v => v.Variant.SourceTemplateId)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.Variant.TemplateId).ToArray());
                var originals = pool.ToDictionary(p => p.Key.ToString(), p => p.Value);
                if (category == "guns")
                    foreach (var source in sources.Keys.ToArray())
                        if (!bot.BotInventory.Mods.ContainsKey(new MongoId(source))) sources.Remove(source);
                var weights = CategorySpawnPolicy.Weights(originals, sources, fraction, variantWeights);
                foreach (var (id, weight) in weights)
                {
                    var definition = _spawnVariants.First(v => v.Variant.TemplateId == id).Variant;
                    if (category is "guns" or "chestRigs") CopyBossWeaponModPool(bot.BotInventory.Mods, new MongoId(definition.SourceTemplateId), new MongoId(id));
                    RigPanelModPool(bot.BotInventory.Mods,definition);
                    pool[new MongoId(id)] = weight;
                    total++;
                }
            }
        }
        return total;
    }

    private void LogFactionRates(RarityConfiguration config, string method)
    {
        foreach (var group in BotSpawnGroups.Expanded)
            logger.Info($"[Escape From Borderlands] {method} {group}: " + string.Join(", ", new[] { "guns", "melee", "backpacks", "chestRigs", "grenades", "earpieces" }.Select(category => $"{category}={(config.AddToNonScavFactions ? config.FactionChance(method, group, category) : 0d):0.###}%")) + ". Ordinary/sniper Scavs are excluded.");
    }

    private static string RigPanelId(VariantDefinition variant, string slot) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("efb:rig-panel:" + variant.StorageFamilyId + ":" + slot)))[..24].ToLowerInvariant();

    private void ApplyRigSoftArmor(TemplateItem rig, VariantDefinition variant)
    {
        if (variant.ChestRigEffect?.SoftArmorClass is not > 0) return;
        foreach (var slot in rig.Properties?.Slots ?? [])
        {
            if (slot.Name is not ("Soft_armor_front" or "Soft_armor_back" or "Groin")) continue;
            foreach (var filter in slot.Properties?.Filters ?? [])
            {
                var original = filter.Filter?.FirstOrDefault();
                if (original is null || !databaseService.GetItems().TryGetValue(original.Value, out var panel)) continue;
                var id = RigPanelId(variant, slot.Name!);
                if (!databaseService.GetItems().ContainsKey(new MongoId(id)))
                {
                    var copy = cloner.Clone(panel)!;
                    copy.Id = new MongoId(id);
                    var property = typeof(TemplateItemProperties).GetProperty("ArmorClass") ?? throw new InvalidOperationException("Native soft armor class property was not found.");
                    property.SetValue(copy.Properties!, Convert.ChangeType(variant.ChestRigEffect.SoftArmorClass, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
                    databaseService.GetItems()[copy.Id] = copy;
                    foreach(var language in databaseService.GetLocales().Global.Keys)
                    {
                        var locale=localeService.GetLocaleDb(language);
                        foreach(var suffix in new[]{" Name"," ShortName"," Description"})
                            if(locale.TryGetValue(original+suffix,out var value))locale[id+suffix]=value;
                    }
                }
                filter.Filter = [new MongoId(id)];
                var plate = filter.GetType().GetProperty("Plate");
                if (plate?.CanWrite == true) plate.SetValue(filter, plate.PropertyType == typeof(string) ? id : new MongoId(id));
            }
        }
    }

    private void RigPanelModPool(Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>> pools,VariantDefinition variant)
    {
        if(variant.ChestRigEffect?.SoftArmorClass is not >0)return;
        var id=new MongoId(variant.TemplateId);
        if(!pools.TryGetValue(id,out var mods))pools[id]=mods=new(StringComparer.Ordinal);
        if(databaseService.GetItems().TryGetValue(id,out var rig))
            foreach(var slot in rig.Properties?.Slots??[])
                if(slot.Name is "Soft_armor_front" or "Soft_armor_back" or "Groin")
                    mods[slot.Name]=[new MongoId(RigPanelId(variant,slot.Name))];
    }

    private bool CreateDefaultPreset(Preset sourcePreset, VariantDefinition variant)
    {
        var preset = cloner.Clone(sourcePreset);
        if (preset is null || preset.Items.Count == 0)
        {
            return false;
        }

        var sourceRoot = preset.Items.FirstOrDefault(item => item.Id.Equals(preset.Parent)) ?? preset.Items[0];
        var root = cloner.Clone(sourceRoot);
        if (root is null)
        {
            return false;
        }

        if (variant.ChestRigEffect?.SoftArmorClass > 0)
            foreach (var child in preset.Items.Where(i => i.ParentId?.ToString() == sourceRoot.Id.ToString() && i.SlotId is "Soft_armor_front" or "Soft_armor_back" or "Groin"))
                child.Template = new MongoId(RigPanelId(variant, child.SlotId!));
        root.Id = new MongoId();
        root.Template = new MongoId(variant.TemplateId);
        root.ParentId = null;
        root.SlotId = null;
        preset.Items = itemHelper.ReparentItemAndChildren(root, preset.Items);
        preset.Id = new MongoId(variant.PresetId);
        preset.Parent = root.Id;
        preset.Encyclopedia = new MongoId(variant.TemplateId);
        preset.ChangeWeaponName = false;
        preset.Name = $"{variant.DisplayName} Default";
        databaseService.GetGlobals().ItemPresets[preset.Id] = preset;
        return true;
    }

    private static bool IsSafeForBotPool(VariantDefinition variant) =>
        variant.SpawnEnabled && variant.WeaponClass is not "grenadeLauncher" and not "specialWeapon";

    private static bool IsHolsterOnly(IReadOnlyCollection<EquipmentSlots> slots) =>
        slots.Count == 1 && slots.Contains(EquipmentSlots.Holster);

    private int AddLegendaryWeaponsToPmcPools(IReadOnlyCollection<SpawnVariant> variants, double chancePercent)
    {
        var safeChancePercent = double.IsNaN(chancePercent) || double.IsInfinity(chancePercent)
            ? 1d
            : chancePercent;
        var fraction = Math.Clamp(safeChancePercent, 0d, 99d) / 100d;
        if (fraction <= 0d || variants.Count == 0)
        {
            return 0;
        }

        var registrations = 0;
        var botTypes = databaseService.GetBots().Types;
        var variantsBySlot = variants
            .SelectMany(entry => entry.EquipmentSlots.Select(slot => (entry.Variant, Slot: slot)))
            .Where(entry => entry.Slot is EquipmentSlots.FirstPrimaryWeapon or EquipmentSlots.SecondPrimaryWeapon or EquipmentSlots.Holster)
            .GroupBy(entry => entry.Slot);

        foreach (var slotGroup in variantsBySlot)
        {
            var entries = slotGroup.ToArray();
            var generatedIds = entries
                .Select(entry => new MongoId(entry.Variant.TemplateId))
                .ToHashSet();

            foreach (var entry in entries)
            {
                // Register compatible mod pools first. A zero placeholder is
                // replaced below with a pool-relative weight for each faction.
                customItemService.AddCustomWeaponToPMCs(entry.Variant.TemplateId, 0d, slotGroup.Key.ToString());
                registrations++;
            }

            foreach (var faction in new[] { "usec", "bear" })
            {
                if (!botTypes.TryGetValue(faction, out var botType) || botType is null ||
                    !botType.BotInventory.Equipment.TryGetValue(slotGroup.Key, out var equipmentPool))
                {
                    logger.Warning($"[Escape From Borderlands] Could not weight Legendary weapons for {faction} {slotGroup.Key}: equipment pool missing.");
                    continue;
                }

                var existingWeight = equipmentPool
                    .Where(entry => !generatedIds.Contains(entry.Key) && entry.Value > 0d)
                    .Sum(entry => entry.Value);
                if (existingWeight <= 0d)
                {
                    logger.Warning($"[Escape From Borderlands] Could not weight Legendary weapons for {faction} {slotGroup.Key}: base pool has no positive weight.");
                    continue;
                }

                // W / (base + W) = requested fraction.
                var totalLegendaryWeight = existingWeight * fraction / (1d - fraction);
                var perVariantWeight = totalLegendaryWeight / entries.Length;
                foreach (var generatedId in generatedIds)
                {
                    equipmentPool[generatedId] = perVariantWeight;
                }
            }
        }

        return registrations;
    }

    private BossRegistrationResult AddLegendaryWeaponsToStandaloneBossPools(
        IReadOnlyCollection<SpawnVariant> variants,
        double chancePercent)
    {
        var fraction = SafeFraction(chancePercent, 10d);
        if (fraction <= 0d || variants.Count == 0)
        {
            return new BossRegistrationResult(0, 0, 0);
        }

        var variantsBySource = variants
            .GroupBy(entry => entry.Variant.SourceTemplateId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var activeIds = variants
            .Select(entry => new MongoId(entry.Variant.TemplateId))
            .ToHashSet();
        var entriesAdded = 0;
        var poolsAdjusted = 0;
        var bossesAdjusted = 0;

        foreach (var (role, botType) in databaseService.GetBots().Types)
        {
            if (botType is null || !IsStandaloneBossRole(role))
            {
                continue;
            }

            var bossWasAdjusted = false;
            foreach (var slot in new[] { EquipmentSlots.FirstPrimaryWeapon, EquipmentSlots.SecondPrimaryWeapon, EquipmentSlots.Holster })
            {
                if (!botType.BotInventory.Equipment.TryGetValue(slot, out var pool))
                {
                    continue;
                }

                foreach (var activeId in activeIds)
                {
                    pool.Remove(activeId);
                }

                var eligibleSources = pool
                    .Where(entry => entry.Value > 0d &&
                        variantsBySource.ContainsKey(entry.Key.ToString()) &&
                        botType.BotInventory.Mods.ContainsKey(entry.Key))
                    .ToArray();
                if (eligibleSources.Length == 0)
                {
                    continue;
                }

                var baseWeight = pool.Where(entry => entry.Value > 0d).Sum(entry => entry.Value);
                var eligibleSourceWeight = eligibleSources.Sum(entry => entry.Value);
                if (baseWeight <= 0d || eligibleSourceWeight <= 0d)
                {
                    continue;
                }

                var totalLegendaryWeight = baseWeight * fraction / (1d - fraction);
                foreach (var sourceEntry in eligibleSources)
                {
                    var sourceId = sourceEntry.Key.ToString();
                    var sourceVariants = variantsBySource[sourceId];
                    var sourceLegendaryWeight = totalLegendaryWeight * sourceEntry.Value / eligibleSourceWeight;
                    var perVariantWeight = sourceLegendaryWeight / sourceVariants.Length;

                    foreach (var spawnVariant in sourceVariants)
                    {
                        var variantId = new MongoId(spawnVariant.Variant.TemplateId);
                        if (!CopyBossWeaponModPool(botType.BotInventory.Mods, sourceEntry.Key, variantId))
                        {
                            continue;
                        }

                        pool[variantId] = perVariantWeight;
                        entriesAdded++;
                    }
                }

                poolsAdjusted++;
                bossWasAdjusted = true;
            }

            if (bossWasAdjusted)
            {
                bossesAdjusted++;
            }
        }

        return new BossRegistrationResult(entriesAdded, poolsAdjusted, bossesAdjusted);
    }

    private static bool CopyBossWeaponModPool(
        Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> modPools,
        MongoId sourceId,
        MongoId variantId)
    {
        if (!modPools.TryGetValue(sourceId, out var sourceMods))
        {
            return false;
        }

        modPools[variantId] = sourceMods.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToHashSet(),
            StringComparer.Ordinal);
        return true;
    }

    private static double SafeFraction(double chancePercent, double fallbackPercent)
    {
        var safeChance = double.IsNaN(chancePercent) || double.IsInfinity(chancePercent)
            ? fallbackPercent
            : chancePercent;
        return Math.Clamp(safeChance, 0d, 99d) / 100d;
    }

    private static bool IsStandaloneBossRole(string role) =>
        role.StartsWith("boss", StringComparison.OrdinalIgnoreCase);

    private void EnableMeleeLooting(LegendaryCatalog catalog)
    {
        MeleeLootingPolicy.Apply(catalog, databaseService.GetItems());
        logger.Info("[Escape From Borderlands] Corpse looting enabled for supported vanilla melee weapons and their Legendary variants. Chained Labrys and Superfors DB 2020 remain unlootable; zombie attacks remain unchanged.");
    }

    private int AddMeleeWeaponsToStandalonePools(RarityConfiguration config)
    {
        var catalog = LegendaryCatalog.Load();
        EnableMeleeLooting(catalog);
        var variants = catalog.Variants.Where(variant => variant.MeleeEffect is not null)
            .GroupBy(variant => variant.SourceTemplateId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var activeIds = catalog.Variants.Where(variant => variant.MeleeEffect is not null)
            .Select(variant => new MongoId(variant.TemplateId)).ToHashSet();
        var entries = 0;
        foreach (var (role, bot) in databaseService.GetBots().Types)
        {
            var isPmc = role is "pmcbear" or "pmcusec";
            var isBoss = IsStandaloneBossRole(role);
            if (!isPmc && !isBoss || bot is null || !bot.BotInventory.Equipment.TryGetValue(EquipmentSlots.Scabbard, out var pool)) continue;
            foreach (var id in activeIds) pool.Remove(id);
            var fraction = isPmc
                ? (config.AddToPmcWeapons ? SafeFraction(config.EffectiveStandalonePmcChancePercent, 1d) : 0d)
                : (config.AddToBossWeapons ? SafeFraction(config.BossLegendaryChancePercent, 10d) : 0d);
            if (fraction <= 0d) continue;
            var eligible = pool.Where(pair => pair.Value > 0d && variants.ContainsKey(pair.Key.ToString())).ToArray();
            var originalWeight = pool.Where(pair => pair.Value > 0d).Sum(pair => pair.Value);
            var eligibleWeight = eligible.Sum(pair => pair.Value);
            if (originalWeight <= 0d || eligibleWeight <= 0d) continue;
            var total = originalWeight * fraction / (1d - fraction);
            foreach (var source in eligible)
            {
                var clones = variants[source.Key.ToString()];
                foreach (var variant in clones)
                {
                    pool[new MongoId(variant.TemplateId)] = total * source.Value / eligibleWeight / clones.Length;
                    entries++;
                }
            }
        }
        return entries;
    }

    public void FinalizeMeleeLooting()
    {
        var catalog = LegendaryCatalog.Load();
        EnableMeleeLooting(catalog);
        var retiredIds = catalog.LegacyAliases.Where(alias => alias.WeaponClass == "melee")
            .Select(alias => new MongoId(alias.TemplateId)).ToHashSet();
        // Remove stale/imported retired melee registrations regardless of configured spawn chance.
        foreach (var bot in databaseService.GetBots().Types.Values)
        {
            if (bot is null) continue;
            foreach (var pool in bot.BotInventory.Equipment.Values)
                foreach (var id in retiredIds) pool.Remove(id);
            foreach (var id in retiredIds) bot.BotInventory.Mods.Remove(id);
        }
    }

    private EquipmentSlots[] GetAllowedEquipmentSlots(MongoId sourceTemplateId)
    {
        if (!databaseService.GetItems().TryGetValue(DefaultInventoryTemplateId, out var inventoryTemplate))
        {
            logger.Error("[Escape From Borderlands] Default player inventory template was not found; generated weapons cannot be whitelisted for equipment slots.");
            return [];
        }

        return (inventoryTemplate.Properties?.Slots ?? [])
            .Where(slot => Enum.TryParse<EquipmentSlots>(slot.Name, out var equipmentSlot) &&
                equipmentSlot is EquipmentSlots.FirstPrimaryWeapon or EquipmentSlots.SecondPrimaryWeapon or EquipmentSlots.Holster or EquipmentSlots.Scabbard or EquipmentSlots.Backpack or EquipmentSlots.TacticalVest or EquipmentSlots.ArmorVest or EquipmentSlots.Earpiece &&
                (slot.Properties?.Filters ?? []).Any(filter =>
                    filter.Filter?.Any(filterId => filterId.Equals(sourceTemplateId) || itemHelper.IsOfBaseclass(sourceTemplateId, filterId)) == true))
            .Select(slot => Enum.Parse<EquipmentSlots>(slot.Name!))
            .Distinct()
            .ToArray();
    }

    private void WhitelistCloneForEquipmentSlots(IEnumerable<EquipmentSlots> equipmentSlots, string cloneTemplateId)
    {
        if (!databaseService.GetItems().TryGetValue(DefaultInventoryTemplateId, out var inventoryTemplate))
        {
            return;
        }

        var allowedSlots = equipmentSlots.ToHashSet();
        var cloneId = new MongoId(cloneTemplateId);
        foreach (var slot in inventoryTemplate.Properties?.Slots ?? [])
        {
            if (!Enum.TryParse<EquipmentSlots>(slot.Name, out var equipmentSlot) || !allowedSlots.Contains(equipmentSlot))
            {
                continue;
            }

            var targetFilter = (slot.Properties?.Filters ?? []).FirstOrDefault(filter => filter.Filter is not null);
            targetFilter?.Filter?.Add(cloneId);
        }
    }

    private void ReportError(string message, ref int count)
    {
        count++;
        if (count <= MaximumReportedErrors)
        {
            logger.Error($"[Escape From Borderlands] {message}");
        }
    }

    private bool TryGetSource(
        string sourceTemplateId,
        string sourceName,
        Dictionary<string, SourceWeapon> sourceCache,
        ref int errors,
        out SourceWeapon source)
    {
        if (sourceCache.TryGetValue(sourceTemplateId, out source!))
        {
            return true;
        }

        var sourceId = new MongoId(sourceTemplateId);
        if (!databaseService.GetItems().TryGetValue(sourceId, out var sourceItem))
        {
            ReportError($"Vanilla source {sourceTemplateId} ({sourceName}) was not found.", ref errors);
            source = null!;
            return false;
        }

        var handbook = databaseService.GetHandbook().Items.Find(item => item.Id == sourceId);
        if (handbook is null)
        {
            ReportError($"Handbook entry for {sourceTemplateId} ({sourceName}) was not found.", ref errors);
            source = null!;
            return false;
        }

        var preset = databaseService.GetGlobals().ItemPresets.Values.FirstOrDefault(candidate =>
            candidate.Encyclopedia.HasValue && candidate.Encyclopedia.Value.Equals(sourceId));
        var fleaPrice = databaseService.GetPrices().TryGetValue(sourceId, out var sourceFleaPrice)
            ? sourceFleaPrice
            : handbook.Price.GetValueOrDefault(50_000d);
        var equipmentSlots = GetAllowedEquipmentSlots(sourceId);
        if (equipmentSlots.Length == 0 && sourceItem.Parent.ToString() != "543be6564bdc2df4348b4568")
        {
            ReportError($"Vanilla source {sourceTemplateId} ({sourceName}) is not allowed in a player weapon slot.", ref errors);
            source = null!;
            return false;
        }

        source = new SourceWeapon(sourceItem, handbook, preset, fleaPrice, equipmentSlots);
        sourceCache[sourceTemplateId] = source;
        return true;
    }

    private sealed record SourceWeapon(
        TemplateItem Item,
        HandbookItem Handbook,
        Preset? Preset,
        double FleaPrice,
        EquipmentSlots[] EquipmentSlots);

    private sealed record SpawnVariant(VariantDefinition Variant, EquipmentSlots[] EquipmentSlots);
    private sealed record BossRegistrationResult(int Entries, int Pools, int Bosses);
}
#pragma warning restore CS0618
