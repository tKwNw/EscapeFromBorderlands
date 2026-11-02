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

// Rewrite the allocation in the real asynchronous loading/unloading operation.
// A constructor detour can be bypassed when Mono inlines the constructor.
[HarmonyPatch]
internal static class BackpackMagazineSpeedPatch
{
    private static readonly Type LoadType = typeof(Player.PlayerInventoryController.Class1204);
    private static readonly Type UnloadType = typeof(Player.PlayerInventoryController.Class1207);
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var targets = typeof(Player.PlayerInventoryController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "LoadMagazine" or "UnloadMagazine")
            .Select(m => m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType)
            .Where(t => t is not null).Select(t => (MethodBase)AccessTools.DeclaredMethod(t!, "MoveNext")).ToArray();
        if (targets.Length != 2) throw new InvalidOperationException($"Expected both native magazine operations; found {targets.Length}.");
        return targets!;
    }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructor)
            {
                var factory = constructor.DeclaringType == LoadType ? nameof(Load) : constructor.DeclaringType == UnloadType ? nameof(Unload) : null;
                if (factory is not null)
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(BackpackMagazineSpeedPatch), factory); replacements++; }
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException($"Expected one magazine process allocation in {original.DeclaringType?.Name}.{original.Name}; found {replacements}.");
    }
    internal static Player.PlayerInventoryController.Class1204 Load(InventoryController inventory, MagazineItemClass magazine, AmmoItemClass ammo, int count, bool elite, float seconds)
    {
        var factor = GrenadeBuffs.Packing(BackpackRuntime.Actor(inventory));
        return GrenadePackingSpeed.Track(new Player.PlayerInventoryController.Class1204(inventory, magazine, ammo, count, elite,
            ChestRigInventory.LoadTime(inventory, BackpackInventoryAccess.MagazineTime(inventory, seconds), false) / factor), factor * ServiceWindow.Speed(BackpackRuntime.Actor(inventory)));
    }
    internal static Player.PlayerInventoryController.Class1207 Unload(InventoryController inventory, MagazineItemClass magazine, float seconds, bool elite)
    {
        var factor = GrenadeBuffs.Packing(BackpackRuntime.Actor(inventory));
        return GrenadePackingSpeed.Track(new Player.PlayerInventoryController.Class1207(inventory, magazine,
            ChestRigInventory.LoadTime(inventory, BackpackInventoryAccess.MagazineTime(inventory, seconds), true) / factor, elite), factor * ServiceWindow.Speed(BackpackRuntime.Actor(inventory)));
    }
}
