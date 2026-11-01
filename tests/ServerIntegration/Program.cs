using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Modding;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using EscapeFromBorderlands.Server;
using Path = System.IO.Path;

if (args.Length != 3) throw new ArgumentException("Usage: ServerIntegration.dll REFERENCES REPORT_JSON MOD_SERVER_DIRECTORY");
var references = Path.GetFullPath(args[0]);
var serverDirectory = Path.GetFullPath(args[2]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
    var directory = name.Name == "EscapeFromBorderlands.Server" ? serverDirectory : Path.Combine(references, "Server");
    var path = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
// Resolve the audit after bootstrap, avoiding native field types during Main JIT.
await (Task)Assembly.GetExecutingAssembly().GetType("Audit")!
    .GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, [references, Path.GetFullPath(args[1])])!;

internal static class Audit
{
    // Defer native static fields until the bootstrap has installed assembly resolution.
    static Audit() { }
    private static readonly MongoId ThirdPartyId = new("aabbccddeeff001122334455");
    private static readonly MongoId ThirdPartyMeleeId = new("aabbccddeeff001122334456");
    private static readonly MongoId ScytheId = new("63495c500c297e20065a08b1");
    private static readonly EquipmentSlots[] WeaponSlots = [EquipmentSlots.FirstPrimaryWeapon, EquipmentSlots.SecondPrimaryWeapon, EquipmentSlots.Holster];
    private static readonly List<Check> Checks = [];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    // Unrelated host fields are deliberately absent from these minimal native fixtures.
    private static T Fixture<T>() => (T)Activator.CreateInstance(typeof(T))!;
    private static ISptLogger<T> Logger<T>() => DispatchProxy.Create<ISptLogger<T>, CaptureLogger>();
    private static void Assert(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static void Test(string id, string name, Action action) {
        try { action(); Checks.Add(new(id, name, "PASS", null)); Console.WriteLine($"PASS {id}: {name}"); }
        catch (Exception error) { Checks.Add(new(id, name, "FAIL", error.ToString())); Console.WriteLine($"FAIL {id}: {error.Message}"); }
    }
    private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString()!;
    private static MongoId Id(JsonElement row, string key = "templateId") => new(Text(row, key));
    private static bool Safe(JsonElement row) => Text(row, "weaponClass") is not "grenadeLauncher" and not "specialWeapon";

    internal static async Task Run(string references, string output)
    {
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        var templates = Fixture<TemplateTable>();
        Set(templates, "Items", json.Deserialize<Dictionary<MongoId, TemplateItem>>(File.ReadAllText(Path.Combine(references, "Database/items.json")))!);
        Set(templates, "Handbook", json.Deserialize<HandbookBase>(File.ReadAllText(Path.Combine(references, "Database/handbook.json")))!);
        Set(templates, "Prices", json.Deserialize<Dictionary<MongoId, double>>(File.ReadAllText(Path.Combine(references, "Database/prices.json")))!);
        var globals = json.Deserialize<GlobalTable>(File.ReadAllText(Path.Combine(references, "Database/globals.json")))!;
        var english = json.Deserialize<GlobalLocaleDictionary>(File.ReadAllText(Path.Combine(references, "Database/en.json")))!;
        var locales = Fixture<LocaleTable>();
        Set(locales, "Global", new Dictionary<string, LazyLoad<GlobalLocaleDictionary>> { ["en"] = new(() => english, true) });
        Set(locales, "Languages", new Dictionary<string, string> { ["en"] = "English" });
        var cloner = new SPTarkov.Server.Core.Utils.Cloners.FastCloner();
        using var catalog = JsonDocument.Parse(typeof(RarityMod).Assembly.GetManifestResourceStream(
            typeof(RarityMod).Assembly.GetManifestResourceNames().Single(name => name.EndsWith("LegendaryDefinitions.json")))!);
        var expanded = typeof(RarityMod).Assembly.GetType("EscapeFromBorderlands.Server.LegendaryCatalog", true)!.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
        var expandedJson = JsonSerializer.SerializeToElement(expanded, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var variants = expandedJson.GetProperty("variants").EnumerateArray().ToArray();
        var aliases = catalog.RootElement.GetProperty("legacyAliases").EnumerateArray().ToArray();
        var activeIds = variants.Select(row => Id(row)).ToHashSet();
        var meleeVariants = variants.Where(row => row.TryGetProperty("meleeEffect", out var effect) && effect.ValueKind != JsonValueKind.Null).ToArray();
        var meleeIds = meleeVariants.Select(row => Id(row)).ToHashSet();
        var vanillaMeleeIds = catalog.RootElement.GetProperty("vanillaMeleeTemplateIds").EnumerateArray().Select(row => new MongoId(row.GetString()!)).ToHashSet();
        var protectedMeleeIds = catalog.RootElement.GetProperty("unlootableMeleeTemplateIds").EnumerateArray().Select(row => new MongoId(row.GetString()!)).ToHashSet();
        var retiredMeleeIds = aliases.Where(row => Text(row, "weaponClass") == "melee").Select(row => Id(row)).ToHashSet();
        var backpackVariants = variants.Where(row => row.TryGetProperty("backpackEffect", out var effect) && effect.ValueKind != JsonValueKind.Null).ToArray();
        var backpackIds = backpackVariants.Select(row => Id(row)).ToHashSet();
        var rigVariants = variants.Where(row => row.TryGetProperty("chestRigEffect", out var effect) && effect.ValueKind != JsonValueKind.Null).ToArray();
        var rigIds = rigVariants.Select(row => Id(row)).ToHashSet();
        var earpieceVariants = variants.Where(row => row.TryGetProperty("isEarpiece", out var value) && value.GetBoolean()).ToArray();
        var earpieceIds = earpieceVariants.Select(row => Id(row)).ToHashSet();
        MongoId PanelId(JsonElement row, string slot) => new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("efb:rig-panel:" + Text(row,"storageFamilyId") + ":" + slot)))[..24].ToLowerInvariant());
        var expectedPanels = new Dictionary<MongoId,(MongoId Source,int Class)>();
        foreach (var row in rigVariants) {
            var effect=row.GetProperty("chestRigEffect");
            if (!effect.TryGetProperty("softArmorClass",out var level) || level.GetInt32()<=0) continue;
            foreach(var slot in templates.Items[Id(row,"sourceTemplateId")].Properties!.Slots ?? [])
                if(slot.Name is "Soft_armor_front" or "Soft_armor_back" or "Groin")
                    foreach(var filter in slot.Properties!.Filters ?? [])
                        if(filter.Filter?.FirstOrDefault() is MongoId sourcePanel) expectedPanels[PanelId(row,slot.Name!)]=(sourcePanel,level.GetInt32());
        }
        var firstBagSource = Id(backpackVariants[0], "sourceTemplateId");
        var otherBagSource = Id(backpackVariants.First(v => Id(v,"sourceTemplateId")!=firstBagSource), "sourceTemplateId");
        var retiredIds = aliases.Select(row => Id(row)).ToHashSet();
        var allIds = activeIds.Union(retiredIds).ToHashSet();
        var rifle = Id(variants.First(row => Text(row, "sourceShortName") == "M4A1"), "sourceTemplateId");
        var otherRifle = Id(variants.First(row => Text(row, "sourceShortName") == "AKM"), "sourceTemplateId");
        var pistol = Id(variants.First(row => Text(row, "sourceShortName") == "TT Gold"), "sourceTemplateId");
        var melee = Id(meleeVariants.Single(row => Text(row, "specialId") == "melee_point_taken"), "sourceTemplateId");
        var otherMelee = Id(meleeVariants.Single(row => Text(row, "specialId") == "melee_standard_issue"), "sourceTemplateId");
        var thirdParty = cloner.Clone(templates.Items[rifle])!; thirdParty.Id = ThirdPartyId; templates.Items[ThirdPartyId] = thirdParty;
        var thirdPartyMelee = cloner.Clone(templates.Items[melee])!; thirdPartyMelee.Id = ThirdPartyMeleeId; templates.Items[ThirdPartyMeleeId] = thirdPartyMelee;
        var bots = Fixture<BotTable>();
        Set(bots, "Types", new Dictionary<string, BotType> {
            ["usec"] = Bot(rifle, otherRifle, pistol, melee, otherMelee, false), ["bear"] = Bot(rifle, otherRifle, pistol, melee, otherMelee, false),
            ["bossbully"] = Bot(rifle, otherRifle, pistol, melee, otherMelee, true), ["bossgluhar"] = Bot(rifle, otherRifle, pistol, melee, otherMelee, true, false),
            ["assault"] = Bot(rifle, otherRifle, pistol, melee, otherMelee, false), ["followerbully"] = Bot(rifle, otherRifle, pistol, melee, otherMelee, false)
        });
        foreach (var (role, bot) in bots.Types)
            bot!.BotInventory!.Equipment![EquipmentSlots.Backpack] = new() { [firstBagSource]=role=="bossgluhar"?0:100, [otherBagSource]=role=="bossgluhar"?0:50 };
        var originalBots = cloner.Clone(bots.Types)!;
        var originalItems = templates.Items.ToDictionary(pair => pair.Key, pair => json.Serialize(pair.Value));
        var originalPrices = new Dictionary<MongoId, double>(templates.Prices);
        var originalHandbook = templates.Handbook.Items.ToDictionary(item => item.Id, item => json.Serialize(item));
        var originalPresets = globals.ItemPresets.ToDictionary(pair => pair.Key, pair => json.Serialize(pair.Value));
        var pmc = Fixture<PmcConfig>(); pmc.GlobalLootBlacklist = []; pmc.UsecType = "usec"; pmc.BearType = "bear";
        var ragfair = Fixture<RagfairConfig>(); ragfair.Dynamic = Fixture<Dynamic>();
        ragfair.Dynamic.Blacklist = Fixture<RagfairBlacklist>(); ragfair.Dynamic.Blacklist.Custom = [];
        var baseClasses = new ItemBaseClassService(Logger<ItemBaseClassService>(), templates, null!);
        baseClasses.HydrateItemBaseClassCache();
        var itemHelper = new ItemHelper(Logger<ItemHelper>(), templates, null!, null!, baseClasses, null!, null!, null!, cloner);
        var loaded = new SptMod { Directory = Path.GetDirectoryName(typeof(RarityMod).Assembly.Location)!, ModMetadata = new ModMetadata(), Assemblies = [typeof(RarityMod).Assembly] };
        var cache = new ModItemCacheService(Logger<ModItemCacheService>(), [loaded]);
        var custom = new CustomItemService(Logger<CustomItemService>(), templates, locales, bots, itemHelper, pmc, baseClasses, cache, cloner);
        RarityMod Mod() => new(Logger<RarityMod>(), templates, globals, bots, new TradersTable(), custom, itemHelper, new LocaleService(Logger<LocaleService>(), locales, new LocaleConfig { GameLocale="en", ServerLocale="en", ServerSupportedLocales=["en"], Fallbacks=[] }), locales, cloner, ragfair, null!);
        var mod = Mod();
        var configPath = Path.Combine(loaded.Directory, "config", "config.json");
        var previousConfig = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
        var hadConfigDirectory = Directory.Exists(Path.GetDirectoryName(configPath));
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var defaultConfig = new RarityConfiguration();
        void Config(RarityConfiguration config) => File.WriteAllText(configPath, JsonSerializer.Serialize(config, Options));
        try {
            Config(defaultConfig);
            Console.WriteLine("Real SPT 4.1.6 models/services and exported tables; minimal native bot/config fixtures.");
            await mod.OnLoadAsync(CancellationToken.None);
            Test("N01", "Native registration includes all active/retired definitions and private rig armor panels", () =>
                Assert(allIds.All(templates.Items.ContainsKey) && templates.Items.Count == originalItems.Count + allIds.Count + expectedPanels.Count && expectedPanels.Keys.All(templates.Items.ContainsKey), "missing or unexpected generated templates"));
            Test("N02", "Every generated item has a positive native flea-price entry, including blocked aliases", () =>
                Assert(allIds.All(id => templates.Prices.TryGetValue(id, out var price) && price > 0), "missing or zero flea price"));
            Test("N03", "Active handbook/flea prices retain the configured 10x base multiplier", () => {
                foreach (var row in variants) {
                    var source = templates.Handbook.Items.Single(item => item.Id == Id(row, "sourceTemplateId"));
                    var basePrice = Math.Max(1, source.Price.GetValueOrDefault(50_000));
                    var marketPrice = originalPrices.GetValueOrDefault(source.Id, basePrice); if (marketPrice <= 0) marketPrice = basePrice;
                    Assert(templates.Handbook.Items.Single(item => item.Id == Id(row)).Price == Math.Ceiling(basePrice * 10), "handbook price changed");
                    Assert(templates.Prices[Id(row)] == Math.Ceiling(marketPrice * 10), "flea base price changed");
                }
            });
            Test("N04", "Active and retired clones remain blocked from flea listing and requirements", () =>
                Assert(allIds.All(id => templates.Items[id].Properties!.CanSellOnRagfair == false && templates.Items[id].Properties!.CanRequireOnRagfair == false && ragfair.Dynamic.Blacklist.Custom.Contains(id)), "flea blocking missing"));
            Test("N05", "Native mod cache records exactly the generated clone IDs", () =>
                Assert(cache.GetCachedItemIdsFromMod(new ModMetadata().ModGuid).ToHashSet().SetEquals(allIds), "mod cache IDs differ"));
            Test("N06", "Generated names and descriptions materialize through native LazyLoad locales", () => {
                var values = locales.Global!["en"].Value!;
                foreach (var row in variants.Concat(aliases)) foreach (var (suffix, key) in new[] { ("Name", "displayName"), ("ShortName", "shortName"), ("Description", "description") }) {
                    var expected = Text(row, key);
                    if (suffix == "Description" && ((row.TryGetProperty("isEarpiece", out var ear) && ear.GetBoolean()) || (row.TryGetProperty("backpackEffect", out var effect) && effect.ValueKind != JsonValueKind.Null) || (row.TryGetProperty("chestRigEffect", out effect) && effect.ValueKind != JsonValueKind.Null) || (row.TryGetProperty("grenadeChargesPerRaid",out var charges)&&charges.GetInt32()>0)))
                        expected = "<color=#66D9EF>" + Text(row, "effectText") + "</color>" + (expected.Length == 0 ? "" : "\n\n" + expected);
                    Assert(values.TryGetValue(Text(row, "templateId") + " " + suffix, out var text) && text.ToString() == expected,
                        $"locale mismatch for {Text(row, "templateId")} {suffix}");
                }
            });
            Test("N40", "Every backpack retains its source template and handbook category instead of a pistol category", () => {
                var handbook = templates.Handbook.Items.ToDictionary(item => item.Id);
                var backpackBase = new MongoId("5448e53e4bdc2d60728b4567");
                var firearmBase = new MongoId("5422acb9af1c889c16000029");
                foreach (var row in backpackVariants) {
                    var id = Id(row); var source = Id(row, "sourceTemplateId");
                    Assert(templates.Items[id].Parent == templates.Items[source].Parent, "backpack template parent changed");
                    Assert(handbook[id].ParentId == handbook[source].ParentId, "backpack handbook category changed");
                    var seen = new HashSet<MongoId>(); var current = id;
                    while (seen.Add(current) && templates.Items.TryGetValue(current, out var item)) current = item.Parent;
                    Assert(seen.Contains(backpackBase) && !seen.Contains(firearmBase), "backpack resolves to a firearm base class");
                }
            });
            Test("N41", "Backpack locale entries contain actual item text rather than inspection prefab placeholders", () => {
                var values = locales.Global!["en"].Value!;
                foreach (var row in backpackVariants) foreach (var suffix in new[] { "Name", "ShortName", "Description" }) {
                    var text = values[Text(row,"templateId") + " " + suffix].ToString();
                    Assert(!string.IsNullOrWhiteSpace(text) && text != "blablabla" && text != "OK" && text != "WEAPON > PISTOL", "placeholder stored as a backpack locale");
                }
            });
            Test("N07", "Original items preserve every property except intended melee loot flags and clone whitelists", () => {
                var allowed = new HashSet<MongoId> { new("55d7217a4bdc2d86028b456d"), ItemTpl.HIDEOUTAREACONTAINER_WEAPONSTAND_STASH_1, ItemTpl.HIDEOUTAREACONTAINER_WEAPONSTAND_STASH_2, ItemTpl.HIDEOUTAREACONTAINER_WEAPONSTAND_STASH_3 };
                foreach (var (id, before) in originalItems.Where(pair => !allowed.Contains(pair.Key))) {
                    var expected = before;
                    if (vanillaMeleeIds.Contains(id)) {
                        var item = json.Deserialize<TemplateItem>(before)!;
                        item.Properties!.Unlootable = false; item.Properties.UnlootableFromSide = [];
                        expected = json.Serialize(item);
                    }
                    if (protectedMeleeIds.Contains(id)) {
                        var item = json.Deserialize<TemplateItem>(before)!;
                        item.Properties!.Unlootable = true; item.Properties.UnlootableFromSlot = "Scabbard";
                        item.Properties.UnlootableFromSide = [PlayerSideMask.Bear, PlayerSideMask.Usec, PlayerSideMask.Savage];
                        expected = json.Serialize(item);
                    }
                    var expectedItem = json.Deserialize<TemplateItem>(expected)!;
                    var oldConflicts = expectedItem.Properties?.ConflictingItems;
                    var newConflicts = templates.Items[id].Properties?.ConflictingItems;
                    if (oldConflicts is not null && newConflicts is not null) {
                        Assert(oldConflicts.All(newConflicts.Contains), "native conflict removed");
                        foreach (var added in newConflicts.Except(oldConflicts))
                            Assert(earpieceIds.Contains(added) && oldConflicts.Contains(Id(earpieceVariants.Single(row => Id(row) == added), "sourceTemplateId")), "unrelated native conflict added");
                        expectedItem.Properties!.ConflictingItems = newConflicts;
                        expected = json.Serialize(expectedItem);
                    }
                    Assert(json.Serialize(templates.Items[id]) == expected, $"original item mutated: {id}");
                }
                Assert(json.Serialize(templates.Items[ThirdPartyId]) == originalItems[ThirdPartyId], "third-party fixture mutated");
                Assert(json.Serialize(templates.Items[ThirdPartyMeleeId]) == originalItems[ThirdPartyMeleeId], "third-party melee mutated");
            });
            Test("N08", "Original handbook, prices and presets are preserved", () =>
                Assert(originalHandbook.All(pair => json.Serialize(templates.Handbook.Items.Single(item => item.Id == pair.Key)) == pair.Value) && originalPrices.All(pair => templates.Prices[pair.Key] == pair.Value) && originalPresets.All(pair => json.Serialize(globals.ItemPresets[pair.Key]) == pair.Value), "original lookup changed"));
            Test("N09", "Clones inherit source attachment slots without sharing mutable properties", () => {
                foreach (var row in variants.Concat(aliases)) {
                    var clone = templates.Items[Id(row)]; var source = templates.Items[Id(row, "sourceTemplateId")];
                    Assert(!ReferenceEquals(clone.Properties, source.Properties), "clone shares source properties");
                    var armoredRig = row.TryGetProperty("chestRigEffect",out var rigEffect) && rigEffect.ValueKind!=JsonValueKind.Null && rigEffect.TryGetProperty("softArmorClass",out var level) && level.GetInt32()>0;
                    if(!armoredRig) Assert(json.Serialize(clone.Properties!.Slots)==json.Serialize(source.Properties!.Slots),"clone attachment filter mismatch");
                    else foreach(var slot in clone.Properties!.Slots ?? []) {
                        if(slot.Name is "Soft_armor_front" or "Soft_armor_back" or "Groin")
                            Assert(slot.Properties!.Filters!.All(filter=>filter.Filter!.SequenceEqual(new[]{PanelId(row,slot.Name!)})),"rig does not require its own armor panel");
                        else Assert(json.Serialize(slot)==json.Serialize(source.Properties!.Slots!.Single(original=>original.Name==slot.Name)),"unrelated rig attachment slot changed");
                    }
                }
            });
            Test("N10", "Generated equipment whitelists match source firearm slots and melee Scabbard slots", () => {
                var inventory = templates.Items[new MongoId("55d7217a4bdc2d86028b456d")];
                foreach (var row in variants.Concat(aliases)) {
                    if(row.TryGetProperty("grenadeChargesPerRaid",out var charges)&&charges.GetInt32()>0)continue;
                    var source = Id(row, "sourceTemplateId");
                    var expected = inventory.Properties!.Slots!.Where(slot => WeaponSlots.Append(EquipmentSlots.Scabbard).Append(EquipmentSlots.Backpack).Append(EquipmentSlots.TacticalVest).Append(EquipmentSlots.ArmorVest).Append(EquipmentSlots.Earpiece).Any(value => value.ToString() == slot.Name) && slot.Properties!.Filters!.Any(filter => filter.Filter!.Any(id => id == source || itemHelper.IsOfBaseclass(source, id)))).ToArray();
                    Assert(expected.Length > 0 && expected.All(slot => slot.Properties!.Filters!.Any(filter => filter.Filter!.Contains(Id(row)))), "clone not equippable");
                    if (expected.Length == 1 && expected[0].Name == "Holster") Assert(templates.Items[Id(row)].Properties!.WeapUseType == "secondary", "holster-only receiver is not secondary");
                }
            });
            var expectedPresetRows = variants.Where(row => originalPresets.Keys.Any(key => globals.ItemPresets[key].Encyclopedia == Id(row, "sourceTemplateId"))).ToArray();
            Test("N11", "All available firearm and rig source presets generate at their frozen catalog preset IDs", () =>
                Assert(expectedPresetRows.Length > 1208 && globals.ItemPresets.Count == originalPresets.Count + expectedPresetRows.Length && expectedPresetRows.All(row => globals.ItemPresets.ContainsKey(Id(row, "presetId"))), "generated preset count or IDs differ"));
            Test("N12", "Native preset reparenting leaves one clone root and valid attachment parents", () => {
                foreach (var row in expectedPresetRows) {
                    var preset = globals.ItemPresets[Id(row, "presetId")]; var root = preset.Items.Single(item => item.Id == preset.Parent);
                    Assert(root.Template == Id(row) && root.ParentId == null && root.SlotId == null && preset.Encyclopedia == Id(row), "preset root mismatch");
                    Assert(preset.Items.Select(item => item.Id).Distinct().Count() == preset.Items.Count && preset.Items.Where(item => item.Id != root.Id).All(item => !string.IsNullOrEmpty(item.ParentId) && preset.Items.Any(parent => parent.Id.ToString() == item.ParentId)), "dangling preset attachment");
                }
            });
            Test("N42", "Rig armor panels preserve source properties/locales and apply the specified armor class", () => {
                Assert(expectedPanels.Count>0,"no armored rig panels exercised");
                foreach(var (id,panel) in expectedPanels) {
                    var expected=json.Deserialize<TemplateItem>(originalItems[panel.Source])!;expected.Id=id;expected.Properties!.ArmorClass=panel.Class;
                    Assert(json.Serialize(templates.Items[id])==json.Serialize(expected),"private panel changes unrelated source properties");
                    foreach(var suffix in new[]{" Name"," ShortName"," Description"})
                        Assert(english[id+suffix]==english[panel.Source+suffix],"private panel locale differs from source");
                }
            });
            Test("N43", "Every rig retains its source parent, handbook category and actual generated panel templates", () => {
                foreach(var row in rigVariants) {
                    var id=Id(row);var source=Id(row,"sourceTemplateId");
                    Assert(templates.Items[id].Parent==templates.Items[source].Parent,"rig parent changed");
                    Assert(templates.Handbook.Items.Single(x=>x.Id==id).ParentId==templates.Handbook.Items.Single(x=>x.Id==source).ParentId,"rig handbook category changed");
                }
                foreach(var row in expectedPresetRows.Where(row=>rigIds.Contains(Id(row))))
                    Assert(globals.ItemPresets[Id(row,"presetId")].Items.All(item=>templates.Items.ContainsKey(item.Template)),"rig preset references an absent panel");
            });
            Test("N13", "No spawn-pool mutation occurs during early receiver registration", () =>
                Assert(json.Serialize(bots.Types) == json.Serialize(originalBots), "early bot pools changed"));
            // Final-stage scenarios reuse the registered native catalog and restore the bot fixture.
            // Only pending spawn-variant state is copied into fresh production coordinators.
            RarityMod PendingMod() {
                Set(bots, "Types", cloner.Clone(originalBots)!); var pending = Mod();
                var field = typeof(RarityMod).GetField("_spawnVariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
                field.SetValue(pending, field.GetValue(mod)); return pending;
            }
            async Task Finalize(RarityMod pending, RarityConfiguration config) {
                Config(config); await new ApbsCompatibility(Logger<ApbsCompatibility>(), new NoServices(), pending).OnLoadAsync(CancellationToken.None);
            }
            await Finalize(mod, defaultConfig);
            Test("N14", "Native no-APBS final startup selects standalone", () =>
                Assert(CaptureLogger.Messages.Any(entry => entry.Text.Contains("APBS was not found")) && CaptureLogger.Messages.Any(entry => entry.Text.Contains("Final spawn method: standalone")), "standalone selection absent"));
            Test("N15", "Both PMC factions have exactly 1% combined EFB weight in populated weapon slots", () => {
                foreach (var faction in new[] { "usec", "bear" }) foreach (var slot in WeaponSlots) Share(bots.Types[faction]!.BotInventory!.Equipment![slot]!, activeIds, .01);
            });
            Test("N16", "Boss pools have 10% EFB weight restricted to positive source/mod pools", () => {
                foreach (var role in new[] { "bossbully", "bossgluhar" }) foreach (var slot in WeaponSlots) {
                    var before = originalBots[role]!.BotInventory!; var pool = bots.Types[role]!.BotInventory!.Equipment![slot]!;
                    var eligible = before.Equipment![slot]!.Where(pair => pair.Value > 0 && pair.Key != ThirdPartyId && before.Mods!.ContainsKey(pair.Key)).Select(pair => pair.Key).ToHashSet();
                    var spawned = variants.Where(row => pool.ContainsKey(Id(row))).ToArray();
                    Assert(spawned.All(row => Safe(row) && eligible.Contains(Id(row, "sourceTemplateId"))), "boss received unnatural weapon");
                    Share(pool, activeIds, eligible.Count > 0 ? .1 : 0);
                }
            });
            Test("N17", "Existing vanilla and third-party bot weights/mod filters remain unchanged", () => {
                foreach (var (role, before) in originalBots) {
                    var after = bots.Types[role]!.BotInventory!;
                    Assert(before!.BotInventory!.Equipment!.All(slot => slot.Value!.All(pair => after.Equipment![slot.Key]![pair.Key] == pair.Value)), "base bot weight changed");
                    Assert(before!.BotInventory!.Mods!.All(pair => json.Serialize(after.Mods![pair.Key]) == json.Serialize(pair.Value)), "original bot attachment filters changed");
                }
            });
            Test("N18", "Scavs remain untouched and followers receive only eligible current variants", () => {
                foreach (var role in new[] { "assault" }) Assert(json.Serialize(bots.Types[role]) == json.Serialize(originalBots[role]), "non-boss bot changed");
                var excluded = retiredIds.Union(variants.Where(row => !Safe(row)).Select(row => Id(row))).ToHashSet();
                Assert(bots.Types.Values.All(bot => bot!.BotInventory!.Equipment!.Values.All(pool => !pool!.Keys.Any(excluded.Contains))), "retired or unsafe weapon registered");
            });
            Test("N19", "Spawned clone mod pools exist and boss pools are independent copies", () => {
                foreach (var (role, bot) in bots.Types) foreach (var id in bot!.BotInventory!.Equipment!.Values.SelectMany(pool => pool!.Keys).Where(id => activeIds.Contains(id) && !meleeIds.Contains(id) && !backpackIds.Contains(id)).Distinct()) {
                    Assert(bot!.BotInventory!.Mods!.ContainsKey(id), "clone attachment pool missing");
                    if (role.StartsWith("boss")) {
                        var source = Id(variants.Single(row => Id(row) == id), "sourceTemplateId");
                        Assert(!ReferenceEquals(bot!.BotInventory!.Mods![id], bot!.BotInventory!.Mods![source]) && json.Serialize(bot!.BotInventory!.Mods![id]) == json.Serialize(bot!.BotInventory!.Mods![source]), "boss mod pool not copied");
                    }
                }
            });
            Test("N25", "All 21 supported vanilla melee and 48 active clones are lootable; zombie attacks are unchanged", () => {
                Assert(vanillaMeleeIds.Count == 21 && meleeIds.Count == 48, "wrong melee catalog counts");
                Assert(vanillaMeleeIds.Union(meleeIds).All(id => templates.Items[id].Properties!.Unlootable == false && !templates.Items[id].Properties!.UnlootableFromSide!.Any()), "melee corpse protection remains");
                var zombie = new MongoId("670ad7f1ad195290cd00da7a");
                Assert(!vanillaMeleeIds.Contains(zombie) && json.Serialize(templates.Items[zombie]) == originalItems[zombie], "zombie attack changed");
            });
            Test("N26", "Both native PMC factions receive 1% melee weight restricted to natural positive sources", () => {
                foreach (var role in new[] { "usec", "bear" }) {
                    var original = originalBots[role]!.BotInventory!.Equipment![EquipmentSlots.Scabbard]!;
                    var pool = bots.Types[role]!.BotInventory!.Equipment![EquipmentSlots.Scabbard]!;
                    Share(pool, meleeIds, .01);
                    foreach (var row in meleeVariants.Where(row => pool.ContainsKey(Id(row))))
                        Assert(original.TryGetValue(Id(row, "sourceTemplateId"), out var weight) && weight > 0, "PMC received unnatural melee");
                    Assert(pool[ThirdPartyMeleeId] == original[ThirdPartyMeleeId], "third-party melee weight changed");
                }
            });
            Test("N27", "Boss Scabbard receives 10% only of natural melee; zero-weight sources and the scythe add none", () => {
                var pool = bots.Types["bossbully"]!.BotInventory!.Equipment![EquipmentSlots.Scabbard]!;
                Share(pool, meleeIds, .1);
                Assert(meleeVariants.Where(row => pool.ContainsKey(Id(row))).All(row => Id(row, "sourceTemplateId") == melee), "boss received unnatural melee");
                Share(bots.Types["bossgluhar"]!.BotInventory!.Equipment![EquipmentSlots.Scabbard]!, meleeIds, 0);
            });
            Test("N28", "Melee clones stay in Scabbard and never enter firearm equipment or attachment pools", () => {
                foreach (var bot in bots.Types.Values) {
                    Assert(WeaponSlots.All(slot => !bot!.BotInventory!.Equipment![slot]!.Keys.Any(meleeIds.Contains)), "melee clone entered firearm slot");
                    Assert(!bot!.BotInventory!.Mods!.Keys.Any(meleeIds.Contains), "melee clone entered firearm attachment pools");
                }
            });
            Test("N29", "Both unsupported boss models and four saved aliases have native Scabbard protection on all sides", () => {
                Assert(protectedMeleeIds.Count == 2 && retiredMeleeIds.Count == 4, "wrong protected model or alias count");
                foreach (var id in protectedMeleeIds.Union(retiredMeleeIds)) {
                    var props = templates.Items[id].Properties!;
                    Assert(props.Unlootable == true && props.UnlootableFromSlot == "Scabbard" && props.UnlootableFromSide!.ToHashSet().SetEquals(new[] { PlayerSideMask.Bear, PlayerSideMask.Usec, PlayerSideMask.Savage }), "missing native corpse-loot protection");
                }
            });
            Test("N32", "All 1340 backpack identities register and have native Backpack equipment whitelists", () => {
                Assert(backpackVariants.Length==1340 && backpackIds.All(templates.Items.ContainsKey), "backpack registration missing");
                var slot=templates.Items[new MongoId("55d7217a4bdc2d86028b456d")].Properties!.Slots!.Single(s=>s.Name=="Backpack");
                Assert(backpackIds.All(id=>slot.Properties!.Filters!.Any(f=>f.Filter!.Contains(id))),"backpack whitelist missing");
            });
            Test("N33", "Registered native backpack grids match their persistent layout definitions", () => {
                foreach(var row in backpackVariants) {
                    var definition = ((System.Collections.IEnumerable)expanded.GetType().GetProperty("Variants")!.GetValue(expanded)!).Cast<object>().Single(v => (string)v.GetType().GetProperty("TemplateId")!.GetValue(v)! == Text(row,"templateId"));
                    var effect=definition.GetType().GetProperty("BackpackEffect")!.GetValue(definition)!;
                    var sourceProps = cloner.Clone(templates.Items[Id(row,"sourceTemplateId")].Properties)!;
                    var storage=typeof(RarityMod).Assembly.GetType("EscapeFromBorderlands.Server.BackpackStorage",true)!;
                    var expected = storage.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Single(m=>m.Name=="Apply"&&m.GetParameters().Length==2).Invoke(null,[sourceProps,definition])!;
                    var actual = templates.Items[Id(row)].Properties!;
                    var expectedGrids=JsonSerializer.SerializeToElement(expected).GetProperty("Grids").GetRawText();
                    Assert(JsonSerializer.SerializeToElement(actual).GetProperty("Grids").GetRawText()==expectedGrids,"native registered grid graph differs from storage transformer");
                }
            });
            Test("N34", "Default backpack weights are 1% for both PMCs and 10% for eligible natural boss sources", () => {
                foreach(var role in new[]{"usec","bear","bossbully"}) Share(bots.Types[role]!.BotInventory!.Equipment![EquipmentSlots.Backpack]!,backpackIds,role.StartsWith("boss")?.1:.01);
                Share(bots.Types["bossgluhar"]!.BotInventory!.Equipment![EquipmentSlots.Backpack]!,backpackIds,0);
                foreach(var (role,bot) in bots.Types) foreach(var id in bot!.BotInventory!.Equipment![EquipmentSlots.Backpack]!.Keys.Where(backpackIds.Contains))
                    Assert(originalBots[role]!.BotInventory!.Equipment![EquipmentSlots.Backpack]!.GetValueOrDefault(Id(backpackVariants.Single(v=>Id(v)==id),"sourceTemplateId"))>0,"unnatural bag source");
            });
            var finalized = json.Serialize(bots.Types); mod.ApplyStandalonePmcSpawning(defaultConfig); mod.SelectApbsPmcSpawning(defaultConfig);
            Test("N20", "Repeated final-stage calls cannot double-register or switch methods", () => Assert(json.Serialize(bots.Types) == finalized, "repeat finalization changed pools"));
            await Finalize(PendingMod(), new RarityConfiguration { AddToNonScavFactions = false, StandalonePmcLegendaryChancePercent = 0, BossLegendaryChancePercent = 0, SpawnChances = new() { ["standalone"]=new(){Guns=0,Melee=0,Backpacks=0,ChestRigs=0}, ["bosses"]=new(){Guns=0,Melee=0,Backpacks=0,ChestRigs=0} } });
            Test("N21", "Native 0% configuration leaves all bot pools unchanged", () => Assert(json.Serialize(bots.Types) == json.Serialize(originalBots), "zero chance inserted clones"));
            await Finalize(PendingMod(), new RarityConfiguration { AddToPmcWeapons = false });
            Test("N22", "PMC disable preserves PMC inventories independently of bosses", () => {
                foreach (var faction in new[] { "usec", "bear" }) Assert(json.Serialize(bots.Types[faction]) == json.Serialize(originalBots[faction]), "disabled PMC changed");
                Share(bots.Types["bossbully"]!.BotInventory!.Equipment![EquipmentSlots.Holster]!, activeIds, .1);
                Share(bots.Types["bossbully"]!.BotInventory!.Equipment![EquipmentSlots.Scabbard]!, meleeIds, .1);
            });
            await Finalize(PendingMod(), new RarityConfiguration { AddToBossWeapons = false });
            Test("N23", "Boss disable preserves boss inventories independently of PMCs", () => {
                foreach (var role in new[] { "bossbully", "bossgluhar" }) Assert(json.Serialize(bots.Types[role]) == json.Serialize(originalBots[role]), "disabled boss changed");
                Share(bots.Types["usec"]!.BotInventory!.Equipment![EquipmentSlots.Holster]!, activeIds, .01);
                Share(bots.Types["usec"]!.BotInventory!.Equipment![EquipmentSlots.Scabbard]!, meleeIds, .01);
            });
            Test("N30", "Final stage removes stale retired melee from every native equipment and mod pool at any chance", () => {
                foreach (var config in new[] { new RarityConfiguration { AddToNonScavFactions = false, StandalonePmcLegendaryChancePercent = 0, BossLegendaryChancePercent = 0, SpawnChances = new() { ["standalone"]=new(){Guns=0,Melee=0,Backpacks=0,ChestRigs=0}, ["bosses"]=new(){Guns=0,Melee=0,Backpacks=0,ChestRigs=0} } }, new RarityConfiguration() }) {
                    var pending = PendingMod();
                    foreach (var bot in bots.Types.Values) foreach (var id in retiredMeleeIds) {
                        foreach (var pool in bot!.BotInventory!.Equipment!.Values) pool![id] = 1000;
                        bot!.BotInventory!.Mods![id] = new() { ["stale"] = [id] };
                    }
                    Finalize(pending, config).GetAwaiter().GetResult();
                    Assert(bots.Types.Values.All(bot => bot!.BotInventory!.Equipment!.Values.All(pool => !pool!.Keys.Any(retiredMeleeIds.Contains)) && !bot.BotInventory.Mods!.Keys.Any(retiredMeleeIds.Contains)), "stale retired melee survived finalization");
                }
            });
            Test("N31", "Final stage repairs loot flags overwritten by a later mod without changing unsupported models or aliases", () => {
                foreach (var id in vanillaMeleeIds.Union(meleeIds)) templates.Items[id].Properties!.Unlootable = true;
                foreach (var id in protectedMeleeIds.Union(retiredMeleeIds)) {
                    templates.Items[id].Properties!.Unlootable = false; templates.Items[id].Properties!.UnlootableFromSide = [];
                }
                mod.FinalizeMeleeLooting();
                Assert(vanillaMeleeIds.Union(meleeIds).All(id => templates.Items[id].Properties!.Unlootable == false && !templates.Items[id].Properties!.UnlootableFromSide!.Any()), "supported melee remained protected");
                Assert(protectedMeleeIds.Union(retiredMeleeIds).All(id => templates.Items[id].Properties!.Unlootable == true && templates.Items[id].Properties!.UnlootableFromSlot == "Scabbard" && templates.Items[id].Properties!.UnlootableFromSide!.Count() == 3), "unsupported models remained lootable");
            });
            await Finalize(PendingMod(), new RarityConfiguration { SpawnChances=new() { ["standalone"]=new(){Guns=0,Melee=2,Backpacks=7}, ["bosses"]=new(){Guns=0,Melee=3,Backpacks=9} } });
            Test("N35", "Gun, melee and backpack spawn settings operate independently", () => {
                foreach(var role in new[]{"usec","bear","bossbully"}) {
                    var boss=role.StartsWith("boss");var inventory=bots.Types[role]!.BotInventory!;
                    foreach(var slot in WeaponSlots) Share(inventory.Equipment![slot]!,activeIds,0);
                    Share(inventory.Equipment![EquipmentSlots.Scabbard]!,meleeIds,boss?.03:.02);
                    Share(inventory.Equipment![EquipmentSlots.Backpack]!,backpackIds,boss?.09:.07);
                }
            });
            ServerRewardAudit.Run(Test, configPath);
            var rigOnlyConfig = new RarityConfiguration { AddToNonScavFactions=false, AddToBossWeapons=false, SpawnChances = new() { ["standalone"]=new(){Guns=0,Melee=0,Backpacks=0,ChestRigs=7} } };
            var rigPending=PendingMod();
            foreach(var bot in bots.Types.Values)bot!.BotInventory!.Equipment![EquipmentSlots.TacticalVest]=new(){[Id(rigVariants[0],"sourceTemplateId")]=100};
            await Finalize(rigPending,rigOnlyConfig);
            Test("N44", "Chest-rig spawn chance independently produces exactly 7% PMC weight", () => {
                foreach(var faction in new[]{"usec","bear"}) {
                    var equipment=bots.Types[faction]!.BotInventory!.Equipment!;
                    Share(equipment[EquipmentSlots.TacticalVest]!,rigIds,.07);
                    foreach(var slot in WeaponSlots.Append(EquipmentSlots.Scabbard).Append(EquipmentSlots.Backpack))Share(equipment[slot]!,activeIds,0);
                }
            });
            rigPending=PendingMod();
            foreach(var bot in bots.Types.Values)bot!.BotInventory!.Equipment![EquipmentSlots.TacticalVest]=new(){[Id(rigVariants[0],"sourceTemplateId")]=100};
            rigOnlyConfig.SpawnChances["standalone"].ChestRigs=0;
            await Finalize(rigPending,rigOnlyConfig);
            Test("N45", "Explicit zero chest-rig chance leaves every equipment pool without EFB entries", () =>
                Assert(bots.Types.Values.All(bot=>bot!.BotInventory!.Equipment!.Values.All(pool=>!pool!.Keys.Any(activeIds.Contains))),"zero rig chance inserted EFB entries"));
            Test("N49", "All reusable grenade clones register with their native throwable parent and properties", () => {
                var grenades=variants.Where(row=>row.TryGetProperty("grenadeChargesPerRaid",out var charges)&&charges.GetInt32()>0).ToArray();
                Assert(grenades.Length==37,"grenade definitions missing");
                foreach(var row in grenades){var source=templates.Items[Id(row,"sourceTemplateId")];var clone=templates.Items[Id(row)];Assert(clone.Parent==source.Parent&&!ReferenceEquals(clone.Properties,source.Properties),"grenade parent/properties changed");Assert(clone.Properties!.CanSellOnRagfair==false,"grenade flea policy missing");}
            });
            Test("N50", "The grenade loot postfix installs on the real native 4.1.6 cache API", () => {
                var target=typeof(SPTarkov.Server.Core.Services.Bot.BotLootCacheService).GetMethod("GetLootFromCache")!;
                Assert(target.GetParameters().Take(3).Select(p=>p.Name).SequenceEqual(new[]{"botRole","isPmc","lootType"}),"native cache argument contract changed");
                var patch=HarmonyLib.Harmony.GetPatchInfo(target);
                Assert(patch is not null&&patch.Postfixes.Any(p=>p.owner=="com.tkw.escapefromborderlands.server.grenades"&&p.PatchMethod.Name=="AfterPool"),"native grenade hook not installed");
            });
            Test("N51", "All 39 earpieces retain native parents and are equippable in the real Earpiece slot", () => {
                Assert(earpieceVariants.Length == 39 && earpieceVariants.Select(row => Id(row,"sourceTemplateId")).Distinct().Count() == 11, "earpiece catalog incomplete");
                var slot = templates.Items[new MongoId("55d7217a4bdc2d86028b456d")].Properties!.Slots!.Single(s => s.Name == "Earpiece");
                foreach (var row in earpieceVariants) {
                    var clone = templates.Items[Id(row)]; var source = templates.Items[Id(row,"sourceTemplateId")];
                    Assert(clone.Parent == source.Parent && !ReferenceEquals(clone.Properties,source.Properties), "native earpiece parent/properties changed");
                    Assert(slot.Properties!.Filters!.Any(f => f.Filter!.Contains(Id(row))), "headset clone not equippable");
                }
            });
            Test("N52", "Native helmet and mask conflicts extend to every applicable headset clone", () => {
                var exercised = 0;
                foreach (var (id, before) in originalItems) {
                    var conflicts = json.Deserialize<TemplateItem>(before)!.Properties?.ConflictingItems;
                    if (conflicts is null) continue;
                    foreach (var row in earpieceVariants.Where(row => conflicts.Contains(Id(row,"sourceTemplateId")))) {
                        Assert(templates.Items[id].Properties!.ConflictingItems!.Contains(Id(row)), "one-sided source restriction lost");
                        exercised++;
                    }
                }
                Assert(exercised > 0, "no real native conflicts exercised");
            });
            Test("N24", "Native registration and final spawning produce no error logs", () => Assert(CaptureLogger.Messages.All(entry => entry.Level != "Error"), "native services logged errors"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                version = typeof(RarityMod).Assembly.GetName().Version!.ToString(), targetSptVersion = "4.1.6",
                scope = "Actual SPT assemblies, exported item/handbook/price/preset/locale tables, native CustomItemService/ItemHelper/FastCloner/base-class/cache; minimal native bot/config fixtures. Full host startup, bot generation, native raids, current APBS and Fika are not executed.",
                status = Checks.All(check => check.Status == "PASS") ? "PASS" : "FAIL", passed = Checks.Count(check => check.Status == "PASS"), failed = Checks.Count(check => check.Status == "FAIL"),
                activeWeapons = activeIds.Count, activeFirearms = activeIds.Count - meleeIds.Count - backpackIds.Count - rigIds.Count - 37 - earpieceIds.Count, activeEarpieces = earpieceIds.Count, activeGrenades = 37, activeChestRigs = rigIds.Count, privateRigArmorPanels = expectedPanels.Count, activeBackpacks = backpackIds.Count, activeMelee = meleeIds.Count,
                vanillaMeleeLootable = vanillaMeleeIds.Count, protectedMeleeSources = protectedMeleeIds.Count, retiredMeleeAliases = retiredMeleeIds.Count, retiredAliases = retiredIds.Count, generatedPresets = globals.ItemPresets.Count - originalPresets.Count,
                skippedPresetSources = variants.Except(expectedPresetRows).Select(row => Text(row, "sourceTemplateId")).Distinct().ToArray(), checks = Checks, logs = CaptureLogger.Messages
            }, Options));
        }
        finally {
            if (previousConfig == null) File.Delete(configPath); else File.WriteAllBytes(configPath, previousConfig);
            if (!hadConfigDirectory && !Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(configPath)!).Any()) Directory.Delete(Path.GetDirectoryName(configPath)!);
        }
        if (Checks.Any(check => check.Status == "FAIL")) Environment.ExitCode = 1;
    }
    private static BotType Bot(MongoId rifle, MongoId otherRifle, MongoId pistol, MongoId melee, MongoId otherMelee, bool boss, bool withRifleMods = true) {
        var inventory = Fixture<BotTypeInventory>();
        Set(inventory, "Equipment", new Dictionary<EquipmentSlots, Dictionary<MongoId, double>> {
            [EquipmentSlots.FirstPrimaryWeapon] = boss ? new() { [rifle] = 100, [ThirdPartyId] = 25 } : new() { [rifle] = 60, [otherRifle] = 40, [ThirdPartyId] = 25 },
            [EquipmentSlots.SecondPrimaryWeapon] = boss ? new() { [rifle] = 100, [ThirdPartyId] = 25 } : new() { [rifle] = 70, [otherRifle] = 30, [ThirdPartyId] = 25 },
            [EquipmentSlots.Holster] = new() { [pistol] = 100, [ThirdPartyId] = 25 },
            [EquipmentSlots.Scabbard] = !boss ? new() { [melee] = 100, [otherMelee] = 40, [ThirdPartyMeleeId] = 25 }
                : withRifleMods ? new() { [melee] = 100, [ThirdPartyMeleeId] = 25 }
                : new() { [melee] = 0, [ScytheId] = 60, [ThirdPartyMeleeId] = 25 }
        });
        var mods = new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> {
            [pistol] = new() { ["mod_fixture"] = [pistol] }, [ThirdPartyId] = new() { ["mod_fixture"] = [ThirdPartyId] }
        };
        if (withRifleMods) mods[rifle] = new() { ["mod_fixture"] = [rifle] };
        if (!boss) mods[otherRifle] = new() { ["mod_fixture"] = [otherRifle] };
        Set(inventory, "Mods", mods); var bot = Fixture<BotType>(); Set(bot, "BotInventory", inventory); return bot;
    }
    private static void Share(Dictionary<MongoId, double> pool, HashSet<MongoId> active, double expected) {
        var total = pool.Values.Where(value => value > 0).Sum(); var generated = pool.Where(pair => active.Contains(pair.Key) && pair.Value > 0).Sum(pair => pair.Value);
        Assert(total == 0 ? expected == 0 && generated == 0 : Math.Abs(generated / total - expected) < 1e-10, $"EFB share {generated / total:R}, expected {expected:R}");
    }
    private sealed record Check(string Id, string Name, string Status, string? Error);
    private sealed class NoServices : IServiceProvider { public object? GetService(Type serviceType) => null; }
}
public sealed record LogEntry(string Level, string Text);
public class CaptureLogger : DispatchProxy {
    public static List<LogEntry> Messages { get; } = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args) {
        if (method!.Name == "IsLogEnabled") return false;
        if (args?.FirstOrDefault() is string text) {
            Messages.Add(new(method.Name, text));
            if (!text.StartsWith("Reassigning root item")) Console.WriteLine(method.Name + ": " + text);
        }
        return null;
    }
}
