using System;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class LegendaryRuntime
{
    internal static EffectStatusModel.Status? CurrentStatus(Player player, Weapon weapon)
    {
        if (player.HandsController?.Item != weapon || player.HealthController?.IsAlive != true ||
            !RarityResolver.TryGetDefinition(weapon, out var definition) || definition.MeleeEffect is not null || definition.BackpackEffect is not null) return null;
        if (PermanentStatus(definition)) return null;
        UpdateLiveState(player, weapon);
        lock (Sync)
        {
            // This is the same non-consuming next-shot evaluation used by live
            // weapon stats. Never reuse LastApplication from a previous bullet.
            var application = GetLiveApplication(weapon);
            var state = GetState(weapon.Id);
            if (RemainingAmmo(weapon) <= 0 && application.ReloadSpeedMultiplier == 1f)
            {
                var pending = StatusDelay(definition, state, player);
                return pending.Seconds > 0f ? new EffectStatusModel.Status("gun:" + weapon.Id, definition.ShortName, false, ready: pending.Seconds, detail: pending.Label) : null;
            }
            var active = application.StatusActive ?? (application.Active && application.HasMechanicalEffect);
            var detail = string.Empty;
            switch (definition.SpecialId)
            {
                case "zantetsuken": active = state.DrawEffectArmed; detail = "Next shot ready"; break;
                case "full_send": active &= LegendaryFireModePatch.IsHeld((Player.FirearmController)player.HandsController); break;
                case "compound_interest": active = state.CompoundInterestStacks > 0; break;
                case "wave015_binary_hunter": case "wave015_two_in_one": case "wave015_balance":
                    active = LegendaryFireModePatch.LogicalMode(weapon) == "burst"; detail = "Binary Trigger active"; break;
                case "wave015_dont_stop": active = false; break; // Confirmed transfers produce a short trigger receipt.
                case "kabans_kabab": active = application.ExplosionDamageFactor > 0f; detail = "Next explosive shot ready"; break;
                case "requiem": active = RemainingAmmo(weapon) == 1; detail = "Final round ready"; break;
                case "wave015_like_a_ghost": active = false; break; // Real deployed smoke has its own lifetime.
                case "wave015_one_and_done": detail = "Next shot ready"; break;
                case "wave015_sight_picture": case "wave015_held_breath": detail = "Next shot ready"; break;
            }
            if (definition.CustomEffect?.Trigger is "draw_next_shot" or "stationary_next_shot" or "ads_seconds_next_shot" or "chamber_next_shot" ||
                definition.CustomEffect?.Trigger.StartsWith("every_n", StringComparison.Ordinal) == true ||
                definition.EffectKind == "ordinary" && definition.TriggerIndex is 6 or 12 or 24 or 25 ||
                definition.EffectKind == "meme" && definition.TriggerIndex is 2 or 4 or 10)
                detail = "Next shot ready";
            if (!string.IsNullOrEmpty(application.ConditionalTargetProfileId)) detail = "Same target ready";
            if (active) return new EffectStatusModel.Status("gun:" + weapon.Id, definition.ShortName, true, detail: detail);
            var delay = StatusDelay(definition, state, player);
            return delay.Seconds > 0f ? new EffectStatusModel.Status("gun:" + weapon.Id, definition.ShortName, false, ready: delay.Seconds, detail: delay.Label) : null;
        }
    }
    internal static bool PermanentStatus(VariantDefinition definition) => definition.CustomEffect?.Trigger == "always" ||
        definition.CustomEffect is null && definition.SpecialId is "bunker_buster" or "split_decision" or "mp7a2_volks_tribute" or "ak545_wall_pass" or "wave015_core_and_jacket" or "wave015_echo";
    private static (float Seconds, string Label) StatusDelay(VariantDefinition d, WeaponState s, Player p)
    {
        var now = Time.time;
        var c = d.CustomEffect;
        if (c?.Trigger == "after_pause" || d.EffectKind == "ordinary" && d.TriggerIndex is 2 or 36)
        {
            var needed = c?.Seconds ?? d.Number(d.RuntimeKey, d.TriggerIndex == 2 ? 2 : 1, d.TriggerIndex == 2 ? 4 : 2);
            return (s.LastShotTime >= 0f ? Math.Max(0f, needed - (now - s.LastShotTime)) : 0f, "Cooldown");
        }
        if (c?.Trigger is "stationary" or "stationary_next_shot" && !s.StationaryEffectConsumed && s.StationaryStartTime >= 0f)
            return (Math.Max(0f, c.Seconds - (now - s.StationaryStartTime)), "Charging");
        if (c?.Trigger is "ads_seconds" or "ads_seconds_next_shot" && !s.AdsEffectConsumed && p.HandsController?.IsAiming == true && s.AimStartTime >= 0f)
            return (Math.Max(0f, c.Seconds - (now - s.AimStartTime)), "Charging");
        if (d.EffectKind == "ordinary" && d.TriggerIndex == 14 && s.LastAiming)
            return (Math.Max(0f, d.Number(d.RuntimeKey, 3, 7) - (now - s.AimStartTime)), "Charging");
        if (d.EffectKind == "ordinary" && d.TriggerIndex == 19 && s.StationaryStartTime >= 0f)
            return (Math.Max(0f, d.Number(d.RuntimeKey, 3, 10) - (now - s.StationaryStartTime)), "Charging");
        return (0f, string.Empty);
    }
}
