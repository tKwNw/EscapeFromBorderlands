using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Optional integration. Never ship or modify Use Items Anywhere assemblies.
// Its collectors implement user-configured grenade/reload slots; native EFT
// GetReachableItemsOfTypeNonAlloc does not preserve those rules.
internal static class UseItemsAnywhereCompatibility
{
    private const string PluginId = "com.cj.useFromAnywhere";
    private const string QueriesType = "UseItemsAnywhere.Patches.InventorySlotQueries";
    internal static bool Installed => Chainloader.PluginInfos is not null && Chainloader.PluginInfos.Keys.Any(id =>
        string.Equals(id, PluginId, StringComparison.OrdinalIgnoreCase));

    internal static bool IsCollector(MethodInfo method) =>
        method.DeclaringType?.FullName == QueriesType &&
        (method.Name == "CollectGrenades" || method.Name == "CollectReloadItems" || method.Name == "CollectNativeItems") &&
        CollectorSignature(method);

    private static bool CollectorSignature(MethodInfo method)
    {
        if (!method.IsStatic || !method.IsGenericMethod || method.GetGenericArguments().Length != 1 || method.ReturnType != typeof(void)) return false;
        var item = method.GetGenericArguments()[0];
        return method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] {
            typeof(InventoryController), typeof(IList<>).MakeGenericType(item), typeof(Predicate<>).MakeGenericType(item) });
    }

    private static MethodInfo? _grenades;
    private static MethodInfo? _reloadMagazines;
    private static MethodInfo? _botMagazines;
    private static bool _loggedFailure;

    private static MethodInfo? Resolve<T>(string name) where T : Item
    {
        var type = AccessTools.TypeByName(QueriesType);
        var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == name && m.IsGenericMethodDefinition && CollectorSignature(m));
        return method?.MakeGenericMethod(typeof(T));
    }

    private static bool Invoke<T>(InventoryController inventory, IList<T> items, Predicate<T>? predicate,
        ref MethodInfo? collector, string name) where T : Item
    {
        if (!Installed) return false;
        try { collector ??= Resolve<T>(name); }
        catch (Exception e)
        {
            if (!_loggedFailure)
            {
                Plugin.LogSource.LogError($"Use Items Anywhere query signature {name} changed: {e}");
                _loggedFailure = true;
            }
            return false;
        }
        if (collector is null)
        {
            if (!_loggedFailure)
            {
                Plugin.LogSource.LogWarning("Use Items Anywhere is installed but its inventory collector was not found. Using vanilla grenade/reload slots.");
                _loggedFailure = true;
            }
            return false;
        }
        try
        {
            // A changed/throwing optional collector must not leave half of its
            // results behind before the native fallback appends its own list.
            var collected = new List<T>(items);
            collector.Invoke(null, new object?[] { inventory, collected, predicate });
            items.Clear();
            foreach (var item in collected) items.Add(item);
            return true;
        }
        catch (Exception e)
        {
            if (!_loggedFailure)
            {
                Plugin.LogSource.LogError($"Use Items Anywhere inventory query {name} failed, falling back to native lookup: {e}");
                _loggedFailure = true;
            }
            return false;
        }
    }

    internal static void CollectGrenades(InventoryController inventory, IList<ThrowWeap> items, Predicate<ThrowWeap>? predicate)
    {
        if (!Invoke(inventory, items, predicate, ref _grenades, "CollectGrenades"))
            inventory.GetReachableItemsOfTypeNonAlloc(items, predicate);
    }

    internal static void CollectMagazines(InventoryController inventory, IList<Magazine> items,
        Predicate<Magazine>? predicate, bool bot)
    {
        if (bot)
        {
            if (Invoke(inventory, items, predicate, ref _botMagazines, "CollectNativeItems")) return;
        }
        else if (Invoke(inventory, items, predicate, ref _reloadMagazines, "CollectReloadItems")) return;
        inventory.GetReachableItemsOfTypeNonAlloc(items, predicate);
    }
}

// Use Items Anywhere's resource predicate considers ordinary grenades usable.
// EFB keeps the original item on zero charges, so make the wheel's existing
// resource test charge-aware for EFB instances only. This also covers its
// grouped-item fallback and tap-last-item logic without editing the other mod.
[HarmonyPatch]
internal static class UseItemsAnywhereGrenadeWheelPatch
{
    private static bool Prepare() => UseItemsAnywhereCompatibility.Installed &&
        TargetMethod() is MethodInfo method && method.IsStatic && method.ReturnType == typeof(bool);
    private static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory"), "HasResource", new[] { typeof(Item) });
    private static void Postfix(Item __0, ref bool __result)
    {
        if (__result && __0 is ThrowWeap && !ReusableGrenadeRuntime.Usable(__0)) __result = false;
    }
}
