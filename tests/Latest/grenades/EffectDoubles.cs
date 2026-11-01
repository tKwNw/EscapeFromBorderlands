// Engine doubles for running the unchanged production signatures and Harmony
// adapters. NativeContracts independently verifies the real SPT 4.0.13 API.
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using UnityEngine;

namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value = default!; public object Definition = new(); }
    public class ConfigFile { public void Remove(object definition) {} public ConfigEntry<T> Bind<T>(string section, string key, T value, string description="") => new() { Value = value }; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method)] public class HarmonyPriority(int priority) : Attribute { public int Value = priority; }
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyAfter(string owner):Attribute {public string Owner=owner;}
    public static partial class AccessTools
    {
        public static ConstructorInfo Constructor(Type type, Type[] parameters) => type.GetConstructor(parameters)!;
        public static MethodInfo DeclaredMethod(Type type,string name)=>type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)!;
    }
}
namespace UnityEngine
{
    public static class Time { public static float time, unscaledTime; }
    public class Object
    {
        public static readonly List<Object> All = [];
        public Object() => All.Add(this);
        public static T[] FindObjectsOfType<T>() => All.OfType<T>().ToArray();
        public static void Destroy(Object obj) { if(obj is GameObject go)go.Active=false; All.Remove(obj); }
        public static T Instantiate<T>(T original) where T : Object
        {
            if (original is SmokeGrenadeSettings s) return (T)(Object)new SmokeGrenadeSettings { _sizeOverTime = s._sizeOverTime, Offset = s.Offset };
            if (original is GrenadeSettings g) return (T)(Object)new GrenadeSettings { Offset = g.Offset };
            throw new NotSupportedException();
        }
    }
    public class GameObject : Object
    {
        public bool Active = true;
        public readonly Transform transform;
        public readonly List<Component> Components = [];
        public readonly List<GameObject> Children = [];
        public GameObject(Transform? owner = null) => transform = owner ?? new Transform(this);
        public void SetActive(bool value) => Active = value;
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault()!;
        public T Add<T>(T c) where T : Component { c.gameObject = this; Components.Add(c); return c; }
        public GameObject AddChild(bool active = true) { var child = new GameObject { Active=active }; child.transform.parent=transform; Children.Add(child); return child; }
        public IEnumerable<Component> InChildren(bool inactive) => Components.Concat(Children.Where(c=>inactive||c.Active).SelectMany(c=>c.InChildren(inactive)));
    }
    public class Component : Object
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public T GetComponentInParent<T>() where T : class => gameObject.GetComponent<T>();
        public IEnumerable<Component> GetComponentsInChildren(Type type,bool inactive)=>gameObject.InChildren(inactive).Where(type.IsInstanceOfType);
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector3 position, localScale = new(1, 1, 1); public Vector3 lossyScale=>parent==null?localScale:Vector3.Scale(localScale,parent.lossyScale); public Transform? parent;
        public Quaternion rotation = Quaternion.identity;
        public Transform() => gameObject = new(this);
        public Transform(GameObject owner) => gameObject = owner;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public Vector3 InverseTransformPoint(Vector3 point) => Quaternion.Inverse(rotation) * (point - position);
        public Vector3 TransformPoint(Vector3 point) => position + rotation * point;
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
    }
    public readonly record struct Quaternion(float x, float y, float z, float w)
    {
        public static Quaternion identity => new(0, 0, 0, 1);
        private System.Numerics.Quaternion Value => new(x, y, z, w);
        public static Quaternion Inverse(Quaternion q) { var v = System.Numerics.Quaternion.Inverse(q.Value); return new(v.X, v.Y, v.Z, v.W); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { var v = a.Value * b.Value; return new(v.X, v.Y, v.Z, v.W); }
        public static Vector3 operator *(Quaternion q, Vector3 a) { var v = System.Numerics.Vector3.Transform(new(a.x, a.y, a.z), q.Value); return new(v.X, v.Y, v.Z); }
    }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => new(); public static Vector3 up => new(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Distance(Vector3 a, Vector3 b) { var d = a - b; return MathF.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z); }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public static class Mathf
    {
        public const float Deg2Rad = MathF.PI / 180;
        public static float Sqrt(float x) => MathF.Sqrt(x); public static float Cos(float x) => MathF.Cos(x); public static float Sin(float x) => MathF.Sin(x);
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
        public static float InverseLerp(float a, float b, float v) => a == b ? 0 : Clamp01((v - a) / (b - a));
    }
    public static class Random { public static float Range(float a, float b) => (a + b) / 2; }
    public class WaitForSeconds(float seconds) { public float Seconds = seconds; }
    public struct Keyframe(float t, float v) { public float time = t, value = v; }
    public class AnimationCurve { public Keyframe[] keys = [new(0, 0), new(.3f, 1), new(1, 0)]; }
    public class Rigidbody { public Vector3 velocity, angularVelocity; public bool isKinematic; }
    public class Collider : Component { }
    public static class Physics
    {
        public static bool Blocked;
        public static readonly HashSet<(Collider,Collider)> Ignored=[];
        public static void IgnoreCollision(Collider a,Collider b,bool ignore=true) { if(ignore)Ignored.Add((a,b));else{Ignored.Remove((a,b));Ignored.Remove((b,a));} }
        public static bool GetIgnoreCollision(Collider a,Collider b)=>Ignored.Contains((a,b))||Ignored.Contains((b,a));
        public static bool Linecast(Vector3 from, Vector3 to, int mask) => Blocked;
    }
    public enum ParticleSystemScalingMode { Hierarchy, Local, Shape }
    public class ParticleSystem : Component
    {
        public ParticleSystemScalingMode Mode=ParticleSystemScalingMode.Local;
        public MainModule main=>new(this);
        public struct MainModule(ParticleSystem owner)
        { public ParticleSystemScalingMode scalingMode { get=>owner.Mode;set=>owner.Mode=value; } }
        public Vector3 VisibleScale=>Mode==ParticleSystemScalingMode.Hierarchy?transform.lossyScale:Mode==ParticleSystemScalingMode.Local?transform.localScale:new(1,1,1);
        public void Stop(bool children){} public void Clear(bool children){}
    }
}
public class GrenadeSettings : Component { public Vector3 Offset; public string EmmisionEffect => "smoke"; }
public class SmokeGrenadeSettings : GrenadeSettings { public AnimationCurve _sizeOverTime = new(); public Component _emissionArea = new(); }
public class GrenadePrefab : Component { public GrenadeSettings GrenadeItself = new(); }
public class ThrowWeapTemplate
{
    public string Id = ""; public float MinTimeToContactExplode = -1; public float ExplDelay = 3; public int EmitTime; public float Strength = 100, MinExplosionDistance = 2, MaxExplosionDistance = 5;
    public int FragmentsCount = 75; public Vector3 Blindness, Contusion, ArmorDistanceDistanceDamage = new(2, 5, 10);
    public string FragmentType = "test-fragment";
}
public partial class ThrowWeap : IExplosiveItem
{
    public ThrowWeapTemplate Template = new();
    public ThrowWeap() { NativeAttributes(); }
    public ThrowWeap(string id, ThrowWeapTemplate template) { Id = id; Template = template; StringTemplateId = template.Id; CurrentAddress = null; NativeAttributes(); }
    private void NativeAttributes()
    {
        Attributes.Add(new ItemAttribute(EItemAttributeId.ExplosionDelay) { Base = () => GetExplDelay, StringValue = () => GetExplDelay + " sec" });
        Attributes.Add(new ItemAttribute(EItemAttributeId.MaximumThrowDamage) { Base = () => GetStrength, StringValue = () => GetStrength.ToString() });
        Attributes.Add(new ItemAttribute(EItemAttributeId.ExplosionDistance) { Base = () => MinExplosionDistance, StringValue = () => MinExplosionDistance + " - " + MaxExplosionDistance + " meters" });
        Attributes.Add(new ItemAttribute(EItemAttributeId.FragmentsCount) { Base = () => FragmentsCount, StringValue = () => FragmentsCount.ToString() });
    }
    public float GetExplDelay => Template.ExplDelay;
    public float MinTimeToContactExplode => Template.MinTimeToContactExplode;
    public string FragmentType => Template.FragmentType;
    public Vector3 Blindness => Template.Blindness; public Vector3 Contusion => Template.Contusion;
    public Vector3 ArmorDistanceDistanceDamage => Template.ArmorDistanceDistanceDamage;
    public float MinExplosionDistance => Template.MinExplosionDistance; public float MaxExplosionDistance => Template.MaxExplosionDistance;
    public int FragmentsCount => Template.FragmentsCount; public float GetStrength => Template.Strength;
    public float EmitTime => Template.EmitTime;
    public bool IsDummy => GetStrength <= 0 && Blindness.y <= 0;
    public Ammo CreateFragment() => new();
}
public partial class ItemFactory
{
    public Dictionary<string, ItemTemplate> ItemTemplates = new() { ["test-fragment"] = new AmmoTemplate { Damage = 65 } };
    public static ThrowWeapTemplate Template(string id) => new() { Id = id,
        ExplDelay = id == "58d3db5386f77426186285a0" ? 5 : id == "5a0c27731526d80618476ac4" ? 2 : id == "619256e5f8af2c1a4e1f5d92" ? 1.5f : 3,
        Strength = id is "5a0c27731526d80618476ac4" or "619256e5f8af2c1a4e1f5d92" ? 0 : 100,
        FragmentsCount = id is "5a0c27731526d80618476ac4" or "619256e5f8af2c1a4e1f5d92" ? 0 : 75,
        EmitTime = id == "5a2a57cfc4a2826c6e06d44a" ? 120 : id == "617aa4dd8166f034d57de9c5" ? 90 : 0,
        Blindness = id is "5a0c27731526d80618476ac4" or "619256e5f8af2c1a4e1f5d92" ? new(10, 20, 40) : new() };
}
namespace EFT.AssetsManager
{
    public partial class ObjectsFactory
    {
        public GameObject CreateItem(Item item, bool animated) { var go = new GameObject(); go.Add(new GrenadePrefab { GrenadeItself = item is ThrowWeap t && t.EmitTime > 0 ? new SmokeGrenadeSettings() : new GrenadeSettings() }); return go; }
    }
    public static class AssetPoolObject { public static void ReturnToPool(GameObject go) { } }
}
public class GrenadeEmission : Component
{
    public float _defaultStartFillDelay = 25, _startFillSize = 3.5f, _startFillDistance = 1;
    public Component? _particleSystem; public ParticleSystem _fillSystem;
    public GrenadeEmission() { _particleSystem=gameObject.Add(new ParticleSystem()); _fillSystem=gameObject.AddChild(false).Add(new ParticleSystem{Mode=ParticleSystemScalingMode.Shape}); }
    public void StopAllCoroutines(){} public void ReleaseSoundSource(){}
    public bool Stopped; public float Life, Prewarm; public Transform? Parent;
    public void AttachTo(Transform t, Vector3 offset) { Parent = t; LegendaryGrenadeEffects.Attached(this, t); }
    public void SetFillParams(float elapsed, float life) => Life = life;
    public void StartEmission(float prewarm) => Prewarm = prewarm;
    public void StopEmission(Grenade g) => Stopped = true;
    public void Stall(object obj) { }
}
namespace Systems.Effects
{
    public class Effects
    {
        public GrenadeEmission GetEmissionEffect(string name) => new();
        public void DisposeEmissionEffect(GrenadeEmission emission) => LegendaryGrenadeEffects.RestoreEmission(emission);
    }
}
namespace EFT.InventoryLogic
{
    public class Weapon : Item { public string WeapClass = "pistol"; public float RecoilDelta => 0; }
    public partial class Equipment { public List<Slot> Slots = []; }
}
public class Shot { public float Damage = 100; }
public class BodyPartCollider : Component { public string PlayerProfileID = ""; }
public class ContactPoint { public Vector3 normal = Vector3.up; }
public class Collision { public Component collider = new(); public ContactPoint[] contacts = [new()]; }
public static class LayersMaskController { public const int GrenadeObstaclesColliderMask = 1; }
public struct PlayerEmitterCache { public float Distance; public Vector3 DirectionToEmitter; public bool TryToApplyStun, TryToApplyBurnEyes, TryToApplyContusion; }
public static class FirearmControllerSharedMethods
{
    public static void ApplyLightAndSoundHealthEffects(List<IObserverToPlayerBridge> players, Dictionary<IObserverToPlayerBridge, PlayerEmitterCache> caches, Vector3 position, Vector3 blind, Vector3 contusion = default) =>
        PatchHooks.Prefix("LegendaryGrenadeFlashPatch", players, caches, position, blind, contusion);
}
public static class ExplosionSharedMethods { public static void AffectPlayersShared() { } }
namespace EFT
{
    public interface IObserverToPlayerBridge { Player iPlayer { get; } bool IsAI { get; } }
    public class Bridge(Player p) : IObserverToPlayerBridge { public Player iPlayer => p; public bool IsAI => p.IsAI; }
    public enum EBodyPart { Common, Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }
    public enum EDamageType { Bullet, GrenadeFragment, Other }
    public enum EBotState { Active, Inactive }
    public class BotSettings { public BotFile FileSettings = new(); }
    public class BotFile { public GrenadeConfig Grenade = new(); }
    public class GrenadeConfig { public float FLASH_GRENADE_TIME_COEF = 1; }
    public class BotFlashes { public float Sound, Visual; public void AddSoundEffect(float time, Vector3 pos) => Sound = time; public void AddBlindEffect(float time, Vector3 pos) => Visual = time; }
    public class Bot { public EBotState BotState = EBotState.Active; public BotSettings Settings = new(); public BotFlashes FlashGrenade = new(); }
    public class AIData { public Bot BotOwner = new(); }
    public class Bones { public Transform Head = new(); }
    public class Breath { public float HipPenalty; }
    public class Pwa { public Breath Breath = new(); public int Refreshes; public void UpdateWeaponVariables()=>Refreshes++; }
    public enum EInteractionType { Open }
    public class FirearmsAnimator { public float Speed = 1; public void SetAnimationSpeed(float speed) => Speed = speed; }
    public partial class Player
    {
        public Vector3 Position, LookDirection = new(0, 0, 1); public Bones PlayerBones = new(); public AIData AIData = new();
        public Pwa ProceduralWeaponAnimation = new(); public FirearmsAnimator HandsAnimator = new(); public bool Crouched;
        private HealthSystem.ActiveHealthController? health;
        public HealthSystem.ActiveHealthController ActiveHealthController => health ??= new() { Player = this };
        public partial class BaseGrenadeHandsController
        {
            public GrenadeEmission? GrenadeEmission; public Transform _grenadeTransform = new();
            public void Spawn(float animationSpeed, Action callback) { }
            public void StartCountdown() { }
            public void Cook() { if(LegendaryGrenadeEffects.DeferInHandSmoke(this))return;GrenadeEmission = new(); GrenadeEmission.StartEmission(0); }
        }
        public class GrenadeHandsController : BaseGrenadeHandsController { }
        public class MedsController : AbstractHandsController { }
        public partial class PlayerInventoryController
        {
            public class LoadMagazineProcess
            {
                public InventoryController _inventoryController; public float _loadOneAmmoSpeed,_cooldown;
                public LoadMagazineProcess(InventoryController inventory,Magazine magazine,Ammo ammo,int count,bool elite,float seconds){_inventoryController=inventory;_loadOneAmmoSpeed=_cooldown=seconds;}
                public Task method_5()=>Task.CompletedTask;
            }
            public class UnloadMagazineProcess
            {
                public InventoryController _inventoryController; public float _loadOneAmmoSpeed,_cooldown;
                public UnloadMagazineProcess(InventoryController inventory,Magazine magazine,float seconds,bool elite){_inventoryController=inventory;_loadOneAmmoSpeed=_cooldown=seconds;}
                public Task method_4()=>Task.CompletedTask;
            }
        }
        public class FirearmController : AbstractHandsController
        {
            public Player? Owner; public float HipInaccuracy = 1; public int ReloadRefreshes;
            public void Spawn(float animationSpeed, Action callback) { }
            public void SetAnimatorAndProceduralValues() => ReloadRefreshes++;
            public void UpdateHipInaccuracy() { HipInaccuracy = GrenadeBuffs.Hip(Owner); }
        }
    }
    public partial class Grenade : Component
    {
        public ThrowWeap WeaponSource { get => Original!; set => Original = value; }
        public Rigidbody Rigidbody = new(); public Vector3 Velocity; public bool enabled = true, Destroyed;
        public GrenadeSettings GrenadeSettings = new(); public float Prewarm;
        public Action<Grenade>? VelocityBelowThreshold;
        public void SetKinematic(bool value) => Rigidbody.isKinematic = value;
        public int Id=>WeaponSource.Id.GetHashCode(); public void StopAllCoroutines(){if(this is SmokeGrenade s)s.TimerStarts=0;} public void RemoveRigidbody(){}
        public void StartTimer(){}
        public void StopTimer() { }
        public void OnCollisionEnter(object collision) => LegendaryGrenadeEffects.Collision(this, collision);
        public float Lifetime; public bool ContactPending;
        public void ProcessContactExplodeCollision(float impulse){if(WeaponSource.MinTimeToContactExplode>=0&&Lifetime>=WeaponSource.MinTimeToContactExplode)ContactPending=true;}
        public void LateUpdate(){LegendaryGrenadeEffects.Follow(this);if(ContactPending){ContactPending=false;InvokeBlowUpEvent();}}
        public static List<(Item Item, Vector3 Position, IExplosiveItem Effect, string Owner)> Blasts = [];
        public void InvokeBlowUpEvent()
        {
            if (!LegendaryGrenadeEffects.Fuse(this)) return;
            LegendaryGrenadeEffects.Try(Original, out var payload);
            var old = LegendaryGrenadeEffects.CurrentExplosion; LegendaryGrenadeEffects.CurrentExplosion = payload;
            try
            {
                Blasts.Add((WeaponSource, transform.position, ReusableGrenadeRuntime.Explosion(WeaponSource, WeaponSource), ProfileId));
                LegendaryGrenadeEffects.Detonated(this, WeaponSource, transform.position);
                if (this is SmokeGrenade smoke) smoke.OnExplosion(); else Destroyed = true;
            }
            finally { LegendaryGrenadeEffects.CurrentExplosion = old; }
        }
    }
    public partial class GameWorld
    {
        public readonly List<Player> Actors = []; public readonly List<Grenade> Grenades = [];
        public IEnumerable<Player> RegisteredPlayers => Actors.Concat(MainPlayer == null ? [] : new[] { MainPlayer }).Distinct();
        public void RegisterGrenade(Grenade grenade) => Grenades.Add(grenade);
        public void UnregisterGrenade(string id)=>Grenades.RemoveAll(g=>g.WeaponSource.Id==id);
        public IObserverToPlayerBridge? GetAlivePlayerBridgeByCollider(Component collider) => collider is BodyPartCollider b && RegisteredPlayers.FirstOrDefault(p => p.ProfileId == b.PlayerProfileID) is { } player ? new Bridge(player) : null;
    }
}
public class SmokeGrenade : Grenade
{
    public SmokeGrenadeSettings _smokeGrenadeSettings => (SmokeGrenadeSettings)GrenadeSettings;
    public float _emitTime; public float EmitTime=>_emitTime; public float LifeTime=>EmitTime; public bool Emitting;
    public int TimerStarts;
    public void StartEmissionBehaviour(){if(LegendaryGrenadeEffects.StartSmokeTimer(this))TimerStarts++;}
    public readonly List<float> AreaTimes=[]; public void SetEmission(float time){Emitting=true;}
    public void OnExplosion(){SetEmission(Time.time);LegendaryGrenadeEffects.Emitting(this,Time.time);}
    public Action<Grenade>? EmissionEnd;
    public void UpdateEmissionArea(float normalTime) { AreaTimes.Add(normalTime); }
    public float Radius => 10;
}
public class GrenadeFactory
{
    public static bool RunCreatedHook=true;
    public Grenade Create(GrenadeSettings settings, Vector3 position, Quaternion rotation, Vector3 force, ThrowWeap item, string profileId, float prewarm = 0, bool withBallisticCalculator = true, bool isBeingPlanted = false)
    {
        Grenade g = settings is SmokeGrenadeSettings ? new SmokeGrenade() : new Grenade(); g.gameObject.Components.Add(g);
        g.gameObject.Add(new Collider()); g.gameObject.AddChild(false).Add(new Collider());
        g.transform.SetPositionAndRotation(position, rotation); g.Original = item; g.ProfileId = profileId; g.GrenadeSettings = settings; g.Rigidbody.velocity = force; g.Prewarm = prewarm;
        if(g is SmokeGrenade s){s._emitTime=item.EmitTime-prewarm;s.StartEmissionBehaviour();}
        if(RunCreatedHook)LegendaryGrenadeEffects.Created(g, item, profileId); return g;
    }
}
namespace EFT.HealthSystem
{
    public class BodyHealth { public float Current = 100; }
    public class ActiveHealthController
    {
        public Player Player = null!; public float DamageCoeff = 1; public bool IsAlive => Player.HealthController.IsAlive;
        public readonly Dictionary<EBodyPart, BodyHealth> Parts = Enum.GetValues<EBodyPart>().ToDictionary(x => x, _ => new BodyHealth());
        public readonly HashSet<EBodyPart> Heavy = []; public float Stun, Flash, Contusion;
        public BodyHealth GetBodyPartHealth(EBodyPart part, bool rounded) => Parts[part];
        public void ApplyDamage() { }
        public void DoBleed(bool heavy, EBodyPart part) { if (heavy) Heavy.Add(part); }
        public void DoStun(float maxTime, float normalizedStrength) => Stun = maxTime * normalizedStrength;
        public void DoBurnEyes(Vector3 position, float distanceStrength, float normalTime, float maxTime)
        {
            Flash = maxTime * distanceStrength;
            if (LegendaryGrenadeEffects.CurrentExplosion is { } p) GrenadeBuffs.Flashed(p, Player);
        }
        public void DoContusion(float applyTime, float strength) { if (PatchHooks.Prefix("LegendaryGrenadeConcussionImmunityPatch", this)) Contusion = applyTime * strength; }
    }
}
public struct DamageInfo
{
    public EDamageType DamageType; public Item? Weapon; public IObserverToPlayerBridge? Player; public string SourceId; public Vector3 MasterOrigin;
    public bool Blunt; public float Damage;
}
namespace EFT.Interactive
{
    public enum EDoorState { Shut, Open, Locked }
    public class InteractionResult(EFT.EInteractionType type) { public EFT.EInteractionType InteractionType=type; }
    public class Door : Component
    {
        public EDoorState DoorState = EDoorState.Shut; public bool IsBroken, Operatable = true;
        public void KickOpen(Vector3 from) { IsBroken = true; DoorState = EDoorState.Open; }
        public void Interact(InteractionResult result) { if(result.InteractionType==EFT.EInteractionType.Open && DoorState!=EDoorState.Locked)DoorState=EDoorState.Open; }
    }
}
namespace EscapeFromBorderlands.Client
{
    internal static class AdvancedMeleeRuntime { internal static bool Crouched(Player p) => p.Crouched; }
    internal static class ChestRigInventory { internal static float LoadTime(InventoryController inventory,float seconds,bool unload)=>seconds; }
    internal static class BackpackNative
    {
        internal static Player? WeaponOwner(Weapon w) => Singleton<GameWorld>.Instance.RegisteredPlayers.FirstOrDefault(p => p.HandsController?.Item == w);
        internal static float MedicalFactor = 1f;
        internal static float UseFactor(Player p, Item item) => MedicalFactor;
    }
    internal static class AdvancedLegendaryActions
    {
        internal static readonly List<(Vector3 Position, float Seconds, bool Instant)> Smokes = [];
        internal static void DeploySmokeAt(Player p, Vector3 position, float duration, string statusName = "", string statusKey = "", bool instant = false) => Smokes.Add((position, duration, instant));
        internal static void DeploySmokeAt(string profile, Vector3 position, float duration, string statusName = "", string statusKey = "", bool instant = false) => Smokes.Add((position, duration, instant));
    }
    internal static partial class Native
    {
        internal static void Set(object obj, string name, object? value)
        { var p = obj.GetType().GetProperty(name); if (p?.SetMethod != null) p.SetValue(obj, value); else obj.GetType().GetField(name)?.SetValue(obj, value); }
        internal static float Number(object obj, string name) => Convert.ToSingle(Get(obj, name));
    }
}
