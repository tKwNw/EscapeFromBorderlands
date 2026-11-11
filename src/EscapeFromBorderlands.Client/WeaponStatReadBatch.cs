using System;
using EFT;
using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

// All getters in one synchronous read see the same held weapon and effect
// preview. Never keep a preview between updates or use it for a real shot.
internal static class WeaponStatReadBatch
{
    private struct State
    {
        internal Weapon? Weapon;
        internal bool HasRoll, HasOwner, HasDefinition, HasStatRig, HasStatEarpiece;
        internal VariantDefinition? Definition;
        internal RarityResolver.ShotRoll? Roll;
        internal Player? Owner, ControllerOwner;
        internal WeaponLiveStats? Application;
        internal ChestRigRuntime.State? Rig;
        internal ChestRigRuntime.StatFactors? RigStats;
        internal ChestRigRuntime.State? StatRig;
        internal EarpieceRuntime.State? StatEarpiece;
    }

    [ThreadStatic] private static State _current;

    internal readonly struct Scope : IDisposable
    {
        private readonly State _previous;

        internal Scope(Weapon weapon, Player? controllerOwner = null)
        {
            _previous = _current;
            _current = new State { Weapon = weapon, ControllerOwner = controllerOwner };
        }

        public void Dispose() => _current = _previous;
        internal Scope(Weapon weapon, Player? controllerOwner, VariantDefinition? definition)
        {
            _previous = _current;
            _current = new State { Weapon = weapon, ControllerOwner = controllerOwner, Definition = definition, HasDefinition = true };
        }
    }

    internal static RarityResolver.ShotRoll? ResolveRoll(Weapon weapon)
    {
        if (!ReferenceEquals(_current.Weapon, weapon)) return RarityResolver.Resolve(weapon);
        if (!_current.HasRoll)
        {
            _current.Roll = Definition(weapon) is { } definition ? RarityResolver.Resolve(weapon, definition) : null;
            _current.HasRoll = true;
        }
        return _current.Roll;
    }
    internal static VariantDefinition? Definition(Weapon weapon)
    {
        if (!IsReading(weapon)) return RarityResolver.TryGetDefinition(weapon, out var outside) ? outside : null;
        if (!_current.HasDefinition)
        {
            RarityResolver.TryGetDefinition(weapon, out _current.Definition!);
            _current.HasDefinition = true;
        }
        return _current.Definition;
    }

    internal static Player? ResolveOwner(Weapon weapon)
    {
        if (!ReferenceEquals(_current.Weapon, weapon)) return BackpackNative.FindWeaponOwner(weapon);
        if (!_current.HasOwner)
        {
            _current.Owner = BackpackNative.FindWeaponOwner(weapon, _current.ControllerOwner);
            _current.HasOwner = true;
        }
        return _current.Owner;
    }

    internal static bool IsReading(Weapon weapon) => ReferenceEquals(_current.Weapon, weapon);
    internal static ChestRigRuntime.State? ResolveStatRig(Weapon weapon)
    {
        if (!IsReading(weapon) || !WeaponAttachmentReadBatch.IsPolling(weapon))
            return ChestRigRuntime.Get(ResolveOwner(weapon));
        if (!_current.HasStatRig)
        {
            _current.StatRig = ChestRigRuntime.Get(ResolveOwner(weapon));
            _current.HasStatRig = true;
        }
        return _current.StatRig;
    }
    internal static EarpieceRuntime.State? ResolveStatEarpiece(Weapon weapon)
    {
        if (!IsReading(weapon) || !WeaponAttachmentReadBatch.IsPolling(weapon))
            return EarpieceRuntime.Get(ResolveOwner(weapon));
        if (!_current.HasStatEarpiece)
        {
            _current.StatEarpiece = EarpieceRuntime.Get(ResolveOwner(weapon));
            _current.HasStatEarpiece = true;
        }
        return _current.StatEarpiece;
    }
    internal static WeaponLiveStats? Preview => _current.Application;
    internal static void RememberPreview(Weapon weapon, WeaponLiveStats application)
    {
        if (IsReading(weapon)) _current.Application = application;
    }
    internal static bool TryRigStats(Weapon weapon, ChestRigRuntime.State rig, out ChestRigRuntime.StatFactors stats)
    {
        if (IsReading(weapon) && ReferenceEquals(rig, _current.Rig) && _current.RigStats is { } cached)
        { stats = cached; return true; }
        stats = default;
        return false;
    }
    internal static void RememberRigStats(Weapon weapon, ChestRigRuntime.State rig, ChestRigRuntime.StatFactors stats)
    {
        if (!IsReading(weapon)) return;
        _current.Rig = rig;
        _current.RigStats = stats;
    }
}
