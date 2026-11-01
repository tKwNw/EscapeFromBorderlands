using System;using System.Linq;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;
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
