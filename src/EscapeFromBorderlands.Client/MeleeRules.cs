using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

/// <summary>Pure effect calculations; native adapters supply current, rather than cached, conditions.</summary>
internal static class MeleeRules
{
    internal static readonly HashSet<string> Ak74Family = new(StringComparer.Ordinal)
    {
        "5644bd2b4bdc2d3b4c8b4572", "57dc2fa62459775949412633",
        "583990e32459771419544dd2", "5839a40f24597726f856b511",
        "5ab8e9fcd8ce870019439434", "5ac4cd105acfc40016339859",
        "5bf3e03b0db834001d2c4a9c", "5bf3e0490db83400196199af",
    };

    public static bool Enabled(MeleeEffectDefinition effect, float healthFraction, bool hasAk74) =>
        (!effect.RequiresAk74 || hasAk74) && healthFraction <= effect.MaximumHealthFraction + 0.00001f;

    public static float Damage(MeleeEffectDefinition effect, bool stab, int attackNumber,
        float buildup, bool behind, bool bleeding, bool moving, bool leg)
    {
        var multiplier = effect.DamageMultiplier * (stab ? effect.StabDamageMultiplier : effect.SlashDamageMultiplier);
        if (effect.AttackInterval > 0 && attackNumber > 0 && attackNumber % effect.AttackInterval == 0)
            multiplier *= effect.IntervalDamageMultiplier;
        if (behind) multiplier *= effect.RearDamageMultiplier;
        if (bleeding) multiplier *= effect.BleedingTargetDamageMultiplier;
        if (moving) multiplier *= effect.MovingDamageMultiplier;
        if (leg) multiplier *= effect.LegDamageMultiplier;
        return multiplier * (1f + Math.Max(0f, Math.Min(effect.MaximumDamageBonus, buildup)));
    }

    public static float NextBuildup(MeleeEffectDefinition effect, float previous) =>
        Math.Min(effect.MaximumDamageBonus, previous + effect.DamagePerFleshHit);

    // Ballistic jump height is proportional to the square of launch velocity.
    public static float JumpVelocityMultiplier(float heightMultiplier) => (float)Math.Sqrt(heightMultiplier);

    public static float RestoreStamina(float current, float maximum, float fraction) =>
        Math.Min(maximum, current + Math.Max(0f, maximum) * fraction);
}
