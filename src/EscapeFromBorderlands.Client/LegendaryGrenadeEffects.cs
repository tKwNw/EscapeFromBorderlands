using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Systems.Effects;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class LegendaryGrenadeEffects
{
    internal sealed class Payload
    {
        internal RarityResolver.ShotRoll Roll = null!;
        internal string Owner = "", DirectTarget = "";
        internal string Key => SmokeChild ? "" : Roll.Definition.RuntimeKey;
        internal float Expires, SmokeStarted = -1f, SmokeUntil;
        internal bool Secondary, SignatureFired, PistolOnly, Landed, FuseReady, SmokeChild, SmokeEnded, SmokeTimerStarted, LandingTimersPrepared;
        internal bool WaitForLanding => SmokeChild || Key == "GR65";
        internal bool DeferredEmission => WaitForLanding || Key == "GR59";
        internal Grenade? Grenade;
        internal Transform? Anchor;
        internal Vector3 LocalPoint;
        internal Quaternion LocalRotation;
        internal GrenadeEmission? Emission;
        internal TrailblazerPath? Path;
    }
    private sealed class EmissionState
    {
        internal Vector3 Scale;
        internal float Delay, Size, Distance;
        internal string ProjectileId = "";
        internal SmokeParticleScale[] Particles = Array.Empty<SmokeParticleScale>();
    }
    private static readonly Dictionary<string, Payload> Payloads = new(StringComparer.Ordinal);
    private static readonly Dictionary<GrenadeEmission, EmissionState> Emissions = new();
    private static GameWorld? World;
    private static Task? Assets;
    private static float RetryAssetsAt;
    private static int Generation;
    private const string F1 = "5710c24ad2720bc3458b45a3";
    private const string M18 = "617aa4dd8166f034d57de9c5";
    private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
    [ThreadStatic] internal static Payload? CurrentExplosion;
    internal static bool Try(Item? item, out Payload p) => Payloads.TryGetValue(item?.Id ?? "", out p!);
    internal static bool HasRecoilSmoke => Payloads.Values.Any(p => p.Key == "GR58" && !p.SmokeEnded && p.Expires > Time.time);
    internal static void Reset()
    {
        Generation++;
        try
        {
            foreach (var p in Payloads.Values.ToArray())
                if (p.Emission != null && p.Grenade != null)
                    SessionCleanup.Run("grenade smoke stop", () => p.Emission.StopEmission(p.Grenade));
            foreach (var emission in Emissions.Keys.ToArray())
                SessionCleanup.Run("grenade smoke restoration", () => RestoreEmission(emission));
        }
        finally
        {
            Payloads.Clear(); Emissions.Clear(); World = null; Assets = null;
            RetryAssetsAt = 0f; CurrentExplosion = null;
        }
    }
    internal static void Maintain()
    {
        var world = Singleton<GameWorld>.Instance;
        if (world != World) { World = world; Assets = null; RetryAssetsAt = 0f; }
        if (world?.MainPlayer != null && (Assets == null || Assets.IsFaulted || Assets.IsCanceled) && Time.time >= RetryAssetsAt)
        {
                try
                {
                    var item = Singleton<ItemFactory>.Instance.CreateItem(NewId(), F1, null);
                    Assets = Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid,
                        ObjectsFactory.AssemblyType.Local, new[] { item.Prefab }, Diz.Jobs.JobYieldPriority.Low, ct: CancellationToken.None);
                }
                catch (Exception e) { Assets = Task.FromException(e); Plugin.LogSource.LogError("M.I.R.V. asset preload failed: " + e.Message); }
                RetryAssetsAt = Time.time + 10f;
        }
        foreach (var id in Payloads.Where(kv => kv.Value.Expires < Time.time).Select(kv => kv.Key).ToArray()) Payloads.Remove(id);
    }
    private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 24);
    internal static ThrowWeap Projectile(RarityResolver.ShotRoll roll, bool secondary = false)
    {
        var baseItem = (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(NewId(), roll.Definition.SourceTemplateId, null);
        // Clone only the template for this projectile. Never alter the native database
        // template, or a saved inventory item's stats, to adjust a fuse or smoke life.
        var template = (ThrowWeapTemplate)Clone.Invoke(baseItem.Template, null)!;
        template.ExplDelay = LegendaryGrenadeStats.FuseDelay(template.ExplDelay, roll);
        if (GrenadeEffectRules.ImpactFlash(roll.Definition.RuntimeKey)) template.MinTimeToContactExplode = .3f;
        if (GrenadeEffectRules.FlashPulseCount(roll.Definition.RuntimeKey) > 1)
        { template.Strength = 0f; template.FragmentsCount = 0; }
        if (roll.Definition.GrenadeCategory == "smoke") template.EmitTime = LegendaryGrenadeStats.SmokeDuration(template.EmitTime, roll);
        var proxy = new ThrowWeap(baseItem.Id, template);
        Payloads[proxy.Id] = new Payload { Roll = roll.GrenadeSnapshot(), Expires = Time.time + 900f, Secondary = secondary };
        return proxy;
    }
    internal static void Created(Grenade grenade, ThrowWeap item, string owner)
    {
        if (!Try(item, out var p)) return;
        p.Grenade = grenade; p.Owner = owner;
        if (p.WaitForLanding && !p.LandingTimersPrepared)
        {
            p.LandingTimersPrepared = true;
            // Cancel initialization-time emission expiry even if Mono inlines
            // that small timer method. Keep the original native fuse minimum.
            grenade.StopTimer(); grenade.StopAllCoroutines(); grenade.StartTimer();
        }
        if (p.Key == "GR13") Maintain();
        var actor = GrenadeBuffs.Find(owner);
        if (actor != null)
        {
            var weapons = actor.InventoryController.Inventory.Equipment.Slots.Select(s => s.ContainedItem).OfType<Weapon>().ToArray();
            p.PistolOnly = weapons.Length == 1 && weapons[0].WeapClass == "pistol";
        }
        if (p.Key == "GR65" && p.Path is null)
        {
            p.Path = new TrailblazerPath(grenade.transform.position);
            DropSmoke(p, grenade.transform.position);
        }
    }
    internal static bool DeferInHandSmoke(Player.BaseGrenadeHandsController controller) =>
        ReusableGrenadeRuntime.IsReusable(controller.Item) && RarityResolver.TryGetDefinition(controller.Item, out var d) && d.RuntimeKey is "GR59" or "GR65";
    internal static void BindSmoke(Player.BaseGrenadeHandsController controller, Grenade grenade)
    {
        if (grenade is not SmokeGrenade smoke || !Try(grenade.WeaponSource, out var p)) return;
        if (p.DeferredEmission) return;
        try
        {
            var emission = Native.Get(controller, "GrenadeEmission") as GrenadeEmission;
            if (emission == null)
            {
                Native.Call(controller, "Cook");
                emission = Native.Get(controller, "GrenadeEmission") as GrenadeEmission;
            }
            if (emission == null) throw new InvalidOperationException("Native smoke emission was not created.");
            emission.AttachTo(grenade.transform, Vector3.zero);
            emission.SetFillParams(0f, smoke.EmitTime);
            smoke.EmissionEnd += emission.StopEmission;
            smoke.VelocityBelowThreshold += emission.Stall;
            p.Emission = emission;
        }
        catch (Exception e) { Plugin.LogSource.LogError("Legendary grenade smoke binding failed: " + e.Message); }
    }
    internal static IExplosiveItem Explosion(IExplosiveItem source, Item item) => Try(item, out var p) && !p.SmokeChild
        ? new LegendaryGrenadeExplosive(source, p) : source;
    internal static void Fragment(Shot shot, Item weapon)
    { if (Try(weapon, out var p)) shot.Damage *= p.Roll.GrenadeFragmentDamageMultiplier; }
    internal static bool Collision(Grenade grenade, object collision)
    {
        if (!Try(grenade.WeaponSource, out var p)) return true;
        var collider = Native.Get(collision, "collider") as Component;
        if (collider == null) return true;
        var body = collider.GetComponentInParent<BodyPartCollider>();
        var hitOwner = body?.PlayerProfileID ?? (Native.Call(Singleton<GameWorld>.Instance, "GetAlivePlayerBridgeByCollider", collider) as IObserverToPlayerBridge)?.iPlayer.ProfileId;
        if (hitOwner == p.Owner) return true;
        // Record direct contact before native impact detonation runs in LateUpdate.
        if (p.DirectTarget.Length == 0 && hitOwner != null) p.DirectTarget = hitOwner;
        if (!p.WaitForLanding || GroundContact(collision))
        {
            if (p.Key == "GR65" && !p.Landed) p.Path?.Advance(grenade.transform.position, position => DropSmoke(p, position));
            p.Landed = true;
            if (p.WaitForLanding && p.FuseReady && !p.SignatureFired) grenade.InvokeBlowUpEvent();
        }
        if (p.Key != "GR04") return true;
        if (p.Anchor != null) return false;
        p.Anchor = collider.transform;
        p.LocalPoint = p.Anchor.InverseTransformPoint(grenade.transform.position);
        p.LocalRotation = Quaternion.Inverse(p.Anchor.rotation) * grenade.transform.rotation;
        var rigidbody = Native.Get(grenade, "Rigidbody");
        if (rigidbody != null)
        {
            Native.Set(rigidbody, "velocity", Vector3.zero); Native.Set(rigidbody, "angularVelocity", Vector3.zero);
            grenade.SetKinematic(true);
        }
        return false; // Also prevents native glass pass-through and impact detonation.
    }
    internal static void Follow(Grenade grenade)
    {
        if (!Try(grenade.WeaponSource, out var p)) return;
        if (p.Key == "GR04" && p.Anchor != null)
            grenade.transform.SetPositionAndRotation(p.Anchor.TransformPoint(p.LocalPoint), p.Anchor.rotation * p.LocalRotation);
        if (p.Key == "GR65" && !p.Landed && !p.SignatureFired)
            p.Path?.Advance(grenade.transform.position, position => DropSmoke(p, position));
    }
    internal static bool Fuse(Grenade grenade)
    {
        if (!Try(grenade.WeaponSource, out var p) || !p.WaitForLanding) return true;
        if (p.SignatureFired) return false;
        if (p.Landed) return true;
        p.FuseReady = true; return false;
    }
    internal static void Detonated(Grenade? grenade, Item original, Vector3 position)
    {
        if (!Try(original, out var p) || p.SignatureFired) return;
        p.SignatureFired = true;
        if (!p.Secondary && GrenadeEffectRules.FlashPulseCount(p.Key) > 1 && Singleton<GameWorld>.Instance is { } flashWorld)
            flashWorld.StartCoroutine(RepeatFlashes(flashWorld, p, position, Generation));
        if (p.Roll.GrenadeDoorBreach) OpenDoors(position);
        if (p.Key == "GR12") AdvancedLegendaryActions.DeploySmokeAt(p.Owner, position, 15f, instant: true);
        if (Singleton<GameWorld>.Instance is { } world && (p.Key == "GR13" || p.Key == "GR26" && !p.Secondary))
            world.StartCoroutine(Secondary(world, p, position, Generation));
    }
    private static IEnumerator Secondary(GameWorld world, Payload parent, Vector3 position, int generation)
    {
        if (parent.Key == "GR26") yield return new WaitForSeconds(1f);
        else
        {
            var deadline = Time.time + 10f;
            while (Assets?.IsCompleted == false && Time.time < deadline && generation == Generation) yield return null;
            if (Assets?.IsFaulted == true || Assets?.IsCanceled == true || Assets?.IsCompleted == false)
            { Plugin.LogSource.LogError("M.I.R.V. F-1 assets unavailable; child grenades could not spawn."); yield break; }
        }
        if (generation != Generation || world != Singleton<GameWorld>.Instance) yield break;
        if (world.GrenadeFactory is not { } factory) yield break;
        var count = parent.Key == "GR13" ? 3 : 1;
        var startAngle = UnityEngine.Random.Range(0f, 360f);
        for (var i = 0; i < count; i++)
        {
            try
            {
                var item = parent.Key == "GR13"
                    ? (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(NewId(), F1, null)
                    : Projectile(parent.Roll, true);
                var pooled = Singleton<ObjectsFactory>.Instance.CreateItem(item, true);
                GrenadeSettings settings;
                try { settings = UnityEngine.Object.Instantiate(pooled.GetComponent<GrenadePrefab>().GrenadeItself); }
                finally { AssetPoolObject.ReturnToPool(pooled); }
                var child = factory.Create(settings, position + (parent.Key == "GR13" ? Vector3.up * .2f : Vector3.zero), Quaternion.identity, Vector3.zero, item, parent.Owner);
                Created(child, item, parent.Owner);
                world.RegisterGrenade(child);
                if (parent.Key == "GR13")
                {
                    // A 45-degree ballistic launch targets an approximate 5–10 m
                    // landing radius on level terrain; native bounces remain intact.
                    var radians = (startAngle + i * 120f + UnityEngine.Random.Range(-40f, 40f)) * Mathf.Deg2Rad;
                    var speed = Mathf.Sqrt(9.81f * UnityEngine.Random.Range(5f, 10f) / 2f);
                    var velocity = new Vector3(Mathf.Cos(radians) * speed, speed, Mathf.Sin(radians) * speed);
                    Native.Set(Native.Get(child, "Rigidbody")!, "velocity", velocity);
                    Native.Set(child, "Velocity", velocity);
                    if (Singleton<GlobalEventDispatcher>.Instantiated) Native.Call(Singleton<GlobalEventDispatcher>.Instance, "ThrowGrenade", child, position, velocity, .5f);
                }
                else { child.StopTimer(); child.InvokeBlowUpEvent(); }
            }
            catch (Exception e) { Plugin.LogSource.LogError("Legendary grenade secondary effect failed: " + e.Message); }
        }
    }

    private static IEnumerator RepeatFlashes(GameWorld world, Payload parent, Vector3 position, int generation)
    {
        var count = GrenadeEffectRules.FlashPulseCount(parent.Key);
        var interval = GrenadeEffectRules.FlashPulseInterval(parent.Key);
        for (var pulse = 1; pulse < count; pulse++)
        {
            yield return new WaitForSeconds(interval);
            if (generation != Generation || world != Singleton<GameWorld>.Instance || world.GrenadeFactory is not { } factory) yield break;
            try
            {
                // Reuse the native flash path for every pulse: cover, facing,
                // player/AI effects, visuals and sound are evaluated afresh.
                // No inventory throw/release occurs, so no extra charge is spent.
                var item = Projectile(parent.Roll, true);
                var pooled = Singleton<ObjectsFactory>.Instance.CreateItem(item, true);
                GrenadeSettings settings;
                try { settings = UnityEngine.Object.Instantiate(pooled.GetComponent<GrenadePrefab>().GrenadeItself); }
                finally { AssetPoolObject.ReturnToPool(pooled); }
                var child = factory.Create(settings, position, Quaternion.identity, Vector3.zero, item, parent.Owner);
                Created(child, item, parent.Owner);
                world.RegisterGrenade(child);
                child.StopTimer(); child.InvokeBlowUpEvent();
            }
            catch (Exception error) { Plugin.LogSource.LogError("Legendary flash pulse failed: " + error.Message); }
        }
    }
    private static void OpenDoors(Vector3 position)
    {
        foreach (var door in UnityEngine.Object.FindObjectsOfType<Door>())
            if (door.GetType() == typeof(Door) && door.DoorState == EDoorState.Shut && Vector3.Distance(door.transform.position, position) <= 3f && door.Operatable)
            {
                // Breach intact doors normally. Reopen a previously broken
                // door through its native Open interaction without rebreaking it.
                if (door.IsBroken) door.Interact(new InteractionResult(EInteractionType.Open));
                else door.KickOpen(position);
            }
    }
    private static bool ActiveSmoke(Payload p) => !p.SmokeChild && !p.SmokeEnded && p.SmokeStarted >= 0f && Time.time < p.SmokeUntil && p.Grenade != null;
    internal static bool InSmoke(Player player, string key) => Payloads.Values.Any(p => p.Key == key && p.Owner == player.ProfileId &&
        ActiveSmoke(p) && Vector3.Distance(player.Position, p.Grenade!.transform.position) <= 8f);
    internal static IEnumerable<EffectStatusModel.Status> SmokeStatuses(Player player)
    {
        foreach (var group in Payloads.Values.Where(p => p.Key is "GR57" or "GR58" or "GR62" or "GR63" or "GR64" &&
            p.Owner == player.ProfileId && ActiveSmoke(p) && Vector3.Distance(player.Position, p.Grenade!.transform.position) <= 8f &&
            (p.Key != "GR58" || AdvancedMeleeRuntime.Crouched(player))).GroupBy(p => p.Key))
        {
            var p = group.OrderByDescending(p => p.SmokeUntil).First();
            yield return new EffectStatusModel.Status("grenade-buff:smoke:" + p.Key, p.Roll.Definition.ShortName, true, p.SmokeUntil - Time.time);
        }
    }
    internal static bool StartSmokeTimer(SmokeGrenade smoke) => !Try(smoke.WeaponSource, out var p) || !p.WaitForLanding || p.SmokeStarted >= 0f;
    internal static void Emitting(SmokeGrenade smoke, float time)
    {
        if (!Try(smoke.WeaponSource, out var p)) return;
        p.Grenade = smoke;
        if (p.Owner.Length == 0) p.Owner = smoke.ProfileId;
        if (p.SmokeStarted >= 0f) return;
        p.SmokeStarted = time; p.SmokeUntil = time + Math.Max(.01f, smoke.LifeTime); p.SmokeEnded = false;
        if (p.WaitForLanding && !p.SmokeTimerStarted) { p.SmokeTimerStarted = true; smoke.StartEmissionBehaviour(); }
        if (p.DeferredEmission) CreateEmission(smoke, p);
        if (p.Key == "GR59") smoke.UpdateEmissionArea(smoke._smokeGrenadeSettings._sizeOverTime.keys.OrderByDescending(k => k.value).First().time);
    }
    private static void CreateEmission(SmokeGrenade smoke, Payload p)
    {
        if (p.Emission != null) return;
        try
        {
            var emission = Singleton<Effects>.Instance.GetEmissionEffect(smoke._smokeGrenadeSettings.EmmisionEffect);
            emission.AttachTo(smoke.transform, Vector3.zero);
            p.Emission = emission;
            var prewarm = p.Key == "GR59" && Emissions.TryGetValue(emission, out var original) ? original.Delay : 0f;
            if (p.Key == "GR59") Native.Set(emission, "_defaultStartFillDelay", 0f);
            emission.SetFillParams(0f, smoke.EmitTime);
            smoke.EmissionEnd += emission.StopEmission; smoke.VelocityBelowThreshold += emission.Stall;
            emission.StartEmission(prewarm);
        }
        catch (Exception e) { Plugin.LogSource.LogError("Legendary smoke deployment failed: " + e.Message); }
    }
    private static bool GroundContact(object collision)
    {
        if (Native.Get(collision, "contacts") is not IEnumerable contacts) return false;
        foreach (var contact in contacts)
            if (Native.Get(contact, "normal") is Vector3 normal && normal.y >= .35f) return true;
        return false;
    }
    private static void DropSmoke(Payload parent, Vector3 position)
    {
        if (Singleton<GameWorld>.Instance is not { } world || world.GrenadeFactory is not { } factory) return;
        try
        {
            var item = (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(NewId(), M18, null);
            Payloads[item.Id] = new Payload { Roll = parent.Roll.GrenadeSnapshot(), Owner = parent.Owner, SmokeChild = true, Expires = Time.time + 900f };
            var pooled = Singleton<ObjectsFactory>.Instance.CreateItem(item, true);
            GrenadeSettings settings;
            try { settings = UnityEngine.Object.Instantiate(pooled.GetComponent<GrenadePrefab>().GrenadeItself); }
            finally { AssetPoolObject.ReturnToPool(pooled); }
            var child = factory.Create(settings, position, Quaternion.identity, Vector3.zero, item, parent.Owner);
            // Drops begin on the flight path. Ignore only this parent/child
            // collider pair before the next physics step, keeping map collisions.
            if (parent.Grenade != null) TrailblazerCollision.Ignore(parent.Grenade, child);
            Created(child, item, parent.Owner); world.RegisterGrenade(child);
        }
        catch (Exception e) { Plugin.LogSource.LogError("Trailblazer smoke drop failed: " + e.Message); }
    }
    internal static void SmokeArea(SmokeGrenade grenade, ref float normalTime)
    {
        if (!Try(grenade.WeaponSource, out var p) || p.SmokeChild) return;
        var curve = grenade._smokeGrenadeSettings._sizeOverTime;
        var peakTime = curve.keys.OrderByDescending(k => k.value).First().time;
        if (normalTime > 0f && normalTime < peakTime) normalTime = p.Key == "GR59" ? peakTime : Math.Min(peakTime, normalTime / p.Roll.GrenadeSmokeBuildTimeMultiplier);
    }
    internal static void SmokeScale(SmokeGrenade grenade)
    {
        if (!Try(grenade.WeaponSource, out var p) || p.SmokeChild) return;
        if (Native.Get(grenade._smokeGrenadeSettings, "_emissionArea") is Component area)
        {
            var size = LegendaryGrenadeStats.SmokeSize(p.Roll);
            area.transform.localScale = Vector3.Scale(area.transform.localScale, new Vector3(size, size, size));
        }
    }
    internal static void Attached(GrenadeEmission emission, Transform transform)
    {
        var grenade = transform.GetComponent<Grenade>();
        RarityResolver.ShotRoll? roll = null;
        var projectileId = "";
        if (grenade != null && Try(grenade.WeaponSource, out var p))
        { projectileId = grenade.WeaponSource.Id; if (!p.SmokeChild) roll = p.Roll; }
        else
        {
            // Native smoke emission can start in the hand before release. Apply
            // growth settings on that first attachment, before its fill coroutine
            // captures a delay; changing the delay after release would be too late.
            var hands = Singleton<GameWorld>.Instance?.RegisteredPlayers.OfType<Player>()
                .Select(player => player.HandsController).OfType<Player.BaseGrenadeHandsController>()
                .FirstOrDefault(h => ReferenceEquals(Native.Get(h, "_grenadeTransform"), transform));
            if (hands?.Item is { } item && ReusableGrenadeRuntime.IsReusable(item)) roll = RarityResolver.Resolve(item);
        }
        if (roll?.Definition.GrenadeCategory != "smoke" && projectileId.Length == 0) return;
        RestoreEmission(emission);
        Emissions[emission] = new EmissionState { Scale = emission.transform.localScale, Delay = Native.Number(emission, "_defaultStartFillDelay"),
            Size = Native.Number(emission, "_startFillSize"), Distance = Native.Number(emission, "_startFillDistance"), ProjectileId = projectileId };
        if (projectileId.Length > 0 && Payloads.TryGetValue(projectileId, out var tracked)) { tracked.Emission = emission; tracked.SmokeEnded = false; }
        if (roll == null) return;
        var size = LegendaryGrenadeStats.SmokeSize(roll);
        Emissions[emission].Particles = SmokeParticleScale.Apply(emission);
        emission.transform.localScale = Vector3.Scale(emission.transform.localScale, new Vector3(size, size, size));
        Native.Set(emission, "_defaultStartFillDelay", Emissions[emission].Delay * roll.GrenadeSmokeBuildTimeMultiplier);
        // The native fill particle inherits the root scale too. Scaling its
        // world-space spawn distance is necessary; scaling its size again is not.
        Native.Set(emission, "_startFillSize", Emissions[emission].Size);
        Native.Set(emission, "_startFillDistance", Emissions[emission].Distance * size);
    }
    internal static void RestoreEmission(GrenadeEmission emission)
    {
        if (!Emissions.TryGetValue(emission, out var s)) return;
        Emissions.Remove(emission);
        if (s.ProjectileId.Length > 0 && Payloads.TryGetValue(s.ProjectileId, out var p) && p.Emission == emission)
        { p.Emission = null; p.SmokeEnded = true; }
        if (emission == null) return;
        foreach (var particle in s.Particles) SessionCleanup.Run("smoke particle restoration", particle.Restore);
        SessionCleanup.Run("smoke scale restoration", () => emission.transform.localScale = s.Scale);
        SessionCleanup.Run("smoke delay restoration", () => Native.Set(emission, "_defaultStartFillDelay", s.Delay));
        SessionCleanup.Run("smoke size restoration", () => Native.Set(emission, "_startFillSize", s.Size));
        SessionCleanup.Run("smoke distance restoration", () => Native.Set(emission, "_startFillDistance", s.Distance));
    }
}

internal sealed class LegendaryGrenadeExplosive : IExplosiveItem
{
    private readonly IExplosiveItem Source;
    private readonly LegendaryGrenadeEffects.Payload Payload;
    private RarityResolver.ShotRoll Roll => Payload.Roll;
    internal LegendaryGrenadeExplosive(IExplosiveItem source, LegendaryGrenadeEffects.Payload payload) { Source = source; Payload = payload; }
    // Radius bonuses affect visual flash, with audio still using its normal radius.
    public Vector3 Blindness => Payload.Key == "GR35" ? new Vector3(0f, 25f, 20f)
        : new Vector3(Source.Blindness.x * Roll.GrenadeFlashRadiusMultiplier, Source.Blindness.y * Roll.GrenadeFlashRadiusMultiplier, Source.Blindness.z * Roll.GrenadeFlashDurationMultiplier);
    public Vector3 Contusion => Source.Contusion;
    public Vector3 ArmorDistanceDistanceDamage => new(Source.ArmorDistanceDistanceDamage.x * Roll.GrenadeRadiusMultiplier, Source.ArmorDistanceDistanceDamage.y * Roll.GrenadeRadiusMultiplier, Source.ArmorDistanceDistanceDamage.z * Roll.GrenadeDamageMultiplier);
    public float MinExplosionDistance => Source.MinExplosionDistance * Roll.GrenadeRadiusMultiplier;
    public float MaxExplosionDistance => Source.MaxExplosionDistance * Roll.GrenadeRadiusMultiplier;
    public int FragmentsCount => (int)Math.Round(Source.FragmentsCount * Roll.GrenadeFragmentMultiplier, MidpointRounding.AwayFromZero);
    public float GetStrength => Source.GetStrength * Roll.GrenadeDamageMultiplier;
    public bool IsDummy => Source.IsDummy;
    public Ammo CreateFragment() => Source.CreateFragment();
}
