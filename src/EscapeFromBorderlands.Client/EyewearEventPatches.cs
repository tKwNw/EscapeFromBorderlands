using System.Threading.Tasks;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(GClass3515), "method_6")]
internal static class EyewearSearchPatch
{
    private static void Prefix(GClass3515 __instance, out Eyewear.State? __state)
    {
        __state = null;
        var player = BackpackSearch.Owner(__instance);
        if (!ActorCapabilities.IsHuman(player) || Eyewear.Get(player) is not { } s)
            return;
        var corpse = BackpackSearch.CorpseRoot(__instance.Item);
        if (s.Key == "EW015" && corpse is not null)
            Eyewear.SearchStarted(s, corpse);
        if (s.Key == "EW033" && corpse is null && s.Ready && __instance.IplayerSearchController_0.ContainsUnknownItems(__instance.Item))
            __state = s;
    }
    private static void Postfix(GClass3515 __instance, Eyewear.State? __state, ref Task __result)
    {
        if (__state is { } s && __result is not null)
            __result = Complete(__result, __instance, s, Eyewear.Generation);
    }
    private static async Task Complete(Task original, GClass3515 operation, Eyewear.State s, int generation)
    {
        await original;
        if (generation != Eyewear.Generation || !PlayerRefresh.CanObserve(s.Player) || Eyewear.Get(s.Player) != s || operation.Boolean_0 ||
            operation.IplayerSearchController_0.ContainsUnknownItems(operation.Item))
            return;
        Eyewear.SearchCompleted(s, operation.Item);
    }
}
