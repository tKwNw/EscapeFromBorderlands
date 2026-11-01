using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ChestRigCombat
{
    private static readonly Dictionary<string,(string Owner,float Until)> Flashes=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,(float Factor,float Until)> FragmentFactors=new(StringComparer.Ordinal);
    internal static float? FragmentFactor(AmmoItemClass ammo)=>FragmentFactors.TryGetValue(ammo.Id,out var v)&&v.Until>Time.time?v.Factor:null;
    internal static void Fragment(AmmoItemClass ammo,float factor)=>FragmentFactors[ammo.Id]=(factor,Time.time+30f);
    private sealed class DoorAction
    {internal ChestRigRuntime.State State=null!;internal Door Door=null!;internal EDoorState Before;internal float Until;internal bool Complete,Breach;}
    private static readonly Dictionary<object,DoorAction> Doors=new();
    private static readonly Dictionary<Player.BaseGrenadeHandsController,(ChestRigRuntime.State State,float Until)> Throws=new();
    internal static void QueueDoor(object action,Player player,Door door,EDoorState before,bool breach=false)
    {if(ChestRigRuntime.Get(player) is { } s)Doors[action]=new DoorAction{State=s,Door=door,Before=before,Breach=breach,Until=Time.time+8f};}
    internal static void DoorCompleted(object action)
    {if(Doors.TryGetValue(action,out var entry))entry.Complete=true;Maintain();}
    internal static void Maintain()
    {
        foreach(var key in FragmentFactors.Where(kv=>kv.Value.Until<Time.time).Select(kv=>kv.Key).ToArray())FragmentFactors.Remove(key);
        foreach(var pair in Doors.ToArray())
        {
            var d=pair.Value;var s=d.State;
            if(d.Until<Time.time||ChestRigRuntime.Get(s.Player)!=s){Doors.Remove(pair.Key);continue;}
            if(!d.Complete)continue;
            if(!d.Breach&&(d.Door.DoorState==d.Before||d.Door.DoorState is not(EDoorState.Open or EDoorState.Shut)))continue;
            Doors.Remove(pair.Key);if(!s.Ready)continue;
            if(d.Breach&&d.Door.IsBroken)
            {
                if(s.Key=="TR20-1"){ChestRigRuntime.RestoreArms(s.Player,.4f);ChestRigRuntime.ArmReload(s,1.5f);s.Cooldown(20f);}
                if(s.Key=="AR19-2"){s.Arm("breach",25f);s.Cooldown(25f);}
            }
            else if(s.Key=="AR15-2"){s.Arm("door",20f);s.Cooldown(20f);}
        }
        foreach(var key in Throws.Where(kv=>kv.Value.Until<Time.time||ChestRigRuntime.Get(kv.Value.State.Player)!=kv.Value.State).Select(kv=>kv.Key).ToArray())Throws.Remove(key);
    }
    [ThreadStatic] internal static string? FlashOwner;
    internal static void Reset(){Flashes.Clear();Doors.Clear();Throws.Clear();FragmentFactors.Clear();FlashOwner=null;}
    internal static bool BlindedBy(Player target,Player shooter)=>Flashes.TryGetValue(target.ProfileId,out var flash)&&flash.Owner==shooter.ProfileId&&flash.Until>Time.time;
    internal static float FlashRemaining(Player owner)=>Flashes.Where(f=>f.Value.Owner==owner.ProfileId&&f.Value.Until>Time.time&&Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().Any(p=>p.ProfileId==f.Key&&p.HealthController?.IsAlive==true)==true).Select(f=>f.Value.Until-Time.time).DefaultIfEmpty(0f).Max();
    internal static void Flash(Player target,float seconds){if(FlashOwner is { } owner&&seconds>0f)Flashes[target.ProfileId]=(owner,Time.time+seconds);}
    internal static void Thrown(Player.BaseGrenadeHandsController controller)
    {
        var s=ChestRigRuntime.Get(BackpackRuntime.Actor(controller));
        if(s?.Key=="TR07-1"&&s.Ready)Throws[controller]=(s,Time.time+10f);
    }
    internal static void ThrowCompleted(Player.BaseGrenadeHandsController controller)
    {
        if(!Throws.TryGetValue(controller,out var entry))return;Throws.Remove(controller);
        var s=entry.State;
        if(entry.Until>=Time.time&&ChestRigRuntime.Get(s.Player)==s&&s.Ready){s.Arm("grenade",6f);s.Cooldown(20f);}
    }
    internal static bool GrenadeBonus(Player.BaseGrenadeHandsController controller,string key)=>ChestRigRuntime.Is(BackpackRuntime.Actor(controller),key);
}
[HarmonyPatch(typeof(ArmorComponent),nameof(ArmorComponent.ApplyDamage))]
internal static class ChestRigArmorStopPatch
{
    private static void Postfix(ArmorComponent __instance,ref DamageInfoStruct damageInfo)
    {
        if(damageInfo.DamageType!=EDamageType.Bullet||damageInfo.BlockedBy!=__instance.Item.Id&&damageInfo.DeflectedBy!=__instance.Item.Id)return;
        var p=BackpackNative.ItemOwner(__instance.Item);var s=ChestRigRuntime.Get(p);
        if(s is null||!BackpackNative.InBag(__instance.Item,s.Rig)||damageInfo.Player?.iPlayer.ProfileId==p!.ProfileId)return;
        if(s.Key=="AR01-1"&&s.Ready){ChestRigRuntime.RestoreArms(p!, .35f);s.Arm("aim",6f);s.Cooldown(20f);}
        if(s.Key=="AR01-2"&&s.StillSeconds>=3f)damageInfo.Damage*=.5f;
        if(s.Key=="AR03-2")s.HitDamage=Math.Min(1.45f,s.HitDamage+.15f);
        if(s.Key=="AR14-2"&&p!.HandsController is Player.BaseKnifeController)damageInfo.Damage*=.5f;
        if(s.Key=="AR23-2"&&s.Ready)
        {
            var slot=Native.Get(__instance.Item.CurrentAddress,"Slot");var name=Native.Get(slot,"ID")?.ToString()??Native.Get(slot,"Id")?.ToString()??"";
            if(name=="Back_plate"||name=="Soft_armor_back"){BackpackRuntime.RestoreLeg(p!, .2f);s.Arm("escape",4f);s.Cooldown(15f);}
        }
    }
}
[HarmonyPatch(typeof(GameWorld),nameof(GameWorld.HackShot))]
internal static class ChestRigMeleeDamagePatch
{
    private static void Prefix(ref DamageInfoStruct damageInfo)
    {if(damageInfo.DamageType==EDamageType.Melee&&damageInfo.Player?.iPlayer is Player p&&ChestRigRuntime.Is(p,"AR14-2")&&p.HandsController is Player.BaseKnifeController)damageInfo.Damage*=1.4f;}
}
[HarmonyPatch(typeof(Player.GrenadeHandsController),nameof(Player.GrenadeHandsController.Spawn))]
internal static class ChestRigGrenadeReadyPatch
{
    private static void Prefix(Player.GrenadeHandsController __instance,ref float animationSpeed)
    {if(ChestRigCombat.GrenadeBonus(__instance,"TR07-2"))animationSpeed*=1.5f;}
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController),"method_10")]
internal static class ChestRigGrenadeThrowPatch
{
    private static void Postfix(Player.BaseGrenadeHandsController __instance,Grenade __result)
    {if(__result is not null)ChestRigCombat.Thrown(__instance);}
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController),"method_2")]
internal static class ChestRigGrenadeCompletionPatch
{
    private static void Postfix(Player.BaseGrenadeHandsController __instance)=>ChestRigCombat.ThrowCompleted(__instance);
}
internal sealed class ChestRigExplosive : IExplosiveItem
{
    private readonly IExplosiveItem Source;
    private readonly float Damage,Radius;
    internal ChestRigExplosive(IExplosiveItem source,float damage,float radius){Source=source;Damage=damage;Radius=radius;}
    public Vector3 Blindness=>Source.Blindness;
    public Vector3 Contusion=>Source.Contusion;
    public Vector3 ArmorDistanceDistanceDamage=>new(Source.ArmorDistanceDistanceDamage.x*Radius,Source.ArmorDistanceDistanceDamage.y*Radius,Source.ArmorDistanceDistanceDamage.z*Damage);
    public float MinExplosionDistance=>Source.MinExplosionDistance*Radius;
    public float MaxExplosionDistance=>Source.MaxExplosionDistance*Radius;
    public int FragmentsCount=>Source.FragmentsCount;
    public float GetStrength=>Source.GetStrength*Damage;
    public bool IsDummy=>Source.IsDummy;
    public AmmoItemClass CreateFragment(){var ammo=Source.CreateFragment();ChestRigCombat.Fragment(ammo,Damage);return ammo;}
}
[HarmonyPatch(typeof(Grenade),nameof(Grenade.Explosion))]
internal static class ChestRigExplosionPatch
{
    private static void Prefix(ref IExplosiveItem grenadeItem,Item originalWeaponItem,string playerProfileIDWhoThrew,out string? __state)
    {
        __state=ChestRigCombat.FlashOwner;
        if(grenadeItem.Blindness.sqrMagnitude>0f)ChestRigCombat.FlashOwner=playerProfileIDWhoThrew;
        var p=Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(x=>x.ProfileId==playerProfileIDWhoThrew);
        var s=ChestRigRuntime.Get(p);if(s is null)return;
        if(s.Key=="AR21-1"&&originalWeaponItem is ThrowWeapItemClass&&grenadeItem.FragmentsCount>0)
            grenadeItem=new ChestRigExplosive(grenadeItem,1.3f,1.5f);
        if(s.Key=="AR21-2"&&originalWeaponItem is LauncherItemClass)grenadeItem=new ChestRigExplosive(grenadeItem,1.3f,1.25f);
    }
    private static Exception? Finalizer(string? __state,Exception? __exception){ChestRigCombat.FlashOwner=__state;return __exception;}
}
[HarmonyPatch(typeof(ActiveHealthController),nameof(ActiveHealthController.DoStun))]
internal static class ChestRigFlashTrackingPatch
{
    private static void Postfix(ActiveHealthController __instance,float maxTime,float normalizedStrength)=>ChestRigCombat.Flash(__instance.Player,maxTime*normalizedStrength*__instance.Player.BlindnessDuration);
}
