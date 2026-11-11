using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(BreachDoorStateClass),nameof(BreachDoorStateClass.ExecuteDoorInteraction))]
internal static class ChestRigBreachPatch
{
    private static void Prefix(WorldInteractiveObject interactive,out bool __state)=>__state=(interactive as Door)?.IsBroken==true;
    private static void Postfix(BreachDoorStateClass __instance,WorldInteractiveObject interactive,InteractionResult interactionResult,Player user,bool __state)
    {
        if(__state||interactionResult.InteractionType!=EInteractionType.Breach||interactive is not Door{IsBroken:true})return;
        ChestRigCombat.QueueDoor(__instance,user,(Door)interactive,((Door)interactive).DoorState,true);
    }
}
[HarmonyPatch]
internal static class ChestRigDoorPatch
{
    private static IEnumerable<MethodBase> TargetMethods()=>new[]{AccessTools.Method(typeof(BreachDoorStateClass),nameof(BreachDoorStateClass.ExecuteDoorInteraction)),AccessTools.Method(typeof(DoorInteractionStateClass),nameof(DoorInteractionStateClass.ExecuteDoorInteraction))};
    private static void Prefix(WorldInteractiveObject __0,out EDoorState? __state)=>__state=(__0 as Door)?.DoorState;
    private static void Postfix(object __instance,WorldInteractiveObject __0,InteractionResult interactionResult,Player user,EDoorState? __state)
    {
        if(__0 is not Door door||!__state.HasValue||interactionResult.InteractionType is not(EInteractionType.Open or EInteractionType.Close))return;
        ChestRigCombat.QueueDoor(__instance,user,door,__state.Value);
    }
}
[HarmonyPatch]
internal static class ChestRigInteractionEndPatch
{
    private static IEnumerable<MethodBase> TargetMethods()=>new[]{AccessTools.Method(typeof(BreachDoorStateClass),nameof(BreachDoorStateClass.Exit)),AccessTools.Method(typeof(DoorInteractionStateClass),nameof(DoorInteractionStateClass.Exit))};
    private static void Postfix(object __instance)=>ChestRigCombat.DoorCompleted(__instance);
}
[HarmonyPatch(typeof(Player),nameof(Player.UpdateSpeedLimitByHealth))]
internal static class ChestRigLegMovementPatch
{
    private static void Postfix(Player __instance)
    {
        var s=ChestRigRuntime.Get(__instance);
        if(s?.Key!="AR18-2")return;
        // Preserve native damaged-leg flags, injury HP loss and their real
        // medical conditions. Remove only the named health movement limit.
        if(BackpackRuntime.BlackLeg(__instance))__instance.RemoveStateSpeedLimit(Player.ESpeedLimit.HealthCondition);
    }
}
[HarmonyPatch(typeof(MovementContext),"get_CanSprint")]
internal static class ChestRigLegSprintPatch
{
    private static void Postfix(MovementContext __instance,ref bool __result)
    {
        var p=BackpackRuntime.Actor(__instance);var s=ChestRigRuntime.Get(p);if(s is null)return;
        var eligible=s.Key=="AR18-2"&&BackpackRuntime.BlackLeg(p!)||s.Key=="AR16-2"&&Fractured(p!);
        if(eligible&&!__instance.PhysicalConditionIs(EPhysicalCondition.SprintDisabled)&&!__instance.PhysicalConditionIs(EPhysicalCondition.UsingMeds)&&!__instance.PhysicalConditionIs(EPhysicalCondition.HealingLegs))__result=true;
    }
    internal static bool Fractured(Player p)=>p.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg)||p.HealthController.IsBodyPartBroken(EBodyPart.RightLeg);
}
[HarmonyPatch(typeof(MovementContext),nameof(MovementContext.PhysicalConditionIs))]
internal static class ChestRigArmHandlingPatch
{
    private static void Postfix(MovementContext __instance,EPhysicalCondition c,ref bool __result)
    {
        if(!__result||c is not(EPhysicalCondition.LeftArmDamaged or EPhysicalCondition.RightArmDamaged))return;
        var p=BackpackRuntime.Actor(__instance);
        if(ChestRigRuntime.Is(p,"AR18-2")&&p!.ActiveHealthController.IsBodyPartDestroyed(c==EPhysicalCondition.LeftArmDamaged?EBodyPart.LeftArm:EBodyPart.RightArm))__result=false;
    }
}
[HarmonyPatch(typeof(Player),nameof(Player.UpdateArmsCondition))]
internal static class ChestRigArmsRefreshPatch
{
    private static void Postfix(Player __instance)
    {
        if(!ChestRigRuntime.Is(__instance,"AR18-2"))return;
        if(__instance.ActiveHealthController.IsBodyPartDestroyed(EBodyPart.LeftArm))__instance.MovementContext.SetPhysicalCondition(EPhysicalCondition.LeftArmDamaged,false);
        if(__instance.ActiveHealthController.IsBodyPartDestroyed(EBodyPart.RightArm))__instance.MovementContext.SetPhysicalCondition(EPhysicalCondition.RightArmDamaged,false);
    }
}
[HarmonyPatch(typeof(ProceduralWeaponAnimation),nameof(ProceduralWeaponAnimation.UpdateWeaponVariables))]
internal static class ChestRigAimingSpeedPatch
{
    private static readonly FieldInfo Speed=AccessTools.Field(typeof(ProceduralWeaponAnimation),"_aimingSpeed");
    private static void Postfix(ProceduralWeaponAnimation __instance)
    {
        var p=ChestRigRuntime.States.Keys.FirstOrDefault(p=>ReferenceEquals(p.ProceduralWeaponAnimation,__instance));
        if(p?.HandsController?.Item is not Weapon w||ChestRigRuntime.Get(p) is not { } s)return;
        var speed=(float)Speed.GetValue(__instance)*ChestRigRuntime.Stats(s,w).Aim;
        Speed.SetValue(__instance,speed);
        if(Native.Get(__instance,"_aimSwayBlender") is Player.ValueBlender blend)blend.Speed=__instance.SwayFalloff/speed;
    }
}
[HarmonyPatch(typeof(Player),nameof(Player.GetRotationMultiplier))]
internal static class ChestRigTurningPatch
{
    private static void Postfix(Player __instance,ref float __result)
    {
        var s=ChestRigRuntime.Get(__instance);if(s?.Roll.ChestRig.RemoveTurningPenalty!=true||__instance.HandsController?.IsAiming==true)return;
        var combined=Native.Number(__instance,"_mouseSensitivityModifier");
        var own=s.Roll.Definition.ChestRigEffect!.TurningPenalty/100f;
        if(combined>-.99f&&own<0f)__result*=(1f+combined-own)/(1f+combined);
    }
}
[HarmonyPatch(typeof(Player.FirearmController),nameof(Player.FirearmController.SetAnimatorAndProceduralValues))]
internal static class ChestRigSwapAnimatorPatch
{
    private static void Prefix(Player.FirearmController __instance,out float __state)
    {
        __state=__instance.BuffInfo?.SwapSpeed??1f;
        if(__instance.BuffInfo is not null)
        {
            var player=BackpackRuntime.Actor(__instance);
            __instance.BuffInfo.SwapSpeed*=EarpieceRuntime.Draw(player);
            if(ChestRigRuntime.Get(player) is { } s)__instance.BuffInfo.SwapSpeed*=ChestRigNative.Swap(s,__instance.Item);
        }
    }
    private static Exception? Finalizer(Player.FirearmController __instance,float __state,Exception? __exception)
    {if(__instance.BuffInfo is not null)__instance.BuffInfo.SwapSpeed=__state;return __exception;}
}
[HarmonyPatch(typeof(Player.FirearmController.DefaultWeaponOperationClass),nameof(Player.FirearmController.DefaultWeaponOperationClass.Start))]
internal static class ChestRigBoltCyclePatch
{
    private static readonly Dictionary<object,float> Speeds=new();
    private static void Postfix(Player.FirearmController.DefaultWeaponOperationClass __instance)
    {
        var s = ChestRigNative.For(__instance.Weapon_0);
        var hat = __instance.Weapon_0.BoltAction ? Headwear.Factor(Headwear.Owner(__instance.Weapon_0), HatStat.Bolt) : 1f;
        var speed = (s?.Key == "TR16-2" ? 1.5f : 1f) * (s?.CycleCharge ?? 1f) * hat;
        if(speed==1f)return;
        var original=Native.Get(Native.Get(__instance.FirearmsAnimator_0,"Animator"),"speed") is float f?f:1f;
        Speeds[__instance]=original;__instance.FirearmsAnimator_0.SetAnimationSpeed(original*speed);if(s is not null)s.CycleCharge=1f;
    }
    internal static void End(Player.FirearmController.GenericFireOperationClass operation)
    {if(Speeds.TryGetValue(operation,out var speed)){operation.FirearmsAnimator_0.SetAnimationSpeed(speed);Speeds.Remove(operation);}}
}
[HarmonyPatch(typeof(Player.FirearmController.GenericFireOperationClass),nameof(Player.FirearmController.GenericFireOperationClass.OnIdleStartEvent))]
internal static class ChestRigBoltCycleEndPatch
{
    private static void Prefix(Player.FirearmController.GenericFireOperationClass __instance)=>ChestRigBoltCyclePatch.End(__instance);
}
