using System;
using EFT.Quests;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class QuestHitCopyPolicy
{
    private static bool? Compatible;
    internal static void Reset() { Compatible = null; LegendaryQuestItems.ResetHitIdentities(); }
    // Update runs after plugin initialization and again after each raid reset.
    // Keep CanShare's lazy fallback for hits arriving before that update.
    internal static void Prepare()
    {
        if (Compatible.HasValue) return;
        LegendaryQuestItems.Prepare();
        _ = CanShare;
    }
    internal static bool CanShare => Compatible ??= Inspect();
    private static bool Inspect()
    {
        try
        {
            foreach (var method in Harmony.GetAllPatchedMethods())
            {
                var type = method.DeclaringType;
                while (type?.DeclaringType is { } parent) type = parent;
                if (type != typeof(GClass4042) && type != typeof(ConditionProgressChecker) && type != typeof(GClass4015)) continue;
                if (Harmony.GetPatchInfo(method) is not { } patches) continue;
                foreach (var owner in patches.Owners) if (owner != Plugin.PluginGuid) return false;
            }
            return true;
        }
        catch { return false; }
    }
}
