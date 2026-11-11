using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class BackpackVisitorExtraction
{
    internal const string Key = "BP25-2";
    private static readonly FieldInfo EntriesField = AccessTools.Field(typeof(EndByExitTrigerScenario), "_escapes");
    private static readonly FieldInfo PendingField = AccessTools.Field(typeof(EndByExitTrigerScenario), "list_1");
    private static readonly FieldInfo SharedField = AccessTools.Field(typeof(EndByExitTrigerScenario), "_monitoredEP");
    private static AbstractGame? _game;
    private static ExfiltrationPoint? _point;
    private static bool _wearing, _usedPass;
    private static readonly List<ExfiltrationPoint> SharedSnapshot = new();

    internal static float Duration(float seconds, Player? player) => BackpackRuntime.Is(player, Key) ? seconds * .5f : seconds;
    internal static ExitStatus Outcome(ExitStatus status, Player? player) =>
        status == ExitStatus.Runner && BackpackRuntime.Is(player, Key) ? ExitStatus.Survived : status;

    internal static void QueueShared(EndByExitTrigerScenario scenario)
    {
        // Shared exits have a single world clock. Queue just the wearer at
        // their earlier deadline; do not shorten the point's settings or end
        // the shared timer for other players.
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (!BackpackRuntime.Is(player, Key) || player is null ||
            Native.Get(scenario, "_game") is not EndByExitTrigerScenario.IGame game ||
            game.Status is not (GameStatus.Started or GameStatus.SoftStopping)) return;
        if (PendingField.GetValue(scenario) is not List<(Player, string, IEnumerable<IExfiltrationRequirement>)> pending ||
            SharedField.GetValue(scenario) is not List<ExfiltrationPoint> shared) return;
        if (shared.Count == 0) return;
        // Native requirement callbacks can mutate the monitored list; retain
        // the snapshot and fall back to a private one on a nested callback.
        var snapshot = SharedSnapshot.Count == 0 ? SharedSnapshot : new List<ExfiltrationPoint>();
        snapshot.AddRange(shared);
        try
        {
            foreach (var point in snapshot)
            {
                var elapsed = game.PastTime - point.ExfiltrationStartTime;
                var normal = point.Settings.ExfiltrationTime;
                if (point.Status != EExfiltrationStatus.Countdown || elapsed < Duration(normal, player) || elapsed > normal ||
                    !point.Entered.Contains(player) || player.HealthController?.IsAlive != true ||
                    point.UnmetRequirements(player).Any() || pending.Any(p => p.Item1 == player)) continue;
                pending.Add((player, point.Settings.Name, point.Requirements));
            }
        }
        finally { snapshot.Clear(); }
    }

    internal static float UiDuration(float seconds, AbstractGame game, ExfiltrationPoint point)
    {
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        var wearing = BackpackRuntime.Is(player, Key);
        if (!wearing && !(_usedPass && _point == point && _game == game)) return seconds;
        var duration = Duration(seconds, player);
        // Native UI already subtracts the shared timer's elapsed time. Its
        // ordinary timer expects a remaining duration when the UI is refreshed.
        if (point.Status != EExfiltrationStatus.Countdown &&
            Native.Get(game, "_endByExitTrigerScenario") is EndByExitTrigerScenario scenario &&
            EntriesField.GetValue(scenario) is List<EndByExitTrigerScenario.LocationEscape> entries)
        {
            var entry = entries.FirstOrDefault(e => e.Player == player && e.Trigger == point);
            if (entry is not null) duration = Math.Max(0f, duration - (game.PastTime - entry.StartTime));
        }
        return duration;
    }

    internal static void ObserveUi(AbstractGame game, ExfiltrationPoint point, bool contains)
    {
        if (!contains)
        {
            if (_point == point) Reset();
            return;
        }
        if (_game != game || _point != point) _usedPass = false;
        _game = game; _point = point;
        _wearing = BackpackRuntime.Is(Singleton<GameWorld>.Instance?.MainPlayer, Key);
        _usedPass |= _wearing;
    }

    internal static void Maintain(GameWorld? world)
    {
        if (_game is null || _point is null) return;
        var player = world?.MainPlayer;
        if (player is null || player.HealthController?.IsAlive != true || player.ExfiltrationPoint != _point ||
            _game.Status is not (GameStatus.Started or GameStatus.SoftStopping)) { Reset(); return; }
        var wearing = BackpackRuntime.Is(player, Key);
        if (wearing == _wearing) return;
        _wearing = wearing; _usedPass = true;
        _game.UpdateExfiltrationUi(_point, true);
    }

    internal static void Reset() { _game = null; _point = null; _wearing = false; _usedPass = false; }
}

[HarmonyPatch(typeof(EndByExitTrigerScenario), nameof(EndByExitTrigerScenario.Update))]
internal static class BackpackVisitorCountdownPatch
{
    private static void Prefix(EndByExitTrigerScenario __instance) => BackpackVisitorExtraction.QueueShared(__instance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        var time = AccessTools.Field(typeof(ExitTriggerSettings), nameof(ExitTriggerSettings.ExfiltrationTime));
        var trigger = AccessTools.Field(typeof(EndByExitTrigerScenario.LocationEscape), nameof(EndByExitTrigerScenario.LocationEscape.Trigger));
        var player = AccessTools.Field(typeof(EndByExitTrigerScenario.LocationEscape), nameof(EndByExitTrigerScenario.LocationEscape.Player));
        var reads = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            var instruction = codes[i];
            yield return instruction;
            if (!instruction.LoadsField(time)) continue;
            reads++;
            if (reads != 1) continue;
            // The first time read belongs to this individual player's entry.
            // Leave the later shared deadline and its cleanup path intact.
            if (i < 3 || !codes[i - 2].LoadsField(trigger))
                throw new InvalidOperationException("The native individual extraction entry was unavailable.");
            // Reuse the native local-load opcode/operand. This also preserves
            // its LocalBuilder when another transpiler remaps method locals.
            yield return new CodeInstruction(codes[i - 3].opcode, codes[i - 3].operand);
            yield return new CodeInstruction(OpCodes.Ldfld, player);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BackpackVisitorExtraction), nameof(BackpackVisitorExtraction.Duration)));
        }
        if (reads != 2) throw new InvalidOperationException($"Expected two native extraction-time reads, found {reads}.");
    }
}

[HarmonyPatch(typeof(AbstractGame), nameof(AbstractGame.UpdateExfiltrationUi))]
internal static class BackpackVisitorCountdownUiPatch
{
    private static void Postfix(AbstractGame __instance, ExfiltrationPoint point, bool contains) =>
        BackpackVisitorExtraction.ObserveUi(__instance, point, contains);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var time = AccessTools.Field(typeof(ExitTriggerSettings), nameof(ExitTriggerSettings.ExfiltrationTime));
        var reads = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!instruction.LoadsField(time)) continue;
            reads++;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BackpackVisitorExtraction), nameof(BackpackVisitorExtraction.UiDuration)));
        }
        if (reads != 2) throw new InvalidOperationException($"Expected two native extraction UI time reads, found {reads}.");
    }
}

[HarmonyPatch(typeof(LocalGame), nameof(LocalGame.Stop))]
internal static class BackpackVisitorSurvivedPatch
{
    private static void Prefix(string profileId, ref ExitStatus exitStatus)
    {
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player?.ProfileId == profileId) exitStatus = BackpackVisitorExtraction.Outcome(exitStatus, player);
    }
}
