using System;
using System.Collections.Generic;
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
    private static readonly List<(string Owner, string Target)> ExpiredMarks = new();
    private static readonly HashSet<string> OwnersWithMarks = new(StringComparer.Ordinal);
    private static readonly List<string> ExpiredIds = new(), ExpiredTimers = new();
    private static readonly List<Player> StalePlayers = new();
    private static float NextTick;
    private static State Get(Player p)
    { if (!States.TryGetValue(p.ProfileId, out var s)) States[p.ProfileId] = s = new(); return s; }
    internal static void Reset() { States.Clear(); Marked.Clear(); MarkNames.Clear(); Inputs.Clear(); NextTick = 0f; }
    internal static bool HasState(Player? p) => (States.Count != 0 || LegendaryGrenadeEffects.HasSmokePayload("GR58")) &&
        PlayerRefresh.CanObserve(p) && (States.ContainsKey(p.ProfileId) || Smoke(p, "GR58"));
    internal static bool ChangesFirearm(Player? p, bool observed = false)
    {
        if (p is null || (States.Count == 0 && !LegendaryGrenadeEffects.HasSmokePayload("GR58")) || !observed && !PlayerRefresh.CanObserve(p)) return false;
        if (States.TryGetValue(p.ProfileId, out var s))
        {
            var now = Time.time;
            if (s.Timers.TryGetValue("reload", out var reload) && reload.Until > now ||
                s.Timers.TryGetValue("hip", out var hip) && hip.Until > now ||
                s.Timers.TryGetValue("recoil", out var recoil) && recoil.Until > now) return true;
        }
        return Smoke(p, "GR58");
    }

    internal static void Maintain(GameWorld? world, RaidPlayerSnapshot live)
    {
        var now = Time.time;
        if (now < NextTick) return;
        NextTick = now + .05f;
        if (world is null) { if (States.Count != 0 || Inputs.Count != 0 || Marked.Count != 0 || MarkNames.Count != 0) Reset(); return; }
        var smoke = LegendaryGrenadeEffects.HasRecoilSmoke;
        if (States.Count == 0 && Inputs.Count == 0 && Marked.Count == 0 && !smoke) return;
        live.Capture(world);
        Prune(now, live);
        foreach (var player in live)
        {
            if (!PlayerRefresh.CanObserve(player)) continue;
            if (!States.ContainsKey(player.ProfileId) && !Inputs.ContainsKey(player) &&
                (!smoke || !LegendaryGrenadeEffects.HasOwnedSmoke(player.ProfileId, "GR58"))) continue;
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
    private static void Prune(float now, RaidPlayerSnapshot live)
    {
        try
        {
            foreach (var pair in Marked)
                if (pair.Value <= now || !live.ContainsProfile(pair.Key.Owner) || !live.ContainsProfile(pair.Key.Target))
                    ExpiredMarks.Add(pair.Key);
                else OwnersWithMarks.Add(pair.Key.Owner);
            foreach (var key in ExpiredMarks) Marked.Remove(key);
            foreach (var id in MarkNames.Keys) if (!OwnersWithMarks.Contains(id)) ExpiredIds.Add(id);
            foreach (var id in ExpiredIds) MarkNames.Remove(id);
            ExpiredIds.Clear();
            foreach (var pair in States)
            {
                if (!live.ContainsProfile(pair.Key)) { ExpiredIds.Add(pair.Key); continue; }
                var state = pair.Value;
                foreach (var timer in state.Timers) if (timer.Value.Until <= now) ExpiredTimers.Add(timer.Key);
                foreach (var key in ExpiredTimers) state.Timers.Remove(key);
                ExpiredTimers.Clear();
                if (state.Timers.Count == 0 && !state.Draw) ExpiredIds.Add(pair.Key);
            }
            foreach (var id in ExpiredIds) States.Remove(id);
            foreach (var player in Inputs.Keys)
                if (!live.ContainsProfile(player.ProfileId) || !PlayerRefresh.CanObserve(player)) StalePlayers.Add(player);
            foreach (var player in StalePlayers) Inputs.Remove(player);
        }
        finally
        { ExpiredMarks.Clear(); OwnersWithMarks.Clear(); ExpiredIds.Clear(); ExpiredTimers.Clear(); StalePlayers.Clear(); }
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
    internal static Player? Find(string id)
    {
        var world = Singleton<GameWorld>.Instance;
        if (!string.IsNullOrEmpty(id) && world?.GetAlivePlayerByProfileID(id) is { } indexed && indexed != null) return indexed;
        return Find(world, id);
    }
    internal static Player? Find(GameWorld? world, string id)
    {
        if (world is null) return null;
        foreach (var entry in world.RegisteredPlayers)
            if (entry is Player player && player.ProfileId == id) return player;
        return null;
    }
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
    internal static void AppendStatuses(Player p, List<EffectStatusModel.Status> statuses)
    {
        LegendaryGrenadeEffects.AppendSmokeStatuses(p, statuses);
        var now = Time.time; var id = p.ProfileId; var count = 0; var latest = 0f;
        foreach (var mark in Marked)
            if (mark.Key.Owner == id && mark.Value > now && Find(mark.Key.Target)?.HealthController?.IsAlive == true)
            { count++; latest = Math.Max(latest, mark.Value); }
        if (count > 0)
            statuses.Add(new EffectStatusModel.Status("grenade-buff:marked", MarkNames[id], true, latest - now, detail: count + " marked targets"));
        if (!States.TryGetValue(id, out var s)) return;
        foreach (var pair in s.Timers)
            if (pair.Value.Until > now)
                statuses.Add(new EffectStatusModel.Status("grenade-buff:" + pair.Key, pair.Value.Name, true, pair.Value.Until - now));
        if (s.Draw) statuses.Add(new EffectStatusModel.Status("grenade-buff:draw", "Rapid Response", true, detail: "Next firearm draw +100% speed"));
    }
}
