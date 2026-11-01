using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class AdvancedMeleeRuntime
{
    internal static IEnumerable<EffectStatusModel.Status> Statuses(Player player)
    {
        // Native pain relief intentionally continues after the melee weapon is
        // stowed. Read the actual native effect, not a duplicate HUD countdown.
        if (KillPainkillers.TryGetValue(player, out var relief) && relief.Existing && relief.TimeLeft > 0f &&
            KillPainkillerSources.TryGetValue(player, out var source))
            yield return new EffectStatusModel.Status("melee:relief:" + player.ProfileId, source.ShortName + " · pain relief", true, Math.Max(0f, relief.TimeLeft));
        var held = MeleeRuntime.Held(player);
        if (held is null || !MeleeRuntime.Enabled(player, held.Effect) || PermanentStatus(held.Effect)) yield break;
        var e = held.Effect; var s = held.Advanced;
        var active = true; var detail = string.Empty; var delay = 0f;
        if (e.Ability is "combo" or "sprint_charge" or "counter_charge" or "draw_charge")
        {
            active = e.Ability == "draw_charge" ? s.FirstAttack : s.Charged;
            detail = e.Ability is "combo" or "sprint_charge" ? "Next stab ready" : "Next attack ready";
            if (!active && e.Ability == "sprint_charge" && s.SprintSince >= 0f)
            { delay = Math.Max(0f, e.ChargeSeconds - (Time.time - s.SprintSince)); detail = "Charging"; }
        }
        else if (e.StationarySeconds > 0f)
        {
            active = s.StationaryRecoveryActive;
            if (!active && player.InputDirection.sqrMagnitude < .0001f)
            { delay = Math.Max(0f, e.StationarySeconds - (Time.time - s.StationarySince)); detail = "Charging"; }
        }
        else if (e.DamagePerFleshHit > 0f) { active = held.Buildup > 0f; detail = $"+{held.Buildup:P0} damage"; }
        else if (e.AttackInterval > 0) { active = (held.AttackNumber + 1) % e.AttackInterval == 0; detail = "Next attack ready"; }
        else if (e.UnlocksDoorOncePerRaid) { active = MasterKeyRuntime.Available(player); detail = "Ready · 1 use this raid"; }
        else if (e.ExtraJumpHeightMultiplier > 0f) { active = ExtraJumpReady(player); detail = "Extra jump ready"; }
        else if (e.SlashSpeedPerAttack > 0f) { active = s.SlashSpeed > 0f; detail = $"+{s.SlashSpeed:P0} next slash speed"; }
        else if (e.NearbyEnemyRadius > 0f) { var bonus = NearbySpeedBonus(held); active = bonus > 0f; detail = $"+{bonus:P0} attack speed"; }
        else if (e.ChainPenetrationPerHit > 0f) { active = s.PreviousAttackConnected && s.ChainPenetration > 0f; detail = "Same target ready"; }
        else if (e.MarkedAttackerDamageMultiplier < 1f) { active = s.MarkedEnemies.Count > 0; detail = "Marked enemies deal less damage"; }
        else if (e.CrouchMovementMultiplier != 1f || e.CrouchExplosionMultiplier != 1f) active = Crouched(player);
        else if (e.MovingDamageMultiplier != 1f) active = player.InputDirection.sqrMagnitude > .0001f;
        // Target-specific and instantaneous effects are described as armed
        // effects rather than claiming an unconditional damage/stat buff.
        else if (e.BleedingTargetDamageMultiplier != 1f || e.BleedingHitLegStaminaFraction > 0f || e.RearDamageMultiplier != 1f ||
            e.HeavyBleedOnStab || e.FirstPartStabDamageMultiplier != 1f || e.LongStabDamageMultiplier != 1f ||
            e.HealOnFleshHit > 0f || e.ChestSlashStomachDamage > 0f || e.FractureOnLegHit || e.HeadSlashDamageMultiplier != 1f ||
            e.KillPainkillerSeconds > 0f || e.HealOnKill > 0f || e.KillLegStaminaFraction > 0f || e.KillStopsBleeding) active = false;
        if (active || delay > 0f)
            yield return new EffectStatusModel.Status("melee:" + held.Item.Id, held.Roll.Definition.ShortName, active, ready: delay, detail: detail);
    }
    internal static bool PermanentStatus(MeleeEffectDefinition e) =>
        e.Ability.Length == 0 && !e.RequiresAk74 && !e.RequiresEmptyPrimaries && !e.RequiresSniper && !e.RequiresNight &&
        !e.RequiresBleeding && !e.RequiresProne && e.MaximumHealthFraction >= 1f && e.StationarySeconds <= 0f &&
        e.DamagePerFleshHit <= 0f && e.AttackInterval <= 0 && !e.UnlocksDoorOncePerRaid && e.ExtraJumpHeightMultiplier <= 0f &&
        e.SlashSpeedPerAttack <= 0f && e.NearbyEnemyRadius <= 0f && e.ChainPenetrationPerHit <= 0f && e.MarkedAttackerDamageMultiplier >= 1f &&
        e.CrouchMovementMultiplier == 1f && e.CrouchExplosionMultiplier == 1f && e.MovingDamageMultiplier == 1f &&
        e.BleedingTargetDamageMultiplier == 1f && e.BleedingHitLegStaminaFraction <= 0f && e.RearDamageMultiplier == 1f &&
        !e.HeavyBleedOnStab && e.FirstPartStabDamageMultiplier == 1f && e.LongStabDamageMultiplier == 1f &&
        e.HealOnFleshHit <= 0f && e.ChestSlashStomachDamage <= 0f && !e.FractureOnLegHit && e.HeadSlashDamageMultiplier == 1f &&
        e.KillPainkillerSeconds <= 0f && e.HealOnKill <= 0f && e.KillLegStaminaFraction <= 0f && e.KillArmStaminaFraction <= 0f && !e.KillStopsBleeding && !e.KillClearsTremor && e.TargetLowHealthFraction <= 0f;
}
