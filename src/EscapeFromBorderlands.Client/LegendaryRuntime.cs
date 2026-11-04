using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class LegendaryRuntime
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, WeaponState> States = new(StringComparer.Ordinal);
    private static readonly HashSet<WeaponState> ActiveVolleyStates = new();
    private static readonly Dictionary<Shot, EffectApplication> ShotApplications = new();
    private static readonly Dictionary<string, EffectApplication> ShotApplicationsByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> HeldWeaponsByPlayer = new(StringComparer.Ordinal);
    [ThreadStatic] private static ShotScope? _shotScope;
    private static int _scopeIndex;

    internal sealed class ShotScope
    {
        public Weapon Weapon = null!;
        public Ammo Ammo = null!;
        public EffectApplication Application = null!;
        public ShotScope? Previous;
    }

    public static ShotScope? BeginShot(Weapon weapon, Ammo ammo, string profileId, int? remainingBeforeShot = null)
    {
        if (!RarityResolver.TryGetDefinition(weapon, out var definition)) return null;
        var previous = _shotScope;
        _shotScope = null;
        // Commit after cartridge consumption but before EFT reads combat stats.
        var application = RegisterShot(weapon, ammo, profileId, --_scopeIndex, definition, remainingBeforeShot);
        return _shotScope = new ShotScope { Weapon = weapon, Ammo = ammo, Application = application, Previous = previous };
    }

    public static void EndShot(ShotScope? scope)
    {
        if (scope is not null && ReferenceEquals(_shotScope, scope)) _shotScope = scope.Previous;
    }

    public static bool ReplaceNaturalPellets(Ammo ammo) =>
        ReferenceEquals(_shotScope?.Ammo, ammo) && _shotScope!.Application.ReplaceNaturalProjectiles;

    public static void SetShotCenter(Weapon weapon, Vector3 direction)
    {
        if (_shotScope?.Weapon == weapon) _shotScope.Application.ShellCenterDirection = direction.normalized;
    }

    public static int RemainingAmmo(Weapon weapon) => (weapon.GetCurrentMagazine()?.Count ?? 0) + weapon.ChamberAmmoCount;
    public static int Capacity(Weapon weapon) => Math.Max(1,
        (weapon.GetCurrentMagazine()?.MaxCount ?? weapon.GetMaxMagazineCount()) + weapon.Chambers.Length);
    public static int ShotCount(Weapon weapon) { lock (Sync) return GetState(weapon.Id).TotalShots; }
    public static int ReloadGeneration(Weapon weapon) { lock (Sync) return GetState(weapon.Id).ReloadGeneration; }

    public static void ResetSession()
    {
        lock (Sync)
        {
            ElephantGunRules.Reset();
            States.Clear(); ActiveVolleyStates.Clear(); ShotApplications.Clear(); ShotApplicationsByKey.Clear(); HeldWeaponsByPlayer.Clear();
            _shotScope = null; _scopeIndex = 0;
        }
    }

    public static void NotifyTriggerReleased(Weapon? weapon)
    {
        if (weapon is null || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            state.TriggerHeldShots = 0; state.UninterruptedShots = 0; state.FullSendStacks = 0;
        }
    }

    public static EffectApplication RegisterShot(
        Weapon weapon,
        Ammo ammo,
        string playerProfileId,
        int fireIndex,
        VariantDefinition definition,
        int? remainingBeforeShot = null)
    {
        if (definition.EffectKind == "procedural")
        {
            return EffectApplication.Empty;
        }

        lock (Sync)
        {
            var state = GetState(weapon.Id);
            if (_shotScope?.Weapon == weapon)
            {
                state.LastFireIndex = fireIndex;
                BeginVolleyOutcome(state, fireIndex);
                return _shotScope.Application;
            }
            if (state.LastFireIndex == fireIndex && state.LastApplication is not null)
            {
                return state.LastApplication;
            }

            var now = Time.time;
            var player = GetPlayer(playerProfileId);
            state.PlayerProfileId = playerProfileId;
            UpdateContinuousState(state, player, weapon, now);
            var magazine = weapon.GetCurrentMagazine();
            var remainingAfterShot = RemainingAmmo(weapon);
            var remaining = remainingBeforeShot ?? remainingAfterShot + 1;
            var capacity = Capacity(weapon);
            var reloaded = ObserveReloadBoundary(state, weapon);

            var secondsSinceLastShot = state.LastShotTime < 0f ? float.MaxValue : now - state.LastShotTime;
            state.UninterruptedShots++;
            state.TotalShots++;
            state.ShotsInMagazine++;
            if (state.MagazineCheckActive) state.ShotsSinceMagazineCheck++;
            var triggerPressed = player?.HandsController is Player.FirearmController controller && LegendaryFireModePatch.IsHeld(controller);
            if (triggerPressed)
            {
                state.TriggerHeldShots++;
            }
            else if (definition.Automatic)
            {
                state.TriggerHeldShots = 1;
            }
            if (state.ReloadMomentumAvailable && state.ShotsInMagazine > 1 && secondsSinceLastShot > 0.5f)
            {
                state.ReloadMomentumAvailable = false;
            }
            var context = new ShotContext(
                now,
                secondsSinceLastShot,
                remaining,
                capacity,
                player,
                IsSuppressed(weapon),
                ammo.InitialSpeed <= 343f,
                definition.CaliberAverageVelocity > 0 && ammo.InitialSpeed > definition.CaliberAverageVelocity,
                reloaded,
                state);

            if (context.IsMovingInput)
            {
                state.MovingShotsInMagazine++;
            }

            if (!string.Equals(state.LastShotFireMode, context.FireMode, StringComparison.OrdinalIgnoreCase))
            {
                state.LastShotFireMode = context.FireMode;
                state.ShotsInCurrentFireMode = 0;
            }
            state.ShotsInCurrentFireMode++;
            state.ShotsSinceDraw++;

            var application = BuildApplication(definition, state, context, ammo.AmmoTemplate, ammo.StringTemplateId);
            if (application.Active && definition.CustomEffect?.Trigger == "stationary_next_shot")
            {
                state.StationaryEffectConsumed = true;
            }
            if (application.Active && definition.CustomEffect?.Trigger == "ads_seconds_next_shot")
            {
                state.AdsEffectConsumed = true;
            }
            if (application.HasMechanicalEffect && (definition.SpecialId is "wave015_sight_picture" or "wave015_held_breath"))
            {
                state.AdsEffectConsumed = true;
                state.StationaryEffectConsumed = true;
            }
            if (definition.CustomEffect?.Trigger == "draw_next_shot" && state.DrawEffectArmed)
            {
                state.DrawEffectArmed = false;
            }
            if ((definition.SpecialId is "zantetsuken" or "wave015_one_and_done") && state.DrawEffectArmed)
            {
                state.DrawEffectArmed = false;
            }
            if (definition.SpecialId == "wave015_like_a_ghost" && context.RemainingAmmo <= 1 && !state.SmokeDeployedThisMagazine)
            {
                state.SmokeDeployedThisMagazine = true;
                AdvancedLegendaryActions.DeploySmoke(player, 20f, definition.ShortName, "gun-smoke:" + weapon.Id);
            }
            state.LastApplication = application;
            state.LastShotFrame = Time.frameCount;
            state.LastFireIndex = fireIndex;
            state.LastShotTime = now;
            state.LastRemainingAmmo = remainingAfterShot;
            if (fireIndex >= 0) BeginVolleyOutcome(state, fireIndex);
            state.PreviousMiss = false;
            state.PreviousArmorImpact = false;
            state.PreviousLimbHit = false;
            state.PreviousSurfaceImpact = false;
            state.PreviousBodyHit = false;
            state.PreviousRicochet = false;
            state.PreviousFragmentation = false;
            state.ChamberChecked = false;
            state.StanceChanged = false;
            return application;
        }
    }

    public static void Associate(Shot shot, EffectApplication application, bool bonusProjectile = false)
    {
        if (application.Definition is null)
        {
            return;
        }

        lock (Sync)
        {
            var stored = bonusProjectile ? application.AsBonusProjectile() : application;
            ShotApplications[shot] = stored;
            if (shot.Weapon is Weapon trackedWeapon)
            {
                var state = GetState(trackedWeapon.Id);
                var volley = BeginVolleyOutcome(state, shot.FireIndex);
                volley.Pending.Add(shot); volley.Finalized = false; volley.CompletedTime = -1f;
                state.PendingVolleys[shot.FireIndex] = volley;
                ActiveVolleyStates.Add(state);
            }
            if (!bonusProjectile && shot.Weapon is Weapon weapon)
            {
                ShotApplicationsByKey[ShotKey(weapon, shot.FireIndex)] = stored;
            }
        }
    }

    public static bool TryGetApplication(Shot shot, out EffectApplication application)
    {
        lock (Sync)
        {
            return ShotApplications.TryGetValue(shot, out application!);
        }
    }

    public static void Release(Shot shot)
    {
        lock (Sync)
        {
            ShotApplications.Remove(shot);
            if (shot.Weapon is Weapon weapon && States.TryGetValue(weapon.Id, out var state) && state.Volleys.TryGetValue(shot.FireIndex, out var volley))
            {
                if (volley.Pending.Remove(shot) && volley.Pending.Count == 0 && !volley.Finalized) volley.CompletedTime = Time.time;
            }
        }
    }

    public static void RecordImpact(Shot shot)
    {
        if (shot.Weapon is not Weapon weapon || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            var volley = BeginVolleyOutcome(state, shot.FireIndex);
            if (volley.Generation != state.ReloadGeneration) return;
            if (shot.HittedBallisticCollider is BodyPartCollider body)
            {
                volley.AnyBodyHit = true;
                volley.ResolvedMiss = false;
                if (volley.TargetId.Length == 0)
                {
                    volley.TargetId = body.PlayerProfileID ?? string.Empty;
                    volley.BodyPart = body.BodyPartType.ToString();
                }
                state.PreviousMiss = false;
                state.PreviousBodyHit = true;
                state.PreviousSurfaceImpact = false;
                state.PreviousArmorImpact |= body is ArmorPlateCollider;
                state.PreviousLimbHit |= IsLimb(body.BodyPartType.ToString());
                if (body is ArmorPlateCollider && !volley.ArmorCounted)
                {
                    state.ArmorHits++;
                    volley.ArmorCounted = true;
                }
                if (TryGetApplication(shot, out var application) && application.SpecialId == "compound_interest") volley.CompoundHit = true;
                RecalculateStreaks(state);
                if (TryGetApplication(shot, out var targetApplication) && targetApplication.SpecialId == "wave015_debt_collector" && state.SameTargetHits >= 4) state.DebtCollectorActive = true;
            }
            else volley.AnySurfaceImpact |= shot.HittedBallisticCollider is not null;
            state.PreviousRicochet |= shot.BulletState == Shot.EBulletState.RicochetHit;
            state.PreviousFragmentation |= shot.BulletState == Shot.EBulletState.FragmentationHit || shot.Fragments.Count > 1;
        }
    }

    public static void RecordMiss(Shot shot)
    {
        if (shot.Weapon is not Weapon weapon || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var volley = BeginVolleyOutcome(GetState(weapon.Id), shot.FireIndex);
            if (volley.Pending.Count == 0 && volley.CompletedTime < 0f) volley.CompletedTime = Time.time;
            // Pending siblings must finish before a cartridge can be a miss.
            AdvancedLegendaryActions.DeferMissResolution(weapon.Id, shot.FireIndex);
        }
    }

    public static void ResolveDeferredMiss(string weaponId, int fireIndex)
    {
        lock (Sync)
        {
            if (!States.TryGetValue(weaponId, out var state) || !state.Volleys.TryGetValue(fireIndex, out var volley) ||
                volley.Generation != state.ReloadGeneration || volley.AnyBodyHit || volley.Pending.Count > 0) return;
            volley.ResolvedMiss = true;
            state.PreviousMiss = true;
            state.PreviousBodyHit = false;
            state.PreviousLimbHit = false;
            state.PreviousArmorImpact = false;
            state.PreviousSurfaceImpact = volley.AnySurfaceImpact;
            RecalculateStreaks(state);
        }
    }

    public static void MaintainVolleys()
    {
        lock (Sync)
        {
            foreach (var state in ActiveVolleyStates.ToArray())
            {
                foreach (var pair in state.PendingVolleys.ToArray())
                {
                    var volley = pair.Value;
                    if (Time.time - volley.CreatedTime > 60f)
                    {
                        foreach (var projectile in volley.Pending) ShotApplications.Remove(projectile);
                        volley.Pending.Clear();
                        volley.CompletedTime = Time.time - 1f;
                    }
                    if (!volley.Finalized && volley.Pending.Count == 0 && volley.CompletedTime >= 0f && Time.time - volley.CompletedTime >= 0.05f)
                    {
                        volley.Finalized = true;
                        ResolveDeferredMiss(state.WeaponId, pair.Key);
                        CleanupShotApplication(state.WeaponId, pair.Key);
                        state.PendingVolleys.Remove(pair.Key);
                    }
                }
                if (state.PendingVolleys.Count == 0) ActiveVolleyStates.Remove(state);
                // Keep a bounded outcome history, not references to finished bullets.
                if (state.Volleys.Count > 1024)
                    foreach (var pair in state.Volleys.Where(p => p.Value.Finalized).OrderBy(p => p.Value.Sequence).Take(state.Volleys.Count - 1024).ToArray())
                        state.Volleys.Remove(pair.Key);
            }
        }
    }

    public static void CleanupShotApplication(string weaponId, int fireIndex)
    {
        lock (Sync)
        {
            if (!States.TryGetValue(weaponId, out var state) || !state.Volleys.TryGetValue(fireIndex, out var volley) || volley.Pending.Count == 0)
                ShotApplicationsByKey.Remove($"{weaponId}:{fireIndex}");
        }
    }

    public static void ApplyFleshHitEffects(
        Weapon? weapon,
        int fireIndex,
        bool penetrated,
        string targetProfileId,
        string bodyPart,
        ref float heavyBleedingDelta)
    {
        if (weapon is null)
        {
            return;
        }

        lock (Sync)
        {
            var key = ShotKey(weapon, fireIndex);
            if (!ShotApplicationsByKey.TryGetValue(key, out var application) || !penetrated)
            {
                return;
            }
            var state = GetState(weapon.Id);
            var volley = BeginVolleyOutcome(state, fireIndex);
            if (volley.FleshCounted || volley.Generation != state.ReloadGeneration) return;
            volley.FleshCounted = true;
            if (application.SpecialId == "wave015_dont_stop")
            {
                state.AmmoTransferFleshHits++;
                if (state.AmmoTransferFleshHits % 3 == 0)
                {
                    AdvancedLegendaryActions.TransferMatchingRound(
                        GetPlayer(state.PlayerProfileId),
                        weapon,
                        application.AmmoTemplateId);
                }
            }

            var conditionalTargetMatches = string.IsNullOrEmpty(application.ConditionalTargetProfileId) ||
                string.Equals(application.ConditionalTargetProfileId, targetProfileId, StringComparison.Ordinal);
            var conditionalBodyPartMatches = string.IsNullOrEmpty(application.ConditionalBodyPart) ||
                string.Equals(application.ConditionalBodyPart, bodyPart, StringComparison.Ordinal);
            if (application.ForceHeavyBleedOnFleshHit && conditionalTargetMatches && conditionalBodyPartMatches)
            {
                heavyBleedingDelta = 1f;
            }
            if (application.HeavyBleedEveryFleshHits > 0)
            {
                state.FleshHitsInMagazine++;
                if (state.FleshHitsInMagazine % application.HeavyBleedEveryFleshHits == 0)
                {
                    heavyBleedingDelta = 1f;
                }
            }
        }
    }

    public static void ObserveHeldWeapon(Player? player)
    {
        if (player is null)
        {
            return;
        }

        var profileId = player.ProfileId;
        var weapon = player.HandsController?.Item as Weapon;
        var weaponId = weapon?.Id ?? string.Empty;
        lock (Sync)
        {
            if (HeldWeaponsByPlayer.TryGetValue(profileId, out var previous) &&
                string.Equals(previous, weaponId, StringComparison.Ordinal))
            {
                return;
            }

            HeldWeaponsByPlayer[profileId] = weaponId;
            if (weapon is null || !RarityResolver.TryGetDefinition(weapon, out var definition))
            {
                return;
            }

            var state = GetState(weapon.Id);
            state.PlayerProfileId = profileId;
            state.ShotsSinceDraw = 0;
            if (definition.CustomEffect?.Trigger == "draw_next_shot" ||
                (definition.SpecialId is "zantetsuken" or "wave015_one_and_done"))
            {
                state.DrawEffectArmed = true;
            }
        }
    }

    public static void NotifyChamberCheck(Weapon? weapon)
    {
        if (weapon is null || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            ObserveReloadBoundary(state, weapon);
            state.ChamberChecked = true;
            state.ChamberBuffActive = true;
            state.ReadyWallPassActive = true;
        }
    }

    public static void NotifyMagazineCheck(Weapon? weapon)
    {
        if (weapon is null || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            ObserveReloadBoundary(state, weapon);
            state.MagazineCheckActive = true;
            state.ShotsSinceMagazineCheck = 0;
        }
    }

    public static void NotifyReloadCompleted(Weapon? weapon, bool emergency)
    {
        if (weapon is null || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            CompleteReloadBoundary(state, weapon, emergency);
        }
    }

    public static void NotifyInspection(Weapon? weapon)
    {
        if (weapon is null || !RarityResolver.TryGetDefinition(weapon, out var definition)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            ObserveReloadBoundary(state, weapon);
            state.InspectionBuffActive = true;
            if (definition.SpecialId == "warranty_void") state.WarrantyFactoryTune = !state.WarrantyFactoryTune;
        }
    }

    public static void UpdateLiveState(Player? player, Weapon? weapon)
    {
        ObserveHeldWeapon(player);
        if (player is null || weapon is null || !RarityResolver.TryGetDefinition(weapon, out _)) return;
        lock (Sync)
        {
            var state = GetState(weapon.Id);
            state.PlayerProfileId = player.ProfileId;
            ObserveReloadBoundary(state, weapon);
            UpdateContinuousState(state, player, weapon, Time.time);
        }
    }

    public static StatModifiers GetStatModifiers(Weapon weapon)
    {
        return GetLiveApplication(weapon).StatModifiers;
    }

    public static float GetVelocityMultiplier(Weapon weapon) => GetLiveApplication(weapon).VelocityMultiplier;
    public static float GetReloadSpeedMultiplier(Weapon weapon) =>
        ElephantGunRules.ReloadSpeed(weapon, GetLiveApplication(weapon).ReloadSpeedMultiplier);

    private static EffectApplication GetLiveApplication(Weapon weapon)
    {
        if (!RarityResolver.TryGetDefinition(weapon, out var definition) || definition.EffectKind == "procedural")
        {
            return EffectApplication.Empty;
        }

        lock (Sync)
        {
            var state = GetState(weapon.Id);
            ObserveReloadBoundary(state, weapon);
            if (_shotScope?.Weapon == weapon) return _shotScope.Application;
            var now = Time.time;
            var player = GetPlayer(state.PlayerProfileId);
            var magazine = weapon.GetCurrentMagazine();
            UpdateContinuousState(state, player, weapon, now);
            var remaining = RemainingAmmo(weapon);
            var capacity = Capacity(weapon);
            var secondsSinceLastShot = state.LastShotTime < 0f ? float.MaxValue : now - state.LastShotTime;
            var preview = state.Clone();
            preview.TotalShots++;
            preview.ShotsInMagazine++;
            preview.UninterruptedShots++;
            var triggerPressed = player?.HandsController is Player.FirearmController controller && LegendaryFireModePatch.IsHeld(controller);
            preview.TriggerHeldShots = triggerPressed ? preview.TriggerHeldShots + 1 : definition.Automatic ? 1 : preview.UninterruptedShots;
            preview.ShotsSinceDraw++;
            var mode = LegendaryFireModePatch.LogicalMode(weapon);
            if (!string.Equals(preview.LastShotFireMode, mode, StringComparison.OrdinalIgnoreCase))
            { preview.LastShotFireMode = mode; preview.ShotsInCurrentFireMode = 0; }
            preview.ShotsInCurrentFireMode++;
            if (preview.MagazineCheckActive) preview.ShotsSinceMagazineCheck++;
            if ((player?.InputDirection.sqrMagnitude ?? 0f) > 0.0001f) preview.MovingShotsInMagazine++;
            var currentAmmo = weapon.CurrentAmmoTemplate;
            var currentAmmoSpeed = currentAmmo?.InitialSpeed ?? float.MaxValue;
            var context = new ShotContext(
                now,
                secondsSinceLastShot,
                remaining,
                capacity,
                player,
                IsSuppressed(weapon),
                currentAmmoSpeed <= 343f,
                definition.CaliberAverageVelocity > 0 && currentAmmoSpeed > definition.CaliberAverageVelocity,
                false,
                preview);
            var application = BuildApplication(definition, preview, context, currentAmmo, string.Empty);
            if (definition.SpecialId == "warranty_void")
            {
                application.StatModifiers = state.WarrantyFactoryTune
                    ? new StatModifiers(recoilDelta: -0.35f)
                    : new StatModifiers(fireRateMultiplier: 1.25f);
                application.Active = true;
            }

            return application;
        }
    }

    private static EffectApplication BuildApplication(
        VariantDefinition definition,
        WeaponState state,
        ShotContext context,
        AmmoTemplate? ammo,
        string ammoTemplateId)
    {
        if (definition.CustomEffect is not null)
        {
            return BuildCustomApplication(definition, state, context);
        }

        if (!string.IsNullOrEmpty(definition.SpecialId))
        {
            return BuildSpecialApplication(definition, state, context, ammo, ammoTemplateId);
        }

        if (definition.EffectKind == "launcher" || definition.EffectKind == "launcher_meme")
        {
            return BuildLauncherApplication(definition, state, context);
        }

        if (definition.EffectKind == "rocket" || definition.EffectKind == "rocket_meme")
        {
            return BuildRocketApplication(definition);
        }

        var active = definition.EffectKind == "meme"
            ? EvaluateMemeTrigger(definition, state, context)
            : EvaluateTrigger(definition, state, context);
        if (!active)
        {
            state.PayoffRampStacks = 0;
            return EffectApplication.Inactive(definition);
        }

        if (definition.EffectKind == "ordinary" && definition.PayoffIndex is 33 or 34)
        {
            state.PayoffRampStacks = context.SecondsSinceLastShot <= 0.5f && state.LastApplication?.Active == true
                ? state.PayoffRampStacks + 1
                : 1;
        }
        else
        {
            state.PayoffRampStacks = 0;
        }

        return definition.EffectKind == "meme"
            ? BuildMemePayoff(definition)
            : BuildOrdinaryPayoff(definition, state, context, ammo?.PenetrationPower ?? 0);
    }

    private static EffectApplication BuildCustomApplication(VariantDefinition definition, WeaponState state, ShotContext context)
    {
        var custom = definition.CustomEffect!;
        var triggerActive = custom.Trigger switch
        {
            "always" => true,
            "stationary" => context.StationarySeconds >= custom.Seconds,
            "stationary_next_shot" => context.StationarySeconds >= custom.Seconds && !state.StationaryEffectConsumed,
            "while_ads" => context.IsAiming,
            "hipfire" => !context.IsAiming,
            "suppressed" => context.Suppressed,
            "unsuppressed" => !context.Suppressed,
            "moving_input" => context.IsMovingInput,
            "fire_mode_fullauto" => string.Equals(context.FireMode, "fullauto", StringComparison.OrdinalIgnoreCase),
            "fire_mode_burst" => string.Equals(context.FireMode, "burst", StringComparison.OrdinalIgnoreCase),
            "draw_next_shot" => state.DrawEffectArmed,
            "ads_seconds" => context.IsAiming && context.AimSeconds >= custom.Seconds,
            "ads_seconds_next_shot" => context.IsAiming && context.AimSeconds >= custom.Seconds && !state.AdsEffectConsumed,
            "stamina_at_least" => context.Stamina >= custom.Threshold,
            "stamina_at_most" => context.Stamina <= custom.Threshold,
            "every_n_uninterrupted" => custom.Count > 0 && state.UninterruptedShots % custom.Count == 0,
            "uninterrupted_at_least" => (custom.RequireTriggerHeld ? state.TriggerHeldShots : state.UninterruptedShots) >= custom.Count,
            "consecutive_hits" => state.ConsecutiveHits >= custom.Count,
            "after_pause" => context.SecondsSinceLastShot >= custom.Seconds,
            "last_n_rounds" => context.RemainingAmmo > 0 && context.RemainingAmmo <= custom.Count,
            "last_round" => context.RemainingAmmo <= 1,
            "chamber_buff" => state.ChamberBuffActive,
            "chamber_next_shot" => state.ChamberChecked,
            "inspection_buff" => state.InspectionBuffActive,
            "magazine_check_first_n" => state.MagazineCheckActive && state.ShotsSinceMagazineCheck <= custom.Count,
            "previous_body_hit" => state.PreviousBodyHit,
            "every_n_shot" => custom.Count > 0 && state.TotalShots % custom.Count == 0,
            "every_n_magazine_shot" => custom.Count > 0 && state.ShotsInMagazine % custom.Count == 0,
            "every_n_moving_shot" => context.IsMovingInput && custom.Count > 0 && state.MovingShotsInMagazine % custom.Count == 0,
            "every_n_shot_ads" => context.IsAiming && custom.Count > 0 && state.TotalShots % custom.Count == 0,
            "prone" => context.IsProne,
            "stance_changed" => state.StanceChanged,
            "crouched_slow_walk" => context.IsCrouched && context.Speed <= 2f,
            "trigger_held" => context.IsTriggerPressed,
            _ => false,
        };
        if (custom.RequireTriggerHeld && !context.IsTriggerPressed)
        {
            triggerActive = false;
        }
        if (!triggerActive && custom.AlwaysRecoilDelta == 0f)
        {
            return EffectApplication.Inactive(definition);
        }

        var rampShots = custom.RampUsesTriggerHeldShots ? state.TriggerHeldShots : state.UninterruptedShots;
        var fireRateMultiplier = triggerActive ? custom.FireRateMultiplier : 1f;
        if (triggerActive && custom.FireRatePerShot > 0f)
        {
            fireRateMultiplier = 1f + Math.Min(custom.FireRateMaxBonus, custom.FireRatePerShot * Math.Max(1, rampShots));
        }
        var recoilDelta = custom.AlwaysRecoilDelta + (triggerActive ? custom.RecoilDelta : 0f);
        if (triggerActive && custom.RecoilReductionPerShot > 0f)
        {
            recoilDelta -= Math.Min(custom.RecoilReductionMax, custom.RecoilReductionPerShot * Math.Max(1, rampShots));
        }

        return new EffectApplication(definition)
        {
            Active = true,
            StatusActive = triggerActive,
            DamageMultiplier = triggerActive ? custom.DamageMultiplier : 1f,
            HeadDamageMultiplier = triggerActive ? custom.HeadDamageMultiplier : 1f,
            BodyDamageMultiplier = triggerActive ? custom.BodyDamageMultiplier : 1f,
            VelocityMultiplier = triggerActive ? custom.VelocityMultiplier : 1f,
            PenetrationMultiplier = triggerActive ? custom.PenetrationMultiplier : 1f,
            ArmorDamageMultiplier = triggerActive ? custom.ArmorDamageMultiplier : 1f,
            RicochetChanceBonus = triggerActive ? custom.RicochetChanceBonus : 0f,
            FragmentationChanceBonus = triggerActive ? custom.FragmentationChanceBonus : 0f,
            RicochetRetention = triggerActive ? custom.RicochetRetention : 0f,
            ProjectileCount = triggerActive ? Math.Max(1, custom.ProjectileCount) : 1,
            ProjectileDamageMultiplier = triggerActive ? custom.ProjectileDamageMultiplier : 1f,
            ProjectileSpreadDegrees = triggerActive ? custom.ProjectileSpreadDegrees : 0f,
            ProjectilePattern = custom.ProjectilePattern,
            ExplosionDamageFactor = triggerActive ? custom.ExplosionDamageFactor : 0f,
            ExplosionRadius = triggerActive ? custom.ExplosionRadius : 0f,
            ReloadSpeedMultiplier = triggerActive ? custom.ReloadSpeedMultiplier : 1f,
            StatModifiers = new StatModifiers(
                recoilDelta: recoilDelta,
                ergonomicsDelta: triggerActive ? custom.ErgonomicsDelta : 0f,
                accuracyDelta: triggerActive ? custom.AccuracyDelta : 0f,
                fireRateMultiplier: fireRateMultiplier),
        };
    }

    private static EffectApplication BuildSpecialApplication(
        VariantDefinition definition,
        WeaponState state,
        ShotContext context,
        AmmoTemplate? ammo,
        string ammoTemplateId)
    {
        var ammoPenetration = ammo?.PenetrationPower ?? 0;
        var application = new EffectApplication(definition) { Active = true, SpecialId = definition.SpecialId };
        switch (definition.SpecialId)
        {
            case "elephant_gun" when ElephantGunRules.IsSlug(ammo):
                application.DamageMultiplier = 7f;
                application.StatModifiers = new StatModifiers(recoilDelta: 3f);
                application.ReloadSpeedMultiplier = 0.40f;
                break;
            case "budget_king" when ammoPenetration <= 17:
                application.HeavyBleedEveryFleshHits = 3;
                break;
            case "frankenstein" when context.Suppressed:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.30f, fireRateMultiplier: 1.25f);
                break;
            case "frankenstein":
                application.DamageMultiplier = 1.30f;
                application.PenetrationMultiplier = 1.30f;
                break;
            case "rush_b" when context.IsMovingInput:
                application.PenetrationMultiplier = 1.30f;
                application.StatModifiers = new StatModifiers(recoilDelta: 0.20f, fireRateMultiplier: 1.50f);
                if (state.MovingShotsInMagazine % 2 == 0)
                {
                    application.ProjectileCount = 2;
                    application.ProjectileDamageMultiplier = 1f;
                }
                break;
            case "kabans_kabab":
                application.StatModifiers = new StatModifiers(recoilDelta: -0.50f, fireRateMultiplier: 1.50f);
                if (state.ShotsInMagazine % 10 == 0)
                {
                    application.ExplosionDamageFactor = 0.10f;
                    application.ExplosionRadius = 1.5f;
                }
                break;
            case "zantetsuken":
                application.StatModifiers = new StatModifiers(recoilDelta: 1f);
                if (state.DrawEffectArmed)
                {
                    application.DamageMultiplier = 5f;
                    application.VelocityMultiplier = 3f;
                }
                break;
            case "bunker_buster":
                application.DamageMultiplier = 1.50f;
                application.PenetrationMultiplier = 1.50f;
                break;
            case "requiem":
                // RemainingAmmo includes the cartridge being fired. The final-round
                // package replaces the normal signature bonuses rather than stacking.
                var finalCylinderRound = context.RemainingAmmo == 1;
                application.DamageMultiplier = finalCylinderRound ? 2.50f : 1.75f;
                application.PenetrationMultiplier = finalCylinderRound ? 1.75f : 1.50f;
                break;
            case "fifth_amendment" when state.TotalShots % 5 == 0:
                application.DamageMultiplier = 1.10f;
                application.ExplosionDamageFactor = 0.15f;
                application.ExplosionRadius = 2f;
                break;
            case "opening_statement" when state.ShotsInMagazine <= 4:
                application.DamageMultiplier = 2f;
                application.PenetrationMultiplier = 1.25f;
                break;
            case "compound_interest":
                application.DamageMultiplier = 1f + state.CompoundInterestStacks * 0.10f;
                break;
            case "full_send":
                state.FullSendStacks = Math.Min(6, state.FullSendStacks + 1);
                application.StatModifiers = new StatModifiers(recoilDelta: state.FullSendStacks * -0.05f, fireRateMultiplier: 1f + state.FullSendStacks * 0.05f);
                break;
            case "cold_start" when state.TotalShots % 10 == 0:
                application.DamageMultiplier = 1.75f;
                application.PenetrationMultiplier = 1.25f;
                break;
            case "mood_swing" when state.ShotsInMagazine % 2 == 1:
                application.DamageMultiplier = 1.60f;
                application.PenetrationMultiplier = 1.25f;
                break;
            case "mood_swing":
                application.RicochetChanceBonus = 0.50f;
                application.FragmentationChanceBonus = 0.50f;
                break;
            case "split_decision":
                application.ProjectileCount = 2;
                application.ProjectileDamageMultiplier = 1f;
                application.ProjectileSpreadDegrees = 1.35f;
                break;
            case "mp7a2_volks_tribute":
                application.ProjectileCount = 2;
                application.ProjectileDamageMultiplier = 1f;
                application.ProjectileSpreadDegrees = 1.35f;
                break;
            case "redacted" when context.Suppressed:
                application.DamageMultiplier = 1.45f;
                application.PenetrationMultiplier = 1.25f;
                break;
            case "executive_order" when context.IsAiming && context.AimSeconds >= 3f:
                application.DamageMultiplier = 1.50f;
                application.VelocityMultiplier = 1.40f;
                application.PenetrationMultiplier = 1.20f;
                break;
            case "adar_fourth_double" when state.TotalShots % 4 == 0:
                application.ProjectileCount = 2;
                application.ProjectileDamageMultiplier = 1f;
                break;
            case "ak101_ads_overpressure" when context.IsAiming && context.AimSeconds >= 5f:
                application.VelocityMultiplier = 1.50f;
                application.PenetrationMultiplier = 1.30f;
                break;
            case "ak101_low_profile" when context.IsCrouched && context.Speed <= 2f:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.30f);
                break;
            case "ak101_belly_down" when context.IsProne:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.20f, fireRateMultiplier: 1.30f);
                break;
            case "ak101_rooted_can_opener" when context.StationarySeconds >= 10f:
                application.ArmorDamageMultiplier = 1.50f;
                application.PenetrationMultiplier = 1.20f;
                break;
            case "ak101_chamber_velocity" when state.ChamberBuffActive:
                application.VelocityMultiplier = 1.60f;
                break;
            case "ak102_tenth_explosion" when state.TotalShots % 10 == 0:
                application.ExplosionDamageFactor = 0.35f;
                application.ExplosionRadius = 2f;
                break;
            case "ak102_tenth_ammo_savant" when state.TotalShots % 10 == 0:
                if (ammoPenetration < 40) application.DamageMultiplier = 1.45f;
                else application.StatModifiers = new StatModifiers(recoilDelta: -0.40f);
                break;
            case "ak102_tenth_control" when state.TotalShots % 10 == 0:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.40f);
                application.VelocityMultiplier = 1.40f;
                break;
            case "ak103_sighted_damage" when context.IsAiming && state.TriggerHeldShots >= 7:
                application.DamageMultiplier = 1.40f;
                application.PenetrationMultiplier = 1.15f;
                break;
            case "ak103_sighted_armor" when context.IsAiming && state.TriggerHeldShots >= 8:
                application.PenetrationMultiplier = 1.25f;
                application.ArmorDamageMultiplier = 1.60f;
                break;
            case "ak103_tactical_frag" when state.EmergencyReloadActive:
                application.DamageMultiplier = 1.25f;
                application.FragmentationChanceBonus = 0.25f;
                break;
            case "ak105_alternating" when state.ShotsInMagazine % 2 == 1:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.40f);
                break;
            case "ak105_alternating":
                application.DamageMultiplier = 1.30f;
                break;
            case "ak105_ammo_savant":
                if (ammoPenetration < 40) application.DamageMultiplier = 1.50f;
                else application.StatModifiers = new StatModifiers(recoilDelta: -0.30f);
                break;
            case "ak105_ads_recoil" when context.IsAiming && context.AimSeconds >= 7f:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.45f);
                break;
            case "ak545_wall_pass":
                application.DamageMultiplier = 1.25f;
                application.ArmorDamageMultiplier = 1.30f;
                break;
            case "ak545_ready_wall_pass" when state.ReadyWallPassActive:
                application.DamageMultiplier = 1.35f;
                application.ArmorDamageMultiplier = 1.35f;
                break;
            case "ak545_empty_ramp" when state.ReloadMomentumAvailable:
                application.StatModifiers = new StatModifiers(fireRateMultiplier: 1f + Math.Min(0.50f, state.UninterruptedShots * 0.05f));
                break;
            case "ak545_fresh_fan" when state.HasReloadHistory && state.ShotsInMagazine <= 6:
                application.ProjectileCount = 3;
                application.ProjectileDamageMultiplier = 0.70f;
                application.ProjectileSpreadDegrees = 1.25f;
                break;
            case "ak74m_hip_control" when !context.IsAiming:
                application.StatModifiers = new StatModifiers(recoilDelta: -0.20f, accuracyDelta: -0.40f);
                break;
            case "ak74n_fifth_wall" when state.TotalShots % 5 == 0:
                application.DamageMultiplier = 2f;
                application.ArmorDamageMultiplier = 1.40f;
                break;
            case "ak74n_suppressed_ergo" when context.Suppressed:
                application.StatModifiers = new StatModifiers(ergonomicsDelta: 0.60f);
                break;
            case "ak74n_chamber_penetration" when state.ChamberBuffActive:
                application.PenetrationMultiplier = 1.40f;
                break;

            case "wave015_spit_it_out" when state.TriggerHeldShots is >= 1 and <= 5:
                application.StatModifiers = new StatModifiers(fireRateMultiplier: 1.80f);
                break;
            case "wave015_core_and_jacket":
                application.ProjectileCount = 2;
                application.PenetrationMultiplier = 1.50f;
                application.BonusProjectileDamageScale = 1.50f;
                application.BonusProjectilePenetrationScale = 2f / 3f;
                break;
            case "wave015_echo":
                application.ProjectileCount = 2;
                application.BonusProjectileVelocityScale = 0.65f;
                break;
            case "wave015_like_a_ghost":
            case "wave015_binary_hunter":
            case "wave015_two_in_one":
            case "wave015_balance":
                break;
            case "wave015_high_noon" when IsFireMode(context, "burst"):
                // ShotContext preserves the selected High Noon mode while native
                // revolver operations temporarily use the double-action carrier.
                application.StatModifiers = new StatModifiers(recoilDelta: -0.75f);
                break;
            case "wave015_dont_stop":
                application.AmmoTemplateId = ammoTemplateId;
                break;
            case "wave015_gift_basket" when IsFireMode(context, "burst") && (ammo?.ProjectileCount ?? 1) > 1:
            case "wave015_buccaneer" when IsFireMode(context, "burst") && (ammo?.ProjectileCount ?? 1) > 1:
                application.ProjectileCount = 2;
                application.BonusProjectilePenetrationScale = 2f;
                application.BonusProjectileDamageScale = 1.50f;
                application.CenterBonusOnShell = true;
                break;
            case "wave015_pinpoint" when (ammo?.ProjectileCount ?? 1) > 1:
                application.StatModifiers = new StatModifiers(
                    accuracyDelta: -Math.Min(0.80f, state.TriggerHeldShots * 0.20f));
                break;
            case "wave015_buckslugs" when (ammo?.ProjectileCount ?? 1) > 1 && state.ShotsInMagazine % 2 == 1:
                application.PenetrationMultiplier = 1.50f;
                application.StatModifiers = new StatModifiers(accuracyDelta: -0.60f);
                break;
            case "wave015_buckslugs" when (ammo?.ProjectileCount ?? 1) > 1:
                application.DamageMultiplier = 1.50f;
                application.StatModifiers = new StatModifiers(accuracyDelta: 0.60f);
                break;
            case "wave015_out_with_a_bang" when IsFireMode(context, "doublet"):
                application.ExplosionDamageFactor = 0.10f;
                application.ExplosionRadius = 1.25f;
                application.StatModifiers = new StatModifiers(recoilDelta: 3f, accuracyDelta: 0.90f);
                break;
            case "wave015_running_start" when context.IsMovingInput && state.TriggerHeldShots > 5:
                application.StatModifiers = new StatModifiers(
                    fireRateMultiplier: 1f + Math.Min(0.42f, (state.TriggerHeldShots - 5) * 0.06f));
                break;
            case "wave015_fifth_beat" when state.TriggerHeldShots > 0 && state.TriggerHeldShots % 5 == 0:
                application.DamageMultiplier = 2.25f;
                application.PenetrationMultiplier = 1.50f;
                break;
            case "wave015_hammer_pair" when IsFireMode(context, "burst"):
                application.StatModifiers = new StatModifiers(fireRateMultiplier: 2f);
                if (state.ShotsInCurrentFireMode % 2 == 0) application.DamageMultiplier = 1.50f;
                break;
            case "wave015_short_notice" when state.ShotsSinceDraw is >= 1 and <= 3:
                application.DamageMultiplier = 1.75f;
                application.PenetrationMultiplier = 1.50f;
                break;
            case "wave015_pressure_switch" when IsFireMode(context, "single"):
                application.PenetrationMultiplier = 1.60f;
                break;
            case "wave015_pressure_switch" when IsFireMode(context, "fullauto"):
                application.DamageMultiplier = 1.35f;
                application.StatModifiers = new StatModifiers(recoilDelta: -0.30f);
                break;
            case "wave015_debt_collector" when state.DebtCollectorActive:
                application.DamageMultiplier = 1.35f;
                break;
            case "wave015_overmatch" when state.PreviousArmorImpact:
                application.PenetrationMultiplier = 2f;
                application.DamageMultiplier = 1.50f;
                break;
            case "wave015_six_pack" when state.ShotsInMagazine % 6 == 0:
                application.ProjectileCount = 3;
                application.ProjectileDamageMultiplier = 0.70f;
                application.ProjectileSpreadDegrees = 1.5f;
                break;
            case "wave015_half_measures" when context.RemainingAmmo <= Math.Ceiling(context.Capacity / 2f):
                application.DamageMultiplier = 1.50f;
                application.PenetrationMultiplier = 1.30f;
                break;
            case "wave015_quiet_exit" when context.RemainingAmmo is > 0 and <= 3:
                application.ProjectileCount = 2;
                break;
            case "wave015_fast_three" when IsFireMode(context, "burst"):
                application.StatModifiers = new StatModifiers(fireRateMultiplier: 4f / 3f);
                if (state.ShotsInCurrentFireMode % 3 == 0)
                {
                    application.ExplosionDamageFactor = 0.10f;
                    application.ExplosionRadius = 1.25f;
                }
                break;
            case "wave015_runaway" when state.TriggerHeldShots >= 5:
                application.DamageMultiplier = 1.25f;
                application.StatModifiers = new StatModifiers(fireRateMultiplier: 1.50f);
                break;
            case "wave015_fifty_fifty" when context.RemainingAmmo > context.Capacity / 2f:
                application.PenetrationMultiplier = 1.50f;
                break;
            case "wave015_fifty_fifty":
                application.DamageMultiplier = 1.50f;
                break;
            case "wave015_hard_stop" when context.RemainingAmmo is > 0 and <= 4:
                application.DamageMultiplier = 2f;
                application.ForceHeavyBleedOnFleshHit = true;
                break;
            case "wave015_walking_fire" when context.IsMovingInput && state.TriggerHeldShots > 5:
                application.StatModifiers = new StatModifiers(
                    accuracyDelta: -Math.Min(0.70f, (state.TriggerHeldShots - 5) * 0.10f));
                break;
            case "wave015_tenth_man" when state.ShotsInMagazine % 10 == 0:
                application.ProjectileCount = 5;
                application.ProjectileDamageMultiplier = 0.70f;
                application.ProjectileSpreadDegrees = 3f;
                break;
            case "wave015_long_shift" when state.TriggerHeldShots > 5:
                application.DamageMultiplier = 1f + Math.Min(0.75f, (state.TriggerHeldShots - 5) * 0.05f);
                break;
            case "wave015_sight_picture" when context.IsAiming && context.AimSeconds >= 4f && context.StationarySeconds >= 4f && !state.AdsEffectConsumed:
                application.ProjectileCount = 2;
                break;
            case "wave015_accrued_interest" when state.SameTargetHits > 0:
                application.ConditionalTargetProfileId = state.LastTargetProfileId;
                application.ConditionalDamageMultiplier = 1f + Math.Min(1f, state.SameTargetHits * 0.25f);
                break;
            case "wave015_third_reading" when state.SameTargetHits > 0 && (state.SameTargetHits + 1) % 3 == 0:
                application.ConditionalTargetProfileId = state.LastTargetProfileId;
                application.ConditionalDamageMultiplier = 2.50f;
                application.ForceHeavyBleedOnFleshHit = true;
                break;
            case "wave015_one_and_done" when state.DrawEffectArmed:
                application.DamageMultiplier = 2.50f;
                application.PenetrationMultiplier = 2f;
                break;
            case "wave015_held_breath" when context.IsAiming && context.AimSeconds >= 4f && context.StationarySeconds >= 4f && !state.AdsEffectConsumed:
                application.DamageMultiplier = 2f;
                application.PenetrationMultiplier = 1.75f;
                break;
            case "wave015_second_opinion" when state.PreviousBodyHit:
                application.ProjectileCount = 2;
                break;
            case "wave015_switch_hitter" when IsFireMode(context, "single") && (ammo?.ProjectileCount ?? 1) > 1:
                application.PenetrationMultiplier = 1.60f;
                break;
            case "wave015_switch_hitter" when IsFireMode(context, "semiauto"):
                application.StatModifiers = new StatModifiers(recoilDelta: -0.40f, accuracyDelta: -0.50f);
                break;
            case "wave015_door_tax" when (ammo?.ProjectileCount ?? 1) > 1 && state.ShotsInMagazine % 3 == 0:
                application.ProjectileCount = (ammo?.ProjectileCount ?? 1) + 1;
                application.ProjectileDamageMultiplier = 0.70f;
                application.ProjectileSpreadDegrees = 3f;
                application.ProjectilePattern = "random_cone";
                break;
            case "wave015_five_chamber_forecast":
                var chamberStep = (state.ShotsInMagazine - 1) % 5 + 1;
                application.DamageMultiplier = 1f + chamberStep * 0.25f;
                if (chamberStep == 5)
                {
                    application.ExplosionDamageFactor = 0.10f;
                    application.ExplosionRadius = 1.5f;
                }
                break;
            case "wave015_pocket_cannon" when context.RemainingAmmo <= 1:
                application.ProjectileCount = 4;
                application.ReplaceNaturalProjectiles = true;
                application.ProjectileDamageMultiplier = 0.70f;
                application.ProjectileSpreadDegrees = 2.5f;
                application.ProjectilePattern = "random_cone";
                break;
            case "wave015_center_mass" when state.SameBodyPartHits > 0:
                application.ConditionalTargetProfileId = state.LastBodyPartTargetProfileId;
                application.ConditionalBodyPart = state.LastBodyPart;
                application.ConditionalDamageMultiplier = 1f + Math.Min(1f, state.SameBodyPartHits * 0.25f);
                break;
            case "wave015_nine_count" when state.ShotsInMagazine % 9 == 0:
                application.DamageMultiplier = 3f;
                application.ForceHeavyBleedOnFleshHit = true;
                break;
        }

        application.Active = application.HasMechanicalEffect ||
            definition.SpecialId.StartsWith("wave015_", StringComparison.Ordinal) ||
            definition.SpecialId is "compound_interest" or "full_send" or "warranty_void" or "budget_king";
        return application;
    }

    private static bool IsFireMode(ShotContext context, string mode) =>
        string.Equals(context.FireMode, mode, StringComparison.OrdinalIgnoreCase);

    private static bool EvaluateTrigger(VariantDefinition definition, WeaponState state, ShotContext context)
    {
        var key = definition.RuntimeKey;
        switch (definition.TriggerIndex)
        {
            case 0: return state.ReloadMomentumAvailable && state.ShotsInMagazine <= definition.Number(key, 2, 5);
            case 1: return state.MagazineCheckActive && state.ShotsSinceMagazineCheck <= definition.Number(key, 2, 4);
            case 2: return context.SecondsSinceLastShot >= definition.Number(key, 2, 4);
            case 3: return state.HasReloadHistory && state.ShotsInMagazine <= definition.Number(key, 2, 6);
            case 4: return context.RemainingAmmo > 0 && context.RemainingAmmo <= 4;
            case 5: return context.RemainingAmmo <= 1;
            case 6: return state.TotalShots % definition.Number(key, 4, 9) == 0;
            case 7: return definition.Automatic ? state.UninterruptedShots >= 5 : state.ShotsInMagazine >= 5;
            case 8: return state.ConsecutiveHits >= definition.Number(key, 3, 6);
            case 9: return state.PreviousMiss;
            case 10: return state.PreviousArmorImpact;
            case 11: return true;
            case 12: return state.ShotsInMagazine % definition.Number(key, 3, 5) == 0;
            case 13: return context.IsAiming;
            case 14: return context.IsAiming && context.AimSeconds >= definition.Number(key, 3, 7);
            case 15: return !context.IsAiming && context.SecondsSinceAimEnded <= 1f;
            case 16: return !context.IsAiming;
            case 17: return context.IsCrouched && context.Speed <= 2f;
            case 18: return context.IsProne;
            case 19: return context.StationarySeconds >= definition.Number(key, 3, 10);
            case 20: return context.Speed > 0.1f && context.Speed <= 3f;
            case 21: return context.Stamina >= 0.75f;
            case 22: return context.Stamina <= 0.35f;
            case 23: return state.StanceChanged;
            case 24: return state.UninterruptedShots % definition.Number(key, 7, 10) == 0;
            case 25:
                var armorTarget = definition.Number(key, 2, 5);
                if (state.ArmorHits < armorTarget) return false;
                state.ArmorHits = 0;
                return true;
            case 26: return state.PreviousBodyHit;
            case 27: return state.PreviousLimbHit;
            case 28: return context.Suppressed;
            case 29: return !context.Suppressed;
            case 30: return context.Subsonic;
            case 31: return context.AboveCaliberAverageVelocity;
            case 32: return state.PreviousRicochet;
            case 33: return state.PreviousFragmentation;
            case 34:
                return context.IsAiming && (definition.Automatic ? state.TriggerHeldShots : state.UninterruptedShots) >= definition.Number(key, 5, 10);
            case 35: return state.UninterruptedShots >= 2 && context.SecondsSinceLastShot <= 0.35f;
            case 36:
                return context.SecondsSinceLastShot >= definition.Number(key, 1, 2);
            case 37: return state.ReadyWallPassActive;
            case 38: return state.ChamberBuffActive;
            case 39: return context.RemainingAmmo >= context.Capacity * 0.75f;
            case 40: return context.RemainingAmmo <= context.Capacity * 0.25f;
            default: return false;
        }
    }

    private static bool EvaluateMemeTrigger(VariantDefinition definition, WeaponState state, ShotContext context)
    {
        var key = definition.RuntimeKey;
        return definition.TriggerIndex switch
        {
            0 => state.InspectionBuffActive,
            1 => context.RemainingAmmo <= 1,
            2 => state.TotalShots % definition.Number(key, 7, 10) == 0,
            3 => state.ChamberBuffActive,
            4 => state.TotalShots % definition.Number(key, 7, 10) == 0,
            5 => state.EmergencyReloadActive,
            6 => state.SelectorBuffActive,
            7 => context.StationarySeconds >= 3f,
            8 => state.ConsecutiveHits >= definition.Number(key, 3, 6),
            9 => context.IsCrouched,
            10 => context.IsAiming && state.TotalShots % definition.Number(key, 6, 10) == 0,
            11 => state.UninterruptedShots >= definition.Number(key, 16, 30),
            12 => context.IsAiming,
            _ => false,
        };
    }

    private static EffectApplication BuildOrdinaryPayoff(VariantDefinition d, WeaponState state, ShotContext c, int ammoPenetration)
    {
        var a = new EffectApplication(d) { Active = true };
        var useSecondaryPayoff = d.TriggerIndex == 11 && d.SecondaryPayoffIndex >= 0 && state.ShotsInMagazine % 2 == 0;
        var k = useSecondaryPayoff ? d.RuntimeKey + "-secondary" : d.RuntimeKey;
        var payoffIndex = useSecondaryPayoff
            ? d.SecondaryPayoffIndex
            : d.PayoffIndex;
        float Percent(string key, int min, int max) => d.StepNumber(key, min, max) / 100f;
        switch (payoffIndex)
        {
            case 0: a.DamageMultiplier = 1f + Percent(k, 40, 65); a.PenetrationMultiplier = 1f + Percent(k + "p", 12, 20); break;
            case 1: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k, 28, 45)); break;
            case 2: a.StatModifiers = new StatModifiers(ergonomicsDelta: d.Number(k, 25, 40) / 100f); break;
            case 3: a.StatModifiers = new StatModifiers(accuracyDelta: -Percent(k, 35, 55)); break;
            case 4: a.VelocityMultiplier = 1f + Percent(k, 25, 40); a.PenetrationMultiplier = 1f + Percent(k + "p", 12, 20); break;
            case 5: a.DamageMultiplier = 1f + Percent(k, 40, 65); a.FragmentationChanceBonus = Percent(k + "f", 20, 35); break;
            case 6: a.StatModifiers = new StatModifiers(fireRateMultiplier: 1f + Percent(k, 20, 35)); break;
            case 7: a.DamageMultiplier = 1f + Percent(k, 40, 65); a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 20, 35)); break;
            case 8: a.PenetrationMultiplier = 1f + Percent(k, 22, 38); a.ArmorDamageMultiplier = 1f + Percent(k + "a", 35, 60); break;
            case 9: a.FragmentationChanceBonus = Percent(k, 25, 40); a.DamageMultiplier = 1f + Percent(k + "d", 40, 65); break;
            case 10: a.RicochetChanceBonus = Percent(k, 45, 70); a.RicochetRetention = Percent(k + "d", 70, 90); break;
            case 11: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 22, 35)); a.DamageMultiplier = 1.10f; break;
            case 12: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 22, 35)); a.PenetrationMultiplier = 1.10f; break;
            case 13: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "f", 20, 35)); a.FragmentationChanceBonus = 0.15f; break;
            case 14: a.ExplosionDamageFactor = Math.Max(.10f, Percent(k, 65, 100) - .60f); a.ExplosionRadius = d.Decimal(k + "r", 15, 23); break;
            case 15: a.ProjectileCount = 3; a.ProjectileDamageMultiplier = 0.70f; a.ProjectileSpreadDegrees = 1.25f; break;
            case 16: a.ProjectileCount = 5; a.ProjectileDamageMultiplier = 0.70f; a.ProjectileSpreadDegrees = 2.25f; a.ProjectilePattern = "random_cone"; break;
            case 17: a.ProjectileCount = 3; a.ProjectileDamageMultiplier = 0.70f; a.ProjectileSpreadDegrees = 0.75f; break;
            case 18: a.ProjectileCount = 2; a.ProjectileDamageMultiplier = 1f; a.ProjectileSpreadDegrees = d.Decimal(k, 6, 12); break;
            case 19:
                if (state.TotalShots % 2 == 1) a.VelocityMultiplier = 1f + Percent(k, 30, 45);
                else a.DamageMultiplier = 1f + Percent(k + "d", 45, 65);
                break;
            case 20: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 20, 35), accuracyDelta: -Percent(k, 40, 60)); a.VelocityMultiplier = 1.10f; break;
            case 21: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 20, 35), accuracyDelta: -Percent(k, 40, 60)); a.DamageMultiplier = 1.10f; break;
            case 22: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 30, 48)); a.ArmorDamageMultiplier = 1.20f; break;
            case 23: a.ProjectileCount = 2; a.ProjectileDamageMultiplier = 1f; a.ProjectileSpreadDegrees = 0.25f; break;
            case 24: a.VelocityMultiplier = 1f + Percent(k, 30, 45); a.PenetrationMultiplier = 1f + Percent(k + "p", 15, 20); a.ArmorDamageMultiplier = 1.10f; break;
            case 25: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 30, 48)); a.VelocityMultiplier = 1.20f; break;
            case 26: a.ProjectileCount = 3; a.ProjectileDamageMultiplier = 0.70f; a.ProjectileSpreadDegrees = 1.5f; break;
            case 27:
                var emptyFactor = 1f - Mathf.Clamp01(c.RemainingAmmo / (float)c.Capacity);
                a.PenetrationMultiplier = 1f + Percent(k, 45, 70) * emptyFactor;
                break;
            case 28: a.DamageMultiplier = 1f + Percent(k, 40, 60); a.ArmorDamageMultiplier = 1f + Percent(k + "a", 35, 55); break;
            case 29: a.ArmorDamageMultiplier = 1f + Percent(k, 50, 80); a.PenetrationMultiplier = 1f + Percent(k + "p", 15, 25); break;
            case 30: a.DamageMultiplier = 1f + Percent(k, 50, 80); a.FragmentationChanceBonus = Percent(k + "f", 20, 35); a.ArmorDamageMultiplier = 1.20f; break;
            case 31: a.LimbDamageMultiplier = 1.35f + Percent(k, 0, 20); break;
            case 32: a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 20, 35)); a.PenetrationMultiplier = 1.15f; break;
            case 33:
                var fireStacks = state.PayoffRampStacks;
                var firePerShot = Percent(k, 5, 10);
                var fireCap = Percent(k + "s", 35, 55);
                a.StatModifiers = new StatModifiers(fireRateMultiplier: 1f + Math.Min(fireCap, firePerShot * fireStacks));
                break;
            case 34:
                var recoilStacks = state.PayoffRampStacks;
                var recoilPerShot = Percent(k, 5, 10);
                var recoilCap = Percent(k + "s", 35, 55);
                a.StatModifiers = new StatModifiers(recoilDelta: -Math.Min(recoilCap, recoilPerShot * recoilStacks));
                break;
            case 35:
                if (ammoPenetration < 40) a.DamageMultiplier = 1f + Percent(k, 45, 65);
                else a.StatModifiers = new StatModifiers(recoilDelta: -Percent(k + "r", 30, 45));
                break;
            case 36: a.DamageMultiplier = 1f + Percent(k, 35, 50); a.VelocityMultiplier = 1f + Percent(k + "v", 15, 25); break;
            case 37: a.PenetrationMultiplier = 1f + Percent(k, 20, 30); a.ArmorDamageMultiplier = 1f + Percent(k + "a", 40, 55); break;
            case 38: a.VelocityMultiplier = 1f + Percent(k, 25, 40); a.FragmentationChanceBonus = Percent(k + "f", 20, 30); break;
            case 39: a.DamageMultiplier = 1f + Percent(k, 45, 60); a.ArmorDamageMultiplier = 1f + Percent(k + "a", 30, 45); break;
            case 40: a.LimbDamageMultiplier = 1f + Percent(k, 30, 45); a.PenetrationMultiplier = 1f + Percent(k + "p", 15, 25); break;
        }

        if (d.TriggerIndex == 4) a.DamageMultiplier *= 2f;
        if (d.WeaponClass == "machinegun" && d.TriggerIndex == 5)
        {
            a.DamageMultiplier *= 2.5f;
            a.PenetrationMultiplier *= 1.40f;
        }
        return a;
    }

    private static EffectApplication BuildMemePayoff(VariantDefinition d)
    {
        var a = new EffectApplication(d) { Active = true };
        switch (d.PayoffIndex)
        {
            case 0: a.DamageMultiplier = 1.70f; break;
            case 1: a.VelocityMultiplier = 1.50f; break;
            case 2: a.PenetrationMultiplier = 1.45f; break;
            case 3: a.DamageMultiplier = 1.80f; break;
            case 4: a.StatModifiers = new StatModifiers(fireRateMultiplier: 1.40f); a.VelocityMultiplier = 1.45f; break;
            case 5: a.StatModifiers = new StatModifiers(recoilDelta: -0.60f); break;
            case 6: a.PenetrationMultiplier = 1.50f; a.ArmorDamageMultiplier = 1.85f; break;
            case 7: a.VelocityMultiplier = 1.50f; a.PenetrationMultiplier = 1.30f; break;
            case 8: a.VelocityMultiplier = 1.50f; a.PenetrationMultiplier = 1.30f; a.ArmorDamageMultiplier = 1.20f; break;
            case 9: a.DamageMultiplier = 1.70f; a.FragmentationChanceBonus = 0.45f; break;
            case 10: a.ArmorDamageMultiplier = 1.90f; a.PenetrationMultiplier = 1.30f; break;
            case 11: a.PenetrationMultiplier = 1.50f; a.DamageMultiplier = 1.75f; break;
            case 12: a.ArmorDamageMultiplier = 1.50f; break;
        }
        return a;
    }

    private static EffectApplication BuildLauncherApplication(VariantDefinition d, WeaponState state, ShotContext context)
    {
        var active = d.EffectKind == "launcher_meme" || EvaluateLauncherTrigger(d, state, context);
        if (!active) return EffectApplication.Inactive(d);
        var a = new EffectApplication(d) { Active = true };
        var payoff = d.PayoffIndex;
        switch (payoff)
        {
            case 0: a.VelocityMultiplier = 1.35f; break;
            case 1: a.DamageMultiplier = 1.65f; break;
            case 2: a.FragmentationChanceBonus = 0.50f; break;
            case 3: a.VelocityMultiplier = 1.25f; a.PenetrationMultiplier = 1.20f; break;
            case 4: a.StatModifiers = new StatModifiers(accuracyDelta: -0.45f); break;
            case 5: a.DamageMultiplier = 1.65f; a.PenetrationMultiplier = 1.25f; break;
            case 6: a.StatModifiers = new StatModifiers(recoilDelta: -0.45f); break;
            case 7: a.StatModifiers = new StatModifiers(accuracyDelta: -0.60f); break;
            case 8: a.ArmorDamageMultiplier = 1.70f; break;
        }
        return a;
    }

    private static bool EvaluateLauncherTrigger(VariantDefinition d, WeaponState state, ShotContext c)
    {
        if (d.Automatic)
        {
            return d.TriggerIndex switch
            {
                0 => state.UninterruptedShots >= 4,
                1 => state.TotalShots % 5 == 0,
                2 => state.UninterruptedShots >= 6,
                3 => state.TotalShots % 8 == 0,
                4 => c.IsAiming,
                5 => !c.IsAiming && state.UninterruptedShots >= 2,
                6 => c.IsProne,
                7 => c.IsCrouched,
                8 => c.AimSeconds >= 3f,
                9 => state.UninterruptedShots >= 10,
                _ => false,
            };
        }

        return d.TriggerIndex switch
        {
            0 => state.ChamberBuffActive,
            1 => state.PreviousSurfaceImpact || state.PreviousLimbHit || state.PreviousArmorImpact,
            2 => state.PreviousRicochet,
            3 => c.SecondsSinceLastShot >= 2f,
            4 => c.IsAiming,
            5 => !c.IsAiming,
            6 => c.IsProne,
            7 => c.IsCrouched,
            8 => c.AimSeconds >= 3f,
            9 => c.RemainingAmmo <= 1,
            _ => false,
        };
    }

    private static EffectApplication BuildRocketApplication(VariantDefinition d)
    {
        var a = new EffectApplication(d) { Active = true };
        switch (d.PayoffIndex)
        {
            case 0: a.DamageMultiplier = 1.65f; break;
            case 1: a.DamageMultiplier = 1.35f; break;
            case 2: a.VelocityMultiplier = 1.45f; break;
            case 3: a.PenetrationMultiplier = 1.40f; break;
            case 4: a.FragmentationChanceBonus = 0.60f; break;
            case 5: a.VelocityMultiplier = 1.25f; break;
            case 6: a.StatModifiers = new StatModifiers(accuracyDelta: -0.50f); break;
            case 7: a.VelocityMultiplier = 1.45f; a.PenetrationMultiplier = 1.30f; break;
            case 8: a.ArmorDamageMultiplier = 1.55f; a.PenetrationMultiplier = 1.25f; break;
            default: a.VelocityMultiplier = 1.40f; a.DamageMultiplier = 1.30f; break;
        }
        return a;
    }

    private static void UpdateContinuousState(WeaponState state, Player? player, Weapon weapon, float now)
    {
        var aiming = player?.HandsController?.IsAiming ?? false;
        if (aiming && !state.LastAiming) state.AimStartTime = now;
        if (!aiming && state.LastAiming)
        {
            state.LastAimEndTime = now;
            state.AdsEffectConsumed = false;
        }
        state.LastAiming = aiming;

        var speed = player?.Velocity.magnitude ?? 0f;
        if (speed <= 0.1f)
        {
            if (state.StationaryStartTime < 0f) state.StationaryStartTime = now;
        }
        else
        {
            state.StationaryStartTime = -1f;
            state.StationaryEffectConsumed = false;
            state.AdsEffectConsumed = false;
            if (aiming) state.AimStartTime = now;
        }

        var moving = (player?.InputDirection.sqrMagnitude ?? 0f) > 0.0001f;
        if (!moving && state.LastMovingInput && RarityResolver.TryGetDefinition(weapon, out var movingDefinition) &&
            movingDefinition.SpecialId is "wave015_running_start" or "wave015_walking_fire") state.TriggerHeldShots = 0;
        state.LastMovingInput = moving;

        var pose = player?.Pose.ToString() ?? string.Empty;
        if (state.LastPose.Length > 0 && !string.Equals(state.LastPose, pose, StringComparison.Ordinal))
        {
            state.StanceChanged = true;
        }
        state.LastPose = pose;

        var fireMode = LegendaryFireModePatch.LogicalMode(weapon);
        if (state.LastFireMode.Length > 0 && !string.Equals(state.LastFireMode, fireMode, StringComparison.Ordinal))
        {
            state.SelectorChanges++;
            if (state.SelectorChanges >= 2) state.SelectorBuffActive = true;
        }
        state.LastFireMode = fireMode;

        var triggerPressed = player?.HandsController is Player.FirearmController controller && LegendaryFireModePatch.IsHeld(controller);
        if (!triggerPressed && state.LastTriggerPressed)
        {
            state.TriggerHeldShots = 0;
            state.UninterruptedShots = 0;
            state.FullSendStacks = 0;
        }
        state.LastTriggerPressed = triggerPressed;
    }

    private static Player? GetPlayer(string profileId)
    {
        var world = Singleton<GameWorld>.Instance;
        return world is null || string.IsNullOrEmpty(profileId) ? null : world.GetAlivePlayerByProfileID(profileId);
    }

    private static bool IsSuppressed(Weapon weapon) => weapon.Mods.Any(mod => mod is Silencer);
    private static bool IsLimb(string bodyPart) => bodyPart.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0 || bodyPart.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0;
    private sealed class VolleyOutcome
    {
        public int Sequence;
        public int Generation;
        public float CreatedTime;
        public float CompletedTime = -1f;
        public readonly HashSet<Shot> Pending = new();
        public bool AnyBodyHit;
        public bool AnySurfaceImpact;
        public bool ResolvedMiss;
        public bool ArmorCounted;
        public bool CompoundHit;
        public bool FleshCounted;
        public bool Finalized;
        public string TargetId = string.Empty;
        public string BodyPart = string.Empty;
    }

    private static VolleyOutcome BeginVolleyOutcome(WeaponState state, int fireIndex)
    {
        if (!state.Volleys.TryGetValue(fireIndex, out var volley))
        {
            volley = new VolleyOutcome { Sequence = state.TotalShots, Generation = state.ReloadGeneration, CreatedTime = Time.time };
            state.Volleys[fireIndex] = volley;
            state.PendingVolleys[fireIndex] = volley;
            ActiveVolleyStates.Add(state);
        }
        return volley;
    }

    private static void RecalculateStreaks(WeaponState state)
    {
        state.ConsecutiveHits = 0;
        state.CompoundInterestStacks = 0;
        ResetTargetStreaks(state);
        foreach (var volley in state.Volleys.Values.Where(v => v.Generation == state.ReloadGeneration).OrderBy(v => v.Sequence))
        {
            if (volley.ResolvedMiss)
            {
                state.ConsecutiveHits = 0;
                state.CompoundInterestStacks = 0;
                ResetTargetStreaks(state);
            }
            else if (volley.AnyBodyHit)
            {
                state.ConsecutiveHits++;
                if (volley.CompoundHit) state.CompoundInterestStacks = Math.Min(15, state.CompoundInterestStacks + 1);
                state.SameTargetHits = volley.TargetId == state.LastTargetProfileId && volley.TargetId.Length > 0 ? state.SameTargetHits + 1 : 1;
                state.LastTargetProfileId = volley.TargetId;
                state.SameBodyPartHits = volley.TargetId == state.LastBodyPartTargetProfileId && volley.BodyPart == state.LastBodyPart && volley.TargetId.Length > 0 ? state.SameBodyPartHits + 1 : 1;
                state.LastBodyPartTargetProfileId = volley.TargetId;
                state.LastBodyPart = volley.BodyPart;
            }
        }
    }

    private static void ResetTargetStreaks(WeaponState state)
    {
        state.LastTargetProfileId = string.Empty;
        state.SameTargetHits = 0;
        state.LastBodyPartTargetProfileId = string.Empty;
        state.LastBodyPart = string.Empty;
        state.SameBodyPartHits = 0;
    }

    private static bool ObserveReloadBoundary(WeaponState state, Weapon weapon)
    {
        var magazine = weapon.GetCurrentMagazine();
        var magazineId = magazine?.Id ?? "internal";
        var remaining = (magazine?.Count ?? 0) + weapon.ChamberAmmoCount;
        if (state.MagazineId.Length == 0 || state.LastRemainingAmmo < 0)
        {
            state.MagazineId = magazineId;
            state.LastRemainingAmmo = remaining;
            return false;
        }

        if (string.Equals(state.MagazineId, magazineId, StringComparison.Ordinal))
        {
            return false;
        }

        CompleteReloadBoundary(state, weapon, false);
        return true;
    }

    private static void CompleteReloadBoundary(WeaponState state, Weapon weapon, bool emergency)
    {
        var magazine = weapon.GetCurrentMagazine();
        var magazineId = magazine?.Id ?? "internal";
        var remaining = (magazine?.Count ?? 0) + weapon.ChamberAmmoCount;
        ClearMagazinePersistentEffects(state);
        state.HasReloadHistory = true;
        state.EmergencyReloadActive = emergency;
        state.ReloadMomentumAvailable = true;
        state.MagazineId = magazineId;
        state.LastRemainingAmmo = remaining;
        state.ShotsInMagazine = 0;
        state.MovingShotsInMagazine = 0;
        state.FleshHitsInMagazine = 0;
        state.AmmoTransferFleshHits = 0;
        state.UninterruptedShots = 0;
        state.TriggerHeldShots = 0;
        state.ShotsInCurrentFireMode = 0;
        state.LastShotFireMode = string.Empty;
        state.SmokeDeployedThisMagazine = false;
        state.DebtCollectorActive = false;
        state.PreviousArmorImpact = false;
        state.PreviousBodyHit = false;
        state.PreviousLimbHit = false;
        state.PreviousMiss = false;
        state.PreviousRicochet = false;
        state.PreviousFragmentation = false;
        state.ConsecutiveHits = 0;
        state.CompoundInterestStacks = 0;
        state.ArmorHits = 0;
        state.ReloadGeneration++;
        ResetTargetStreaks(state);
        state.VolleyFireIndex = int.MinValue;
        state.VolleyAnyBodyHit = false;
        state.VolleyAnySurfaceImpact = false;
        state.VolleyHitCounted = false;
        state.VolleyArmorHitCounted = false;
        state.VolleyCompoundInterestCounted = false;
        state.VolleyTargetHitCounted = false;
    }

    private static void ClearMagazinePersistentEffects(WeaponState state)
    {
        state.ChamberBuffActive = false;
        state.SelectorBuffActive = false;
        state.SelectorChanges = 0;
        state.ReadyWallPassActive = false;
        state.MagazineCheckActive = false;
        state.ShotsSinceMagazineCheck = 0;
        state.InspectionBuffActive = false;
        state.EmergencyReloadActive = false;
        state.ReloadMomentumAvailable = false;
        state.PayoffRampStacks = 0;
        state.LastApplication = null;
    }

    private static WeaponState GetState(string instanceId)
    {
        if (!States.TryGetValue(instanceId, out var state))
        {
            state = new WeaponState { WeaponId = instanceId };
            States[instanceId] = state;
        }
        return state;
    }

    private static string ShotKey(Weapon weapon, int fireIndex) => $"{weapon.Id}:{fireIndex}";

    private sealed class WeaponState
    {
        public string WeaponId { get; set; } = string.Empty;
        public string MagazineId { get; set; } = string.Empty;
        public int LastFireIndex { get; set; } = int.MinValue;
        public int LastShotFrame { get; set; } = int.MinValue;
        public EffectApplication? LastApplication { get; set; }
        public int TotalShots { get; set; }
        public int ShotsInMagazine { get; set; }
        public int UninterruptedShots { get; set; }
        public int LastRemainingAmmo { get; set; } = -1;
        public float LastShotTime { get; set; } = -1f;
        public bool HasReloadHistory { get; set; }
        public bool EmergencyReloadActive { get; set; }
        public bool MagazineCheckActive { get; set; }
        public int ShotsSinceMagazineCheck { get; set; }
        public bool InspectionBuffActive { get; set; }
        public bool ChamberBuffActive { get; set; }
        public bool SelectorBuffActive { get; set; }
        public bool ReadyWallPassActive { get; set; }
        public bool ReloadMomentumAvailable { get; set; }
        public int ConsecutiveHits { get; set; }
        public int ArmorHits { get; set; }
        public int CompoundInterestStacks { get; set; }
        public int FullSendStacks { get; set; }
        public bool PreviousMiss { get; set; }
        public bool PreviousArmorImpact { get; set; }
        public bool PreviousLimbHit { get; set; }
        public bool PreviousSurfaceImpact { get; set; }
        public bool PreviousBodyHit { get; set; }
        public bool PreviousRicochet { get; set; }
        public bool PreviousFragmentation { get; set; }
        public bool ChamberChecked { get; set; }
        public int PayoffRampStacks { get; set; }
        public bool WarrantyFactoryTune { get; set; } = true;
        public bool LastAiming { get; set; }
        public bool AdsEffectConsumed { get; set; }
        public float AimStartTime { get; set; } = -1f;
        public float LastAimEndTime { get; set; } = -10f;
        public float StationaryStartTime { get; set; } = -1f;
        public bool StationaryEffectConsumed { get; set; }
        public string LastPose { get; set; } = string.Empty;
        public bool StanceChanged { get; set; }
        public string LastFireMode { get; set; } = string.Empty;
        public int SelectorChanges { get; set; }
        public bool LastTriggerPressed { get; set; }
        public bool LastMovingInput { get; set; }
        public int ReloadGeneration { get; set; }
        public Dictionary<int, VolleyOutcome> Volleys { get; } = new();
        public Dictionary<int, VolleyOutcome> PendingVolleys { get; } = new();
        public int TriggerHeldShots { get; set; }
        public int MovingShotsInMagazine { get; set; }
        public int FleshHitsInMagazine { get; set; }
        public int AmmoTransferFleshHits { get; set; }
        public bool DrawEffectArmed { get; set; }
        public int ShotsSinceDraw { get; set; }
        public string LastShotFireMode { get; set; } = string.Empty;
        public int ShotsInCurrentFireMode { get; set; }
        public bool SmokeDeployedThisMagazine { get; set; }
        public string LastTargetProfileId { get; set; } = string.Empty;
        public int SameTargetHits { get; set; }
        public string LastBodyPartTargetProfileId { get; set; } = string.Empty;
        public string LastBodyPart { get; set; } = string.Empty;
        public int SameBodyPartHits { get; set; }
        public bool DebtCollectorActive { get; set; }
        public int VolleyFireIndex { get; set; } = int.MinValue;
        public bool VolleyAnyBodyHit { get; set; }
        public bool VolleyAnySurfaceImpact { get; set; }
        public bool VolleyHitCounted { get; set; }
        public bool VolleyArmorHitCounted { get; set; }
        public bool VolleyCompoundInterestCounted { get; set; }
        public bool VolleyTargetHitCounted { get; set; }
        public int VolleyStartingConsecutiveHits { get; set; }
        public int VolleyStartingCompoundInterestStacks { get; set; }
        public string PlayerProfileId { get; set; } = string.Empty;

        public WeaponState Clone() => (WeaponState)MemberwiseClone();
    }

    private readonly struct ShotContext
    {
        public ShotContext(float now, float secondsSinceLastShot, int remainingAmmo, int capacity, Player? player, bool suppressed, bool subsonic, bool aboveAverage, bool reloaded, WeaponState state)
        {
            SecondsSinceLastShot = secondsSinceLastShot;
            RemainingAmmo = remainingAmmo;
            Capacity = capacity;
            IsAiming = player?.HandsController?.IsAiming ?? false;
            IsProne = player?.IsInPronePose ?? false;
            IsCrouched = string.Equals(player?.Pose.ToString(), "Duck", StringComparison.OrdinalIgnoreCase);
            Speed = player?.Velocity.magnitude ?? 0f;
            IsMovingInput = (player?.InputDirection.sqrMagnitude ?? 0f) > 0.0001f;
            Stamina = player?.Physical?.Stamina?.NormalValue ?? 1f;
            IsTriggerPressed = player?.HandsController is Player.FirearmController controller && LegendaryFireModePatch.IsHeld(controller);
            IsFolded = false;
            Suppressed = suppressed;
            Subsonic = subsonic;
            AboveCaliberAverageVelocity = aboveAverage;
            FireMode = player?.HandsController?.Item is Weapon activeWeapon
                ? LegendaryFireModePatch.LogicalMode(activeWeapon)
                : string.Empty;
            Reloaded = reloaded;
            AimSeconds = IsAiming && state.AimStartTime >= 0f ? now - state.AimStartTime : 0f;
            SecondsSinceAimEnded = now - state.LastAimEndTime;
            StationarySeconds = state.StationaryStartTime >= 0f ? now - state.StationaryStartTime : 0f;
            if (player?.HandsController?.Item is Weapon heldWeapon) IsFolded = heldWeapon.Folded;
        }

        public float SecondsSinceLastShot { get; }
        public int RemainingAmmo { get; }
        public int Capacity { get; }
        public bool IsAiming { get; }
        public bool IsProne { get; }
        public bool IsCrouched { get; }
        public float Speed { get; }
        public bool IsMovingInput { get; }
        public float Stamina { get; }
        public bool IsTriggerPressed { get; }
        public bool IsFolded { get; }
        public bool Suppressed { get; }
        public bool Subsonic { get; }
        public bool AboveCaliberAverageVelocity { get; }
        public string FireMode { get; }
        public bool Reloaded { get; }
        public float AimSeconds { get; }
        public float SecondsSinceAimEnded { get; }
        public float StationarySeconds { get; }
    }
}

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
