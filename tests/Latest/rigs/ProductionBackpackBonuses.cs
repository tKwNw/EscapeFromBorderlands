using System;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static partial class BackpackRuntime {
internal static float Movement(Player p)
    {
        var s = Get(p); if (s is null) return 1f;
        var sprint = p.MovementContext.IsSprintEnabled;
        switch (s.Key)
        {
            case "BP02-1": return BackpackRules.RushHour(sprint);
            case "BP07-2": return 1f;
            case "BP07-3": return sprint && BlackLeg(p) ? 1.2f : 1f;
            case "BP17-1": return s.Record.Active("fallSpeed") ? 1.15f : 1f;
            case "BP22-3": return Woods(p) ? 1.2f : 1f;
            case "BP24-1": return Daytime() ? 1.1f : 1f;
            case "BP28-3": return p.MovementContext.IsInPronePose ? 1.5f : 1f;
            case "BP31-2": return Milestone(s) ? 1.15f : 1f;
            case "BP34-1": return BackpackRules.Pilgrimage(sprint, EffectStatusModel.Remaining(s.Record.Until, "walkLockout", Time.time));
            case "BP44-3": return sprint ? 1.4f : 1.3f;
            case "BP45-2": return MainGridFull(s.Bag) ? 1.15f : 1f;
            default: return 1f;
        }
    }
internal static float MotionFactor(Player p)
    {
        return Movement(p) * (Get(p)?.Roll.Backpack.Movement ?? 1f);
    }
internal static float Value(Player p, string kind)
    {
        var s = Get(p); if (s is null) return 1f;
        if (kind is "legCapacity" or "armCapacity") return s.Roll.Backpack.StaminaCapacity * (s.Key == "BP22-3" && Woods(p) ? 1.25f : 1f) * (kind == "legCapacity" ? BackpackRules.LegCapacity(s.Key) : 1f);
        if (kind == "oxygenCapacity") return s.Key == "BP22-3" && Woods(p) ? 1.5f : 1f;
        if (kind == "legRecovery") return s.Roll.Backpack.LegRecovery;
        if (kind == "aimCost") return s.Roll.Backpack.AimCost * (s.Key == "BP30-1" && AimingLoadedPrimary(p) ? .6f : 1f);
        if (kind == "sprintCost")
        {
            var factor = s.Roll.Backpack.SprintCost;
            factor *= BackpackRules.SprintCost(s.Key);
            if (s.Key == "BP12-1" && s.Record.Active("lootSprint")) factor *= .5f;
            if (s.Key == "BP40-2" && (s.Record.Active("traversal:sprint") || TraversalFactor(p, "sprint") < 1f)) factor *= .8f;
            return factor;
        }
        return 1f;
    }
}
internal static partial class BackpackNative {
    internal static float ErgonomicsFactor(BackpackRuntime.State? s) => s is null ? 1f : s.Roll.Backpack.Ergonomics * BackpackRules.Ergonomics(s.Key, s.Key == "BP24-1" && BackpackRuntime.Daytime(), s.Key == "BP31-2" && BackpackRuntime.Milestone(s));
}
