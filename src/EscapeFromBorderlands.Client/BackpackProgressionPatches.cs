using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;
[HarmonyPatch(typeof(AbstractSkillClass), nameof(AbstractSkillClass.OnTrigger))]
internal static class BackpackMovementExperiencePatch
{
    private static void Prefix(AbstractSkillClass __instance, SkillManager.SkillActionClass skillAction, ref float val)
    {
        var p = Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => ReferenceEquals(p.Skills.Strength, __instance) || ReferenceEquals(p.Skills.Endurance, __instance));
        if (p is not null && BackpackRuntime.Is(p, "BP08-1") && ReferenceEquals(skillAction, p.Skills.MovementAction) && p.InputDirection.sqrMagnitude > .0001f && val > 0f) val *= 1.75f;
    }
}
[HarmonyPatch(typeof(BreachDoorStateClass), nameof(BreachDoorStateClass.ExecuteDoorInteraction))]
internal static class BackpackBreachRewardPatch
{
    private static void Prefix(WorldInteractiveObject interactive, out bool __state) => __state = (interactive as Door)?.IsBroken == true;
    private static void Postfix(WorldInteractiveObject interactive, InteractionResult interactionResult, Player user, bool __state)
    {
        if (__state || interactionResult.InteractionType != EInteractionType.Breach || interactive is not Door { IsBroken: true }) return;
        var s = BackpackRuntime.Get(user); if (s?.Key == "BP35-3") s.Record.Arm("meleeReach", 20f);
    }
}
[HarmonyPatch]
internal static class BackpackSearchCompletionPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(PlayerSearchControllerClass).BaseType, "method_2");
    private static void Postfix(object __instance, SearchableItemItemClass item)
    { var p = BackpackRuntime.Actor(Native.Get(__instance, "PlayerInventoryController_0")); if (p is not null) BackpackRuntime.Searched(p, item); }
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "method_9")]
internal static class BackpackGrenadeDistancePatch
{
    private static void Prefix(Player.BaseGrenadeHandsController __instance, ref float lowHighThrow)
    {
        if (RarityResolver.Resolve(__instance.Item) is { } roll && roll.Definition.GrenadeChargesPerRaid > 0)
            lowHighThrow *= (float)Math.Sqrt(roll.GrenadeThrowDistanceMultiplier);
        if (ChestRigCombat.GrenadeBonus(__instance,"TR07-2")) lowHighThrow *= (float)Math.Sqrt(1.35f);
        var p = BackpackRuntime.Actor(__instance);
        if (p != null && BackpackRuntime.Records.TryGetValue(p.ProfileId, out var r) && r.Active("throwDistance")) lowHighThrow *= (float)Math.Sqrt(1.5f);
    }
}
