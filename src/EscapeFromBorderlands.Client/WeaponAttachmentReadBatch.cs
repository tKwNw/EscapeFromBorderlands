using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

// EFT's Mods is lazy: enumerating it walks the equipped weapon's slots.
// Share that traversal for our three adjacent change-detection reads. Each
// native getter still runs its own arithmetic and all its usual postfixes.
[HarmonyPatch]
internal static class WeaponAttachmentReadBatch
{
    private readonly struct NativeGetter
    {
        internal readonly string Name;
        internal readonly MethodInfo? Method;
        internal readonly string? BindError;

        internal NativeGetter(Type declaringType, string property, Type result)
        {
            Name = declaringType.Name + "." + property;
            Method = null;
            BindError = null;
            try
            {
                // Weapon and Mod both hide Item.Template with a different
                // return type. Select the native declaration and signature.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                Method = declaringType.GetProperty(property, flags, null, result, Type.EmptyTypes, null)?.GetGetMethod(true);
            }
            catch (Exception error) { BindError = error.GetType().Name; }
        }
    }

    private static readonly NativeGetter[] StatGetters =
    {
        new(typeof(Weapon), nameof(Weapon.RecoilDelta), typeof(float)),
        new(typeof(Weapon), nameof(Weapon.ErgonomicsDelta), typeof(float)),
        new(typeof(Weapon), nameof(Weapon.CenterOfImpactDelta), typeof(float))
    };
    private static readonly NativeGetter ModsGetter = new(typeof(Weapon), nameof(Weapon.Mods), typeof(IEnumerable<Mod>));
    private static readonly NativeGetter[] InputGetters =
    {
        ModsGetter,
        new(typeof(Weapon), "AllSlots", typeof(IEnumerable<Slot>)),
        new(typeof(CompoundItem), "AllSlotsWithoutArrayInside", typeof(IEnumerable<Slot>)),
        new(typeof(ContainerCollection), "Containers", typeof(IEnumerable<IContainer>)),
        new(typeof(CompoundItem), "Containers", typeof(IEnumerable<IContainer>)),
        new(typeof(Slot), nameof(Slot.ContainedItem), typeof(Item)),
        new(typeof(Slot), "Items", typeof(IEnumerable<Item>)),
        new(typeof(Weapon), nameof(Weapon.Folded), typeof(bool)),
        new(typeof(Weapon), nameof(Weapon.Template), typeof(WeaponTemplate)),
        new(typeof(Mod), nameof(Mod.Template), typeof(ModTemplate)),
        new(typeof(Mod), nameof(Mod.Accuracy), typeof(int))
    };
    private const BindingFlags LocalMethod = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo BatchedGetter = typeof(WeaponAttachmentReadBatch).GetMethod(nameof(Read), LocalMethod)!;
    private static readonly Assembly NativeAssembly = typeof(Weapon).Assembly;
    private static int Compatibility; // Recheck after all plugins are loaded, once per raid.
    private static string? Fallback;
    [ThreadStatic] private static Weapon? PollWeapon;
    [ThreadStatic] private static int Depth;
    [ThreadStatic] private static List<Mod>? Buffer;
    [ThreadStatic] private static bool Ready, Filling;

    private static bool Prepare()
    {
        if (ModsGetter.Method is null) return false;
        foreach (var getter in StatGetters)
            if (getter.Method is null) return false;
        return true;
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var getter in StatGetters)
            if (getter.Method is { } method) yield return method;
    }

    private static bool CanUse(in NativeGetter getter, bool requireOurTranspiler)
    {
        if (getter.Method is not { } method)
        {
            Fallback = getter.Name + " exact getter unavailable" +
                (getter.BindError is null ? "" : " (" + getter.BindError + ")");
            return false;
        }
        try
        {
            if (Harmony.GetPatchInfo(method) is { } patches)
            {
                foreach (var owner in patches.Owners)
                    if (!string.Equals(owner, Plugin.PluginGuid, StringComparison.Ordinal))
                    {
                        Fallback = $"{getter.Name} patched by {owner}";
                        return false;
                    }
                if (requireOurTranspiler)
                    foreach (var patch in patches.Transpilers)
                        if (patch.owner == Plugin.PluginGuid && patch.PatchMethod is { } installed &&
                            installed.DeclaringType == typeof(WeaponAttachmentReadBatch) && installed.Name == nameof(Transpiler)) return true;
            }
            if (!requireOurTranspiler) return true;
            Fallback = getter.Name + " attachment hook was not installed";
            return false;
        }
        catch (Exception error)
        {
            Fallback = $"could not inspect {getter.Name} patches ({error.GetType().Name})";
            return false;
        }
    }

    private static bool CanShare()
    {
        if (Compatibility != 0) return Compatibility > 0;
        foreach (var getter in StatGetters)
            if (!CanUse(getter, requireOurTranspiler: true)) { Compatibility = -1; return false; }
        foreach (var getter in InputGetters)
            if (!CanUse(getter, requireOurTranspiler: false)) { Compatibility = -1; return false; }
        Compatibility = 1;
        return true;
    }

    internal static void ResetCompatibility()
    {
        var reason = Fallback;
        Fallback = null;
        Compatibility = 0;
        if (reason is not null) Plugin.LogSource?.LogInfo("EFB firearm batching used the normal getters: " + reason);
    }

    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (ModsGetter.Method is { } native && instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = BatchedGetter;
            }
            yield return instruction;
        }
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly bool Outer;

        internal Scope(Weapon weapon, bool enabled)
        {
            Outer = Depth == 0;
            if (Outer && enabled && weapon.GetType().Assembly == NativeAssembly && CanShare()) PollWeapon = weapon;
            Depth++;
        }

        internal bool Used => Outer && Ready;

        public void Dispose()
        {
            if (Outer && PollWeapon is not null)
            {
                PollWeapon = null;
                Ready = false;
                Buffer?.Clear();
            }
            Depth--;
        }
    }

    internal static bool IsPolling(Weapon weapon) =>
        Depth == 1 && ReferenceEquals(PollWeapon, weapon);

    private static IEnumerable<Mod> Read(Weapon weapon)
    {
        if (!IsPolling(weapon) || Filling) return weapon.Mods;
        if (Ready) return Buffer!;

        var buffer = Buffer ??= new List<Mod>(16);
        Filling = true;
        try
        {
            buffer.AddRange(weapon.Mods);
            Ready = true;
            return buffer;
        }
        catch
        {
            buffer.Clear();
            throw;
        }
        finally { Filling = false; }
    }
}
