using EFT.Ballistics;
using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Eyewear
{
    internal sealed partial class State
    {
        internal string LastThrownId = "";
    }
    private sealed class GrenadeEffect
    {
        internal float Damage = 1;
        internal bool Repeat, Detonated;
    }
    private static readonly Dictionary<string, GrenadeEffect> GrenadeEffects = new(StringComparer.Ordinal);
    [ThreadStatic] private static bool RepeatingExplosion;
    internal static void Thrown(Player? player, Grenade grenade, ThrowWeap item, Vector3 throwForce, float prewarm)
    {
        if (Get(player) is not { } state || state.LastThrownId == grenade.WeaponSource.Id)
            return;
        state.LastThrownId = grenade.WeaponSource.Id;
        EyewearDamageSources.CaptureGrenade(grenade.WeaponSource, Eyewear.Outgoing(state.Player));
        if (state.Key == "EW012" && grenade is SmokeGrenade)
        {
            state.SmokePrimed = true;
            state.Charges = 1;
            return;
        }
        if (state.Key == "EW023" && grenade is StunGrenade && state.Ready)
        {
            state.Window = Time.time + 10;
            return;
        }
        if (grenade is SmokeGrenade or StunGrenade || item is not IExplosiveItem { MaxExplosionDistance: > 0 })
            return;
        // An existing multi-grenade signature owns its generations. Other native
        // grenade signatures and all per-instance rolls remain intact.
        if (RarityResolver.TryGetDefinition(item, out var definition) && EyewearCombatRules.ConflictingGrenade(definition.RuntimeKey))
            return;
        if (state.Key == "EW012" && state.SmokePrimed)
        {
            state.SmokePrimed = false;
            state.Charges = 0;
            GrenadeEffects[grenade.WeaponSource.Id] = new GrenadeEffect { Damage = .5f };
            if (PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) is not { GrenadeFactory: { } factory } world)
                return;
            // Factory.Create queues an impulse. The new Rigidbody has not yet
            // simulated it, so its current velocity may still be zero. Reuse
            // the native release impulse, rather than reading/setting velocity.
            Grenade? previous = null;
            for (var i = 0; i < 2; i++)
            {
                try
                {
                    var source = RarityResolver.TryGetDefinition(item, out var d)
                        ? LegendaryGrenadeEffects.Projectile(RarityResolver.Resolve(item, d), true)
                        : (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), item.StringTemplateId, null);
                    var pooled = Singleton<ObjectsFactory>.Instance.CreateItem(source, true);
                    GrenadeSettings settings;
                    try
                    {
                        settings = UnityEngine.Object.Instantiate(pooled.GetComponent<GrenadePrefab>().GrenadeItself);
                    }
                    finally
                    {
                        AssetPoolObject.ReturnToPool(pooled);
                    }
                    var fan = Quaternion.AngleAxis(i == 0 ? -7 : 7, Vector3.up) * throwForce;
                    var child = factory.Create(settings, grenade.transform.position, grenade.transform.rotation, fan, source, state.Player.ProfileId, prewarm);
                    GrenadeEffects[source.Id] = new GrenadeEffect { Damage = .5f };
                    // These copies begin at the same native release point. Ignore
                    // only this family's pairs so initial overlap cannot arrest
                    // the fan or make a sticky copy attach to its sibling.
                    GrenadeLaunch.IgnoreSibling(grenade, child);
                    if (previous != null)
                        GrenadeLaunch.IgnoreSibling(previous, child);
                    previous = child;
                    world.RegisterGrenade(child);
                    if (Singleton<GlobalEventDispatcher>.Instantiated)
                        Singleton<GlobalEventDispatcher>.Instance.ThrowGrenade(child, child.transform.position, fan, .5f);
                }
                catch (Exception error)
                {
                    Plugin.LogSource.LogWarning($"EFB Hot Work Permit grenade: {error.Message}");
                }
            }
        }
        else if (state.Key == "EW038")
        {
            state.CombatThrows++;
            if (state.CombatThrows % 2 == 0 && state.Ready)
            {
                GrenadeEffects[grenade.WeaponSource.Id] = new GrenadeEffect { Repeat = true };
                state.Cool(90);
            }
        }
    }
    internal static void PrepareExplosion(Item? weapon, ref IExplosiveItem explosive)
    {
        if (RepeatingExplosion)
            explosive = new EyewearExplosive(explosive, .4f, .75f);
        else if (weapon is not null && GrenadeEffects.TryGetValue(weapon.Id, out var effect) && effect.Damage != 1)
            explosive = new EyewearExplosive(explosive, effect.Damage, 1);
    }
    internal static void ExplosionFinished(Item? weapon, IExplosiveItem explosive, Vector3 position, string owner)
    {
        if (RepeatingExplosion || weapon is null || !GrenadeEffects.TryGetValue(weapon.Id, out var effect) || effect.Detonated)
            return;
        effect.Detonated = true;
        GrenadeEffects.Remove(weapon.Id);
        if (effect.Repeat && PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) is { } world)
            world.StartCoroutine(RepeatExplosion(world, weapon, explosive, position, owner, CombatGeneration));
    }
    private static IEnumerator RepeatExplosion(GameWorld world, Item weapon, IExplosiveItem explosive, Vector3 position, string owner, int generation)
    {
        yield return new WaitForSeconds(.6f);
        if (generation != CombatGeneration || PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) != world || world.SharedBallisticsCalculator is null)
            yield break;
        var previous = RepeatingExplosion;
        RepeatingExplosion = true;
        try
        {
            if (Singleton<Systems.Effects.Effects>.Instantiated)
                try
                {
                    LegendaryExplosionFx.Impact(Singleton<Systems.Effects.Effects>.Instance, position, Vector3.up, explosive.MaxExplosionDistance * .75f, 1);
                }
                catch (Exception error)
                {
                    Plugin.LogSource.LogWarning($"EFB Double Take visual: {error.Message}");
                }
            Grenade.Explosion(null, explosive, position, owner, world.SharedBallisticsCalculator, weapon, Vector3.zero);
        }
        finally
        {
            RepeatingExplosion = previous;
        }
    }
}

internal sealed class EyewearExplosive : IExplosiveItem
{
    private readonly IExplosiveItem Source;
    private readonly float Damage, Radius;
    internal EyewearExplosive(IExplosiveItem source, float damage, float radius)
    {
        Source = source;
        Damage = damage;
        Radius = radius;
    }
    public Vector3 Blindness => Source.Blindness;
    public Vector3 Contusion => Source.Contusion;
    public Vector3 ArmorDistanceDistanceDamage => new(Source.ArmorDistanceDistanceDamage.x * Radius, Source.ArmorDistanceDistanceDamage.y * Radius, Source.ArmorDistanceDistanceDamage.z * Damage);
    public float MinExplosionDistance => Source.MinExplosionDistance * Radius;
    public float MaxExplosionDistance => Source.MaxExplosionDistance * Radius;
    public int FragmentsCount => Source.FragmentsCount;
    public float GetStrength => Source.GetStrength * Damage;
    public bool IsDummy => Source.IsDummy;
    public Ammo CreateFragment()
    {
        var ammo = Source.CreateFragment();
        ChestRigCombat.Fragment(ammo, (ChestRigCombat.FragmentFactor(ammo) ?? 1) * Damage);
        return ammo;
    }
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "CreateAndRegisterGrenade")]
internal static class EyewearGrenadeReleasePatch
{
    private static void Postfix(Player.BaseGrenadeHandsController __instance, Grenade __result, Vector3 __2, float __3)
    {
        if (__result != null && __instance.Item is ThrowWeap item)
            Eyewear.Thrown(BackpackRuntime.Actor(__instance), __result, item, __2, __3);
    }
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.Explosion))]
internal static class EyewearExplosionPatch
{
    internal struct Context
    {
        internal IExplosiveItem Original; internal EyewearDamageSources.NativeScope Previous;
    }
    [HarmonyPriority(Priority.First + 1)]
    private static void Prefix(Item originalWeaponItem, IExplosiveItem grenadeItem, out Context __state) =>
        __state = new Context { Original = grenadeItem, Previous = EyewearDamageSources.BeginNative(EyewearDamageSources.GrenadeFactor(originalWeaponItem)) };
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Item originalWeaponItem, Context __state, Vector3 grenadePosition, string playerProfileIDWhoThrew) =>
        Eyewear.ExplosionFinished(originalWeaponItem, __state.Original, grenadePosition, playerProfileIDWhoThrew);
    private static Exception? Finalizer(Context __state, Exception? __exception)
    {
        EyewearDamageSources.EndNative(__state.Previous);
        return __exception;
    }
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.Explosion))]
internal static class EyewearExplosionScalePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Item originalWeaponItem, ref IExplosiveItem grenadeItem) => Eyewear.PrepareExplosion(originalWeaponItem, ref grenadeItem);
}
