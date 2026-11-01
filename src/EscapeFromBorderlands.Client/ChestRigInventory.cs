using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ChestRigInventory
{
    private static readonly HashSet<string> Busy = new(StringComparer.Ordinal);
    private static readonly Dictionary<string,float> LastHeld = new(StringComparer.Ordinal), NextFill = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string,MagazineItemClass> ReloadMagazines = new(StringComparer.Ordinal);
    private static int Generation;
    internal static void Reset(){Generation++;Busy.Clear();LastHeld.Clear();NextFill.Clear();ReloadMagazines.Clear();}
    internal static Item[] Carried(ChestRigRuntime.State s)=>s.Player.InventoryController.Inventory.Equipment.GetAllItems().ToArray();
    // Former weight signatures and the frame-weight boon have been replaced.
    internal static float Weight(ChestRigRuntime.State? s,float original) => original;
    internal static void Maintain(ChestRigRuntime.State s,float dt)
    {
        if(s.Key is not("TR17-1" or "TR05-2"))return;
        var id=s.Player.ProfileId;
        var sprinting=s.Player.MovementContext.IsSprintEnabled&&s.Player.InputDirection.sqrMagnitude>.0001f;
        if(s.Key=="TR05-2"&&!sprinting){NextFill.Remove(id);return;}
        var interval=s.Key=="TR05-2"?1f:2f;
        if(!NextFill.TryGetValue(id,out var time)){NextFill[id]=Time.time+interval;return;}
        if(Busy.Contains(id)||Time.time<time)return;
        NextFill[id]=Time.time+interval;_ = FillAll(s,Generation);
    }
    private static async Task FillAll(ChestRigRuntime.State s,int generation)
    {
        var id=s.Player.ProfileId;if(!Busy.Add(id))return;
        try
        {
            foreach(var mag in Carried(s).OfType<MagazineItemClass>().Where(m=>m.Count<m.MaxCount).ToArray())
            {
                if(generation!=Generation||ChestRigRuntime.Get(s.Player)!=s)return;
                if(s.Key=="TR05-2"&&(!s.Player.MovementContext.IsSprintEnabled||s.Player.InputDirection.sqrMagnitude<=.0001f))return;
                var carried=Carried(s);
                // Never modify a magazine installed in a firearm or reserved
                // by a reload. Re-read inventory after each real transaction.
                if(!carried.Contains(mag)||mag.Count>=mag.MaxCount||
                    carried.OfType<Weapon>().Any(w=>w.GetCurrentMagazine()?.Id==mag.Id||w.GetAllItems().Contains(mag))||
                    ReloadMagazines.TryGetValue(id,out var incoming)&&incoming.Id==mag.Id)continue;
                var loaded=mag.Cartridges.Items.OfType<AmmoItemClass>().LastOrDefault()?.StringTemplateId;
                if(mag.Count>0&&loaded is null)continue;
                var contained=carried.Where(i=>i is MagazineItemClass or Weapon).SelectMany(i=>i.GetAllItems().Where(child=>child.Id!=i.Id)).Select(i=>i.Id).ToHashSet();
                foreach(var round in carried.OfType<AmmoItemClass>().Where(a=>!a.IsUsed&&a.StackObjectsCount>0&&!contained.Contains(a.Id)&&(loaded is null||a.StringTemplateId==loaded)))
                {
                    if(generation!=Generation||ChestRigRuntime.Get(s.Player)!=s)return;
                    if(s.Key=="TR05-2"&&(!s.Player.MovementContext.IsSprintEnabled||s.Player.InputDirection.sqrMagnitude<=.0001f))return;
                    var op=mag.ApplyWithoutRestrictions(s.Player.InventoryController,round,1,simulate:true);
                    if(op.Failed)continue;
                    var result=await s.Player.InventoryController.TryRunNetworkTransaction(op);
                    if(!result.Failed){EffectStatusModel.Notify(s.Player.ProfileId,"rig:"+s.Key,s.Roll.Definition.ShortName,Time.time,"automatic magazine packing");break;}
                }
            }
        }
        catch(Exception e){Plugin.LogSource.LogWarning(s.Roll.Definition.ShortName+" ammunition transfer was rejected: "+e.Message);}
        finally{if(generation==Generation)Busy.Remove(id);}
    }
    internal static void Packed(InventoryController inventory)
    {
        var s=ChestRigRuntime.Get(BackpackRuntime.Actor(inventory));if(s?.Key!="TR15-1")return;
        if(++s.Packing>=10){s.Packing=0;s.HandloadShots=5;}
    }
    internal static float LoadTime(InventoryController inventory,float seconds,bool unload)
    {
        var s=ChestRigRuntime.Get(BackpackRuntime.Actor(inventory));if(s is null)return seconds;
        var speed=unload?1f:s.Roll.ChestRig.Packing;
        if(unload&&s.Key=="TR05-1")speed*=3f;
        if(s.Key=="TR04-1")speed*=1.5f;
        if(!unload&&s.Key=="TR17-2"&&s.Player.SearchController?.SearchOperations?.Any()==true)speed*=1.75f;
        return seconds/speed;
    }
    internal static void AutoReloadHolsteredPistol(ChestRigRuntime.State s)
    {
        var pistol=s.Player.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem as Weapon;
        if(pistol is null)return;
        if(s.Player.HandsController?.Item?.Id==pistol.Id){LastHeld[pistol.Id]=Time.time;return;}
        if(!LastHeld.TryGetValue(pistol.Id,out var last)){LastHeld[pistol.Id]=Time.time;return;}
        if(Time.time-last<10f||!s.Ready||Busy.Contains(pistol.Id))return;
        ChestRigReload.SwapHolstered(s,pistol);
    }
    internal static void Acquired(Player p,Item source,Item received,bool external)
    {
        var s=ChestRigRuntime.Get(p);
        if(s?.Key=="TR22-1"&&external&&BackpackNative.Barter(source)&&Carried(s).Any(i=>i.Id==received.Id)&&s.Ready)
        {s.Arm("loot",8f);s.Cooldown(30f);}
    }
    internal static bool BeginSwap(string id)=>Busy.Add(id);
    internal static void EndSwap(string id)=>Busy.Remove(id);
}
