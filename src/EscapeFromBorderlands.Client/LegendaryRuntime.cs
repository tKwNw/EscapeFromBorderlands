using Shot = EftBulletClass;
using Ammo = AmmoItemClass;
using Physical = PlayerPhysicalClass;
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
    private static readonly List<WeaponState> VolleyStateBuffer = new();
    private static readonly List<KeyValuePair<int, VolleyOutcome>> PendingVolleyBuffer = new();
    private static readonly Dictionary<Shot, EffectApplication> ShotApplications = new();
    private static readonly Dictionary<(string Weapon, int FireIndex), EffectApplication> ShotApplicationsByKey = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> HeldWeaponsByPlayer = new(StringComparer.Ordinal);
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
    public static int ShotCount(Weapon weapon) { lock (Sync) return States.TryGetValue(weapon.Id, out var state) ? state.TotalShots : 0; }
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
            UpdateContinuousState(state, player, weapon, definition, now);
            var magazine = weapon.GetCurrentMagazine();
            var remainingAfterShot = (magazine?.Count ?? 0) + weapon.ChamberAmmoCount;
            var remaining = remainingBeforeShot ?? remainingAfterShot + 1;
            var capacity = Math.Max(1, (magazine?.MaxCount ?? weapon.GetMaxMagazineCount()) + weapon.Chambers.Length);
            var reloaded = ObserveReloadBoundary(state, magazine?.Id ?? "internal", remainingAfterShot);

            var secondsSinceLastShot = state.LastShotTime < 0f ? float.MaxValue : now - state.LastShotTime;
            state.UninterruptedShots++;
            state.TotalShots++;
            state.ShotsInMagazine++;
            if (state.MagazineCheckActive) state.ShotsSinceMagazineCheck++;
            if (state.LastTriggerPressed)
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
                ReadsSuppression(definition) && IsSuppressed(weapon),
                ammo.InitialSpeed <= 343f,
                definition.CaliberAverageVelocity > 0 && ammo.InitialSpeed > definition.CaliberAverageVelocity,
                reloaded,
                in state.Data);

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

            var application = new EffectApplication(definition,
                BuildApplication(definition, ref state.Data, context, ammo.AmmoTemplate, ammo.StringTemplateId));
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
            if (ActiveVolleyStates.Count == 0) return;
            var now = Time.time;
            try
            {
                VolleyStateBuffer.AddRange(ActiveVolleyStates);
                foreach (var state in VolleyStateBuffer)
                {
                    // Completion mutates the pending map. Reuse a snapshot
                    // without holding finished projectiles after this update.
                    PendingVolleyBuffer.AddRange(state.PendingVolleys);
                    foreach (var pair in PendingVolleyBuffer)
                    {
                        var volley = pair.Value;
                        if (now - volley.CreatedTime > 60f)
                        {
                            foreach (var projectile in volley.Pending) ShotApplications.Remove(projectile);
                            volley.Pending.Clear();
                            volley.CompletedTime = now - 1f;
                        }
                        if (!volley.Finalized && volley.Pending.Count == 0 && volley.CompletedTime >= 0f && now - volley.CompletedTime >= 0.05f)
                        {
                            volley.Finalized = true;
                            ResolveDeferredMiss(state.WeaponId, pair.Key);
                            CleanupShotApplication(state.WeaponId, pair.Key);
                            state.PendingVolleys.Remove(pair.Key);
                        }
                    }
                    PendingVolleyBuffer.Clear();
                    if (state.PendingVolleys.Count == 0) ActiveVolleyStates.Remove(state);
                    // Keep a bounded outcome history, not references to finished bullets.
                    if (state.Volleys.Count > 1024)
                        foreach (var pair in state.Volleys.Where(p => p.Value.Finalized).OrderBy(p => p.Value.Sequence).Take(state.Volleys.Count - 1024).ToArray())
                            state.Volleys.Remove(pair.Key);
                }
            }
            finally { PendingVolleyBuffer.Clear(); VolleyStateBuffer.Clear(); }
        }
    }

    public static void CleanupShotApplication(string weaponId, int fireIndex)
    {
        lock (Sync)
        {
            if (!States.TryGetValue(weaponId, out var state) || !state.Volleys.TryGetValue(fireIndex, out var volley) || volley.Pending.Count == 0)
                ShotApplicationsByKey.Remove((weaponId, fireIndex));
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
        RarityResolver.TryGetDefinition(weapon, out var definition);
        if (definition is null && !HeldWeaponsByPlayer.ContainsKey(profileId)) return;
        lock (Sync) ObserveHeldWeaponLocked(profileId, weapon, definition, true);
    }

    private static void ObserveHeldWeaponLocked(string profileId, Weapon? weapon, VariantDefinition? definition, bool resolved)
    {
        var weaponId = weapon?.Id ?? string.Empty;
        var hadLegendary = HeldWeaponsByPlayer.TryGetValue(profileId, out var previous);
        if (hadLegendary && string.Equals(previous, weaponId, StringComparison.Ordinal)) return;
        if (!resolved) RarityResolver.TryGetDefinition(weapon, out definition!);
        if (weapon is null || definition is null)
        {
            if (hadLegendary) HeldWeaponsByPlayer.TryRemove(profileId, out _);
            return;
        }
        HeldWeaponsByPlayer[profileId] = weaponId;
        var state = GetState(weaponId);
        state.PlayerProfileId = profileId;
        state.ShotsSinceDraw = 0;
        if (definition.CustomEffect?.Trigger == "draw_next_shot" ||
            definition.SpecialId is "zantetsuken" or "wave015_one_and_done") state.DrawEffectArmed = true;
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
        if (player is null) return;
        RarityResolver.TryGetDefinition(weapon, out var definition);
        UpdateLiveState(player, weapon, definition);
    }
    internal static void UpdateLiveState(Player? player, Weapon? weapon, VariantDefinition? definition)
    {
        if (player is null) return;
        var held = player.HandsController?.Item as Weapon;
        var profileId = player.ProfileId;
        var heldDefinition = definition;
        if (!ReferenceEquals(held, weapon)) RarityResolver.TryGetDefinition(held, out heldDefinition!);
        if (heldDefinition is null && definition is null && !HeldWeaponsByPlayer.ContainsKey(profileId)) return;
        lock (Sync)
        {
            ObserveHeldWeaponLocked(profileId, held, heldDefinition, true);
            if (weapon is null || definition is null) return;
            var state = GetState(weapon.Id);
            state.PlayerProfileId = profileId;
            ObserveReloadBoundary(state, weapon);
            UpdateContinuousState(state, player, weapon, definition, Time.time);
        }
    }

    public static StatModifiers GetStatModifiers(Weapon weapon)
    {
        return GetLiveStats(weapon).StatModifiers;
    }

    public static float GetVelocityMultiplier(Weapon weapon) => GetLiveStats(weapon).VelocityMultiplier;
    public static float GetReloadSpeedMultiplier(Weapon weapon) =>
        ElephantGunRules.ReloadSpeed(weapon, GetLiveStats(weapon).ReloadSpeedMultiplier);

    private static WeaponLiveStats GetLiveStats(Weapon weapon)
    {
        // A prepared cartridge owns its application. Even a nested stat read
        // must never substitute a preview for that cartridge.
        if (_shotScope is not null || !WeaponStatReadBatch.IsReading(weapon))
            return new WeaponLiveStats(EvaluateLiveData(weapon, statsOnly: true));
        if (WeaponStatReadBatch.Preview is { } preview) return preview;
        var stats = new WeaponLiveStats(EvaluateLiveData(weapon, statsOnly: true));
        WeaponStatReadBatch.RememberPreview(weapon, stats);
        return stats;
    }

    private static EffectApplicationData EvaluateLiveData(Weapon weapon, bool statsOnly = false)
    {
        var definition = WeaponStatReadBatch.Definition(weapon);
        if (definition is null || definition.EffectKind == "procedural")
        {
            return new EffectApplicationData();
        }

        lock (Sync)
        {
            var state = GetState(weapon.Id);
            var magazine = weapon.GetCurrentMagazine();
            var remaining = (magazine?.Count ?? 0) + weapon.ChamberAmmoCount;
            ObserveReloadBoundary(state, magazine?.Id ?? "internal", remaining);
            if (_shotScope?.Weapon == weapon) return _shotScope.Application.Data;
            var now = Time.time;
            var player = GetPlayer(state.PlayerProfileId);
            UpdateContinuousState(state, player, weapon, definition, now);
            // Preview-only optimization. Tracking still observes reload/draw,
            // HUD evaluation still sees procs, and real cartridges build the full effect.
            if (statsOnly && !MayChangeLiveStats(definition)) return new EffectApplicationData();
            var capacity = Math.Max(1, (magazine?.MaxCount ?? weapon.GetMaxMagazineCount()) + weapon.Chambers.Length);
            var secondsSinceLastShot = state.LastShotTime < 0f ? float.MaxValue : now - state.LastShotTime;
            var preview = state.Data;
            preview.TotalShots++;
            preview.ShotsInMagazine++;
            preview.UninterruptedShots++;
            preview.TriggerHeldShots = state.LastTriggerPressed ? preview.TriggerHeldShots + 1 : definition.Automatic ? 1 : preview.UninterruptedShots;
            preview.ShotsSinceDraw++;
            var mode = LegendaryFireModePatch.LogicalMode(weapon);
            if (!string.Equals(preview.LastShotFireMode, mode, StringComparison.OrdinalIgnoreCase))
            { preview.LastShotFireMode = mode; preview.ShotsInCurrentFireMode = 0; }
            preview.ShotsInCurrentFireMode++;
            if (preview.MagazineCheckActive) preview.ShotsSinceMagazineCheck++;
            if (state.LastMovingInput) preview.MovingShotsInMagazine++;
            var currentAmmo = weapon.CurrentAmmoTemplate;
            var currentAmmoSpeed = currentAmmo?.InitialSpeed ?? float.MaxValue;
            var context = new ShotContext(
                now,
                secondsSinceLastShot,
                remaining,
                capacity,
                player,
                ReadsSuppression(definition) && IsSuppressed(weapon),
                currentAmmoSpeed <= 343f,
                definition.CaliberAverageVelocity > 0 && currentAmmoSpeed > definition.CaliberAverageVelocity,
                false,
                in preview,
                definition);
            var application = BuildApplication(definition, ref preview, context, currentAmmo, string.Empty);
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

    // Only declare neutral cases we can prove from the payoff switches.
    // Unknown or special definitions take the unchanged path.
    internal static bool MayChangeLiveStats(VariantDefinition definition)
    {
        if (definition.CustomEffect is not null || !string.IsNullOrEmpty(definition.SpecialId)) return true;
        static bool Ordinary(int index) => index is not
            (0 or 5 or 8 or 9 or 10 or 14 or 15 or 16 or 17 or 18 or 23 or 26 or 27 or 28 or 29 or 30 or 31 or 37 or 39 or 40);
        return definition.EffectKind switch
        {
            "ordinary" => Ordinary(definition.PayoffIndex) ||
                definition.TriggerIndex == 11 && definition.SecondaryPayoffIndex >= 0 && Ordinary(definition.SecondaryPayoffIndex),
            "meme" => definition.PayoffIndex is not (0 or 2 or 3 or 6 or 9 or 10 or 11 or 12),
            _ => true
        };
    }

    private static EffectApplicationData BuildApplication(
        VariantDefinition definition,
        ref WeaponStateData state,
        ShotContext context,
        AmmoTemplate? ammo,
        string ammoTemplateId)
    {
        if (definition.CustomEffect is not null)
        {
            return BuildCustomApplication(definition, ref state, context);
        }

        if (!string.IsNullOrEmpty(definition.SpecialId))
        {
            return BuildSpecialApplication(definition, ref state, context, ammo, ammoTemplateId);
        }

        if (definition.EffectKind == "launcher" || definition.EffectKind == "launcher_meme")
        {
            return BuildLauncherApplication(definition, ref state, context);
        }

        if (definition.EffectKind == "rocket" || definition.EffectKind == "rocket_meme")
        {
            return BuildRocketApplication(definition);
        }

        var active = definition.EffectKind == "meme"
            ? EvaluateMemeTrigger(definition, ref state, context)
            : EvaluateTrigger(definition, ref state, context);
        if (!active)
        {
            state.PayoffRampStacks = 0;
            return new EffectApplicationData();
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
            : BuildOrdinaryPayoff(definition, ref state, context, ammo?.PenetrationPower ?? 0);
    }

    private static EffectApplicationData BuildCustomApplication(VariantDefinition definition, ref WeaponStateData state, ShotContext context)
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
            return new EffectApplicationData();
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
        var doubleSeal = triggerActive && Headwear.Seal(definition.TemplateId) && Headwear.Is(context.Owner, "HW030-1");
        if (doubleSeal) { fireRateMultiplier = 1f + 2f * (fireRateMultiplier - 1f); recoilDelta = Math.Max(-.90f, 2f * recoilDelta); }
        return new EffectApplicationData()
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

    private static EffectApplicationData BuildSpecialApplication(
        VariantDefinition definition,
        ref WeaponStateData state,
        ShotContext context,
        AmmoTemplate? ammo,
        string ammoTemplateId)
    {
        var ammoPenetration = ammo?.PenetrationPower ?? 0;
        var application = new EffectApplicationData() { Active = true, SpecialId = definition.SpecialId };
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

    private static bool EvaluateTrigger(VariantDefinition definition, ref WeaponStateData state, ShotContext context)
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

    private static bool EvaluateMemeTrigger(VariantDefinition definition, ref WeaponStateData state, ShotContext context)
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

    private static EffectApplicationData BuildOrdinaryPayoff(VariantDefinition d, ref WeaponStateData state, ShotContext c, int ammoPenetration)
    {
        var a = new EffectApplicationData() { Active = true };
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

    private static EffectApplicationData BuildMemePayoff(VariantDefinition d)
    {
        var a = new EffectApplicationData() { Active = true };
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

    private static EffectApplicationData BuildLauncherApplication(VariantDefinition d, ref WeaponStateData state, ShotContext context)
    {
        var active = d.EffectKind == "launcher_meme" || EvaluateLauncherTrigger(d, ref state, context);
        if (!active) return new EffectApplicationData();
        var a = new EffectApplicationData() { Active = true };
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

    private static bool EvaluateLauncherTrigger(VariantDefinition d, ref WeaponStateData state, ShotContext c)
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

    private static EffectApplicationData BuildRocketApplication(VariantDefinition d)
    {
        var a = new EffectApplicationData() { Active = true };
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

    private static void UpdateContinuousState(WeaponState state, Player? player, Weapon weapon, VariantDefinition definition, float now)
    {
        var hands = player?.HandsController;
        var aiming = hands?.IsAiming ?? false;
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
        if (!moving && state.LastMovingInput && definition.SpecialId is "wave015_running_start" or "wave015_walking_fire")
            state.TriggerHeldShots = 0;
        state.LastMovingInput = moving;

        var pose = player?.Pose;
        if (state.LastPose.HasValue && state.LastPose != pose)
        {
            state.StanceChanged = true;
        }
        state.LastPose = pose;

        var fireMode = LegendaryFireModePatch.LogicalFireMode(weapon);
        if (state.LastFireMode.HasValue && state.LastFireMode.Value != fireMode)
        {
            state.SelectorChanges++;
            if (state.SelectorChanges >= 2) state.SelectorBuffActive = true;
        }
        state.LastFireMode = fireMode;

        var triggerPressed = hands is Player.FirearmController controller && LegendaryFireModePatch.IsHeld(controller);
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

    // Avoid walking attachments for signatures whose rules never read this.
    private static bool ReadsSuppression(VariantDefinition definition) =>
        definition.CustomEffect is { } custom ? custom.Trigger is "suppressed" or "unsuppressed" :
        !string.IsNullOrEmpty(definition.SpecialId) ? definition.SpecialId is "frankenstein" or "redacted" or "ak74n_suppressed_ergo" :
        definition.TriggerIndex is 28 or 29;

    private static bool IsSuppressed(Weapon weapon) => weapon.Mods.Any(mod => mod is SilencerItemClass);
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

    private static readonly List<(VolleyOutcome Volley, int Ordinal)> OrderedOutcomes = new();
    private static int CompareOutcomes((VolleyOutcome Volley, int Ordinal) a, (VolleyOutcome Volley, int Ordinal) b)
    {
        var sequence = a.Volley.Sequence.CompareTo(b.Volley.Sequence);
        return sequence != 0 ? sequence : a.Ordinal.CompareTo(b.Ordinal);
    }

    private static void RecalculateStreaks(WeaponState state)
    {
        state.ConsecutiveHits = 0;
        state.CompoundInterestStacks = 0;
        ResetTargetStreaks(state);
        // The caller owns Sync. Preserve the former stable OrderBy tie
        // order for pooled/late shots while avoiding a fresh sort in the
        // common monotonic case.
        var needsSort = false;
        var last = int.MinValue;
        try
        {
            foreach (var entry in state.Volleys.Values)
            {
                if (entry.Generation != state.ReloadGeneration) continue;
                needsSort |= entry.Sequence < last;
                last = entry.Sequence;
                OrderedOutcomes.Add((entry, OrderedOutcomes.Count));
            }
            if (needsSort) OrderedOutcomes.Sort(CompareOutcomes);
            foreach (var entry in OrderedOutcomes)
            {
                var volley = entry.Volley;
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
        finally { OrderedOutcomes.Clear(); }
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
        if (state.LastRemainingAmmo >= 0 && state.MagazineId.Length != 0 &&
            string.Equals(state.MagazineId, magazineId, StringComparison.Ordinal)) return false;
        return ObserveReloadBoundary(state, magazineId, (magazine?.Count ?? 0) + weapon.ChamberAmmoCount);
    }

    private static bool ObserveReloadBoundary(WeaponState state, string magazineId, int remaining)
    {
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

        CompleteReloadBoundary(state, magazineId, remaining, false);
        return true;
    }

    private static void CompleteReloadBoundary(WeaponState state, Weapon weapon, bool emergency)
    {
        var magazine = weapon.GetCurrentMagazine();
        var magazineId = magazine?.Id ?? "internal";
        var remaining = (magazine?.Count ?? 0) + weapon.ChamberAmmoCount;
        CompleteReloadBoundary(state, magazineId, remaining, emergency);
    }

    private static void CompleteReloadBoundary(WeaponState state, string magazineId, int remaining, bool emergency)
    {
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

    private static (string, int) ShotKey(Weapon weapon, int fireIndex) => (weapon.Id, fireIndex);

    private sealed class WeaponState
    {
        public WeaponStateData Data = new();
        public Dictionary<int, VolleyOutcome> Volleys { get; } = new();
        public Dictionary<int, VolleyOutcome> PendingVolleys { get; } = new();
        public string WeaponId { get => Data.WeaponId; set => Data.WeaponId = value; }
        public string MagazineId { get => Data.MagazineId; set => Data.MagazineId = value; }
        public int LastFireIndex { get => Data.LastFireIndex; set => Data.LastFireIndex = value; }
        public int LastShotFrame { get => Data.LastShotFrame; set => Data.LastShotFrame = value; }
        public EffectApplication? LastApplication { get => Data.LastApplication; set => Data.LastApplication = value; }
        public int TotalShots { get => Data.TotalShots; set => Data.TotalShots = value; }
        public int ShotsInMagazine { get => Data.ShotsInMagazine; set => Data.ShotsInMagazine = value; }
        public int UninterruptedShots { get => Data.UninterruptedShots; set => Data.UninterruptedShots = value; }
        public int LastRemainingAmmo { get => Data.LastRemainingAmmo; set => Data.LastRemainingAmmo = value; }
        public float LastShotTime { get => Data.LastShotTime; set => Data.LastShotTime = value; }
        public bool HasReloadHistory { get => Data.HasReloadHistory; set => Data.HasReloadHistory = value; }
        public bool EmergencyReloadActive { get => Data.EmergencyReloadActive; set => Data.EmergencyReloadActive = value; }
        public bool MagazineCheckActive { get => Data.MagazineCheckActive; set => Data.MagazineCheckActive = value; }
        public int ShotsSinceMagazineCheck { get => Data.ShotsSinceMagazineCheck; set => Data.ShotsSinceMagazineCheck = value; }
        public bool InspectionBuffActive { get => Data.InspectionBuffActive; set => Data.InspectionBuffActive = value; }
        public bool ChamberBuffActive { get => Data.ChamberBuffActive; set => Data.ChamberBuffActive = value; }
        public bool SelectorBuffActive { get => Data.SelectorBuffActive; set => Data.SelectorBuffActive = value; }
        public bool ReadyWallPassActive { get => Data.ReadyWallPassActive; set => Data.ReadyWallPassActive = value; }
        public bool ReloadMomentumAvailable { get => Data.ReloadMomentumAvailable; set => Data.ReloadMomentumAvailable = value; }
        public int ConsecutiveHits { get => Data.ConsecutiveHits; set => Data.ConsecutiveHits = value; }
        public int ArmorHits { get => Data.ArmorHits; set => Data.ArmorHits = value; }
        public int CompoundInterestStacks { get => Data.CompoundInterestStacks; set => Data.CompoundInterestStacks = value; }
        public int FullSendStacks { get => Data.FullSendStacks; set => Data.FullSendStacks = value; }
        public bool PreviousMiss { get => Data.PreviousMiss; set => Data.PreviousMiss = value; }
        public bool PreviousArmorImpact { get => Data.PreviousArmorImpact; set => Data.PreviousArmorImpact = value; }
        public bool PreviousLimbHit { get => Data.PreviousLimbHit; set => Data.PreviousLimbHit = value; }
        public bool PreviousSurfaceImpact { get => Data.PreviousSurfaceImpact; set => Data.PreviousSurfaceImpact = value; }
        public bool PreviousBodyHit { get => Data.PreviousBodyHit; set => Data.PreviousBodyHit = value; }
        public bool PreviousRicochet { get => Data.PreviousRicochet; set => Data.PreviousRicochet = value; }
        public bool PreviousFragmentation { get => Data.PreviousFragmentation; set => Data.PreviousFragmentation = value; }
        public bool ChamberChecked { get => Data.ChamberChecked; set => Data.ChamberChecked = value; }
        public int PayoffRampStacks { get => Data.PayoffRampStacks; set => Data.PayoffRampStacks = value; }
        public bool WarrantyFactoryTune { get => Data.WarrantyFactoryTune; set => Data.WarrantyFactoryTune = value; }
        public bool LastAiming { get => Data.LastAiming; set => Data.LastAiming = value; }
        public bool AdsEffectConsumed { get => Data.AdsEffectConsumed; set => Data.AdsEffectConsumed = value; }
        public float AimStartTime { get => Data.AimStartTime; set => Data.AimStartTime = value; }
        public float LastAimEndTime { get => Data.LastAimEndTime; set => Data.LastAimEndTime = value; }
        public float StationaryStartTime { get => Data.StationaryStartTime; set => Data.StationaryStartTime = value; }
        public bool StationaryEffectConsumed { get => Data.StationaryEffectConsumed; set => Data.StationaryEffectConsumed = value; }
        public EPlayerPose? LastPose { get; set; }
        public bool StanceChanged { get => Data.StanceChanged; set => Data.StanceChanged = value; }
        public Weapon.EFireMode? LastFireMode { get; set; }
        public int SelectorChanges { get => Data.SelectorChanges; set => Data.SelectorChanges = value; }
        public bool LastTriggerPressed { get => Data.LastTriggerPressed; set => Data.LastTriggerPressed = value; }
        public bool LastMovingInput { get => Data.LastMovingInput; set => Data.LastMovingInput = value; }
        public int ReloadGeneration { get => Data.ReloadGeneration; set => Data.ReloadGeneration = value; }
        public int TriggerHeldShots { get => Data.TriggerHeldShots; set => Data.TriggerHeldShots = value; }
        public int MovingShotsInMagazine { get => Data.MovingShotsInMagazine; set => Data.MovingShotsInMagazine = value; }
        public int FleshHitsInMagazine { get => Data.FleshHitsInMagazine; set => Data.FleshHitsInMagazine = value; }
        public int AmmoTransferFleshHits { get => Data.AmmoTransferFleshHits; set => Data.AmmoTransferFleshHits = value; }
        public bool DrawEffectArmed { get => Data.DrawEffectArmed; set => Data.DrawEffectArmed = value; }
        public int ShotsSinceDraw { get => Data.ShotsSinceDraw; set => Data.ShotsSinceDraw = value; }
        public string LastShotFireMode { get => Data.LastShotFireMode; set => Data.LastShotFireMode = value; }
        public int ShotsInCurrentFireMode { get => Data.ShotsInCurrentFireMode; set => Data.ShotsInCurrentFireMode = value; }
        public bool SmokeDeployedThisMagazine { get => Data.SmokeDeployedThisMagazine; set => Data.SmokeDeployedThisMagazine = value; }
        public string LastTargetProfileId { get => Data.LastTargetProfileId; set => Data.LastTargetProfileId = value; }
        public int SameTargetHits { get => Data.SameTargetHits; set => Data.SameTargetHits = value; }
        public string LastBodyPartTargetProfileId { get => Data.LastBodyPartTargetProfileId; set => Data.LastBodyPartTargetProfileId = value; }
        public string LastBodyPart { get => Data.LastBodyPart; set => Data.LastBodyPart = value; }
        public int SameBodyPartHits { get => Data.SameBodyPartHits; set => Data.SameBodyPartHits = value; }
        public bool DebtCollectorActive { get => Data.DebtCollectorActive; set => Data.DebtCollectorActive = value; }
        public int VolleyFireIndex { get => Data.VolleyFireIndex; set => Data.VolleyFireIndex = value; }
        public bool VolleyAnyBodyHit { get => Data.VolleyAnyBodyHit; set => Data.VolleyAnyBodyHit = value; }
        public bool VolleyAnySurfaceImpact { get => Data.VolleyAnySurfaceImpact; set => Data.VolleyAnySurfaceImpact = value; }
        public bool VolleyHitCounted { get => Data.VolleyHitCounted; set => Data.VolleyHitCounted = value; }
        public bool VolleyArmorHitCounted { get => Data.VolleyArmorHitCounted; set => Data.VolleyArmorHitCounted = value; }
        public bool VolleyCompoundInterestCounted { get => Data.VolleyCompoundInterestCounted; set => Data.VolleyCompoundInterestCounted = value; }
        public bool VolleyTargetHitCounted { get => Data.VolleyTargetHitCounted; set => Data.VolleyTargetHitCounted = value; }
        public int VolleyStartingConsecutiveHits { get => Data.VolleyStartingConsecutiveHits; set => Data.VolleyStartingConsecutiveHits = value; }
        public int VolleyStartingCompoundInterestStacks { get => Data.VolleyStartingCompoundInterestStacks; set => Data.VolleyStartingCompoundInterestStacks = value; }
        public string PlayerProfileId { get => Data.PlayerProfileId; set => Data.PlayerProfileId = value; }
    }

    // Copy only the rule inputs when previewing. Volley dictionaries and
    // controller/shot bookkeeping remain in the live WeaponState.
    private struct WeaponStateData
    {
        public WeaponStateData() { }
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
        public bool StanceChanged { get; set; }
        public int SelectorChanges { get; set; }
        public bool LastTriggerPressed { get; set; }
        public bool LastMovingInput { get; set; }
        public int ReloadGeneration { get; set; }
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
    }

    private readonly struct ShotContext
    {
        public ShotContext(float now, float secondsSinceLastShot, int remainingAmmo, int capacity, Player? player, bool suppressed, bool subsonic, bool aboveAverage, bool reloaded, in WeaponStateData state, VariantDefinition? preview = null)
        {
            Owner = player;
            var hands = player?.HandsController;
            var ordinary = preview is { EffectKind: "ordinary", CustomEffect: null, SpecialId.Length: 0 };
            var meme = preview is { EffectKind: "meme", CustomEffect: null, SpecialId.Length: 0 };
            var full = !ordinary && !meme;
            var trigger = preview?.TriggerIndex ?? -1;
            var heldWeapon = full ? hands?.Item as Weapon : null;
            SecondsSinceLastShot = secondsSinceLastShot;
            RemainingAmmo = remainingAmmo;
            Capacity = capacity;
            IsAiming = hands?.IsAiming ?? false;
            IsProne = (full || ordinary && trigger == 18) && player?.IsInPronePose == true;
            IsCrouched = (full || ordinary && trigger == 17 || meme && trigger == 9) && player?.Pose == EPlayerPose.Duck;
            Speed = full || ordinary && trigger is 17 or 20 ? player?.Velocity.magnitude ?? 0f : 0f;
            IsMovingInput = full && (player?.InputDirection.sqrMagnitude ?? 0f) > 0.0001f;
            IsTriggerPressed = full && hands is Player.FirearmController controller && LegendaryFireModePatch.IsHeld(controller);
            IsFolded = heldWeapon?.Folded == true;
            Stamina = full || ordinary && trigger is 21 or 22 ? player?.Physical?.Stamina?.NormalValue ?? 1f : 1f;
            Suppressed = suppressed;
            Subsonic = subsonic;
            AboveCaliberAverageVelocity = aboveAverage;
            FireMode = heldWeapon is null ? string.Empty : LegendaryFireModePatch.LogicalMode(heldWeapon);
            Reloaded = reloaded;
            AimSeconds = IsAiming && state.AimStartTime >= 0f ? now - state.AimStartTime : 0f;
            SecondsSinceAimEnded = now - state.LastAimEndTime;
            StationarySeconds = state.StationaryStartTime >= 0f ? now - state.StationaryStartTime : 0f;
        }

        public Player? Owner { get; }
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

internal struct EffectApplicationData
{
    public EffectApplicationData() { }
    public bool Active { get; set; }
    public bool? StatusActive { get; set; }
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
    public bool HasMechanicalEffect => DamageMultiplier != 1f || HeadDamageMultiplier != 1f || BodyDamageMultiplier != 1f ||
        VelocityMultiplier != 1f || PenetrationMultiplier != 1f ||
        ArmorDamageMultiplier != 1f || RicochetChanceBonus != 0f || FragmentationChanceBonus != 0f ||
        ExplosionDamageFactor > 0f || ProjectileCount > 1 || SurfaceRetentionBonus > 0f ||
        LimbDamageMultiplier != 1f || ReloadSpeedMultiplier != 1f || HeavyBleedEveryFleshHits > 0 ||
        ForceHeavyBleedOnFleshHit || !string.IsNullOrEmpty(AmmoTemplateId) ||
        ConditionalDamageMultiplier != 1f || StatModifiers.HasAny;
}

internal sealed class EffectApplication
{
    public static readonly EffectApplication Empty = new(null);
    private EffectApplicationData _data;
    public EffectApplication(VariantDefinition? definition) : this(definition, new EffectApplicationData()) { }
    internal EffectApplication(VariantDefinition? definition, EffectApplicationData data)
    {
        Definition = definition;
        _data = data;
    }
    internal ref readonly EffectApplicationData Data => ref _data;
    public VariantDefinition? Definition { get; }
    public bool BonusProjectile { get; private set; }
    public bool Active { get => _data.Active; set => _data.Active = value; }
    public bool? StatusActive { get => _data.StatusActive; set => _data.StatusActive = value; }
    public string SpecialId { get => _data.SpecialId; set => _data.SpecialId = value; }
    public float DamageMultiplier { get => _data.DamageMultiplier; set => _data.DamageMultiplier = value; }
    public float HeadDamageMultiplier { get => _data.HeadDamageMultiplier; set => _data.HeadDamageMultiplier = value; }
    public float BodyDamageMultiplier { get => _data.BodyDamageMultiplier; set => _data.BodyDamageMultiplier = value; }
    public float VelocityMultiplier { get => _data.VelocityMultiplier; set => _data.VelocityMultiplier = value; }
    public float PenetrationMultiplier { get => _data.PenetrationMultiplier; set => _data.PenetrationMultiplier = value; }
    public float ArmorDamageMultiplier { get => _data.ArmorDamageMultiplier; set => _data.ArmorDamageMultiplier = value; }
    public float RicochetChanceBonus { get => _data.RicochetChanceBonus; set => _data.RicochetChanceBonus = value; }
    public float FragmentationChanceBonus { get => _data.FragmentationChanceBonus; set => _data.FragmentationChanceBonus = value; }
    public float RicochetRetention { get => _data.RicochetRetention; set => _data.RicochetRetention = value; }
    public float SurfaceRetentionBonus { get => _data.SurfaceRetentionBonus; set => _data.SurfaceRetentionBonus = value; }
    public float LimbDamageMultiplier { get => _data.LimbDamageMultiplier; set => _data.LimbDamageMultiplier = value; }
    public float ExplosionDamageFactor { get => _data.ExplosionDamageFactor; set => _data.ExplosionDamageFactor = value; }
    public float ExplosionRadius { get => _data.ExplosionRadius; set => _data.ExplosionRadius = value; }
    public float ExplosionRadiusMultiplier { get => _data.ExplosionRadiusMultiplier; set => _data.ExplosionRadiusMultiplier = value; }
    public float ReloadSpeedMultiplier { get => _data.ReloadSpeedMultiplier; set => _data.ReloadSpeedMultiplier = value; }
    public int HeavyBleedEveryFleshHits { get => _data.HeavyBleedEveryFleshHits; set => _data.HeavyBleedEveryFleshHits = value; }
    public int ProjectileCount { get => _data.ProjectileCount; set => _data.ProjectileCount = value; }
    public float ProjectileDamageMultiplier { get => _data.ProjectileDamageMultiplier; set => _data.ProjectileDamageMultiplier = value; }
    public float ProjectileSpreadDegrees { get => _data.ProjectileSpreadDegrees; set => _data.ProjectileSpreadDegrees = value; }
    public string ProjectilePattern { get => _data.ProjectilePattern; set => _data.ProjectilePattern = value; }
    public bool ReplaceNaturalProjectiles { get => _data.ReplaceNaturalProjectiles; set => _data.ReplaceNaturalProjectiles = value; }
    public bool CenterBonusOnShell { get => _data.CenterBonusOnShell; set => _data.CenterBonusOnShell = value; }
    public Vector3 ShellCenterDirection { get => _data.ShellCenterDirection; set => _data.ShellCenterDirection = value; }
    public float BonusProjectileDamageScale { get => _data.BonusProjectileDamageScale; set => _data.BonusProjectileDamageScale = value; }
    public float BonusProjectilePenetrationScale { get => _data.BonusProjectilePenetrationScale; set => _data.BonusProjectilePenetrationScale = value; }
    public float BonusProjectileVelocityScale { get => _data.BonusProjectileVelocityScale; set => _data.BonusProjectileVelocityScale = value; }
    public bool ForceHeavyBleedOnFleshHit { get => _data.ForceHeavyBleedOnFleshHit; set => _data.ForceHeavyBleedOnFleshHit = value; }
    public string AmmoTemplateId { get => _data.AmmoTemplateId; set => _data.AmmoTemplateId = value; }
    public string ConditionalTargetProfileId { get => _data.ConditionalTargetProfileId; set => _data.ConditionalTargetProfileId = value; }
    public string ConditionalBodyPart { get => _data.ConditionalBodyPart; set => _data.ConditionalBodyPart = value; }
    public float ConditionalDamageMultiplier { get => _data.ConditionalDamageMultiplier; set => _data.ConditionalDamageMultiplier = value; }
    public StatModifiers StatModifiers { get => _data.StatModifiers; set => _data.StatModifiers = value; }
    public bool HasAnyEffect => Active && (Definition is not null);
    public bool HasMechanicalEffect => _data.HasMechanicalEffect;
    public EffectApplication AsBonusProjectile()
    {
        var copy = (EffectApplication)MemberwiseClone();
        copy.BonusProjectile = true;
        copy.ProjectileCount = 1;
        return copy;
    }
}

internal readonly struct WeaponLiveStats
{
    internal readonly StatModifiers StatModifiers;
    internal readonly float VelocityMultiplier, ReloadSpeedMultiplier;
    internal WeaponLiveStats(in EffectApplicationData data)
    {
        StatModifiers = data.StatModifiers;
        VelocityMultiplier = data.VelocityMultiplier;
        ReloadSpeedMultiplier = data.ReloadSpeedMultiplier;
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
