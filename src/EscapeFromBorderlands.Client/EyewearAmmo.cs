using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// Retain the actual loaded cartridge. No inventory item is manufactured and no
// magazine is searched/refilled. Native shot execution still owns recoil, wear,
// heat, sound, projectile creation, animations and malfunction selection.
internal static class EyewearAmmo
{
    private static ConditionalWeakTable<Ammo, object> RetainedFaults = new();
    private static readonly object FaultMarker = new();
    internal static void Reset() { RetainedFaults = new(); EyewearAmmoIdentity.Reset(); }
    internal static void RememberFault(Ammo ammo)
    { RetainedFaults.Remove(ammo); RetainedFaults.Add(ammo, FaultMarker); }
    internal static void ForgetFault(Ammo? ammo) { if (ammo is not null) RetainedFaults.Remove(ammo); }
    internal static bool IsRetainedFault(Weapon weapon, Ammo? ammo) => ammo is not null &&
        RetainedFaults.TryGetValue(ammo, out _) && IsRetained(weapon, ammo);
    internal static bool Enabled(Player.FirearmController.FirearmOperation operation) =>
        Eyewear.Get(operation.Player)?.Key == "EW022";

    internal static Player.FirearmController.FireOperationBase.SingleShotData Retained(Weapon weapon, Ammo ammo) => new()
    {
        AmmoToFire = ammo,
        AmmoWillBeLoadedToChamber = weapon.HasChambers ? ammo : null,
        AmmoCountInChamberBeforeShot = weapon.HasChambers ? weapon.ChamberAmmoCount : 1,
        AmmoCountInChamberAfterShot = weapon.ChamberAmmoCount,
        AmmoCountInMagBeforeShot = weapon.GetCurrentMagazineCount(),
        AmmoCountInMagAfterShot = weapon.GetCurrentMagazineCount(),
        IsAmmoCompatible = weapon.GetCurrentMagazine()?.IsAmmoCompatible(weapon.Chambers) ?? true
    };

    internal static bool IsRetained(Weapon weapon, Ammo? ammo)
    {
        if (ammo is null || ammo.IsUsed) return false;
        foreach (var chamber in weapon.Chambers)
            if (ReferenceEquals(chamber.ContainedItem, ammo)) return true;
        var magazine = weapon.GetCurrentMagazine();
        if (magazine is CylinderMagazine cylinder)
            foreach (var chamber in cylinder.Camoras)
                if (ReferenceEquals(chamber.ContainedItem, ammo)) return true;
        return !weapon.HasChambers && ReferenceEquals(magazine?.Cartridges.Last, ammo);
    }

    internal static void RemoveFiredRound(Slot slot, bool simulate, Player.FirearmController.FirearmOperation operation)
    { if (!Enabled(operation)) slot.RemoveItem(simulate); }

    internal static void MarkFiredRound(Ammo ammo, bool used, Player.FirearmController.FirearmOperation operation)
    { if (!Enabled(operation)) ammo.IsUsed = used; }
}

[HarmonyPatch(typeof(Player.FirearmController.FireOperationBase), "ProcessInventoryOperationsForSingleShot")]
internal static class EyewearRetainCartridge
{
    private static bool Prefix(Player.FirearmController.FireOperationBase __instance, Weapon.EMalfunctionState __0,
        ref Player.FirearmController.FireOperationBase.SingleShotData __result)
    {
        // Selection, inspection and repair of malfunctions remain native.
        // Only the actual cartridge inventory transaction is replaced.
        if (!EyewearAmmo.Enabled(__instance)) return true;
        var weapon = __instance.Weapon;
        var ammo = weapon.HasChambers ? weapon.Chambers[0].ContainedItem as Ammo :
            weapon.GetCurrentMagazine()?.Cartridges.Last as Ammo;
        if (ammo is null || ammo.IsUsed) return true;
        __result = EyewearAmmo.Retained(weapon, ammo);
        if (__0 == Weapon.EMalfunctionState.Feed) __result.FedAmmo = ammo;
        if (__0 != Weapon.EMalfunctionState.None) EyewearAmmo.RememberFault(ammo);
        __instance.UncheckOnShot();
        return false;
    }
}

[HarmonyPatch(typeof(Player.FirearmController.FireOnlyBarrelFireOperation), "SingleShot")]
internal static class EyewearRetainBarrelCartridge
{
    private static bool Prefix(Player.FirearmController.FireOnlyBarrelFireOperation __instance, int __0)
    {
        if (!EyewearAmmo.Enabled(__instance)) return true;
        var weapon = __instance.Weapon;
        if (weapon.Chambers[__0].ContainedItem is not Ammo ammo || ammo.IsUsed) return false;
        __instance._chambersForFire.Add(__0);
        __instance._singleShotsData.Add(EyewearAmmo.Retained(weapon, ammo));
        return false;
    }
    // The native visual bookkeeping installs spent-shell metadata. A retained
    // live round must not subsequently be treated as a spent casing on reload.
}

[HarmonyPatch(typeof(Player.FirearmController.FireOnlyBarrelFireOperation), nameof(Player.FirearmController.FireOnlyBarrelFireOperation.OnFireEvent))]
internal static class EyewearRetainedBarrelVisuals
{
    private static void Postfix(Player.FirearmController.FireOnlyBarrelFireOperation __instance)
    {
        if (!EyewearAmmo.Enabled(__instance)) return;
        var weapon = __instance.Weapon;
        foreach (var index in __instance._chambersForFire)
            if (weapon.Chambers[index].ContainedItem is Ammo { IsUsed: false } ammo)
            {
                weapon.ShellsInChambers[index] = null!;
                __instance.Firearms.SetRoundIntoWeapon(ammo, index);
            }
        __instance.FirearmsAnimator.SetShellsInWeapon(weapon.ShellsInWeaponCount);
    }
}

// Native repair may throw a failed cartridge into the world. A round retained
// by 3D Printer is still in its chamber/magazine; never spawn that same item as
// world loot. Checking actual identity also protects a repair after unequipping.
[HarmonyPatch(typeof(Player.FirearmController.RepairMalfunction), nameof(Player.FirearmController.RepairMalfunction.OnShellEjectEvent))]
internal static class EyewearRetainedMalfunction
{
    private static bool Prefix(Player.FirearmController.RepairMalfunction __instance)
    {
        if (!EyewearAmmo.IsRetainedFault(__instance.Weapon, __instance.Weapon.MalfState.MalfunctionedAmmo)) return true;
        __instance._shellEjected = true;
        return false;
    }
}

[HarmonyPatch(typeof(Player.FirearmController.RepairMalfunction), nameof(Player.FirearmController.RepairMalfunction.OnMalfunctionOffEvent))]
internal static class EyewearRetainedMalfunctionFinished
{
    private static void Prefix(Player.FirearmController.RepairMalfunction __instance, out Ammo? __state) =>
        __state = __instance.Weapon.MalfState.MalfunctionedAmmo;
    private static void Postfix(Ammo? __state) => EyewearAmmo.ForgetFault(__state);
}

// Launchers and flare guns bypass the regular cartridge preparation method.
// Change only their consumption statements; retain native shot/animation logic.
[HarmonyPatch]
internal static class EyewearRetainLauncherCartridge
{
    private static readonly FieldInfo UnderbarrelManager = AccessTools.Field(typeof(Player.FirearmController), "_underbarrelContainer");
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(Player.FirearmController.LauncherFire), "OnFireEvent"),
        AccessTools.Method(typeof(Player.FirearmController.RocketLauncherFire), "OnFireEvent"),
        AccessTools.Method(typeof(Player.FirearmController.FlareGunFire), "OnFireEvent")
    };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var remove = AccessTools.Method(typeof(Slot), nameof(Slot.RemoveItem), new[] { typeof(bool) });
        var used = AccessTools.Field(typeof(Ammo), nameof(Ammo.IsUsed));
        var removals = 0; var marks = 0;
        for (var i = 0; i < code.Count; i++)
        {
            var instruction = code[i];
            if (instruction.Calls(remove) && i + 1 < code.Count && code[i + 1].opcode == OpCodes.Pop)
            {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner);
                owner.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return owner;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(EyewearAmmo), nameof(EyewearAmmo.RemoveFiredRound));
                code[i + 1].MoveLabelsTo(instruction);
                instruction.blocks.AddRange(code[i + 1].blocks);
                yield return instruction; i++; removals++;
            }
            else if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, used))
            {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner);
                owner.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return owner;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(EyewearAmmo), nameof(EyewearAmmo.MarkFiredRound));
                yield return instruction; marks++;
            }
            else yield return instruction;
        }
        if (removals != 1 || marks != 1) throw new InvalidOperationException("Unsupported native launcher cartridge-consumption shape.");
    }
    private static void Postfix(Player.FirearmController.FirearmOperation __instance)
    {
        if (!EyewearAmmo.Enabled(__instance)) return;
        if (__instance is Player.FirearmController.LauncherFire)
        {
            var launcher = __instance.Controller.UnderbarrelWeapon;
            if (launcher?.Chamber.ContainedItem is not Ammo { IsUsed: false } ammo) return;
            launcher.ShellsInChambers[0] = null!;
            (UnderbarrelManager.GetValue(__instance.Controller) as Player.FirearmController.UnderbarrelContainer)?.SetRoundIntoWeapon(ammo);
        }
        else
        {
            var weapon = __instance.Weapon;
            if (weapon.FirstLoadedChamberSlot?.ContainedItem is not Ammo { IsUsed: false } ammo) return;
            weapon.ShellsInChambers[0] = null!;
            __instance.Firearms.SetRoundIntoWeapon(ammo, 0);
        }
        __instance.FirearmsAnimator.SetAmmoInChamber(1);
        __instance.FirearmsAnimator.SetShellsInWeapon(0);
    }
}

[HarmonyPatch(typeof(Player.FirearmController.FireCylinderMagOperation), nameof(Player.FirearmController.FireCylinderMagOperation.PrepareShot))]
internal static class EyewearRetainRevolverCartridge
{
    private static bool Prefix(Player.FirearmController.FireCylinderMagOperation __instance)
    {
        if (!EyewearAmmo.Enabled(__instance)) return true;
        var weapon = __instance.Weapon;
        var cylinder = __instance._cylinderMagazine;
        var ammo = cylinder.GetFirstAmmo(!weapon.CylinderHammerClosed);
        if (ammo is null || ammo.IsUsed) return true;
        __instance._singleShotData = EyewearAmmo.Retained(weapon, ammo);
        __instance.Player.InventoryController.CheckChamber(weapon, false);
        weapon.ShellsInChambers[__instance._camoraIndex] = null!;
        if (weapon.CylinderHammerClosed || weapon.FireMode.FireMode != (Weapon.EFireMode)4) cylinder.IncrementCamoraIndex(false);
        __instance.FirearmsAnimator.SetCamoraIndex(cylinder.CurrentCamoraIndex);
        __instance.FirearmsAnimator.SetFire(__instance.Controller.IsTriggerPressed);
        __instance.ShowIncompatibleNotification();
        __instance.Player.MouseLook(false);
        return false;
    }
}
