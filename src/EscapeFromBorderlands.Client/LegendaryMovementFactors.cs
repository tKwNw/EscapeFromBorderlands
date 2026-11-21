using EFT;

namespace EscapeFromBorderlands.Client;

internal static class LegendaryMovementFactors
{
    internal static float Read(Player player)
    {
        using var equipment = new EquipmentPresence.ReadScope(player);
        DiagnosticPathTrace trace = default;
        DiagnosticPathTrace.Start(ref trace, player.IsAI ? DiagnosticPathTrace.Path.BotMovement : DiagnosticPathTrace.Path.HumanMovement);
        // One presence observation for the three runtimes that use this cache.
        // Existing states still enter their normal cleanup path after removal.
        var factor = MeleeRuntime.HasMovementWork(player) ? AdvancedMeleeRuntime.Movement(player) : 1f;
        trace.Phase(0);
        var gear = EquipmentPresence.Kinds(player);
        var revision = EquipmentPresence.Revision;
        trace.Phase(1);
        factor *= (gear & GearKind.Backpack) != 0 || (BackpackRuntime.States.Count != 0 && BackpackRuntime.States.ContainsKey(player)) ? BackpackRuntime.MotionFactor(player) : 1f;
        trace.Phase(2);
        if (revision != EquipmentPresence.Revision) { gear = EquipmentPresence.Kinds(player); revision = EquipmentPresence.Revision; }
        factor *= (gear & GearKind.Rig) != 0 || (ChestRigRuntime.States.Count != 0 && ChestRigRuntime.States.ContainsKey(player)) ? ChestRigRuntime.Movement(player) : 1f;
        trace.Phase(3);
        // Buffs and smoke can outlive the equipped grenade; never gate on gear.
        factor *= GrenadeBuffs.HasMovementWork ? GrenadeBuffs.Movement(player) : 1f;
        trace.Phase(4);
        if (revision != EquipmentPresence.Revision) gear = EquipmentPresence.Kinds(player);
        factor *= (gear & GearKind.Earpiece) != 0 || (EarpieceRuntime.States.Count != 0 && EarpieceRuntime.States.ContainsKey(player)) ? EarpieceRuntime.Movement(player) : 1f;
        trace.Phase(5);
        var hat = Headwear.Get(player);
        factor *= hat is not null && HeadwearWork.Movement(hat.Key) ? Headwear.Factor(hat, HatStat.Move) : 1f;
        trace.Phase(6);
        return factor;
    }
}
