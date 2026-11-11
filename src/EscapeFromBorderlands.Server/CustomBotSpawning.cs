using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace EscapeFromBorderlands.Server;

// Finish Scav/custom loadouts at the response boundary, after native and
// optional generation hooks. This path owns their roll instead of pool entries.
internal static class CustomBotSpawning
{
    private const string Evaluated = "efbCustomLegendarySpawnEvaluated";
    private static readonly object Sync = new();
    private static readonly HashSet<string> NativeRoles = Enum.GetNames<WildSpawnType>()
        .Concat(new[] { "usec", "bear", "raider", "raiders", "rogue", "rogues" })
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ScavFactions = new(StringComparer.OrdinalIgnoreCase)
        { "scav", "scavs", "savage", "infected" };
    private static Dictionary<MongoId, SourceChoices> BySource = new();
    private static int CategoryMask;
    private static Dictionary<string, GroupRates> RatesByGroup = new(StringComparer.Ordinal);
    private static int AssemblyRevision, ObservedAssemblyRevision = -1;
    private static Type? FactionType, BotType;
    private static System.Reflection.PropertyInfo? FactionsProperty, TypesProperty;
    private static readonly Dictionary<(Type, string), System.Reflection.PropertyInfo?> FactionProperties = new();
    private static bool MetadataEmpty;

    static CustomBotSpawning() => AppDomain.CurrentDomain.AssemblyLoad += (_, _) => Interlocked.Increment(ref AssemblyRevision);

    private sealed class SourceChoices(VariantDefinition[] definitions)
    {
        internal readonly int Category = Array.IndexOf(RarityConfiguration.Categories, LegendarySpawnCategories.For(definitions[0]));
        private readonly Dictionary<string, WeightedSpawnChoices> _byMode = new(StringComparer.OrdinalIgnoreCase);

        internal WeightedSpawnChoices ForMode(string mode)
        {
            if (_byMode.TryGetValue(mode, out var found)) return found;
            var candidates = definitions.Where(v => Compatible(v, mode)).ToArray();
            var choices = candidates.Length == 0 ? WeightedSpawnChoices.Empty : new WeightedSpawnChoices(candidates);
            // Do not retain arbitrary fire-mode strings supplied by mods.
            if (_byMode.Count < 16) _byMode[mode] = choices;
            return choices;
        }
    }
    private static IReadOnlyDictionary<MongoId, TemplateItem> Templates = new Dictionary<MongoId, TemplateItem>();
    private static readonly Dictionary<(string Source, string Destination), bool> Fits = new();
    private static RarityConfiguration Configuration = new();
    private static IServiceProvider Services = null!;
    private static Action<string> Warning = _ => { };
    private static bool Installed, ReportedMetadata;

    // Every role's eligible categories have exactly one owner at the response boundary.
    internal static bool UsesPostGeneration(string? role) => !string.IsNullOrWhiteSpace(role);

    internal static void Install(IEnumerable<VariantDefinition> definitions, RarityConfiguration config,
        IReadOnlyDictionary<MongoId, TemplateItem> templates, IServiceProvider services, Action<string> info, Action<string> warning)
    {
        lock (Sync)
        {
            Configuration = config; Templates = templates; Services = services; Warning = warning; Fits.Clear();
            BySource = definitions.Where(v => v.SpawnEnabled && !v.IsMysteryFleaOffer && v.SpawnWeight > 0 &&
                    v.WeaponClass is not "grenadeLauncher" and not "specialWeapon")
                .Where(v => double.IsFinite(v.SpawnWeight)).GroupBy(v => new MongoId(v.SourceTemplateId))
                .ToDictionary(g => g.Key, g => new SourceChoices(g.ToArray()));
            CategoryMask = 0;
            foreach (var source in BySource.Values)
                if (source.Category >= 0) CategoryMask |= 1 << source.Category;
            RatesByGroup = new(StringComparer.Ordinal);
            foreach (var group in new[] { "scavs", "bosses", "pmcUsec", "pmcBear" }.Concat(BotSpawnGroups.Expanded))
                RatesByGroup[group] = new GroupRates(group);
            ObservedAssemblyRevision = -1;
            MetadataEmpty = false;
            FactionProperties.Clear();
            RefreshMetadata();
            if (!Installed)
            {
                new Harmony("com.tKwNw.escapefromborderlands.server.custombots").Patch(
                    AccessTools.Method(typeof(BotController), nameof(BotController.Generate)),
                    postfix: new HarmonyMethod(typeof(CustomBotSpawning), nameof(AfterGeneration)) { priority = Priority.Last });
                Installed = true;
            }
            info("[Escape From Borderlands] All eligible factions use configured category chances once after generation, independently of APBS imports. Empty slots, unsupported items, infected and unclassified roles remain unchanged.");
        }
    }

    // Reuse the live optional services. No assembly, file or bot scans per frame.
    private static void RefreshMetadata()
    {
        try
        {
            var revision = Volatile.Read(ref AssemblyRevision);
            if (ObservedAssemblyRevision != revision)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                FactionType = Find(assemblies, "MoreBotsServer.Services.FactionService");
                BotType = Find(assemblies, "MoreBotsServer.Services.MoreBotsCustomBotTypeService");
                FactionsProperty = FactionType?.GetProperty("Factions");
                TypesProperty = BotType?.GetProperty("CustomWildSpawnTypes");
                ObservedAssemblyRevision = revision;
            }

            // The optional service's dictionaries can be edited in place. Keep
            // reading live metadata rather than caching a potentially stale role map.
            var factionService = FactionType is null ? null : Services.GetService(FactionType);
            var factions = factionService is null ? null : FactionsProperty?.GetValue(factionService) as IDictionary;
            if (factions is not { Count: > 0 })
            {
                if (!MetadataEmpty) BotSpawnGroups.SetCustomRoles(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
                MetadataEmpty = true;
                return;
            }

            var typeService = BotType is null ? null : Services.GetService(BotType);
            var names = typeService is null ? null : TypesProperty?.GetValue(typeService) as IDictionary;
            var roles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var infectedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (DictionaryEntry pair in factions)
                VisitFaction(pair.Value, pair.Key.ToString() ?? "", false, false, names, roles, infectedRoles, path);
            BotSpawnGroups.SetCustomRoles(roles, infectedRoles);
            MetadataEmpty = false;
        }
        catch (Exception error)
        {
            BotSpawnGroups.SetCustomRoles(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
            MetadataEmpty = true;
            if (!ReportedMetadata) { ReportedMetadata = true; Warning("[Escape From Borderlands] Custom bot faction metadata is unavailable; unclassified roles remain excluded. " + error.Message); }
        }
    }

    private static Type? Find(System.Reflection.Assembly[] assemblies, string name)
    {
        foreach (var assembly in assemblies)
            if (assembly.GetType(name, false) is { } type) return type;
        return null;
    }

    private static object? Get(object? value, string name)
    {
        if (value is null) return null;
        var key = (value.GetType(), name);
        if (!FactionProperties.TryGetValue(key, out var member)) FactionProperties[key] = member = key.Item1.GetProperty(name);
        return member?.GetValue(value);
    }
    private static void VisitFaction(object? faction, string key, bool parentScav, bool parentInfected, IDictionary? names,
        Dictionary<string, bool> roles, HashSet<string> infectedRoles, HashSet<object> path)
    {
        if (faction is null || !path.Add(faction)) return;
        var name = Get(faction, "Name")?.ToString() ?? key;
        var infected = parentInfected || key.Equals("infected", StringComparison.OrdinalIgnoreCase) || name.Equals("infected", StringComparison.OrdinalIgnoreCase);
        var scav = parentScav || infected || ScavFactions.Contains(key) || ScavFactions.Contains(name) || Get(faction, "IsScav") is true;
        if (Get(faction, "BotTypes") is IEnumerable types)
            foreach (var value in types)
            {
                var role = value?.ToString() ?? "";
                if (value is not null && names is not null && (value is not string || int.TryParse(role, out _)))
                {
                    var number = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (names.Contains(number)) role = names[number]?.ToString() ?? role;
                }
                if (NativeRoles.Contains(role) || long.TryParse(role, out _)) continue;
                // Scav ancestry wins if a custom role belongs to several factions.
                roles[role] = scav || roles.GetValueOrDefault(role);
                if (infected) infectedRoles.Add(role);
            }
        if (Get(faction, "SubFactions") is IEnumerable children)
            foreach (var child in children) VisitFaction(child, "", scav, infected, names, roles, infectedRoles, path);
        path.Remove(faction);
    }

    private static void AfterGeneration(ref Task<IEnumerable<BotBase>> __result) => __result = Complete(__result);
    private static async Task<IEnumerable<BotBase>> Complete(Task<IEnumerable<BotBase>> pending)
    {
        var generated = await pending.ConfigureAwait(false);
        var result = generated as BotBase[] ?? generated.ToArray();
        if (result.Length == 0) return result;
        var timing = ServerPerformanceTrace.Measure("bot post-generation");
        var changed = 0;
        lock (Sync)
        {
            RefreshMetadata();
            foreach (var bot in result)
            {
                try { changed += Apply(bot, Random.Shared.NextDouble); }
                catch (Exception error) { Warning("[Escape From Borderlands] A generated bot loadout could not be converted; its remaining original gear is retained. " + error.Message); }
            }
        }
        timing.Complete(result.Length, changed);
        return result;
    }

    private sealed class GroupRates
    {
        internal readonly double[] Fractions = new double[RarityConfiguration.Categories.Length];
        internal readonly int Mask;

        internal GroupRates(string group)
        {
            for (var i = 0; i < Fractions.Length; i++)
            {
                if ((CategoryMask & (1 << i)) == 0) continue;
                Fractions[i] = Configuration.BotFractionForGroup("shared", group, RarityConfiguration.Categories[i]);
                if (Fractions[i] > 0d) Mask |= 1 << i;
            }
        }
    }

    internal static int Apply(BotBase bot, Func<double> random)
    {
        var role = bot.Info?.Settings?.Role;
        var inventory = bot.Inventory;
        if (!Configuration.Enabled || string.IsNullOrWhiteSpace(role) ||
            inventory?.Items is null || inventory.Equipment is null || inventory.ExtensionData?.ContainsKey(Evaluated) == true) return 0;
        var group = BotSpawnGroups.Group(role);
        // The inventory marker survives normal JSON/cache clones. No growing
        // global set and no repeat chance when a cached bot is returned again.
        inventory.ExtensionData ??= new();
        inventory.ExtensionData[Evaluated] = true;
        // Rates are replaced when the finalized spawn configuration is
        // installed. Role classification still observes live metadata.
        if (group is null || !RatesByGroup.TryGetValue(group, out var rates) || rates.Mask == 0) return 0;
        var enabledMask = rates.Mask;
        var categories = RarityConfiguration.Categories;

        var changed = 0;
        var equipmentId = inventory.Equipment.ToString();
        foreach (var item in inventory.Items)
        {
            if (item is null || !BySource.TryGetValue(item.Template, out var source) || source.Category < 0 ||
                (enabledMask & (1 << source.Category)) == 0) continue;
            var equipped = item.ParentId == equipmentId;
            var category = categories[source.Category];
            if (category != "grenades" && (!equipped ||
                (item.SlotId == "Headwear" ? category is not "armoredHeadwear" and not "unarmoredHeadwear" :
                    item.SlotId is not ("FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster" or "Scabbard" or "Backpack" or "TacticalVest" or "ArmorVest" or "Earpiece") ||
                    RarityConfiguration.Category(item.SlotId) != category))) continue;
            if (random() >= rates.Fractions[source.Category]) continue;
            var choices = source.ForMode(item.Upd?.FireMode?.FireMode ?? "");
            if (choices.IsEmpty) continue;
            var chosen = choices.Pick(random());
            if (chosen.Variant.IsHeadwear) HeadwearTemplates.Convert(item, chosen.Id, inventory.Items);
            else item.Template = chosen.Id;
            changed++;
        }
        return changed;
    }

    private static bool Compatible(VariantDefinition variant, string mode)
    {
        // Special soft panels cannot replace generated vanilla plates without
        // altering attachments. Prefer another compatible signature instead.
        if (variant.ChestRigEffect?.SoftArmorClass > 0) return false;
        if (!string.IsNullOrEmpty(mode) && variant.FireModes.Count > 0 && !variant.FireModes.Contains(mode, StringComparer.OrdinalIgnoreCase)) return false;
        var key = (variant.SourceTemplateId, variant.TemplateId);
        if (Fits.TryGetValue(key, out var fits)) return fits;
        if (!Templates.TryGetValue(new MongoId(variant.SourceTemplateId), out var source) ||
            !Templates.TryGetValue(new MongoId(variant.TemplateId), out var target)) return Fits[key] = false;
        var grids = target.Properties?.Grids?.ToArray() ?? [];
        foreach (var original in source.Properties?.Grids ?? [])
        {
            var replacement = grids.FirstOrDefault(g => g.Name == original.Name);
            if (replacement?.Properties is not { } next || original.Properties is not { } old ||
                next.CellsH.GetValueOrDefault() < old.CellsH.GetValueOrDefault() || next.CellsV.GetValueOrDefault() < old.CellsV.GetValueOrDefault() ||
                JsonSerializer.Serialize(next.Filters) != JsonSerializer.Serialize(old.Filters)) return Fits[key] = false;
        }
        return Fits[key] = true;
    }
}
