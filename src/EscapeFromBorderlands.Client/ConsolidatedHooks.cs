using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Only combine equal-priority callbacks whose existing order is verified.
// Original patch classes remain the fallback for foreign/conflicting patches.
internal static class ConsolidatedHooks
{
    private static MethodInfo Method(Type type, string name) => AccessTools.Method(type, name);
    private static MethodInfo Post(Type type) => Method(type, "Postfix");

    internal static void Install(Harmony harmony)
    {
        var count = 0;
        bool Merge(Type target, string getter, string combined, params Type[] originals) =>
            Replace(harmony, AccessTools.PropertyGetter(target, getter), Array.Empty<MethodInfo>(),
                originals.Select(Post).ToArray(), Array.Empty<MethodInfo>(), null,
                Method(typeof(ConsolidatedHooks), combined), null);
        if (Merge(typeof(Deferred<float>), "Value", nameof(CachedWeight), typeof(BackpackCachedValuePatch), typeof(BackpackWeightValuePatch)))
            count++;
        if (Merge(typeof(Weapon), "RecoilDelta", nameof(Recoil), typeof(ChestRigRecoilPatch), typeof(EarpieceRecoilPatch), typeof(WeaponRecoilDeltaPatch)))
            count++;
        if (Merge(typeof(Weapon), "ErgonomicsDelta", nameof(Ergonomics), typeof(BackpackErgonomicsPatch), typeof(ChestRigErgonomicsPatch), typeof(WeaponErgonomicsDeltaPatch)))
            count++;
        if (Merge(typeof(Weapon), "CenterOfImpactDelta", nameof(Accuracy), typeof(ChestRigAccuracyPatch), typeof(EarpieceAccuracyPatch), typeof(WeaponAccuracyDeltaPatch)))
            count++;
        if (Merge(typeof(Weapon), "VelocityDelta", nameof(Velocity), typeof(ChestRigVelocityPatch), typeof(EarpieceVelocityPatch), typeof(WeaponVelocityDeltaPatch)))
            count++;
        if (Merge(typeof(Weapon), "FireRate", nameof(FireRate), typeof(ChestRigFireRatePatch), typeof(EarpieceFireRatePatch), typeof(WeaponFireRatePatch)))
            count++;
        if (Merge(typeof(Weapon), "SingleFireRate", nameof(SingleFireRate), typeof(ChestRigFireRatePatch), typeof(EarpieceFireRatePatch), typeof(WeaponSingleFireRatePatch)))
            count++;
        if (Replace(harmony, AccessTools.Method(typeof(Stamina), "Process"),
            new[] { Method(typeof(BackpackPaidSprintPatch), "Prefix"), Method(typeof(LegendaryMeleeSprintCostPatch), "Prefix") },
            new[] { Post(typeof(BackpackPaidSprintPatch)) }, new[] { Method(typeof(LegendaryMeleeSprintCostPatch), "Finalizer") },
            Method(typeof(ConsolidatedHooks), nameof(SprintBegin)), Method(typeof(ConsolidatedHooks), nameof(SprintEnd)),
            Method(typeof(ConsolidatedHooks), nameof(SprintFinally))))
            count++;
        Plugin.LogSource?.LogInfo($"EFB consolidated {count}/8 compatible callback groups; other groups retain their original hooks.");
        HumanStaminaRoute.Install(harmony);
        HumanCachedValueRoute.Install(harmony);
    }

    private static bool Matches(MethodBase target, Patch[] patches, MethodInfo[] expected)
    {
        if (expected.Length == 0)
            return true;
        // Other EFB priorities (e.g. grenade recoil Priority.Last) remain in place.
        var normal = patches.Where(p => p.priority == Priority.Normal).ToArray();
        return normal.Length == expected.Length && PatchProcessor.GetSortedPatchMethods(target, normal).SequenceEqual(expected) &&
            normal.All(p => p.before.Length == 0 && p.after.Length == 0);
    }

    private static bool Replace(Harmony harmony, MethodBase? target, MethodInfo[] prefixes, MethodInfo[] postfixes,
        MethodInfo[] finalizers, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? finalizer)
    {
        if (target is null)
            return false;
        var info = Harmony.GetPatchInfo(target);
        if (info is null || info.Owners.Any(owner => owner != Plugin.PluginGuid) ||
            !Matches(target, info.Prefixes.ToArray(), prefixes) || !Matches(target, info.Postfixes.ToArray(), postfixes) ||
            !Matches(target, info.Finalizers.ToArray(), finalizers))
            return false;
        var removed = prefixes.Concat(postfixes).Concat(finalizers).ToArray();
        HarmonyMethod? Wrap(MethodInfo? method) => method is null ? null : new HarmonyMethod(method) { priority = Priority.Normal };
        try
        {
            // Startup only, before a GameWorld exists. Never swap hooks mid-raid.
            foreach (var method in removed)
                harmony.Unpatch(target, method);
            harmony.Patch(target, prefix: Wrap(prefix), postfix: Wrap(postfix), finalizer: Wrap(finalizer));
            return true;
        }
        catch (Exception error)
        {
            foreach (var method in new[] { prefix, postfix, finalizer })
                if (method is not null)
                    harmony.Unpatch(target, method);
            foreach (var method in prefixes)
                harmony.Patch(target, prefix: Wrap(method));
            foreach (var method in postfixes)
                harmony.Patch(target, postfix: Wrap(method));
            foreach (var method in finalizers)
                harmony.Patch(target, finalizer: Wrap(method));
            Plugin.LogSource?.LogWarning($"EFB retained original callbacks for {target.Name}: {error.Message}");
            return false;
        }
    }

    private static void CachedWeight(Deferred<float> __instance, ref float __result)
    {
        BackpackCachedValuePatch.Apply(__instance, ref __result);
        BackpackWeightValuePatch.Apply(__instance, ref __result);
    }
    private static void Recoil(Weapon __instance, ref float __result)
    {
        using var read = new WeaponStatReadBatch.Join(__instance);
        ChestRigRecoilPatch.Apply(__instance, ref __result);
        EarpieceRecoilPatch.Apply(__instance, ref __result);
        WeaponRecoilDeltaPatch.Apply(__instance, ref __result);
    }
    private static void Ergonomics(Weapon __instance, ref float __result)
    {
        using var read = new WeaponStatReadBatch.Join(__instance);
        BackpackErgonomicsPatch.Apply(__instance, ref __result);
        ChestRigErgonomicsPatch.Apply(__instance, ref __result);
        WeaponErgonomicsDeltaPatch.Apply(__instance, ref __result);
    }
    private static void Accuracy(Weapon __instance, ref float __result)
    {
        using var read = new WeaponStatReadBatch.Join(__instance);
        ChestRigAccuracyPatch.Apply(__instance, ref __result);
        EarpieceAccuracyPatch.Apply(__instance, ref __result);
        WeaponAccuracyDeltaPatch.Apply(__instance, ref __result);
    }
    private static void Velocity(Weapon __instance, ref float __result)
    {
        using var read = new WeaponStatReadBatch.Join(__instance);
        ChestRigVelocityPatch.Apply(__instance, ref __result);
        EarpieceVelocityPatch.Apply(__instance, ref __result);
        WeaponVelocityDeltaPatch.Apply(__instance, ref __result);
    }
    private static void FireRate(Weapon __instance, ref int __result)
    {
        using var read = new WeaponStatReadBatch.Join(__instance);
        ChestRigFireRatePatch.Apply(__instance, ref __result);
        EarpieceFireRatePatch.Apply(__instance, ref __result);
        WeaponFireRatePatch.Apply(__instance, ref __result);
    }
    private static void SingleFireRate(Weapon __instance, ref int __result)
    {
        using var read = new WeaponStatReadBatch.Join(__instance);
        ChestRigFireRatePatch.Apply(__instance, ref __result);
        EarpieceFireRatePatch.Apply(__instance, ref __result);
        WeaponSingleFireRatePatch.Apply(__instance, ref __result);
    }

    internal struct SprintState
    {
        internal BackpackPaidStaminaSnapshot Paid;
        internal LegendaryMeleeSprintCostPatch.Snapshot? Melee;
    }
    private static void SprintBegin(Stamina __instance, out SprintState __state)
    {
        __state = default;
        __state.Paid = new BackpackPaidStaminaSnapshot(__instance);
        LegendaryMeleeSprintCostPatch.Begin(__instance, out __state.Melee);
    }
    private static void SprintEnd(Stamina __instance, float dt, SprintState __state) => BackpackPaidSprintPatch.Observe(__instance, dt, __state.Paid);
    private static Exception? SprintFinally(SprintState __state, Exception? __exception) => LegendaryMeleeSprintCostPatch.End(__state.Melee, __exception);
}
