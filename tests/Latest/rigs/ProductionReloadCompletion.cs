using System;using System.Linq;using System.Reflection;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;
namespace EscapeFromBorderlands.Client;
internal static class RigReloadHook {
private static void Prefix(Player.FirearmController __instance, MethodBase __originalMethod, ref Callback callback, object[] __args)
    {
        var original = callback;
        var actor = BackpackRuntime.Actor(__instance);
        var rig = ChestRigRuntime.Get(actor);
        if (rig is not null) { rig.Reloading=true; rig.ReloadWeapon=__instance.Item.Id;
            if (__args.OfType<Magazine>().FirstOrDefault() is { } incoming) ChestRigInventory.ReloadMagazines[rig.Player.ProfileId]=incoming; }
        var weapon = __instance.Item;
        var emergency = string.Equals(__originalMethod.Name, nameof(Player.FirearmController.QuickReloadMag), StringComparison.Ordinal);
        if(rig is not null)rig.EmergencyReloading=emergency;
        // Reapply the native base speed even when all bonuses have expired.
        // Skipping the 1x case leaves an earlier boost cached on the animator
        // and incorrectly accelerates subsequent reloads.
        __instance.SetAnimatorAndProceduralValues();
        callback = result =>
        {
            if (result.Succeed) { LegendaryRuntime.NotifyReloadCompleted(weapon, emergency); if(actor is not null&&rig is not null&&ReferenceEquals(ChestRigRuntime.Get(actor),rig))ChestRigRuntime.ReloadCompleted(actor,weapon,emergency); }
            if(rig is not null){rig.Reloading=false;rig.EmergencyReloading=false;ChestRigInventory.ReloadMagazines.Remove(rig.Player.ProfileId);}
            original?.Invoke(result);
        };
    }
}
