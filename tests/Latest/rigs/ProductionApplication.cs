using System;using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal sealed class EffectApplication
{
    public static readonly EffectApplication Empty = new(null) { Active = false };
    public EffectApplication(VariantDefinition? definition) => Definition = definition;
    public VariantDefinition? Definition { get; }
    public bool Active { get; set; }
    public bool? StatusActive { get; set; }
    public bool BonusProjectile { get; private set; }
    public string SpecialId { get; set; } = string.Empty;
    public float DamageMultiplier { get; set; } = 1f;
    public float HeadDamageMultiplier { get; set; } = 1f;
    public float BodyDamageMultiplier { get; set; } = 1f;
    public float VelocityMultiplier { get; set; } = 1f;
    public float PenetrationMultiplier { get; set; } = 1f;
    public float ArmorDamageMultiplier { get; set; } = 1f;
    public float RicochetChanceBonus { get; set; }
    public float FragmentationChanceBonus { get; set; }
    public float RicochetRetention { get; set; }
    public float SurfaceRetentionBonus { get; set; }
    public float LimbDamageMultiplier { get; set; } = 1f;
    public float ExplosionDamageFactor { get; set; }
    public float ExplosionRadius { get; set; }
    public float ExplosionRadiusMultiplier { get; set; } = 1f;
    public float ReloadSpeedMultiplier { get; set; } = 1f;
    public int HeavyBleedEveryFleshHits { get; set; }
    public int ProjectileCount { get; set; } = 1;
    public float ProjectileDamageMultiplier { get; set; } = 1f;
    public float ProjectileSpreadDegrees { get; set; }
    public string ProjectilePattern { get; set; } = "horizontal";
    public bool ReplaceNaturalProjectiles { get; set; }
    public bool CenterBonusOnShell { get; set; }
    public Vector3 ShellCenterDirection { get; set; }
    public float BonusProjectileDamageScale { get; set; } = 1f;
    public float BonusProjectilePenetrationScale { get; set; } = 1f;
    public float BonusProjectileVelocityScale { get; set; } = 1f;
    public bool ForceHeavyBleedOnFleshHit { get; set; }
    public string AmmoTemplateId { get; set; } = string.Empty;
    public string ConditionalTargetProfileId { get; set; } = string.Empty;
    public string ConditionalBodyPart { get; set; } = string.Empty;
    public float ConditionalDamageMultiplier { get; set; } = 1f;
    public StatModifiers StatModifiers { get; set; }
    public bool HasAnyEffect => Active && (Definition is not null);
    public bool HasMechanicalEffect => DamageMultiplier != 1f || HeadDamageMultiplier != 1f || BodyDamageMultiplier != 1f ||
        VelocityMultiplier != 1f || PenetrationMultiplier != 1f ||
        ArmorDamageMultiplier != 1f || RicochetChanceBonus != 0f || FragmentationChanceBonus != 0f ||
        ExplosionDamageFactor > 0f || ProjectileCount > 1 || SurfaceRetentionBonus > 0f ||
        LimbDamageMultiplier != 1f || ReloadSpeedMultiplier != 1f || HeavyBleedEveryFleshHits > 0 ||
        ForceHeavyBleedOnFleshHit || !string.IsNullOrEmpty(AmmoTemplateId) ||
        ConditionalDamageMultiplier != 1f || StatModifiers.HasAny;
    public static EffectApplication Inactive(VariantDefinition definition) => new(definition) { Active = false };
    public EffectApplication AsBonusProjectile()
    {
        var copy = (EffectApplication)MemberwiseClone();
        copy.BonusProjectile = true;
        copy.ProjectileCount = 1;
        return copy;
    }
}
internal readonly struct StatModifiers
{
    public StatModifiers(float recoilDelta = 0f, float ergonomicsDelta = 0f, float accuracyDelta = 0f, float fireRateMultiplier = 1f, float velocityMultiplier = 1f)
    {
        RecoilDelta = recoilDelta;
        ErgonomicsDelta = ergonomicsDelta;
        AccuracyDelta = accuracyDelta;
        FireRateMultiplier = fireRateMultiplier;
        VelocityMultiplier = velocityMultiplier;
    }
    public float RecoilDelta { get; }
    public float ErgonomicsDelta { get; }
    public float AccuracyDelta { get; }
    public float FireRateMultiplier { get; }
    public float VelocityMultiplier { get; }
    public float EffectiveFireRateMultiplier => FireRateMultiplier == 0f ? 1f : FireRateMultiplier;
    public float EffectiveVelocityMultiplier => VelocityMultiplier == 0f ? 1f : VelocityMultiplier;
    public bool HasAny => RecoilDelta != 0f || ErgonomicsDelta != 0f || AccuracyDelta != 0f ||
        (FireRateMultiplier != 0f && FireRateMultiplier != 1f) || (VelocityMultiplier != 0f && VelocityMultiplier != 1f);
}
