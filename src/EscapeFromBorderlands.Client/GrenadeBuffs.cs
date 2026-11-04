using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class GrenadeBuffs
{
    private sealed class State
    {
        internal readonly Dictionary<string, (float Until, string Name)> Timers = new(StringComparer.Ordinal);
        internal bool Draw;
    }
    private static readonly Dictionary<string, State> States = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string Owner, string Target), float> Marked = new();
    private static readonly Dictionary<string, string> MarkNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<Player, (Player.FirearmController Hands, float Reload, float Hip, float Recoil)> Inputs = new();
    private static float NextTick;
    private static State Get(Player p)
    { if (!States.TryGetValue(p.ProfileId, out var s)) States[p.ProfileId] = s = new(); return s; }
    internal static void Reset() { States.Clear(); Marked.Clear(); MarkNames.Clear(); Inputs.Clear(); NextTick = 0f; }
    internal static bool HasState(Player? p) => PlayerRefresh.CanObserve(p) &&
        (States.ContainsKey(p.ProfileId) || Smoke(p, "GR58"));
    internal static void Maintain()
    {
        if (Time.time < NextTick) return;
        NextTick = Time.time + .05f;
        var world = PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance);
        if (world is null) { Reset(); return; }
        if (States.Count == 0 && Inputs.Count == 0 && Marked.Count == 0 && !LegendaryGrenadeEffects.HasRecoilSmoke) return;
        var players = world.RegisteredPlayers.OfType<Player>().Where(PlayerRefresh.CanObserve).ToArray();
        var live = players.Select(p => p.ProfileId).ToHashSet(StringComparer.Ordinal);
        foreach (var key in Marked.Where(kv => kv.Value <= Time.time || !live.Contains(kv.Key.Owner) || !live.Contains(kv.Key.Target)).Select(kv => kv.Key).ToArray()) Marked.Remove(key);
        foreach (var id in MarkNames.Keys.Where(id => !Marked.Keys.Any(k => k.Owner == id)).ToArray()) MarkNames.Remove(id);
        foreach (var id in States.Keys.ToArray())
        {
            if (!live.Contains(id)) { States.Remove(id); continue; }
            var state = States[id];
            foreach (var key in state.Timers.Where(kv => kv.Value.Until <= Time.time).Select(kv => kv.Key).ToArray()) state.Timers.Remove(key);
            if (state.Timers.Count == 0 && !state.Draw) States.Remove(id);
        }
        foreach (var player in Inputs.Keys.Where(p => !live.Contains(p.ProfileId) || !PlayerRefresh.CanObserve(p)).ToArray()) Inputs.Remove(player);
        var smoke = LegendaryGrenadeEffects.HasRecoilSmoke;
        foreach (var player in players)
        {
            if (!States.ContainsKey(player.ProfileId) && !Inputs.ContainsKey(player) && !smoke) continue;
            if (player.HandsController is not Player.FirearmController hands) { Inputs.Remove(player); continue; }
            var input = (hands, Reload(player), Hip(player), Recoil(player));
            var known = Inputs.TryGetValue(player, out var old);
            var modified = input.Item2 != 1f || input.Item3 != 1f || input.Item4 != 1f;
            if ((known && old != input) || (!known && modified))
            {
                PlayerRefresh.Firearm(player, "grenade buffs");
            }
            if (modified) Inputs[player] = input;
            else Inputs.Remove(player);
        }
    }
    private static bool Active(Player p, string key) => States.TryGetValue(p.ProfileId, out var s) && s.Timers.TryGetValue(key, out var t) && t.Until > Time.time;
    private static void Arm(Player p, string kind, string name, float seconds) => Get(p).Timers[kind] = (Time.time + seconds, name);
    internal static void Thrown(Player p, VariantDefinition d)
    {
        switch (d.RuntimeKey)
        {
            case "GR03": Arm(p, "reload", d.ShortName, 5f); break;
            case "GR09": Arm(p, "protection", d.ShortName, 8f); break;
            case "GR18": Arm(p, "packing", d.ShortName, 10f); break;
            case "GR20": Get(p).Draw = true; break;
            case "GR25": Arm(p, "hip", d.ShortName, 6f); break;
            case "GR49": Arm(p, "movement", d.ShortName, 5f); break;
        }
    }
    internal static void Flashed(LegendaryGrenadeEffects.Payload payload, Player target)
    {
        if (target.ProfileId == payload.Owner || target.HealthController?.IsAlive != true) return;
        if (payload.Key == "GR50") { Marked[(payload.Owner, target.ProfileId)] = Time.time + 8f; MarkNames[payload.Owner] = payload.Roll.Definition.ShortName; }
        if (payload.Key == "GR54" && Find(payload.Owner) is { } owner && PlayerRefresh.CanObserve(owner)) Arm(owner, "recoil", payload.Roll.Definition.ShortName, 8f);
    }
    internal static Player? Find(string id) => Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => p.ProfileId == id);
    internal static bool MarkedBy(Player target, Player shooter) => Marked.TryGetValue((shooter.ProfileId, target.ProfileId), out var end) && end > Time.time;
    internal static float Incoming(Player p) => Active(p, "protection") ? .1f : 1f;
    internal static float Reload(Player? p) => p is not null && Active(p, "reload") ? 1.4f : 1f;
    internal static float Hip(Player? p) => p is not null && Active(p, "hip") ? .6f : 1f;
    internal static float Movement(Player p) => Math.Max(Active(p, "movement") ? 1.2f : 1f, Smoke(p, "GR57") ? 1.2f : 1f);
    internal static float Recoil(Player? p) => p is null ? 1f : Math.Min(Active(p, "recoil") ? .6f : 1f, Smoke(p, "GR58") && AdvancedMeleeRuntime.Crouched(p) ? .4f : 1f);
    internal static float Packing(Player? p) => p is null ? 1f : Math.Max(Active(p, "packing") ? 1.75f : 1f, Smoke(p, "GR62") ? 2f : 1f);
    internal static float Medicine(Player p) => Smoke(p, "GR63") ? 1.5f : 1f;
    internal static float Recovery(Player p, string kind) => kind is "legRecovery" or "armRecovery" && Smoke(p, "GR64") ? 1.75f : 1f;
    private static bool Smoke(Player p, string key) => LegendaryGrenadeEffects.InSmoke(p, key);
    internal static float Draw(Player? p, bool consume = false)
    {
        if (p is null || !States.TryGetValue(p.ProfileId, out var s) || !s.Draw) return 1f;
        if (consume) s.Draw = false;
        return 2f;
    }
    internal static IEnumerable<EffectStatusModel.Status> Statuses(Player p)
    {
        foreach (var status in LegendaryGrenadeEffects.SmokeStatuses(p)) yield return status;
        var marks = Marked.Where(kv => kv.Key.Owner == p.ProfileId && kv.Value > Time.time && Find(kv.Key.Target)?.HealthController?.IsAlive == true).ToArray();
        if (marks.Length > 0)
            yield return new EffectStatusModel.Status("grenade-buff:marked", MarkNames[p.ProfileId], true, marks.Max(kv => kv.Value) - Time.time, detail: marks.Length + " marked targets");
        if (!States.TryGetValue(p.ProfileId, out var s)) yield break;
        foreach (var pair in s.Timers.Where(kv => kv.Value.Until > Time.time))
            yield return new EffectStatusModel.Status("grenade-buff:" + pair.Key, pair.Value.Name, true, pair.Value.Until - Time.time);
        if (s.Draw) yield return new EffectStatusModel.Status("grenade-buff:draw", "Rapid Response", true, detail: "Next firearm draw +100% speed");
    }
}
