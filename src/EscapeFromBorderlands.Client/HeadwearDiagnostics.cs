using System;
using System.Collections.Generic;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Temporary, local-player-only diagnostics. Never changes effect or HUD state.
internal static class HeadwearDiagnostics
{
    private static int Count;
    private static readonly Dictionary<string, string> Stages = new();
    internal static void Reset() { Count = 0; Stages.Clear(); }
    private static bool Target(Headwear.State? s) => false;
    private static void Write(string message)
    {
        if (Count >= 120) return;
        Plugin.LogSource.LogInfo($"EFB MiddleManagement trace t={Time.time:F2}: {message}");
        if (++Count == 120) Plugin.LogSource.LogInfo("EFB MiddleManagement trace: raid limit reached (120 entries).");
    }
    internal static void Damage(Headwear.State? s, DamageInfo hit, string stage, float amount)
    {
        if (!Target(s) || Count >= 120) return;
        Write($"{stage}; type={hit.DamageType}; amount={amount:F2}; attackerPresent={hit.Player?.iPlayer is Player}; firearm={hit.Weapon is Weapon}; active={s!.Active}; ready={s.Ready}; buffLeft={Math.Max(0, s.Until - Time.time):F2}; cooldownLeft={Math.Max(0, s.Cooldown - Time.time):F2}");
    }
    internal static void Rows(List<EffectStatusModel.Status> statuses)
    {
        if (Count >= 120 || !Target(Headwear.Get(Headwear.Local))) return;
        var row = statuses.FindIndex(static status => status.Key == "HW019-1");
        Hud("row", row < 0 ? "absent after filtering" : statuses[row].Active ? "active" : "cooldown");
    }
    internal static void Hud(string stage, string value)
    {
        if (Count >= 120 || !Target(Headwear.Get(Headwear.Local))) return;
        if (Stages.TryGetValue(stage, out var previous) && previous == value) return;
        Stages[stage] = value;
        Write($"HUD {stage}: {value}");
    }
}
