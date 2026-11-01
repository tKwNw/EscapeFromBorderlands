using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using Comfort.Common;using EFT;using EFT.AssetsManager;using EFT.InventoryLogic;using HarmonyLib;using Systems.Effects;using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static class SmokeLifecycleFixture {
private const string SmokeGrenadeTemplateId="5a2a57cfc4a2826c6e06d44a"; private static readonly List<ShortSmoke> Smokes=new(); private static GameWorld? AssetWorld; private static Task? AssetLoad; private static CancellationTokenSource? AssetCancellation; internal static SmokeGrenade Last=>Smokes.Last().Grenade; internal static int Count=>Smokes.Count; internal static void Reset(){foreach(var s in Smokes.ToArray())RemoveSmoke(s);AssetWorld=null;AssetLoad=null;AssetCancellation?.Dispose();AssetCancellation=null;}
private sealed class ShortSmoke
    {
        public GameWorld World = null!;
        public SmokeGrenade Grenade = null!;
        public GrenadeEmission? Emission;
        public Component? Area;
        public string Id = string.Empty;
        public float EndTime;
        public float OriginalFillDelay;
        public float AreaTime = 0.35f;
        public string OwnerProfileId = string.Empty, StatusName = string.Empty, StatusKey = string.Empty;
    }
public static void Maintain(GameWorld? world)
    {
        if (world is not null && Singleton<ObjectsFactory>.Instantiated && Singleton<ItemFactory>.Instantiated && AssetWorld != world)
            PreloadSmoke(world);
        foreach (var smoke in Smokes.ToArray())
        {
            if (smoke.World != world || smoke.Grenade == null || Time.time >= smoke.EndTime) RemoveSmoke(smoke);
            else smoke.Grenade.UpdateEmissionArea(smoke.AreaTime);
        }
    }
private static void PreloadSmoke(GameWorld world)
    {
        AssetWorld = world;
        AssetCancellation = new CancellationTokenSource();
        try
        {
            var item = Singleton<ItemFactory>.Instance.CreateItem("efb-smoke-preload", SmokeGrenadeTemplateId, null);
            AssetLoad = Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local,
                new[] { item.Prefab }, Diz.Jobs.JobYieldPriority.Low, ct: AssetCancellation.Token);
        }
        catch (Exception exception)
        {
            AssetLoad = Task.FromException(exception);
            Plugin.LogSource.LogWarning($"Like A Ghost smoke preload failed safely: {exception.Message}");
        }
    }
public static void DeploySmokeAt(Player player, Vector3 position, float durationSeconds, string statusName = "", string statusKey = "", bool instant = false)
    { DeploySmokeAt(player.ProfileId, position, durationSeconds, statusName, statusKey, instant); }
public static void DeploySmokeAt(string ownerProfileId, Vector3 position, float durationSeconds, string statusName = "", string statusKey = "", bool instant = false)
    {
        var world = Singleton<GameWorld>.Instance;
        if (world?.GrenadeFactory is null) return;
        if (AssetWorld != world) PreloadSmoke(world);
        world.StartCoroutine(DeploySmokeWhenReady(world, position, ownerProfileId, durationSeconds, statusName, statusKey, instant));
    }
private static IEnumerator DeploySmokeWhenReady(GameWorld world, Vector3 position, string profileId, float durationSeconds, string statusName, string statusKey, bool instant = false)
    {
        var load = AssetLoad;
        var deadline = Time.time + 30f;
        while (load is not null && !load.IsCompleted && Time.time < deadline)
        {
            if (Singleton<GameWorld>.Instance != world || AssetWorld != world) yield break;
            yield return null;
        }
        if (Singleton<GameWorld>.Instance != world || AssetWorld != world) yield break;
        if (load is null || !load.IsCompleted || load.IsFaulted || load.IsCanceled)
        {
            Plugin.LogSource.LogWarning("Like A Ghost smoke asset was unavailable; deployment was skipped safely.");
            yield break;
        }
        CreateSmoke(world, position, profileId, durationSeconds, statusName, statusKey, instant);
    }
private static void CreateSmoke(GameWorld world, Vector3 position, string profileId, float durationSeconds, string statusName, string statusKey, bool instant = false)
    {
        ShortSmoke? smoke = null;
        GameObject? pooledItem = null;
        SmokeGrenadeSettings? settings = null;
        try
        {
            var id = $"efb-smoke-{Guid.NewGuid():N}";
            var item = (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(id, SmokeGrenadeTemplateId, null);
            pooledItem = Singleton<ObjectsFactory>.Instance.CreateItem(item, true);
            settings = (SmokeGrenadeSettings)UnityEngine.Object.Instantiate(pooledItem.GetComponent<GrenadePrefab>().GrenadeItself);
            settings.Offset = Vector3.zero;
            settings.gameObject.SetActive(true);
            var grenade = (SmokeGrenade)world.GrenadeFactory.Create(settings, position, Quaternion.identity, Vector3.zero, item, profileId, withBallisticCalculator: false);
            smoke = new ShortSmoke { World = world, Grenade = grenade, Id = id, EndTime = Time.time + Math.Max(1f, durationSeconds),
                OwnerProfileId = profileId, StatusName = statusName, StatusKey = statusKey,
                Area = AccessTools.Field(typeof(SmokeGrenadeSettings), "_emissionArea").GetValue(settings) as Component,
                AreaTime = instant ? settings._sizeOverTime.keys.OrderByDescending(k => k.value).First().time : 0.35f };
            Smokes.Add(smoke);
            // Only this grenade instance gets a short lifetime. Cancel native
            // fuse/emission timers before starting its visual and AI screen.
            grenade.StopTimer(); grenade.StopAllCoroutines(); grenade.RemoveRigidbody();
            AccessTools.Field(typeof(SmokeGrenade), "_emitTime").SetValue(grenade,
                grenade.EmitTime * Math.Max(1f, durationSeconds) / Math.Max(0.01f, grenade.LifeTime));
            grenade.SetEmission(Time.time);
            grenade.UpdateEmissionArea(smoke.AreaTime);
            world.RegisterGrenade(grenade);
            var emission = Singleton<Effects>.Instance.GetEmissionEffect(settings.EmmisionEffect)
                ?? throw new InvalidOperationException($"Missing smoke emission effect '{settings.EmmisionEffect}'.");
            smoke.Emission = emission;
            var fillDelay = AccessTools.Field(typeof(GrenadeEmission), "_defaultStartFillDelay");
            smoke.OriginalFillDelay = (float)fillDelay.GetValue(emission);
            fillDelay.SetValue(emission, 0f);
            emission.AttachTo(grenade.transform, Vector3.zero);
            emission.SetFillParams(0f, Math.Max(1f, durationSeconds));
            emission.StartEmission(instant ? smoke.OriginalFillDelay : 0f);
            if (Singleton<GlobalEventDispatcher>.Instantiated)
                Singleton<GlobalEventDispatcher>.Instance.GrenadeExplosion(position, profileId, true,
                    SmokeRadius(smoke), Math.Max(1f, durationSeconds), grenade.Id);
        }
        catch (Exception exception)
        {
            if (smoke is not null) RemoveSmoke(smoke);
            else if (settings is not null) UnityEngine.Object.Destroy(settings.gameObject);
            Plugin.LogSource.LogWarning($"Like A Ghost smoke deployment failed safely: {exception.Message}");
        }
        finally { if (pooledItem is not null) AssetPoolObject.ReturnToPool(pooledItem); }
    }
private static float SmokeRadius(ShortSmoke smoke)
    {
        var area = smoke.Area;
        if (area is null) return smoke.Grenade.Radius;
        var radius = area.GetType().GetProperty("radius")?.GetValue(area);
        return radius is float value ? value * Math.Abs(area.transform.lossyScale.x) : smoke.Grenade.Radius;
    }
private static void RemoveSmoke(ShortSmoke smoke)
    {
        Smokes.Remove(smoke);
        try
        {
            var emission = smoke.Emission;
            if (emission != null)
            {
                emission.StopAllCoroutines(); emission.ReleaseSoundSource();
                // Clear every native particle system before returning the
                // effect to its pool, including fill and lingering particles.
                var particle = AccessTools.Field(typeof(GrenadeEmission), "_particleSystem").GetValue(emission) as Component;
                if (particle is not null)
                    foreach (var system in emission.GetComponentsInChildren(particle.GetType(), true))
                    {
                        AccessTools.Method(system.GetType(), "Stop", [typeof(bool)])?.Invoke(system, [true]);
                        AccessTools.Method(system.GetType(), "Clear", [typeof(bool)])?.Invoke(system, [true]);
                    }
                AccessTools.Field(typeof(GrenadeEmission), "_defaultStartFillDelay").SetValue(emission, smoke.OriginalFillDelay);
                if (Singleton<Effects>.Instantiated) Singleton<Effects>.Instance.DisposeEmissionEffect(emission);
                else UnityEngine.Object.Destroy(emission.gameObject);
            }
            if (smoke.Area != null)
            {
                smoke.Area.gameObject.SetActive(false);
                if (smoke.Grenade != null) smoke.Area.transform.parent = smoke.Grenade.transform;
                else UnityEngine.Object.Destroy(smoke.Area.gameObject);
            }
            if (smoke.World != null) smoke.World.UnregisterGrenade(smoke.Id);
            if (smoke.Grenade != null)
            {
                smoke.Grenade.StopTimer(); smoke.Grenade.StopAllCoroutines();
                UnityEngine.Object.Destroy(smoke.Grenade.gameObject);
            }
        }
        catch (Exception exception)
        {
            Plugin.LogSource.LogWarning($"Like A Ghost smoke cleanup failed safely: {exception.Message}");
        }
    }
}
