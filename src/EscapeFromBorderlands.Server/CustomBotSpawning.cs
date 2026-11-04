using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace EscapeFromBorderlands.Server;

// Finish custom loadouts at the response boundary, after native and optional
// generation hooks. This path owns the roll; custom roles never also use pools.
internal static class CustomBotSpawning
{
    private const string Evaluated = "efbCustomLegendarySpawnEvaluated";
    private static readonly object Sync = new();
    private static readonly HashSet<string> NativeRoles = Enum.GetNames<WildSpawnType>()
        .Concat(new[] { "usec", "bear", "raider", "raiders", "rogue", "rogues" })
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ScavFactions = new(StringComparer.OrdinalIgnoreCase)
        { "scav", "scavs", "savage", "infected" };
    private static Dictionary<string, VariantDefinition[]> BySource = new(StringComparer.Ordinal);
    private static IReadOnlyDictionary<MongoId, TemplateItem> Templates = new Dictionary<MongoId, TemplateItem>();
    private static readonly Dictionary<(string Source, string Destination), bool> Fits = new();
    private static RarityConfiguration Configuration = new();
    private static IServiceProvider Services = null!;
    private static Action<string> Warning = _ => { };
    private static bool Installed, ReportedMetadata;

    internal static bool UsesPostGeneration(string? role) => !string.IsNullOrWhiteSpace(role) && !NativeRoles.Contains(role);

    internal static void Install(IEnumerable<VariantDefinition> definitions, RarityConfiguration config,
        IReadOnlyDictionary<MongoId, TemplateItem> templates, IServiceProvider services, Action<string> info, Action<string> warning)
    {
        lock (Sync)
        {
            Configuration = config; Templates = templates; Services = services; Warning = warning; Fits.Clear();
            BySource = definitions.Where(v => v.SpawnEnabled && !v.IsMysteryFleaOffer && v.SpawnWeight > 0 &&
                    v.WeaponClass is not "grenadeLauncher" and not "specialWeapon")
                .GroupBy(v => v.SourceTemplateId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            RefreshMetadata();
            if (!Installed)
            {
                new Harmony("com.tKwNw.escapefromborderlands.server.custombots").Patch(
                    AccessTools.Method(typeof(BotController), nameof(BotController.Generate)),
                    postfix: new HarmonyMethod(typeof(CustomBotSpawning), nameof(AfterGeneration)) { priority = Priority.Last });
                Installed = true;
            }
            info("[Escape From Borderlands] Custom bot loadouts use otherNonScavs after generation when faction metadata identifies a non-Scav. Unclassified custom roles are excluded; normal faction settings retain priority.");
        }
    }

    // Reuse the live optional services. No assembly, file or bot scans per frame.
    private static void RefreshMetadata()
    {
        try
        {
            Type? Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t is not null);
            var factionType = Find("MoreBotsServer.Services.FactionService");
            var botType = Find("MoreBotsServer.Services.MoreBotsCustomBotTypeService");
            var factionService = factionType is null ? null : Services.GetService(factionType);
            var typeService = botType is null ? null : Services.GetService(botType);
            var factions = factionService is null ? null : factionType?.GetProperty("Factions")?.GetValue(factionService) as IDictionary;
            var names = typeService is null ? null : botType?.GetProperty("CustomWildSpawnTypes")?.GetValue(typeService) as IDictionary;
            var roles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (factions is not null)
                foreach (DictionaryEntry pair in factions)
                    VisitFaction(pair.Value, pair.Key.ToString() ?? "", false, names, roles, new HashSet<object>(ReferenceEqualityComparer.Instance));
            BotSpawnGroups.SetCustomRoles(roles);
        }
        catch (Exception error)
        {
            BotSpawnGroups.SetCustomRoles(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
            if (!ReportedMetadata) { ReportedMetadata = true; Warning("[Escape From Borderlands] Custom bot faction metadata is unavailable; unclassified roles remain excluded. " + error.Message); }
        }
    }

    private static object? Get(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);
    private static void VisitFaction(object? faction, string key, bool parentScav, IDictionary? names,
        Dictionary<string, bool> roles, HashSet<object> path)
    {
        if (faction is null || !path.Add(faction)) return;
        var name = Get(faction, "Name")?.ToString() ?? key;
        var scav = parentScav || ScavFactions.Contains(key) || ScavFactions.Contains(name) || Get(faction, "IsScav") is true;
        if (Get(faction, "BotTypes") is IEnumerable types)
            foreach (var value in types)
            {
                var role = value?.ToString() ?? "";
                if (value is not null && names is not null && (value is not string || int.TryParse(role, out _)))
                {
                    var number = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (names.Contains(number)) role = names[number]?.ToString() ?? role;
                }
                if (!UsesPostGeneration(role) || long.TryParse(role, out _)) continue;
                // Scav ancestry wins if a custom role belongs to several factions.
                roles[role] = scav || roles.GetValueOrDefault(role);
            }
        if (Get(faction, "SubFactions") is IEnumerable children)
            foreach (var child in children) VisitFaction(child, "", scav, names, roles, path);
        path.Remove(faction);
    }

    private static void AfterGeneration(ref Task<IEnumerable<BotBase>> __result) => __result = Complete(__result);
    private static async Task<IEnumerable<BotBase>> Complete(Task<IEnumerable<BotBase>> pending)
    {
        var result = (await pending.ConfigureAwait(false)).ToArray();
        lock (Sync)
        {
            RefreshMetadata();
            foreach (var bot in result)
            {
                try { Apply(bot, Random.Shared.NextDouble); }
                catch (Exception error) { Warning("[Escape From Borderlands] A custom bot loadout could not be converted; its remaining original gear is retained. " + error.Message); }
            }
        }
        return result;
    }

    internal static int Apply(BotBase bot, Func<double> random)
    {
        var role = bot.Info?.Settings?.Role;
        var inventory = bot.Inventory;
        if (!Configuration.Enabled || !UsesPostGeneration(role) || !BotSpawnGroups.IsClassifiedNonScav(role) ||
            inventory?.Items is null || inventory.Equipment is null || inventory.ExtensionData?.ContainsKey(Evaluated) == true) return 0;
        // The inventory marker survives normal JSON/cache clones. No growing
        // global set and no repeat chance when a cached bot is returned again.
        inventory.ExtensionData ??= new();
        inventory.ExtensionData[Evaluated] = true;
        var changed = 0;
        foreach (var item in inventory.Items)
        {
            if (item is null || item.Template.ToString() is not { Length: > 0 } sourceId || !BySource.TryGetValue(sourceId, out var definitions)) continue;
            var equipped = item.ParentId == inventory.Equipment.ToString();
            var category = definitions[0].GrenadeChargesPerRaid > 0 ? "grenades" :
                equipped && item.SlotId is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster" or "Scabbard" or "Backpack" or "TacticalVest" or "ArmorVest" or "Earpiece"
                    ? RarityConfiguration.Category(item.SlotId) : "";
            if (category.Length == 0) continue;
            var chance = Configuration.BotFraction("shared", role!, category);
            if (chance <= 0 || random() >= chance) continue;
            var eligible = definitions.Where(v => Compatible(v, item)).ToArray();
            if (eligible.Length == 0) continue;
            var weight = random() * eligible.Sum(v => v.SpawnWeight);
            var chosen = eligible[^1];
            foreach (var candidate in eligible) { weight -= candidate.SpawnWeight; if (weight < 0) { chosen = candidate; break; } }
            item.Template = new MongoId(chosen.TemplateId);
            changed++;
        }
        return changed;
    }

    private static bool Compatible(VariantDefinition variant, Item item)
    {
        // Special soft panels cannot replace generated vanilla plates without
        // altering attachments. Prefer another compatible signature instead.
        if (variant.ChestRigEffect?.SoftArmorClass > 0) return false;
        var mode = item.Upd?.FireMode?.FireMode;
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
