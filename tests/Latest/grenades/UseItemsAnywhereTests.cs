using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using HarmonyLib;
using UnityEngine;

internal static class UseItemsAnywhereTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    internal static void Run(Action<string, Action> run, Action<bool, string> check)
    {
        var definition = LegendaryCatalog.Load().Variants.Single(v => v.RuntimeKey == "GR26");
        ThrowWeap Grenade(string id) => new() { Id = id, StringTemplateId = definition.TemplateId };
        void Reset(bool installed = false)
        {
            Chainloader.PluginInfos.Clear();
            if (installed) Chainloader.PluginInfos["COM.CJ.USEFROMANYWHERE"] = new();
            AccessTools.HideOptionalTypes = false;
            UiaFixture.Grenades.Clear(); UiaFixture.Reload.Clear(); UiaFixture.Calls = 0; UiaFixture.Last = ""; UiaFixture.FailAfterAdding = false;
            Plugin.LogSource.Messages.Clear();
            foreach (var name in new[] { "_grenades", "_reloadMagazines", "_botMagazines" }) typeof(UseItemsAnywhereCompatibility).GetField(name, All)!.SetValue(null, null);
            typeof(UseItemsAnywhereCompatibility).GetField("_loggedFailure", All)!.SetValue(null, false);
            ReusableGrenadeRuntime.Reset(); Time.time = 100;
        }
        void Test(string name, Action action) => run("UIA: " + name, () => { Reset(); try { action(); } finally { Reset(); } });
        Player Scene()
        {
            var player = new Player(); Singleton<GameWorld>.Instance = new() { MainPlayer = player }; return player;
        }
        void Spend(ThrowWeap item)
        {
            for (var i = 0; i < definition.GrenadeChargesPerRaid; i++)
            {
                var token = Guid.NewGuid().ToString("N");
                check(ReusableGrenadeRuntime.Charges.Reserve(item.Id, definition.GrenadeChargesPerRaid, token, LegendaryGrenadeSettings.Interval(definition)), "reserve own charge");
                ReusableGrenadeRuntime.Charges.Finish(item.Id, token, true);
            }
        }
        bool Wheel(Item? item, bool native = true)
        {
            object?[] args = [item, native]; typeof(UseItemsAnywhereGrenadeWheelPatch).GetMethod("Postfix", All)!.Invoke(null, args); return (bool)args[1]!;
        }
        Item Bag(string key, params Item[] children)
        {
            var variant = LegendaryCatalog.Load().Variants.First(v => v.BackpackEffect?.Key == key);
            var bag = new Item { StringTemplateId = variant.TemplateId }; bag.Children.AddRange(children); return bag;
        }
        Test("standalone grenade lookup uses native inventory and predicate", () => {
            var p = Scene(); var a = Grenade("native"); var hidden = Grenade("unexamined"); hidden.Examined = false; p.InventoryController.Reachable.AddRange([a, hidden]);
            var result = new List<ThrowWeap>(); BackpackInventoryAccess.Grenades(p.InventoryController, result, i => i.Examined);
            check(result.SequenceEqual([a]) && p.InventoryController.NativeQueries == 1 && UiaFixture.Calls == 0, "native behavior preserved");
        });
        Test("standalone player magazine lookup retains native slots", () => {
            var p = Scene(); var a = new Magazine(); p.InventoryController.Reachable.Add(a); var result = new List<Magazine>();
            BackpackInventoryAccess.Magazines(p.InventoryController, result, null); check(result.SequenceEqual([a]) && p.InventoryController.NativeQueries == 1, "native player magazines");
        });
        Test("standalone bot magazine lookup retains native slots", () => {
            var p = Scene(); var a = new Magazine(); p.InventoryController.Reachable.Add(a); var result = new List<Magazine>();
            BackpackInventoryAccess.BotMagazines(p.InventoryController, result, null); check(result.SequenceEqual([a]) && p.InventoryController.NativeQueries == 1, "native bot magazines");
        });
        Test("an available UIA type without the installed plugin is ignored", () => {
            var p = Scene(); UiaFixture.Grenades.Add(Grenade("not-enabled")); var result = new List<ThrowWeap>();
            UseItemsAnywhereCompatibility.CollectGrenades(p.InventoryController, result, null); check(result.Count == 0 && UiaFixture.Calls == 0, "disabled plugin not invoked");
        });
        Test("installed grenade collector honors extra-slot candidates and predicate", () => {
            Reset(true); var p = Scene(); var a = Grenade("armband"); var b = Grenade("belt-unexamined"); b.Examined = false; UiaFixture.Grenades.AddRange([a, b]);
            var result = new List<ThrowWeap>(); BackpackInventoryAccess.Grenades(p.InventoryController, result, g => g.Examined);
            check(result.SequenceEqual([a]) && p.InventoryController.NativeQueries == 0 && UiaFixture.Last == "grenades", "configured candidates are retained");
        });
        Test("player reload delegates to UIA reload slots", () => {
            Reset(true); var p = Scene(); var a = new Magazine(); UiaFixture.Reload.Add(a); var result = new List<Magazine>();
            BackpackInventoryAccess.Magazines(p.InventoryController, result, null); check(result.SequenceEqual([a]) && UiaFixture.Last == "reload" && p.InventoryController.NativeQueries == 0, "player collector");
        });
        Test("bot reload never borrows player-configured reload slots", () => {
            Reset(true); var p = Scene(); var a = new Magazine(); p.InventoryController.Reachable.Add(a); UiaFixture.Reload.Add(new Magazine()); var result = new List<Magazine>();
            BackpackInventoryAccess.BotMagazines(p.InventoryController, result, null); check(result.SequenceEqual([a]) && UiaFixture.Last == "bot", "bot collector");
        });
        Test("missing optional collector falls back without losing native grenades", () => {
            Reset(true); AccessTools.HideOptionalTypes = true; var p = Scene(); var a = Grenade("native"); p.InventoryController.Reachable.Add(a); var result = new List<ThrowWeap>();
            UseItemsAnywhereCompatibility.CollectGrenades(p.InventoryController, result, null); check(result.SequenceEqual([a]) && Plugin.LogSource.Messages.Count == 1, "missing collector fallback");
        });
        Test("throwing collector cannot leak a partial result into native fallback", () => {
            Reset(true); var p = Scene(); var native = Grenade("native"); var leaked = Grenade("partial"); var seed = Grenade("seed"); p.InventoryController.Reachable.Add(native); UiaFixture.Grenades.Add(leaked); UiaFixture.FailAfterAdding = true;
            var result = new List<ThrowWeap> { seed }; UseItemsAnywhereCompatibility.CollectGrenades(p.InventoryController, result, null);
            check(result.SequenceEqual([seed, native]) && p.InventoryController.NativeQueries == 1, "fallback is atomic relative to destination");
        });
        Test("repeated optional failures log once and keep using native lookup", () => {
            Reset(true); var p = Scene(); UiaFixture.FailAfterAdding = true;
            for (var i = 0; i < 4; i++) UseItemsAnywhereCompatibility.CollectGrenades(p.InventoryController, new List<ThrowWeap>(), null);
            check(Plugin.LogSource.Messages.Count == 1 && p.InventoryController.NativeQueries == 4, "rate-limited failure logs");
        });
        Test("successful collector preserves preexisting destination semantics", () => {
            Reset(true); var p = Scene(); var seed = Grenade("seed"); var a = Grenade("new"); UiaFixture.Grenades.Add(a); var result = new List<ThrowWeap> { seed };
            UseItemsAnywhereCompatibility.CollectGrenades(p.InventoryController, result, null); check(result.SequenceEqual([seed, a]), "existing destination is passed through");
        });
        Test("BP35 extra throwables compose with UIA and avoid duplicates", () => {
            Reset(true); var p = Scene(); var a = Grenade("uia"); var b = Grenade("backpack"); UiaFixture.Grenades.Add(a); p.InventoryController.Inventory.Equipment.Bag.ContainedItem = Bag("BP35-1", a, b);
            var result = new List<ThrowWeap>(); BackpackInventoryAccess.Grenades(p.InventoryController, result, null); check(result.SequenceEqual([a, b]), "UIA plus backpack once per instance");
        });
        Test("BP02 player magazine access composes with UIA", () => {
            Reset(true); var p = Scene(); var a = new Magazine(); var b = new Magazine(); UiaFixture.Reload.Add(a); p.InventoryController.Inventory.Equipment.Bag.ContainedItem = Bag("BP02-2", a, b);
            var result = new List<Magazine>(); BackpackInventoryAccess.Magazines(p.InventoryController, result, null); check(result.SequenceEqual([a, b]), "player backpack extension");
        });
        Test("BP02 bot magazine access preserves the bot collector", () => {
            Reset(true); var p = Scene(); var a = new Magazine(); p.InventoryController.Inventory.Equipment.Bag.ContainedItem = Bag("BP02-2", a); var result = new List<Magazine>();
            BackpackInventoryAccess.BotMagazines(p.InventoryController, result, null); check(result.SequenceEqual([a]) && UiaFixture.Last == "bot", "bot backpack extension");
        });
        Test("ordinary bags do not unlock extra throwable access", () => {
            Reset(true); var p = Scene(); var a = Grenade("bag"); p.InventoryController.Inventory.Equipment.Bag.ContainedItem = new Item { Children = [a] }; var result = new List<ThrowWeap>();
            BackpackInventoryAccess.Grenades(p.InventoryController, result, null); check(result.Count == 0, "ordinary bag excluded");
        });
        Test("same-template copies keep charges independent during key selection", () => {
            Reset(true); var p = Scene(); var spent = Grenade("spent"); var usable = Grenade("usable"); Spend(spent); UiaFixture.Grenades.AddRange([spent, usable]); p.InventoryController.Inventory.Equipment.TopPriorityGrenade = spent;
            var result = new List<ThrowWeap>(); BackpackInventoryAccess.Grenades(p.InventoryController, result, null);
            check(result.SequenceEqual([usable]) && p.InventoryController.Inventory.Equipment.TopPriorityGrenade == usable && ReusableGrenadeRuntime.Remaining(spent) == 0, "switch copies without merging");
        });
        Test("regeneration restores exhausted UIA grenade candidates", () => {
            Reset(true); var p = Scene(); var a = Grenade("recharge"); Spend(a); UiaFixture.Grenades.Add(a); var result = new List<ThrowWeap>(); BackpackInventoryAccess.Grenades(p.InventoryController, result, null); check(result.Count == 0, "depleted hidden");
            Time.time += LegendaryGrenadeSettings.Interval(definition); BackpackInventoryAccess.Grenades(p.InventoryController, result, null); check(result.SequenceEqual([a]) && ReusableGrenadeRuntime.Remaining(a) == 1, "regenerated copy selectable");
        });
        Test("wheel rejects spent copies and accepts charged copies", () => {
            var spent = Grenade("spent"); var charged = Grenade("charged"); Spend(spent); check(!Wheel(spent) && Wheel(charged), "wheel reads per-instance ledger");
        });
        Test("wheel never revives a native resource rejection", () => { check(!Wheel(Grenade("charged"), false), "existing rejection retained"); });
        Test("wheel preserves ordinary grenades and non-grenade resource results", () => {
            check(Wheel(new ThrowWeap { StringTemplateId = definition.SourceTemplateId }) && Wheel(new Item()) && !Wheel(new Item(), false) && !Wheel(null, false), "only reusable charge availability changes");
        });
        Test("grouped same-template fallback chooses the charged copy", () => {
            var a = Grenade("spent"); var b = Grenade("charged"); Spend(a); check(new[] { a, b }.FirstOrDefault(i => Wheel(i)) == b, "group predicate skips depleted instance");
        });
        Test("tap-last predicate falls through and re-enables after recharge", () => {
            var a = Grenade("last"); Spend(a); check(!Wheel(a), "spent last item rejected"); Time.time += LegendaryGrenadeSettings.Interval(definition); check(Wheel(a), "same copy usable again");
        });
        Test("collector recognition checks owner and full generic signature", () => {
            var methods = typeof(UseItemsAnywhere.Patches.InventorySlotQueries).GetMethods(All).Where(m => m.Name == "CollectGrenades").ToArray();
            check(methods.Count(UseItemsAnywhereCompatibility.IsCollector) == 1, "wrong List overload ignored");
            check(!UseItemsAnywhereCompatibility.IsCollector(typeof(UnrelatedInventoryQueries).GetMethod("CollectGrenades", All)!), "same name on unrelated owner ignored");
        });
        Test("native and UIA calls map to correct player and bot adapters", () => {
            var transpiler = typeof(BackpackConcreteInventorySearchPatch).GetMethod("Transpiler", All)!;
            var targets = new[] { (typeof(EFT.PlayerInputTranslator), "vmethod_1", typeof(ThrowWeap), "CollectGrenades", "Grenades"), (typeof(EFT.FirearmHandsInputTranslator), "ReloadExternalMagazine", typeof(Magazine), "CollectReloadItems", "Magazines"), (typeof(BotReload), "GetMagazineForReload", typeof(Magazine), "CollectNativeItems", "BotMagazines"), (typeof(BotReload), "AddAmmoToMagazines", typeof(Magazine), "CollectNativeItems", "BotMagazines") };
            foreach (var (owner, name, item, collector, adapter) in targets)
            foreach (var optional in new[] { false, true })
            {
                var original = owner.GetMethod(name)!;
                var method = optional ? typeof(UseItemsAnywhere.Patches.InventorySlotQueries).GetMethods(All).Single(m => m.Name == collector && UseItemsAnywhereCompatibility.IsCollector(m)) : typeof(InventoryController).GetMethod("GetReachableItemsOfTypeNonAlloc")!;
                var input = new[] { new CodeInstruction(OpCodes.Call, method.MakeGenericMethod(item)) };
                var result = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, [input, original])!).Single();
                check(result.opcode == OpCodes.Call && result.operand is MethodInfo m && m.Name == adapter && m.GetParameters().Length == 3, owner.Name + "." + name + " " + optional);
            }
        });
        Test("transpiler leaves delegate references untouched", () => {
            var method = typeof(InventoryController).GetMethod("GetReachableItemsOfTypeNonAlloc")!.MakeGenericMethod(typeof(ThrowWeap));
            var input = new[] { new CodeInstruction(OpCodes.Ldftn, method), new CodeInstruction(OpCodes.Callvirt, method) };
            var result = ((IEnumerable<CodeInstruction>)typeof(BackpackConcreteInventorySearchPatch).GetMethod("Transpiler", All)!.Invoke(null, [input, typeof(EFT.PlayerInputTranslator).GetMethod("vmethod_1")!])!).ToArray();
            check(result[0].opcode == OpCodes.Ldftn && Equals(result[0].operand, method), "only executable calls rewritten");
        });
        Test("patch ordering follows UIA inventory queries explicitly", () => {
            var patch = typeof(BackpackConcreteInventorySearchPatch); check(patch.GetCustomAttribute<HarmonyPriority>()!.Value == Priority.Last && patch.GetCustomAttribute<HarmonyAfter>()!.Owner == "com.cj.useFromAnywhere.inventory-queries", "priority and owner constraints");
        });
        Test("wheel patch prepares only when UIA is installed", () => {
            var prepare = typeof(UseItemsAnywhereGrenadeWheelPatch).GetMethod("Prepare", All)!; check(!(bool)prepare.Invoke(null, null)!, "absent plugin skipped"); Reset(true); check((bool)prepare.Invoke(null, null)!, "installed supported plugin enabled");
        });
    }
}
