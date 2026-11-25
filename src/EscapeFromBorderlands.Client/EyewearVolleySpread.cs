using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Eyewear
{
    internal readonly struct ProjectileDirectionScope
    {
        internal readonly ShotScope? Shot;
        internal readonly Quaternion Previous;
        internal readonly bool Known;
        internal readonly int Generation;
        internal ProjectileDirectionScope(ShotScope shot)
        { Shot = shot; Previous = shot.PelletRotation; Known = shot.PelletBasisKnown; Generation = CombatGeneration; }
    }
    internal static Vector3 CaptureWeaponSpread(Vector3 centerTimes100, float coefficient)
    {
        // Exactly the native calculation and first RNG draw. Only Grand Vizier
        // takes a second draw, using the same live accuracy/stance/heat factors.
        var original = centerTimes100 + coefficient * UnityEngine.Random.insideUnitSphere;
        if (CurrentShot is { Double: true } scope)
        {
            scope.DoubleCenter = (centerTimes100 + coefficient * UnityEngine.Random.insideUnitSphere).normalized;
            scope.DoubleCenterKnown = true;
        }
        return original;
    }
    internal static void SetShotCenter(Weapon weapon, Vector3 direction)
    {
        if (CurrentShot is not { Double: true } scope || !ReferenceEquals(scope.Weapon, weapon)) return;
        scope.OriginalCenter = direction.normalized;
        // Native zero-dispersion firing skips the random branch. Its second
        // shell is also exact; never invent an artificial spread floor.
        if (!scope.DoubleCenterKnown) scope.DoubleCenter = scope.OriginalCenter;
    }
    internal static ProjectileDirectionScope BeginProjectileDirection(BallisticsCalculator calculator, EftBulletClass shot)
    {
        if (CurrentShot is not { Double: true } scope || !ReferenceEquals(shot.Weapon, scope.Weapon)) return default;
        var previous = new ProjectileDirectionScope(scope);
        var direction = scope.DoubleCenter.sqrMagnitude > 0 ? scope.DoubleCenter : shot.Direction;
        if (shot.Ammo is AmmoItemClass ammo && ammo.ProjectileCount > 1)
            direction = (direction * 100 + ammo.buckshotDispersion * calculator.Randoms.GetRandomDirection(UnityEngine.Random.Range(0, 512))).normalized;
        // Every final legendary direction in this pellet's generation is
        // transferred into the independently sampled companion pellet basis.
        // Copying final stats avoids rerunning ammo, recoil or legendary triggers.
        scope.PelletRotation = Quaternion.FromToRotation(shot.Direction, direction);
        scope.PelletBasisKnown = true;
        return previous;
    }
    private static Vector3 DoubleDirection(ShotScope scope, Vector3 original) =>
        scope.PelletBasisKnown ? scope.PelletRotation * original : scope.DoubleCenter.sqrMagnitude > 0 ? scope.DoubleCenter : original;
    internal static void EndProjectileDirection(ProjectileDirectionScope previous)
    {
        if (previous.Shot is not { } shot || previous.Generation != CombatGeneration) return;
        shot.PelletRotation = previous.Previous; shot.PelletBasisKnown = previous.Known;
    }
}

internal static class EyewearVolleySpread
{
    // Harmony's Label is built against the game's Mono mscorlib. Inspect the
    // collection through IList to avoid binding its generic Label type from
    // this netstandard assembly (same compatibility route as stamina hooks).
    private static readonly System.Reflection.FieldInfo Labels = typeof(CodeInstruction).GetField("labels")!;
    private static bool HasLabels(CodeInstruction instruction) => Labels.GetValue(instruction) is System.Collections.IList { Count: > 0 };
    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var random = AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.insideUnitSphere));
        var multiply = AccessTools.Method(typeof(Vector3), "op_Multiply", new[] { typeof(float), typeof(Vector3) });
        var add = AccessTools.Method(typeof(Vector3), "op_Addition", new[] { typeof(Vector3), typeof(Vector3) });
        var replacement = AccessTools.Method(typeof(Eyewear), nameof(Eyewear.CaptureWeaponSpread));
        var matches = 0;
        for (var i = 0; i < code.Count; i++)
        {
            if (i + 2 < code.Count && code[i].Calls(random) && code[i + 1].Calls(multiply) && code[i + 2].Calls(add))
            {
                if (HasLabels(code[i + 1]) || HasLabels(code[i + 2]) || code[i + 1].blocks.Count != 0 || code[i + 2].blocks.Count != 0)
                    throw new InvalidOperationException("Unsupported branched native weapon-spread expression.");
                var instruction = new CodeInstruction(OpCodes.Call, replacement);
                code[i].MoveLabelsTo(instruction); instruction.blocks.AddRange(code[i].blocks);
                yield return instruction;
                i += 2; matches++;
            }
            else yield return code[i];
        }
        if (matches != 1) throw new InvalidOperationException("Unsupported native weapon-spread expression for Grand Vizier.");
    }
}
