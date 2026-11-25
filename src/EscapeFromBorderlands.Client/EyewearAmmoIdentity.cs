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

// Native impact identity is cartridge.Id + fragment index. A retained round
// therefore needs a distinct ballistic identity per actual firing, without
// changing the inventory item or manufacturing replacement ammunition.
internal static class EyewearAmmoIdentity
{
    internal sealed class Identity { internal string Value = ""; internal AmmoItemClass Ammo = null!; }
    internal struct Scope { internal Identity? Previous; internal bool Entered; internal int Generation; }
    [ThreadStatic] private static Identity? Current;
    private static ConditionalWeakTable<EftBulletClass, Identity> Bullets = new();
    private static int LiveCount;
    private static long Sequence;
    private static int Generation;

    internal static void Reset()
    { Current = null; Bullets = new(); LiveCount = 0; Sequence = 0; unchecked { Generation++; } }
    internal static Scope Begin(Player? player, AmmoItemClass ammo)
    {
        if (Eyewear.Get(player)?.Key != "EW022") return default;
        var scope = new Scope { Previous = Current, Entered = true, Generation = Generation };
        Current = new Identity { Ammo = ammo, Value = ammo.Id + ":efb:" + Generation.ToString("x") + ":" + (++Sequence).ToString("x") };
        return scope;
    }
    internal static void End(Scope scope) { if (scope.Entered && scope.Generation == Generation) Current = scope.Previous; }
    internal static void Created(EftBulletClass shot, EftBulletClass? parent)
    {
        if (LiveCount == 0 && Current is null) return;
        if (Bullets.Remove(shot)) LiveCount--; // The native pool may reuse this exact object.
        Identity? identity = null;
        if (parent is not null) Bullets.TryGetValue(parent, out identity);
        if (identity is null && Current is { } current && ReferenceEquals(shot.Ammo, current.Ammo)) identity = current;
        if (identity is null) return;
        Bullets.Add(shot, identity);
        LiveCount++;
    }
    internal static void Released(EftBulletClass shot) { if (LiveCount != 0 && Bullets.Remove(shot)) LiveCount--; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string NativeId(EftBulletClass shot) => LiveCount != 0 && Bullets.TryGetValue(shot, out var identity)
        ? identity.Value : shot.Ammo.Id;
}

// InitiateShot is shared by ordinary firearms and underbarrel ammunition.
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.InitiateShot))]
internal static class EyewearRetainedShotScope
{
    private static void Prefix(Player? ____player, AmmoItemClass ammo, out EyewearAmmoIdentity.Scope __state) =>
        __state = EyewearAmmoIdentity.Begin(____player, ammo);
    private static Exception? Finalizer(EyewearAmmoIdentity.Scope __state, Exception? __exception)
    { EyewearAmmoIdentity.End(__state); return __exception; }
}

[HarmonyPatch(typeof(EftBulletClass), nameof(EftBulletClass.Create))]
internal static class EyewearRetainedProjectileIdentity
{
    private static void Postfix(EftBulletClass __result, EftBulletClass? __23) => EyewearAmmoIdentity.Created(__result, __23);
}

[HarmonyPatch(typeof(EftBulletClass), nameof(EftBulletClass.Release))]
internal static class EyewearRetainedProjectileRelease
{
    private static void Prefix(EftBulletClass __0) => EyewearAmmoIdentity.Released(__0);
}

[HarmonyPatch]
internal static class EyewearNativeShotIdentity
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.DeclaredMethod(typeof(GameWorld), "ShotDelegate"),
        AccessTools.DeclaredMethod(typeof(ClientGameWorld), "ShotDelegate")
    };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var ammo = AccessTools.Field(typeof(EftBulletClass), nameof(EftBulletClass.Ammo));
        var id = AccessTools.PropertyGetter(typeof(Item), nameof(Item.Id));
        var changed = 0;
        for (var i = 0; i < code.Count; i++)
        {
            var instruction = code[i];
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, ammo) && i + 1 < code.Count && code[i + 1].Calls(id))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(EyewearAmmoIdentity), nameof(EyewearAmmoIdentity.NativeId));
                code[i + 1].MoveLabelsTo(instruction);
                instruction.blocks.AddRange(code[i + 1].blocks);
                i++; changed++;
            }
            yield return instruction;
        }
        if (changed != 1) throw new InvalidOperationException("Unsupported native ballistic identity shape.");
    }
}
