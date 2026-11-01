namespace EscapeFromBorderlands.Server;

public sealed class MeleeEffectDefinition
{
    public float DamageMultiplier { get; set; } = 1f;
    public float SlashDamageMultiplier { get; set; } = 1f;
    public float StabDamageMultiplier { get; set; } = 1f;
    public float AttackSpeedMultiplier { get; set; } = 1f;
    public float SwapSpeedMultiplier { get; set; } = 1f;
    public float WeaponSwapSpeedMultiplier { get; set; } = 1f;
    public float ReachMultiplier { get; set; } = 1f;
    public float MovementMultiplier { get; set; } = 1f;
    public float IncomingDamageMultiplier { get; set; } = 1f;
    public float SprintStaminaMultiplier { get; set; } = 1f;
    public float JumpStaminaMultiplier { get; set; } = 1f;
    public float JumpHeightMultiplier { get; set; } = 1f;
    public float NoiseMultiplier { get; set; } = 1f;
    public float RearDamageMultiplier { get; set; } = 1f;
    public float BleedingTargetDamageMultiplier { get; set; } = 1f;
    public float MovingDamageMultiplier { get; set; } = 1f;
    public float LegDamageMultiplier { get; set; } = 1f;
    public float MaximumHealthFraction { get; set; } = 1f;
    public float DamagePerFleshHit { get; set; }
    public float MaximumDamageBonus { get; set; }
    public float PenetrationBonus { get; set; }
    public int AttackInterval { get; set; }
    public float IntervalDamageMultiplier { get; set; } = 1f;
    public float HealOnFleshHit { get; set; }
    public float HealOnKill { get; set; }
    public float KillLegStaminaFraction { get; set; }
    public float BlockedHitDamage { get; set; }
    public bool RequiresAk74 { get; set; }
    public bool HeavyBleedOnStab { get; set; }
    public bool FractureOnLegHit { get; set; }
    public bool FallImmunity { get; set; }
    public string Ability { get; set; } = string.Empty;
    public bool RequiresEmptyPrimaries { get; set; }
    public bool RequiresSniper { get; set; }
    public bool RequiresNight { get; set; }
    public bool RequiresBleeding { get; set; }
    public bool RequiresProne { get; set; }
    public float ChargedDamageMultiplier { get; set; } = 1f;
    public float ChargedPenetrationBonus { get; set; }
    public float ChargedReachMultiplier { get; set; } = 1f;
    public float ChargeSeconds { get; set; }
    public float WeightMultiplier { get; set; } = 1f;
    public float StationarySeconds { get; set; }
    public float StationaryLegStaminaPerSecond { get; set; }
    public float StationaryHealPerSecond { get; set; }
    public float BleedingHitLegStaminaFraction { get; set; }
    public float KillArmStaminaFraction { get; set; }
    public bool KillClearsTremor { get; set; }
    public float KillPainkillerSeconds { get; set; }
    public bool KillStopsBleeding { get; set; }
    public bool HoldPainkiller { get; set; }
    public float CrouchMovementMultiplier { get; set; } = 1f;
    public float ChestSlashStomachDamage { get; set; }
    public float HeadSlashDamageMultiplier { get; set; } = 1f;
    public float HeadSlashPenetrationBonus { get; set; }
    public bool UnlocksDoorOncePerRaid { get; set; }
    public float SlashSpeedPerAttack { get; set; }
    public float SlashSpeedMaximum { get; set; }
    public float VaultSpeedMultiplier { get; set; } = 1f;
    public float VaultLegStaminaMultiplier { get; set; } = 1f;
    public float ExtraJumpHeightMultiplier { get; set; }
    public float CrouchExplosionMultiplier { get; set; } = 1f;
    public float MarkedAttackerDamageMultiplier { get; set; } = 1f;
    public float LongStabDistance { get; set; }
    public float LongStabDamageMultiplier { get; set; } = 1f;
    public float FirstPartStabDamageMultiplier { get; set; } = 1f;
    public float NearbyEnemyRadius { get; set; }
    public float NearbyEnemySpeedBonus { get; set; }
    public float NearbyEnemySpeedMaximum { get; set; }
    public float ChainPenetrationPerHit { get; set; }
    public float ChainPenetrationMaximum { get; set; }
    public float TargetLowHealthFraction { get; set; }
    public float TargetLowHealthDamageMultiplier { get; set; } = 1f;
}
