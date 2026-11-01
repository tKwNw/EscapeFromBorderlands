// Explicit UIA-shaped boundaries. The actual 2.1.6 binary is inspected and
// its transpiler composition is checked separately; these are not its code.
using EFT.InventoryLogic;
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, object> PluginInfos = []; }
}
internal static class UiaFixture
{
    internal static readonly List<Item> Grenades = [], Reload = [];
    internal static bool FailAfterAdding;
    internal static string Last = "";
    internal static int Calls;
    internal static void Collect<T>(string kind, IEnumerable<Item> source, IList<T> items, Predicate<T>? predicate) where T : Item
    {
        Last = kind; Calls++;
        foreach (var item in source.OfType<T>()) if (predicate is null || predicate(item)) items.Add(item);
        if (FailAfterAdding) throw new InvalidOperationException("simulated optional collector failure");
    }
}
namespace UseItemsAnywhere.Patches
{
    internal static class InventorySlotQueries
    {
        internal static void CollectGrenades<T>(InventoryController controller, IList<T> items, Predicate<T>? predicate) where T : Item => UiaFixture.Collect("grenades", UiaFixture.Grenades, items, predicate);
        internal static void CollectReloadItems<T>(InventoryController controller, IList<T> items, Predicate<T>? predicate) where T : Item => UiaFixture.Collect("reload", UiaFixture.Reload, items, predicate);
        internal static void CollectNativeItems<T>(InventoryController controller, IList<T> items, Predicate<T>? predicate) where T : Item => UiaFixture.Collect("bot", controller.Reachable, items, predicate);
        // Same name, wrong list contract: reflection must select the IList API.
        internal static void CollectGrenades<T>(InventoryController controller, List<T> items, Predicate<T>? predicate) where T : Item => throw new Exception("wrong overload");
    }
}
namespace UseItemsAnywhere.QuickUseWheel
{
    internal static class QuickUseWheelInventory
    {
        private static bool HasResource(Item item) => item is not null;
    }
}
internal static class UnrelatedInventoryQueries
{
    internal static void CollectGrenades<T>(InventoryController controller, IList<T> items, Predicate<T>? predicate) where T : Item { }
}
