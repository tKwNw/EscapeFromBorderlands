using EFT.Ballistics;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(EFT.InventoryLogic.Operations.ActiveSearchContentOperation), "SearchContent")]
internal static class EyewearSearchPatch
{
    private static void Prefix(EFT.InventoryLogic.Operations.ActiveSearchContentOperation __instance, out Eyewear.State? __state)
    {
        __state = null;
        var player = BackpackSearch.Owner(__instance);
        if (!ActorCapabilities.IsHuman(player) || Eyewear.Get(player) is not { } s) return;
        var corpse = BackpackSearch.CorpseRoot(__instance.Item);
        if (s.Key == "EW015" && corpse is not null) Eyewear.SearchStarted(s, corpse);
        if (s.Key == "EW033" && corpse is null && s.Ready && __instance._searchController.ContainsUnknownItems(__instance.Item)) __state = s;
    }
    private static void Postfix(EFT.InventoryLogic.Operations.ActiveSearchContentOperation __instance, Eyewear.State? __state, ref Task __result)
    { if (__state is { } s && __result is not null) __result = Complete(__result, __instance, s, Eyewear.Generation); }
    private static async Task Complete(Task original, EFT.InventoryLogic.Operations.ActiveSearchContentOperation operation, Eyewear.State s, int generation)
    {
        await original;
        if (generation != Eyewear.Generation || !PlayerRefresh.CanObserve(s.Player) || Eyewear.Get(s.Player) != s || operation.Terminated ||
            operation._searchController.ContainsUnknownItems(operation.Item)) return;
        Eyewear.SearchCompleted(s, operation.Item);
    }
}
