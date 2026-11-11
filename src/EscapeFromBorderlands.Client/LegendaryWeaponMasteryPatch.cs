using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch]
internal static class LegendaryWeaponMasteryPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        // The group lookup handles drawing and shot credit. The second lookup reads the earned level.
        yield return typeof(SkillManager).GetMethod(nameof(SkillManager.MasteringGroup), new[] { typeof(string) })!;
        yield return typeof(SkillManager).GetMethod(nameof(SkillManager.GetMastering), new[] { typeof(string) })!;
    }

    private static void Prefix(ref string __0)
    {
        if (__0 is not null)
            __0 = RarityResolver.SourceTemplateForMastery(__0);
    }
}
