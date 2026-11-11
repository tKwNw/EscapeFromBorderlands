using Magazine = MagazineItemClass;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class ChestRigReload
{
    internal static async void SwapHolstered(ChestRigRuntime.State s,Weapon pistol)
    {
        if(!ChestRigInventory.BeginSwap(pistol.Id))return;
        try
        {
            var current=pistol.GetCurrentMagazine();if(current?.CurrentAddress is null)return;
            var carried = ChestRigInventory.Carried(s);
            var candidates = carried.OfType<Magazine>().Where(m => m.Count > current.Count && m.CurrentAddress is not null).ToArray();
            if (candidates.Length == 0) return;
            var installed = new HashSet<Magazine>();
            var installedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var weapon in carried.OfType<Weapon>())
            {
                if (weapon.GetCurrentMagazine() is { } active) installedIds.Add(active.Id);
                foreach (var magazine in weapon.GetAllItems().OfType<Magazine>()) installed.Add(magazine);
            }
            foreach (var next in candidates.Where(m => !installed.Contains(m) && !installedIds.Contains(m.Id)).OrderByDescending(m => m.Count))
            {
                if(ChestRigRuntime.Get(s.Player)!=s||s.Player.HandsController?.Item?.Id==pistol.Id)return;
                var operation=InteractionsHandlerClass.Swap(current,next.CurrentAddress!,next,current.CurrentAddress!,s.Player.InventoryController,simulate:true);
                if(operation.Failed)continue;
                var result=await s.Player.InventoryController.TryRunNetworkTransaction(operation);
                if(!result.Failed){s.Cooldown(30f);return;}
            }
        }
        catch(Exception e){Plugin.LogSource.LogWarning("Clean Hands magazine exchange was rejected: "+e.Message);}
        finally{ChestRigInventory.EndSwap(pistol.Id);}
    }
}
