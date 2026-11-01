using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Interactive;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;
[HarmonyPatch(typeof(TransferItemRequirement), nameof(TransferItemRequirement.Met))]
internal static class BackpackFreePaidExtractPatch
{
    private static bool Prefix(TransferItemRequirement __instance, Player player, ExfiltrationPoint point, ref bool __result)
    {
        var id = __instance.Id.ToString();
        if (!BackpackRuntime.Is(player, "BP25-2") || id is not ("5449016a4bdc2d6f028b456f" or "5696686a4bdc2da3298b456a" or "569668774bdc2da2298b4568")) return true;
        if (!point.QueuedPlayers.Contains(player.ProfileId)) Native.Call(point, "OnItemTransferred", player);
        __result = point.QueuedPlayers.Contains(player.ProfileId); return false;
    }
}
[HarmonyPatch(typeof(PlayerPhysicalClass), nameof(PlayerPhysicalClass.GetStaminaRestorationFunc))]
internal static class BackpackDryMarchPatch
{
    private static void Postfix(PlayerPhysicalClass __instance, ref float __result)
    {
        if (!BackpackRuntime.Is(BackpackRuntime.Actor(__instance), "BP05-2")) return;
        var normal = Comfort.Common.Singleton<BackendConfigSettingsClass>.Instance.StaminaDrain.GetAt(1f);
        if (normal > 0f) __result *= __instance.Single_0 / normal;
    }
}
