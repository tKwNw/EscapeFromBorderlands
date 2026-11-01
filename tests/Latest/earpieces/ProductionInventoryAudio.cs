using System;using System.Linq;using System.Reflection;using System.Collections.Generic;using Comfort.Common;using EFT;using EFT.UI;using HarmonyLib;
namespace EscapeFromBorderlands.Client;
[HarmonyPatch]
internal static class BackpackInventorySoundScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(GUISounds).GetMethods().Where(m => m.Name == "PlayItemSound");
    private static void Prefix(out float __state) { __state = BackpackAudio.InventoryFactor; var p = Singleton<GameWorld>.Instance?.MainPlayer; BackpackAudio.InventoryFactor = p is null ? 1f : BackpackRuntime.Noise(p, "inventory") * EarpieceAudio.Volume(p, "item"); }
    private static Exception? Finalizer(float __state, Exception? __exception) { BackpackAudio.InventoryFactor = __state; return __exception; }
}
[HarmonyPatch(typeof(GUISounds), "GetCommonSoundsVolume")]
internal static class BackpackInventoryVolumePatch
{
    private static void Postfix(ref float __result) { if (BackpackAudio.InventoryFactor > 0f) __result *= BackpackAudio.InventoryFactor; }
}