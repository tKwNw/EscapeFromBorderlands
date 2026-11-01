using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(GClass3826), "KeepChildInScrollViewPort")]
internal static class ExpandedStorageAutoPanPatch
{
    private static void Prefix(object scrollRect, out float? __state) =>
        __state = ExpandedStorageNavigation.BeforeAutoPan(scrollRect);

    private static void Postfix(object scrollRect, float? __state) =>
        ExpandedStorageNavigation.AfterAutoPan(scrollRect, __state);
}
