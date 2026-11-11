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
    internal static readonly Dictionary<string,Magazine> ReloadMagazines = new(StringComparer.Ordinal);
    private static int Generation;
    internal static void Reset(){Generation++;Busy.Clear();LastHeld.Clear();NextFill.Clear();ReloadMagazines.Clear();}
    internal static Item[] Carried(ChestRigRuntime.State s)=>s.Player.InventoryController.Inventory.Equipment.GetAllItems().ToArray();
    private sealed class PackingView
    {
        internal readonly List<Ammo> AmmoInOrder = new();
        private readonly HashSet<Magazine> CarriedMagazines = new();
        private readonly HashSet<Item> Installed = new();
        private readonly HashSet<string> InstalledIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> Contained = new(StringComparer.Ordinal);
        internal PackingView(Item[] items)
        {
            foreach (var root in items)
            {
                if (root is Ammo round) AmmoInOrder.Add(round);
                if (root is Magazine magazine) CarriedMagazines.Add(magazine);
                if (root is not (Magazine or Weapon)) continue;
                var weapon = root as Weapon;
                if (weapon?.GetCurrentMagazine() is { } loaded) InstalledIds.Add(loaded.Id);
                foreach (var child in root.GetAllItems())
                {
                    if (weapon is not null && child is Magazine) Installed.Add(child);
                    if (child.Id != root.Id) Contained.Add(child.Id);
                }
            }
        }
        internal bool HasSpare(Magazine magazine) =>
            CarriedMagazines.Contains(magazine) && !InstalledIds.Contains(magazine.Id) && !Installed.Contains(magazine);
        internal bool IsLoose(Ammo ammo, string? loaded) => !ammo.IsUsed && ammo.StackObjectsCount > 0 &&
            !Contained.Contains(ammo.Id) && (loaded is null || ammo.StringTemplateId == loaded);
    }
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
            Item[]? carried = Carried(s);
            var magazines = carried.OfType<Magazine>().Where(m=>m.Count<m.MaxCount).ToArray();
            PackingView? view = null;
            foreach(var mag in magazines)
            {
                if(generation!=Generation||ChestRigRuntime.Get(s.Player)!=s)return;
                if(s.Key=="TR05-2"&&(!s.Player.MovementContext.IsSprintEnabled||s.Player.InputDirection.sqrMagnitude<=.0001f))return;
                // Never modify a magazine installed in a firearm or reserved
                // by a reload. Reuse the index during synchronous checks.
                view ??= new PackingView(carried ?? Carried(s));
                if(mag.Count>=mag.MaxCount||!view.HasSpare(mag)||
                    ReloadMagazines.TryGetValue(id,out var incoming)&&incoming.Id==mag.Id)continue;
                var loaded=mag.Cartridges.Items.OfType<Ammo>().LastOrDefault()?.StringTemplateId;
                if(mag.Count>0&&loaded is null)continue;
                var snapshot = view;
                foreach(var round in snapshot.AmmoInOrder)
                {
                    if (!snapshot.IsLoose(round, loaded)) continue;
                    if(generation!=Generation||ChestRigRuntime.Get(s.Player)!=s)return;
                    if(s.Key=="TR05-2"&&(!s.Player.MovementContext.IsSprintEnabled||s.Player.InputDirection.sqrMagnitude<=.0001f))return;
                    var op=mag.ApplyWithoutRestrictions(s.Player.InventoryController,round,1,simulate:true);
                    if(op.Failed)continue;
                    var result=await s.Player.InventoryController.TryRunNetworkTransaction(op);
                    view = null; carried = null; // Re-read before the next magazine after any real transaction.
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
        speed*=ServiceWindow.Speed(s.Player);
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
