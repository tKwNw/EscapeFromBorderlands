using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Eyewear
{
    internal sealed partial class State
    {
        internal string CombatMagazine = "", ProtectedGrenade = "";
        internal int MagazineShots, CombatThrows, FallFrame = -1;
        internal float ProtectedAt = -100, LastAmmoCount = -1;
        internal Vector3 ProtectedOrigin;
        internal bool SmokePrimed;
        internal ShotScope? CachedShot;
    }
    internal sealed class ShotScope
    {
        internal State State = null!;
        internal Weapon Weapon = null!;
        internal ShotScope? Previous;
        internal float Damage = 1, ExtraDamage = 1;
        internal int Extra, NextFragment = 1000000, Generation;
        internal bool Double, Cross, Explosive, InUse, Printer;
        internal List<Shot>? NativePelletBatch;
        internal bool DoubleCenterKnown, PelletBasisKnown;
        internal Vector3 OriginalCenter, DoubleCenter;
        internal Quaternion PelletRotation;
    }
    [ThreadStatic] internal static ShotScope? CurrentShot;
    private static readonly Dictionary<Shot, byte> ImpactExplosions = new();
    private static readonly HashSet<Shot> PendingCopies = new();
    private static readonly HashSet<State> ArmorImmunities = new();
    private static readonly HashSet<State> PrinterWearers = new();
    internal static bool HasPrinter => PrinterWearers.Count != 0;
    private static readonly Dictionary<BotFlashGrenade, State> BotFlashWearers = new();
    internal static bool HasArmorImmunity => ArmorImmunities.Count != 0;
    private static int CombatGeneration;
    internal static void CombatReset()
    {
        CombatGeneration++; CurrentShot = null; ImpactExplosions.Clear(); ArmorImmunities.Clear(); PrinterWearers.Clear(); BotFlashWearers.Clear(); EyewearDamageSources.Reset();
        EyewearDamagePatch.Reset(); EyewearArmorContextPatch.InArmor = false; EyewearArmorContextPatch.Scale = 1; EyewearBackplatePatch.Active = false;
        try { foreach (var shot in PendingCopies) SessionCleanup.Run("eyewear delayed projectile", shot, static value => { LegendaryRuntime.Release(value); Shot.Release(value); }); }
        finally { PendingCopies.Clear(); GrenadeEffects.Clear(); RepeatingExplosion = false; }
    }
    internal static void CombatForget(State state)
    {
        state.SmokePrimed = false; state.AttackerId = null; ArmorImmunities.Remove(state); PrinterWearers.Remove(state);
        if (state.Player.AIData?.BotOwner?.FlashGrenade is { } flash) BotFlashWearers.Remove(flash);
    }
    internal static void CombatEquipped(State state)
    {
        if (state.Key is "EW001" or "EW011") ArmorImmunities.Add(state);
        if (state.Key == "EW022") PrinterWearers.Add(state);
        if (!state.Human && state.Roll.Eyewear.Flash > 0 && state.Player.AIData?.BotOwner?.FlashGrenade is { } flash) BotFlashWearers[flash] = state;
        CleanImmunities(state);
    }
    internal static float BotBlindness(BotFlashGrenade flash) => BotFlashWearers.Count != 0 && BotFlashWearers.TryGetValue(flash, out var state) ? 1 - state.Roll.Eyewear.Flash : 1;
    internal static void CleanImmunities(State state)
    {
        if (state.Key != "EW004" || state.Player.ActiveHealthController is not { } health) return;
        List<ActiveHealthController.Effect>? remove = null;
        foreach (var effect in health.GetAllEffects())
            if (effect is ActiveHealthController.Effect native && native.Existing && native.GetType().Name is "Pain" or "Contusion")
                (remove ??= new()).Add(native);
        if (remove is not null) foreach (var effect in remove) effect.ForceRemove();
    }

    internal static ShotScope? BeginShot(Player? player, Weapon weapon, Ammo ammo, int? remainingBeforeShot)
    {
        var state = Get(player);
        if (state is null || !EyewearCombatRules.NeedsShotScope(state.Key))
        {
            if (CurrentShot is null) return null;
            return CurrentShot = new ShotScope { State = state ?? CurrentShot.State, Weapon = weapon, Previous = CurrentShot, InUse = true, Generation = CombatGeneration };
        }
        PrepareShot(state);
        var now = Time.time;
        var scope = state.CachedShot?.InUse != true ? (state.CachedShot ??= new ShotScope()) : new ShotScope();
        scope.State = state; scope.Weapon = weapon; scope.Previous = CurrentShot;
        scope.Generation = CombatGeneration;
        scope.NativePelletBatch = null;
        scope.Damage = scope.ExtraDamage = 1; scope.Extra = 0; scope.NextFragment = 1000000;
        scope.Double = scope.Cross = scope.Explosive = false;
        scope.DoubleCenterKnown = scope.PelletBasisKnown = false;
        scope.OriginalCenter = scope.DoubleCenter = Vector3.zero;
        scope.Printer = state.Key == "EW022";
        var single = EyewearCombatRules.SingleBullet(ammo.AmmoTemplate.ProjectileCount, ammo.AmmoTemplate.ExplosionStrength, weapon.WeapClass);
        var count = 0;
        if (state.Key == "EW019")
        {
            count = remainingBeforeShot ?? LegendaryRuntime.RemainingAmmo(weapon) + 1;
            var magazine = weapon.GetCurrentMagazine()?.Id ?? "";
            if (magazine != state.CombatMagazine || count > state.LastAmmoCount)
            { state.MagazineShots = 0; state.CombatMagazine = magazine; }
            state.LastAmmoCount = count; state.MagazineShots++;
        }
        switch (state.Key)
        {
            case "EW009" when state.Human && state.Charges > 0 && single && player!.HandsController?.IsAiming == true:
                scope.Cross = true; scope.Extra = 4; scope.ExtraDamage = .4f; state.Charges = 0; state.Cool(25); break;
            case "EW013" when weapon.WeapClass == "shotgun": scope.Double = true; break;
            case "EW019" when state.Ready && count <= 1 && state.MagazineShots >= 3 && single:
                scope.Extra = 2; scope.ExtraDamage = .4f; state.Cool(20); break;
            case "EW021" when state.Human && state.Aiming && state.Still && state.AimSince > 0 && now - state.AimSince >= 1:
                scope.Damage = 1.3f; state.Second = now + .1f; state.AimSince = now; break;
            case "EW027" when state.Charges > 0 && single:
                scope.Explosive = true; break;
            case "EW031" when single && now - state.LastShot >= 10:
                scope.Extra = 4; scope.ExtraDamage = 1; break;
        }
        state.LastShot = now;
        if (!scope.Double && scope.Extra == 0 && scope.Damage == 1 && !scope.Explosive && !scope.Printer && CurrentShot is null) return null;
        scope.InUse = true;
        CurrentShot = scope;
        return scope;
    }
    internal static void EndShot(ShotScope? scope)
    {
        if (scope is null) return;
        if (scope.Generation == CombatGeneration && ReferenceEquals(CurrentShot, scope)) CurrentShot = scope.Previous;
        scope.InUse = false; scope.Previous = null; scope.NativePelletBatch = null;
    }
    internal static bool FreezeShot(State state) => CurrentShot is { Damage: > 1 } scope && ReferenceEquals(scope.State, state) && state.Key == "EW021";
    internal static void ApplyProjectile(BallisticsCalculator calculator, Shot shot, int fragmentIndex, EffectApplication application)
    {
        if (CurrentShot is not { } scope || shot.Weapon != scope.Weapon) return;
        shot.Damage *= scope.Damage;
        if (scope.Explosive && fragmentIndex == 0 && application.ExplosionDamageFactor <= 0 && ChestRigRuntime.CurrentShot?.Bonuses.DoubleExplosive != true)
        { ImpactExplosions[shot] = 1; scope.State.Charges = 0; scope.State.Cool(60); }
        if (scope.Double) Duplicate(calculator, shot, application, DoubleDirection(scope, shot.Direction), 1, scope, 0);
        if (fragmentIndex != 0 || scope.Extra == 0) return;
        var forward = shot.Direction.normalized;
        var up = Math.Abs(Vector3.Dot(forward, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
        var right = Vector3.Cross(up, forward).normalized;
        up = Vector3.Cross(forward, right).normalized;
        for (var i = 0; i < scope.Extra; i++)
        {
            Vector3 direction;
            if (scope.Cross) direction = Quaternion.AngleAxis(i < 2 ? (i == 0 ? -1 : 1) : (i == 2 ? -1 : 1), i < 2 ? up : right) * forward;
            else if (scope.Extra == 2) direction = Quaternion.AngleAxis(i == 0 ? -1 : 1, up) * forward;
            else direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-1.5f, 1.5f), up) * Quaternion.AngleAxis(UnityEngine.Random.Range(-1.5f, 1.5f), right) * forward;
            Duplicate(calculator, shot, application, direction, scope.ExtraDamage, scope, 0, true);
        }
    }
    // Called for every weapon-generated sibling, including delayed echoes. Only
    // the eyewear's own copies bypass this entry point, so doubling is one generation.
    internal static void LegendarySibling(BallisticsCalculator calculator, Shot shot, EffectApplication application, float delay)
    {
        if (CurrentShot is { Double: true } scope && shot.Weapon == scope.Weapon)
            Duplicate(calculator, shot, application.AsBonusProjectile(), application.CenterBonusOnShell ? scope.DoubleCenter : DoubleDirection(scope, shot.Direction), 1, scope, delay);
        if (CurrentShot is { } active && shot.Weapon == active.Weapon) shot.Damage *= active.Damage;
    }
    private static void Duplicate(BallisticsCalculator calculator, Shot source, EffectApplication application,
        Vector3 direction, float damage, ShotScope scope, float delay, bool extra = false)
    {
        var copy = Shot.Create(source.Ammo, scope.NextFragment, UnityEngine.Random.Range(0, 512), source.StartPosition,
            direction.normalized, source.InitialSpeed, source.Speed, source.BulletMassGram, source.BulletDiameterMilimeters,
            source.Damage * damage, source.PenetrationPower, source.PenetrationChance, source.RicochetChance,
            source.FragmentationChance, source.DeviationChance, source.MinFragmentsCount, source.MaxFragmentsCount,
            source._defaultHitBody, source.Randoms, source.BallisticCoefficient, source.PlayerProfileID, source.Weapon,
            source.FireIndex, null);
        scope.NextFragment += 2;
        copy.ArmorDamage = source.ArmorDamage;
        ChestRigRuntime.AssociateSibling(copy, source);
        LegendaryRuntime.Associate(copy, application, extra || application.BonusProjectile);
        if (delay > 0 && Singleton<GameWorld>.Instance is { } world)
        {
            PendingCopies.Add(copy);
            try { world.StartCoroutine(DelayedCopy(world, calculator, copy, delay, CombatGeneration)); }
            catch { PendingCopies.Remove(copy); LegendaryRuntime.Release(copy); Shot.Release(copy); throw; }
        }
        else if (scope.NativePelletBatch is { } batch)
        batch.Add(copy);
        else calculator.Shoot(copy);
    }
    private static IEnumerator DelayedCopy(GameWorld world, BallisticsCalculator calculator, Shot copy, float delay, int generation)
    {
        yield return new WaitForSeconds(delay);
        if (generation != CombatGeneration) yield break;
        if (!PendingCopies.Remove(copy)) yield break;
        var submitted = false;
        try
        {
            if (PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) == world) { calculator.Shoot(copy); submitted = true; }
        }
        finally { if (!submitted) { LegendaryRuntime.Release(copy); Shot.Release(copy); } }
    }
    internal static void Impact(Shot shot)
    {
        if (ImpactExplosions.Count != 0 && ImpactExplosions.Remove(shot)) ProjectileImpactExplosion.Trigger(shot, .35f, 1.5f);
    }
    internal static void Miss(Shot shot) { if (ImpactExplosions.Count != 0) ImpactExplosions.Remove(shot); }

    internal static bool ImmuneDamage(State state, in DamageInfo hit)
    {
        var now = Time.time;
        if (state.Key == "EW011" && hit.DamageType is EDamageType.Barbed or EDamageType.Flame or EDamageType.Landmine) return true;
        if (state.Key == "EW030" && hit.DamageType == EDamageType.Fall)
        {
            if (state.FallFrame == Time.frameCount) return true;
            if (!ConsumeCharge(state, 120)) return false;
            state.FallFrame = Time.frameCount;
            if (state.Human) { BackpackRuntime.RestoreLeg(state.Player, .25f); state.Second = now + 15; EyewearLegDrain.Activate(state); }
            return true;
        }
        if (state.Key != "EW001" || !Explosive(hit)) return false;
        var grenade = hit.Weapon is ThrowWeap ? hit.Weapon.Id : "";
        if (EyewearCombatRules.ProtectedExplosion(state.ProtectedGrenade, grenade, now - state.ProtectedAt, (hit.MasterOrigin - state.ProtectedOrigin).sqrMagnitude)) return true;
        if (!ConsumeCharge(state, 600)) return false;
        state.ProtectedGrenade = grenade; state.ProtectedAt = now; state.ProtectedOrigin = hit.MasterOrigin;
        return true;
    }
    private static bool Explosive(in DamageInfo hit) => hit.Weapon is ThrowWeap or Launcher ||
        hit.DamageType is EDamageType.Explosion or EDamageType.GrenadeFragment or EDamageType.ThermobaricExplosion or EDamageType.Landmine or EDamageType.Artillery;
    internal static bool ArmorRelevant(in DamageInfo hit) => HasArmorImmunity && (Explosive(in hit) || hit.DamageType is EDamageType.Barbed or EDamageType.Flame);
    internal static float Outgoing(Player? owner) => EyewearCombatRules.Outgoing(Get(owner)?.Key);
    internal static float Incoming(State? state) => EyewearCombatRules.Incoming(state?.Key, state?.Active == true);
    internal static void Damaged(State state, in DamageInfo hit)
    {
        if (hit.Player?.iPlayer is not Player enemy || !AdvancedMeleeRuntime.Enemy(state.Player, enemy)) return;
        if (state.Key == "EW028" && hit.DamageType == EDamageType.Bullet && hit.Weapon is Weapon && state.Ready)
        { state.AttackerId = enemy.ProfileId; state.Buff(6, 35); }
        if (state.Key == "EW025" && state.Ready && (state.Player.Position - enemy.Position).sqrMagnitude <= 25)
        { HealMostInjured(state, 50); state.Buff(3, 300); }
    }
    internal static void EnemyHit(Player target, in DamageInfo hit)
    {
        if (hit.DamageType != EDamageType.Bullet || hit.Weapon is not Weapon || hit.Player?.iPlayer is not Player shooter || Get(shooter) is not { Key: "EW023" } state ||
            !state.InWindow || !AdvancedMeleeRuntime.Enemy(shooter, target)) return;
        state.Window = 0; state.Cool(60);
        FlashAt(state, hit.HitPoint, 4);
    }
    internal static void FlashAt(State state, Vector3 origin, float radius, bool ownerImmune = false, Vector3? direction = null, float coneDot = -1)
    {
        if (PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) is not { } world) return;
        foreach (var candidate in world.RegisteredPlayers)
        {
            if (candidate is not Player target || target.HealthController?.IsAlive != true || ownerImmune && target == state.Player || Headwear.FlashImmune(target)) continue;
            var toTarget = target.PlayerBones.Head.position - origin;
            var squared = toTarget.sqrMagnitude;
            if (squared > radius * radius || direction.HasValue && Vector3.Dot(direction.Value.normalized, toTarget.normalized) < coneDot) continue;
            var facing = Mathf.Clamp01(Vector3.Dot(-toTarget.normalized, target.LookDirection));
            if (facing <= 0 || Physics.Linecast(origin, target.PlayerBones.Head.position, (int)LayersMaskController.GrenadeObstaclesColliderMask)) continue;
            var strength = Mathf.Clamp01(1 - Mathf.Sqrt(squared) / radius) * facing;
            target.ActiveHealthController.DoStun(5, strength);
            target.ActiveHealthController.DoBurnEyes(origin, strength, strength, 5);
            if (target.AIData?.BotOwner is { BotState: EBotState.Active } bot) bot.FlashGrenade.AddBlindEffect(5 * strength, origin);
        }
    }
}

[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.Shot))]
internal static class EyewearCartridgePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => EyewearVolleySpread.Transpile(instructions);
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player.FirearmController __instance, Player? ____player, Weapon weapon, Ammo ammo, out Eyewear.ShotScope? __state) =>
        __state = Eyewear.BeginShot(____player, weapon, ammo, WeaponCombatState.PreparedRemaining(__instance, weapon, ammo));
    private static Exception? Finalizer(Eyewear.ShotScope? __state, Exception? __exception) { Eyewear.EndShot(__state); return __exception; }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage))]
internal static class EyewearDamagePatch
{
    [ThreadStatic] private static int Depth;
    [ThreadStatic] private static int DepthGeneration;
    internal static void Reset() { Depth = 0; DepthGeneration = EyewearDamageSources.Generation; }
    internal struct Context { internal int PreviousDepth; internal Eyewear.State? State; internal float Health; internal Player? PreviousOwner; internal EyewearDamageSources.NativeScope PreviousSource; }
    private static void Prefix(ActiveHealthController __instance, ref float damage, ref DamageInfo damageInfo, out Context __state)
    {
        if (DepthGeneration != EyewearDamageSources.Generation) Reset();
        var outgoing = EyewearDamageSources.OutgoingForDamage(in damageInfo);
        __state = new Context { PreviousDepth = Depth++, PreviousOwner = EyewearDamageSources.Owner, PreviousSource = EyewearDamageSources.BeginNative(outgoing) };
        var shooter = damageInfo.Player?.iPlayer as Player;
        if (shooter is not null) EyewearDamageSources.Owner = shooter;
        if (__state.PreviousDepth != 0 || damage <= 0) return;
        var state = Eyewear.Get(__instance.Player);
        if (state is not null && Eyewear.ImmuneDamage(state, in damageInfo))
        { damage = 0; damageInfo.Damage = 0; damageInfo.BleedBlock = true; return; }
        damage *= outgoing * Eyewear.Incoming(state);
        if (damageInfo.DamageType == EDamageType.Bullet && damageInfo.Weapon is Weapon &&
            Eyewear.Get(shooter) is { Key: "EW028", Active: true } recorder && recorder.AttackerId == __instance.Player.ProfileId) damage *= 1.25f;
        if (state is { Key: "EW025" or "EW028" })
        { __state.State = state; __state.Health = __instance.GetBodyPartHealth(EBodyPart.Common, false).Current; }
    }
    private static void Postfix(ActiveHealthController __instance, DamageInfo damageInfo, Context __state)
    {
        if (__state.State is { } state && __instance.IsAlive && __instance.GetBodyPartHealth(EBodyPart.Common, false).Current < __state.Health)
            Eyewear.Damaged(state, in damageInfo);
    }
    private static Exception? Finalizer(Context __state, Exception? __exception)
    {
        if (__state.PreviousSource.IsCurrent) { Depth = __state.PreviousDepth; EyewearDamageSources.Owner = __state.PreviousOwner; }
        EyewearDamageSources.EndNative(__state.PreviousSource); return __exception;
    }
}
[HarmonyPatch(typeof(Player), nameof(Player.ApplyDamageInfo))]
internal static class EyewearIncomingGatePatch
{
    private static bool Prefix(Player __instance, DamageInfo damageInfo) =>
        damageInfo.Damage <= 0 || Eyewear.Get(__instance) is not { } state || !Eyewear.ImmuneDamage(state, in damageInfo);
}
[HarmonyPatch(typeof(Player), nameof(Player.ApplyShot))]
internal static class EyewearEnemyHitPatch
{
    private static void Prefix(DamageInfo damageInfo, out (Player? Owner, int Generation) __state)
    { __state = (EyewearDamageSources.Owner, EyewearDamageSources.Generation); EyewearDamageSources.Owner = damageInfo.Player?.iPlayer as Player; }
    private static void Postfix(Player __instance, DamageInfo damageInfo, PlayerHitInfo __result)
    {
        if (__result is null) return;
        Eyewear.EnemyHit(__instance, in damageInfo);
        // Flight Recorder triggers on a confirmed bullet impact, including armor stops.
        if (Eyewear.Get(__instance) is { Key: "EW028" } state) Eyewear.Damaged(state, in damageInfo);
    }
    private static Exception? Finalizer((Player? Owner, int Generation) __state, Exception? __exception)
    { if (__state.Generation == EyewearDamageSources.Generation) EyewearDamageSources.Owner = __state.Owner; return __exception; }
}
[HarmonyPatch(typeof(ArmorPlateCollider), nameof(ArmorPlateCollider.ApplyHit))]
internal static class EyewearBackplatePatch
{
    [ThreadStatic] internal static bool Active;
    private static void Prefix(ArmorPlateCollider __instance, ref DamageInfo damageInfo, out (bool Active, int Generation) __state)
    {
        __state = (Active, EyewearDamageSources.Generation); Active = false;
        if (__instance.ArmorPlateColliderType is not (EArmorPlateCollider.Plate_Granit_SAPI_back or EArmorPlateCollider.Plate_6B13_back) ||
            damageInfo.Player?.iPlayer is not Player shooter || Eyewear.Get(shooter) is not { Key: "EW026" }) return;
        Active = true; damageInfo.Damage *= 3;
    }
    private static Exception? Finalizer((bool Active, int Generation) __state, Exception? __exception)
    { if (__state.Generation == EyewearDamageSources.Generation) Active = __state.Active; return __exception; }
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDamage))]
internal static class EyewearArmorContextPatch
{
    [ThreadStatic] internal static float Scale;
    [ThreadStatic] internal static bool InArmor;
    internal struct Context { internal float Scale; internal bool Active; internal int Generation; }
    private static void Prefix(ArmorComponent __instance, DamageInfo damageInfo, out Context __state)
    {
        __state = new Context { Scale = Scale, Active = InArmor, Generation = EyewearDamageSources.Generation }; InArmor = true; Scale = EyewearDamageSources.OutgoingForDamage(in damageInfo);
        if (EyewearBackplatePatch.Active) Scale *= 3;
        if ((damageInfo.Damage > 0 || damageInfo.ArmorDamage > 0) && Eyewear.ArmorRelevant(in damageInfo) && BackpackNative.ItemOwner(__instance.Item) is { } owner && Eyewear.Get(owner) is { } state && Eyewear.ImmuneDamage(state, in damageInfo)) Scale = 0;
    }
    private static Exception? Finalizer(Context __state, Exception? __exception)
    { if (__state.Generation == EyewearDamageSources.Generation) { Scale = __state.Scale; InArmor = __state.Active; } return __exception; }
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDurabilityDamage))]
internal static class EyewearArmorWearPatch
{
    private static void Prefix(ref float armorDamage) { if (EyewearArmorContextPatch.InArmor && EyewearArmorContextPatch.Scale != 1) armorDamage *= EyewearArmorContextPatch.Scale; }
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyExplosionDurabilityDamage))]
internal static class EyewearExplosionArmorPatch
{
    private static void Prefix(ArmorComponent __instance, ref float armorDamage, DamageInfo damageInfo)
    {
        armorDamage *= EyewearDamageSources.OutgoingForDamage(in damageInfo);
        if (armorDamage > 0 && Eyewear.ArmorRelevant(in damageInfo) && BackpackNative.ItemOwner(__instance.Item) is { } owner && Eyewear.Get(owner) is { } state && Eyewear.ImmuneDamage(state, in damageInfo)) armorDamage = 0;
    }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.DoContusion))]
internal static class EyewearContusionPatch
{
    private static bool Prefix(ActiveHealthController __instance, ref float __0)
    { var state = Eyewear.Get(__instance.Player); if (state?.Key == "EW004") return false; if (state is not null) __0 *= 1 - state.Roll.Eyewear.Concussion; return true; }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.DoStun))]
internal static class EyewearStunPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float __0) { if (Eyewear.Get(__instance.Player) is { } state) __0 *= 1 - state.Roll.Eyewear.Flash; }
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.DoBurnEyes))]
internal static class EyewearBlindnessPatch
{
    private static void Prefix(ActiveHealthController __instance, ref float __3) { if (Eyewear.Get(__instance.Player) is { } state) __3 *= 1 - state.Roll.Eyewear.Flash; }
}
[HarmonyPatch]
internal static class EyewearPainPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("Pain", BindingFlags.Public | BindingFlags.NonPublic), "Started");
    private static bool Prefix(ActiveHealthController.Effect __instance)
    { if (Eyewear.Get(__instance.HealthController.Player) is not { Key: "EW004" }) return true; __instance.ForceRemove(); return false; }
}
[HarmonyPatch(typeof(BotFlashGrenade), nameof(BotFlashGrenade.AddBlindEffect))]
internal static class EyewearBotBlindnessPatch
{
    private static void Prefix(BotFlashGrenade __instance, ref float __0) => __0 *= Eyewear.BotBlindness(__instance);
}
[HarmonyPatch(typeof(Shot), nameof(Shot.Release))]
internal static class EyewearProjectileReleasePatch
{
    private static void Prefix(Shot __0) { Eyewear.Miss(__0); EyewearDamageSources.ProjectileReleased(__0); }
}
