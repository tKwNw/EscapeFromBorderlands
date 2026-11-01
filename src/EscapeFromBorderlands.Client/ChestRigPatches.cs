using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ChestRigNative
{
    internal static ChestRigRuntime.State? For(Weapon w)=>ChestRigRuntime.Get(BackpackNative.WeaponOwner(w));
    internal static float Reload(Player.FirearmController f)=>ChestRigRuntime.Get(BackpackRuntime.Actor(f)) is { } s?ChestRigRuntime.ReloadSpeed(s,f.Weapon):1f;
    internal static float Swap(ChestRigRuntime.State s,Weapon w)
    {
        var factor=s.Roll.ChestRig.Swap*Math.Max(s.DrawCharge,s.Weapon(w).DrawCharge);
        if(s.Key=="TR10-1"&&s.EmptyPrimary&&ChestRigRuntime.Secondary(s.Player,w))factor*=2f;
        if(s.Key=="TR13-1"&&ChestRigRuntime.ThreeFirearms(s.Player))factor*=1.5f;
        if(s.Key=="TR24-1"&&s.Active("vault"))factor*=1.5f;
        if(s.Key=="AR19-2"&&s.Active("breach"))factor*=1.4f;
        return factor;
    }
}
[HarmonyPatch(typeof(Weapon),"get_RecoilDelta")]
internal static class ChestRigRecoilPatch
{
    private static void Postfix(Weapon __instance,ref float __result)
    {if(ChestRigNative.For(__instance) is { } s)__result=(1f+__result)*ChestRigRuntime.Stats(s,__instance).Recoil-1f;}
}
[HarmonyPatch(typeof(Weapon),"get_ErgonomicsDelta")]
internal static class ChestRigErgonomicsPatch
{
    private static void Postfix(Weapon __instance,ref float __result)
    {if(ChestRigNative.For(__instance) is { } s)__result=(1f+__result)*ChestRigRuntime.Stats(s,__instance).Ergonomics-1f;}
}
[HarmonyPatch(typeof(Weapon),"get_CenterOfImpactDelta")]
internal static class ChestRigAccuracyPatch
{
    private static void Postfix(Weapon __instance,ref float __result)
    {if(ChestRigNative.For(__instance) is { } s)__result=(1f+__result)*ChestRigRuntime.Stats(s,__instance).Accuracy-1f;}
}
[HarmonyPatch(typeof(Weapon),"get_VelocityDelta")]
internal static class ChestRigVelocityPatch
{
    private static void Postfix(Weapon __instance,ref float __result)
    {if(ChestRigNative.For(__instance) is { } s)__result=(1f+__result)*ChestRigRuntime.Stats(s,__instance).Velocity-1f;}
}
[HarmonyPatch]
internal static class ChestRigFireRatePatch
{
    private static IEnumerable<MethodBase> TargetMethods()=>new[]{AccessTools.PropertyGetter(typeof(Weapon),"FireRate"),AccessTools.PropertyGetter(typeof(Weapon),"SingleFireRate")};
    private static void Postfix(Weapon __instance,ref int __result)
    {if(ChestRigNative.For(__instance) is { } s)__result=Math.Max(1,(int)Math.Round(__result*ChestRigRuntime.Stats(s,__instance).FireRate));}
}
[HarmonyPatch(typeof(BreathEffector),nameof(BreathEffector.Process))]
internal static class ChestRigSwayPatch
{
    private static void Prefix(BreathEffector __instance,out float __state)
    {__state=__instance.Intensity;var p=BackpackRuntime.Actor(__instance.Physical);__instance.Intensity*=EarpieceRuntime.Sway(p);if(p?.HandsController?.Item is Weapon w&&ChestRigRuntime.Get(p) is { } s)__instance.Intensity*=ChestRigRuntime.Stats(s,w).Sway;}
    private static Exception? Finalizer(BreathEffector __instance,float __state,Exception? __exception){__instance.Intensity=__state;return __exception;}
}
[HarmonyPatch(typeof(Player.FirearmController),nameof(Player.FirearmController.GetWeaponDrawSpeedMultiplier))]
internal static class ChestRigDrawSpeedPatch
{
    private static void Postfix(Player.FirearmController __instance,Weapon weapon,ref float __result)
    {if(ChestRigRuntime.Get(BackpackRuntime.Actor(__instance)) is { } s)__result*=ChestRigNative.Swap(s,weapon);}
}
[HarmonyPatch(typeof(Player.FirearmController.GClass2038),nameof(Player.FirearmController.GClass2038.Start))]
internal static class ChestRigUtilitySpeedPatch
{
    internal static readonly Dictionary<object,float> Speeds=new();
    private static void Postfix(Player.FirearmController.GClass2038 __instance)
    {
        if(ChestRigNative.For(__instance.Weapon_0) is not { } s)return;
        var factor=__instance.EutilityType_0==Player.FirearmController.GClass2038.EUtilityType.CheckMagazine?s.Roll.ChestRig.MagazineCheck:
            __instance.EutilityType_0==Player.FirearmController.GClass2038.EUtilityType.CheckChamber?s.Roll.ChestRig.ChamberCheck:1f;
        if(factor==1f)return;
        var animator=__instance.FirearmsAnimator_0;
        var original=Native.Get(Native.Get(animator,"Animator"),"speed") is float value?value:1f;
        Speeds[__instance]=original;animator.SetAnimationSpeed(original*factor);
    }
}
[HarmonyPatch(typeof(JumpStateClass),nameof(JumpStateClass.Enter))]
internal static class ChestRigJumpPatch
{
    private static void Postfix(JumpStateClass __instance)
    {if(BackpackRuntime.Actor(__instance.MovementContext) is { } p)__instance.Float_5*=(float)Math.Sqrt(ChestRigRuntime.JumpHeight(p));}
}
[HarmonyPatch(typeof(ActiveHealthController),nameof(ActiveHealthController.ChangeHealth))]
internal static class ChestRigHealthChangePatch
{
    private static void Prefix(ActiveHealthController __instance,EBodyPart bodyPart,ref float value,DamageInfoStruct damageInfo)
    {
        if(value>=0)return;
        if(ChestRigMedical.PauseBleed(__instance,bodyPart,damageInfo.DamageType)){value=0;return;}
        if(damageInfo.DamageType==EDamageType.Fall&&BackpackLegDamageScope.Player!=__instance.Player&&ChestRigRuntime.Is(__instance.Player,"AR25-2"))value*=.65f;
        if(ChestRigDamagePatch.Depth==0)value*=ChestRigMedical.Incoming(ChestRigRuntime.Get(__instance.Player),damageInfo.DamageType,bodyPart);
    }
}
[HarmonyPatch(typeof(ActiveHealthController),nameof(ActiveHealthController.ApplyDamage))]
internal static class ChestRigDamagePatch
{
    [ThreadStatic] internal static int Depth;
    private static void Prefix(ActiveHealthController __instance,EBodyPart bodyPart,ref float damage,DamageInfoStruct damageInfo,out int __state)
    {__state=Depth++;if(__state==0&&damageInfo.DamageType==EDamageType.Bullet&&damageInfo.Player?.iPlayer is Player shooter&&ChestRigRuntime.Is(shooter,"AR11-2")&&ChestRigCombat.BlindedBy(__instance.Player,shooter))damage*=1.5f;damage*=ChestRigMedical.Incoming(ChestRigRuntime.Get(__instance.Player),damageInfo.DamageType,bodyPart);}
    private static Exception? Finalizer(int __state,Exception? __exception){Depth=__state;return __exception;}
}
[HarmonyPatch]
internal static class ChestRigMedStartPatch
{
    private static MethodBase TargetMethod()=>AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect",BindingFlags.NonPublic),"Started");
    private static void Prefix(object __instance)=>ChestRigMedical.Begin(__instance);
}
[HarmonyPatch]
internal static class ChestRigMedEndPatch
{
    private static MethodBase TargetMethod()=>AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect",BindingFlags.NonPublic),"Residue");
    private static void Postfix(object __instance)=>ChestRigMedical.End(__instance);
}
[HarmonyPatch(typeof(ActiveHealthController),nameof(ActiveHealthController.RestoreBodyPart))]
internal static class ChestRigSurgeryPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ActiveHealthController __instance,EBodyPart bodyPart,float healthPenalty)=>ChestRigMedical.Surgery(__instance,bodyPart,healthPenalty);
}
[HarmonyPatch]
internal static class ChestRigBleedPatch
{
    private static MethodBase TargetMethod()=>AccessTools.Method(typeof(ActiveHealthController),"DoBleed",new[]{typeof(bool),typeof(EBodyPart)});
    private static void Prefix(ActiveHealthController __instance,bool isHeavy,EBodyPart bodyPart,out bool __state)=>__state=__instance.GetAllEffects().Any(e=>e.Existing&&e.BodyPart==bodyPart&&e.GetType().Name==(isHeavy?"HeavyBleeding":"LightBleeding"));
    private static void Postfix(ActiveHealthController __instance,bool isHeavy,EBodyPart bodyPart,bool __state)
    {if(!__state)ChestRigMedical.NewBleed(__instance,isHeavy,bodyPart);}
}
[HarmonyPatch]
internal static class ChestRigMedicalCostPatch
{
    private static MethodBase TargetMethod()=>AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect",BindingFlags.NonPublic),"RegularUpdate");
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count=0;
        foreach(var instruction in instructions)
        {
            if(instruction.opcode==System.Reflection.Emit.OpCodes.Stfld&&Equals(instruction.operand,AccessTools.Field(typeof(MedKitComponent),nameof(MedKitComponent.HpResource))))
            {
                var op=instruction.opcode;var field=instruction.operand;
                instruction.opcode=System.Reflection.Emit.OpCodes.Ldarg_0;instruction.operand=null;yield return instruction;
                yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call,AccessTools.Method(typeof(ChestRigMedical),nameof(ChestRigMedical.MedicalResource)));
                yield return new CodeInstruction(op,field);count++;
            }
            else yield return instruction;
        }
        if(count!=1)throw new InvalidOperationException("Expected one native HP-restoration resource spend; found "+count);
    }
}
[HarmonyPatch(typeof(HealthEffectsComponent),"get_UseTime")]
internal static class ChestRigMedSpeedPatch
{
    private static void Postfix(HealthEffectsComponent __instance,ref float __result)
    {if(BackpackNative.ItemOwner(__instance.Item) is { } p)__result/=ChestRigMedical.UseSpeed(p)*GrenadeBuffs.Medicine(p)*EarpieceRuntime.MedicalSpeed(p,__instance.Item);}
}
[HarmonyPatch(typeof(Player.PlayerInventoryController.Class1204),"method_4")]
internal static class ChestRigManualPackingPatch
{
    private static void Postfix(Player.PlayerInventoryController.Class1204 __instance)=>ChestRigInventory.Packed(__instance.InventoryController_0);
}

[HarmonyPatch(typeof(Player.FirearmController),nameof(Player.FirearmController.UpdateHipInaccuracy))]
internal static class ChestRigHipPatch
{
    private static void Postfix(Player.FirearmController __instance)
    {
        var p=BackpackRuntime.Actor(__instance);
        if(p is null||ChestRigRuntime.Get(p) is not { } s)return;
        __instance.HipInaccuracy*=ChestRigRuntime.Stats(s,__instance.Item).Hip;
        p.ProceduralWeaponAnimation.Breath.HipPenalty=__instance.HipInaccuracy;
    }
}
[HarmonyPatch(typeof(ProceduralWeaponAnimation),nameof(ProceduralWeaponAnimation.PhysicalConditionUpdated))]
internal static class ChestRigInjurySwayPatch
{
    private static readonly FieldInfo Factor=AccessTools.Field(typeof(ProceduralWeaponAnimation),"_swayFactor");
    private static void Postfix(ProceduralWeaponAnimation __instance)
    {
        var p=ChestRigRuntime.States.Keys.FirstOrDefault(p=>ReferenceEquals(p.ProceduralWeaponAnimation,__instance));
        if(p is not null&&ChestRigRuntime.Is(p,"AR06-2")&&ChestRigRuntime.BlackArms(p))
        {Factor.SetValue(__instance,(float)Factor.GetValue(__instance)*.5f);__instance.UpdateSwaySettings();}
    }
}

[HarmonyPatch(typeof(Player.FirearmController.GClass2038),nameof(Player.FirearmController.GClass2038.Reset))]
internal static class ChestRigUtilityResetPatch
{
    private static void Prefix(Player.FirearmController.GClass2038 __instance)
    {if(ChestRigUtilitySpeedPatch.Speeds.TryGetValue(__instance,out var speed)){__instance.FirearmsAnimator_0.SetAnimationSpeed(speed);ChestRigUtilitySpeedPatch.Speeds.Remove(__instance);}}
}
[HarmonyPatch(typeof(Player.FirearmController.GenericFireOperationClass),nameof(Player.FirearmController.GenericFireOperationClass.Reset))]
internal static class ChestRigBoltResetPatch
{
    private static void Prefix(Player.FirearmController.GenericFireOperationClass __instance)=>ChestRigBoltCyclePatch.End(__instance);
}

[HarmonyPatch(typeof(Weapon),"get_ErgonomicsTotal")]
internal static class ChestRigFlatErgonomicsPatch
{
    private static void Postfix(Weapon __instance,ref float __result)
    {if(ChestRigNative.For(__instance) is { } s)__result+=ChestRigRuntime.Stats(s,__instance).FlatErgonomics;}
}
