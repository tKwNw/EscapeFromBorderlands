using System;using System.Linq;using System.Collections.Generic;using Comfort.Common;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using EFT.Interactive;using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static class ChestRigCombat
{
    private static readonly Dictionary<string,(string Owner,float Until)> Flashes=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,(float Factor,float Until)> FragmentFactors=new(StringComparer.Ordinal);
    internal static float? FragmentFactor(Ammo ammo)=>FragmentFactors.TryGetValue(ammo.Id,out var v)&&v.Until>Time.time?v.Factor:null;
    internal static void Fragment(Ammo ammo,float factor)=>FragmentFactors[ammo.Id]=(factor,Time.time+30f);
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
