using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;

namespace EscapeFromBorderlands.Server;

// APBS shares ExUsec between Rogues and Arena fighters, and Default between
// other roles. Normalize each returned role-specific copy, never those shared
// datasets. Capture imported clone IDs before EFB's PMC zero-rate cleanup so
// independent factions still honor APBS import/blacklist decisions.
internal static class FactionEquipmentSpawning
{
    private static RarityConfiguration Config = new();
    private static HashSet<string> AllIds = new(StringComparer.Ordinal);
    private static VariantDefinition[] Active = [];
    private static readonly Dictionary<(int Tier, string Category), HashSet<string>> Imported = new();
    private static readonly HashSet<MethodBase> Installed = [];
    internal static void SelectConfiguration(RarityConfiguration config) => Config = config;

    internal static void Configure(IEnumerable<VariantDefinition> definitions, IEnumerable<string> aliases, RarityConfiguration config)
    {
        var all = definitions.ToArray();
        Config = config;
        AllIds = all.Select(v => v.TemplateId).Concat(aliases).ToHashSet(StringComparer.Ordinal);
        Active = all.Where(v => v.SpawnEnabled && v.GrenadeChargesPerRaid == 0 && v.WeaponClass is not "grenadeLauncher" and not "specialWeapon").ToArray();
        Imported.Clear();
    }

    internal static void RememberImported(int tier, string category, IEnumerable<string> ids)
    {
        if (!Imported.TryGetValue((tier, category), out var set)) Imported[(tier, category)] = set = new(StringComparer.Ordinal);
        set.UnionWith(ids);
    }

    internal static void InstallApbs(Type helper)
    {
        var target = helper.GetMethod("GetEquipmentByBotRole", new[] { typeof(string), typeof(int) })
            ?? throw new InvalidOperationException("APBS role-specific equipment method was not found.");
        if (Installed.Contains(target)) return;
        var returns = target.ReturnType;
        if (!returns.IsGenericType || returns.GetGenericTypeDefinition() != typeof(Dictionary<,>) || returns.GenericTypeArguments[1] != typeof(Dictionary<MongoId, double>))
            throw new InvalidOperationException("APBS equipment dictionary shape is unsupported.");
        var postfix = typeof(FactionEquipmentSpawning).GetMethod(nameof(AfterEquipment), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(returns.GenericTypeArguments[0]);
        new Harmony("com.tkw.escapefromborderlands.server.factions").Patch(target,
            postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
        Installed.Add(target);
    }

    // These native argument names are shared by the supported APBS versions.
    private static void AfterEquipment<TSlot>(ref Dictionary<TSlot, Dictionary<MongoId, double>> __result, string botRole, int tierNumber) where TSlot : notnull
    {
        // APBS 2.0 returns shared datasets; 2.2 returns copies. Own both levels
        // before role normalization so either version keeps its cache untouched.
        __result = __result.ToDictionary(p => p.Key, p => new Dictionary<MongoId, double>(p.Value));
        Apply(__result, botRole, tierNumber);
    }

    internal static void Apply(object equipment, string role, int tier)
    {
        if (equipment is not IDictionary slots || BotSpawnGroups.Group(role) is "pmcUsec" or "pmcBear") return;
        foreach (DictionaryEntry slot in slots)
        {
            if (slot.Value is not IDictionary pool) continue;
            var slotName = slot.Key.ToString() ?? "";
            var eligibleSlot = slotName is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster" or "Scabbard" or "Backpack" or "TacticalVest" or "ArmorVest" or "Earpiece";
            var category = RarityConfiguration.Category(slotName);
            var before = Entries(pool).ToArray();
            foreach (var entry in before) if (AllIds.Contains(entry.Key.ToString() ?? "")) pool.Remove(entry.Key);
            var fraction = eligibleSlot && !CustomBotSpawning.UsesPostGeneration(role) ? Config.BotFraction("apbs", role, category) : 0d;
            if (fraction <= 0d || pool.Count == 0) continue;
            var allowed = Imported.GetValueOrDefault((tier, category));
            var variants = Active.Where(v => Category(v) == category && (category == "melee" || allowed?.Contains(v.TemplateId) == true)).ToArray();
            var sources = variants.GroupBy(v => v.SourceTemplateId).ToDictionary(g => g.Key, g => g.Select(v => v.TemplateId).ToArray(), StringComparer.Ordinal);
            var originals = Entries(pool).ToDictionary(e => e.Key.ToString()!, e => Convert.ToDouble(e.Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);
            var weights = CategorySpawnPolicy.Weights(originals, sources, fraction, variants.ToDictionary(v => v.TemplateId, v => v.SpawnWeight));
            var example = pool.Keys.Cast<object>().First();
            foreach (var pair in weights)
            {
                var key = example is string ? pair.Key : Activator.CreateInstance(example.GetType(), new object[] { pair.Key });
                if (key is not null) pool[key] = pair.Value;
            }
        }
    }

    private static string Category(VariantDefinition v) => v.IsEarpiece ? "earpieces" : v.MeleeEffect is not null ? "melee" : v.BackpackEffect is not null ? "backpacks" : v.ChestRigEffect is not null ? "chestRigs" : "guns";

    private static IEnumerable<DictionaryEntry> Entries(IDictionary pool)
    { foreach (DictionaryEntry entry in pool) yield return entry; }
}
