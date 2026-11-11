using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.AssetsManager;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Headwear
{
    private const string Rshg = "676bf44c5539167c3603e869", Rsh = "633ec7c2a6918cb895019c6c", Ps12b = "5cadf6eeae921500134b2799", M67 = "58d3db5386f77426186285a0";
    private sealed class Summon
    {
        internal State State = null!;
        internal Weapon Gun = null!;
        internal Item? Previous;
        internal int Capacity, Fired;
        internal float Deadline, Retry;
        internal bool Drawn, Restoring, Cooling;
    }
    private static Summon? Current;
    private static State? Pending;
    private static KeyCode ClaimedKey;
    private static bool ClaimedAbility;
    private static int ReleaseFrame = -1;
    private static Task? M67Load;
    private static GameWorld? M67World;
    private static readonly Dictionary<string, string> GrenadeSafety = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Radius = new(StringComparer.Ordinal);
    [ThreadStatic] private static string? SafeOwner;
    [ThreadStatic] private static bool FlashExplosion;

    private static void ResetAbilities()
    {
        Current = null; Pending = null; ClaimedAbility = false; ReleaseFrame = -1; M67Load = null; M67World = null;
        GrenadeSafety.Clear(); Radius.Clear(); SafeOwner = null; FlashExplosion = false;
    }
    private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 24);
    private static bool Hotkey(State? s) => s?.Key is "HW009-2" or "HW023-1" or "HW027-1" or "HW054-3";
    private static void MaintainAbilities()
    {
        if (Current is { } t)
        {
            var p = t.State.Player;
            if (!PlayerRefresh.CanObserve(p)) Current = null;
            else if (p.HandsController?.Item == t.Gun)
            {
                t.Drawn = true;
                if ((t.Fired >= t.Capacity || Get(p) != t.State) && !t.Restoring && Time.time >= t.Retry && HandsReady(p)) Restore(t);
            }
            else if (t.Drawn || Time.time >= t.Deadline)
            {
                if (t.Fired > 0) Cool(t);
                Current = null;
            }
        }
    }

    internal static void ProcessAbilityInput(List<ECommand> commands, InputCombination[]? bindings)
    {
        if (PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance) is not { MainPlayer: { } p } world) return;

        var frame = Time.frameCount;
        if (ClaimedAbility && !HeadwearSettings.IsHeld(ClaimedKey))
        {
            ClaimedAbility = false;
            ReleaseFrame = frame;
        }
        if (!ClaimedAbility && ReleaseFrame != frame && HeadwearSettings.Pressed && !Cursor.visible && !p.IsInventoryOpened &&
            Get(p) is { } s && Hotkey(s))
        {
            ClaimedAbility = true;
            ClaimedKey = HeadwearSettings.Shortcut.MainKey;
            Activate(world, s);
        }
        if ((!ClaimedAbility && ReleaseFrame != frame) || bindings is null) return;
        // The game has calculated its commands but has not dispatched them yet.
        // Only consume grenade bindings sharing the claimed ability key; keep other
        // grenade keys and unclaimed presses intact.
        for (var i = commands.Count - 1; i >= 0; i--)
        {
            var command = commands[i];
            if (command is not (ECommand.PressThrowGrenade or ECommand.ThrowGrenade or ECommand.NextGrenadeStage)) continue;
            foreach (var binding in bindings)
            {
                if (binding is not InputKeyCombination { InputKeys: { } keys } keyBinding || keyBinding.ecommand_0 != command) continue;
                if (!Array.Exists(keys, key => key is InputKey input && input.Key == ClaimedKey)) continue;
                commands.RemoveAt(i);
                break;
            }
        }
    }
    private static bool RequestValid(GameWorld world, State s) =>
        ReferenceEquals(PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance), world) && Get(s.Player) == s && s.Ready &&
        !Cursor.visible && !s.Player.IsInventoryOpened && s.Player.HandsController is not Player.BaseGrenadeHandsController &&
        (!s.Player.IsInBufferZone || s.Player.CanManipulateWithHandsInBufferZone);
    private static bool HandsReady(Player p) => !p.IsInventoryOpened && p.HandsController is { } hands &&
        !hands.IsInInteractionStrictCheck() && !hands.InCanNotBeInterruptedOperation() && !p.InventoryController.IsChangingWeapon;
    private static void Activate(GameWorld world, State s)
    {
        if (Current is not null || Pending is not null || !RequestValid(world, s)) return;
        if (s.Key is not ("HW009-2" or "HW027-1") && !HandsReady(s.Player)) return;
        try
        {
            switch (s.Key)
            {
                case "HW009-2": PrepareWeapon(world, s, true); break;
                case "HW027-1": PrepareWeapon(world, s, false); break;
                case "HW023-1": CastSmoke(s, 10); s.Buff(10, 300); break;
                case "HW054-3": PrepareGrenades(world, s); break;
            }
        }
        catch (Exception e) { if (Pending == s) Pending = null; Plugin.LogSource.LogWarning($"EFB {s.Roll.Definition.ShortName}: {e.Message}"); }
    }
    private static Task Load(Item item) => Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
        ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local,
        item.GetAllItems().Select(i => i.Prefab).Distinct().ToArray(), Diz.Jobs.JobYieldPriority.Low, ct: default);
    private static void PrepareWeapon(GameWorld world, State s, bool launcher)
    {
        var factory = Singleton<ItemFactory>.Instance;
        var weapon = factory.GetPresetItem(launcher ? Rshg : Rsh) as Weapon ?? throw new InvalidOperationException("Native firearm preset unavailable.");
        AttachPrivateOwner(weapon);
        int capacity;
        if (launcher)
        {
            if (weapon is not RocketLauncher || weapon.Chambers.FirstOrDefault() is not { } chamber)
                throw new InvalidOperationException("Native RShG-2 chamber unavailable.");
            LoadRocket(factory, chamber, weapon.Template.defAmmo);
            capacity = 1;
        }
        else
        {
            if (weapon is not Revolver || weapon.GetCurrentMagazine() is not CylinderMagazine cylinder || cylinder.Camoras.Length == 0)
                throw new InvalidOperationException("Native RSh-12 cylinder unavailable.");
            foreach (var slot in cylinder.Camoras)
            {
                if (factory.CreateItem(NewId(), Ps12b, null) is not Ammo round)
                    throw new InvalidOperationException("Native PS12B round unavailable.");
                ReplaceRound(slot, round);
            }
            cylinder.SetNotEmptyCamoraAsActive();
            capacity = cylinder.Camoras.Length;
        }
        var asset = Load(weapon);
        Pending = s;
        world.StartCoroutine(Draw(world, s, weapon, capacity, asset));
    }
    private static void AttachPrivateOwner(Weapon weapon)
    {
        // Unlike a bare fake stash, this root supplies a real native owner for
        // ammo and hands operations. It is never registered with the raid or
        // inserted into the player's equipment, inventory or profile.
        var owner = new ItemController(weapon, NewId(), "EFB temporary weapon", false);
        if (weapon.CurrentAddress?.Container != owner)
            throw new InvalidOperationException("Native temporary weapon ownership unavailable.");
    }
    private static void LoadRocket(ItemFactory factory, Slot chamber, string? defaultAmmo)
    {
        if (chamber.ContainedItem is Rocket { IsUsed: false } loaded && chamber.CheckCompatibility(loaded)) return;

        Rocket? CreateRocket(string? id) => !string.IsNullOrEmpty(id) &&
            factory.ItemTemplates.TryGetValue(id, out var template) && template is RocketTemplate &&
            factory.CreateItem(NewId(), id, null) is Rocket rocket && chamber.CheckCompatibility(rocket) ? rocket : null;
        var replacement = CreateRocket(defaultAmmo);
        if (replacement is null)
        {
            foreach (var filter in chamber.Filters ?? Array.Empty<ItemFilter>())
            {
                foreach (var templateId in filter.Filter ?? Array.Empty<MongoID>())
                    if ((replacement = CreateRocket(templateId)) is not null) break;
                if (replacement is not null) break;
            }
        }
        if (replacement is null) throw new InvalidOperationException("No compatible native rocket found for Prince Kassad.");
        ReplaceRound(chamber, replacement);
    }
    private static void ReplaceRound(Slot slot, Ammo round)
    {
        if ((slot.ContainedItem is not null && !slot.RemoveItem(simulate: false).Succeeded) || !slot.Add(round, false, false).Succeeded)
            throw new InvalidOperationException("Native chamber could not be loaded.");
    }
    private static IEnumerator Draw(GameWorld world, State s, Weapon gun, int capacity, Task load)
    {
        var end = Time.time + 10;
        while (Pending == s && RequestValid(world, s) && !load.IsCompleted && Time.time < end) yield return null;
        if (Pending != s) yield break;
        if (!RequestValid(world, s) || load.Status != TaskStatus.RanToCompletion)
        {
            Pending = null;
            if (load.IsFaulted) Plugin.LogSource.LogWarning($"EFB temporary weapon assets: {load.Exception?.GetBaseException().Message}");
            yield break;
        }
        // Keep one requested draw through a short native hands transition.
        // A different hat, inventory interaction or raid end cancels it.
        end = Time.time + 1.5f;
        while (Pending == s && RequestValid(world, s) && !HandsReady(s.Player) && Time.time < end) yield return null;
        if (Pending != s) yield break;
        Pending = null;
        if (!RequestValid(world, s) || !HandsReady(s.Player)) yield break;
        var t = new Summon { State = s, Gun = gun, Capacity = capacity, Previous = s.Player.HandsController?.Item, Deadline = Time.time + 15 };
        Current = t;
        try
        {
            s.Player.Proceed(gun, result =>
            {
                if (Current != t) return;
                if (result.Failed) { Current = null; Plugin.LogSource.LogWarning("EFB temporary weapon: " + result.Error); }
                else t.Drawn = true;
            });
        }
        catch (Exception e) { Current = null; Plugin.LogSource.LogWarning("EFB temporary weapon: " + e.Message); }
    }
    private static void Restore(Summon t)
    {
        var p = t.State.Player;
        t.Restoring = true;
        void Retry(bool failed) { if (failed) { t.Retry = Time.time + .5f; t.Restoring = false; } }
        try
        {
            if (t.Previous is { CurrentAddress: not null } prev) p.SetInHands(prev, result => Retry(result.Failed));
            else p.SetEmptyHands(result => Retry(result.Failed));
        }
        catch (Exception e) { Retry(true); Plugin.LogSource.LogWarning("EFB temporary weapon return: " + e.Message); }
    }
    private static void Cool(Summon t)
    {
        if (t.Cooling) return;
        t.Cooling = true; t.State.Cool(300);
    }
    internal static bool TemporaryHeld(Player p) => Current is { } t && t.State.Player == p && p.HandsController?.Item == t.Gun;
    internal static bool Temporary(Item? item) => Current is { } t && t.Gun == item;
    private static Player? TemporaryOwner(Weapon weapon) => Current is { } t && t.Gun == weapon ? t.State.Player : null;
    private static void FiredTemporary(Player p)
    {
        if (!TemporaryHeld(p) || Current is not { } t || t.Fired >= t.Capacity) return;
        if (++t.Fired == t.Capacity) Cool(t);
    }
    internal static void RocketFired(Player.FirearmController controller)
    {
        if (Current is { State.Key: "HW009-2" } t && t.Gun == controller.Item) FiredTemporary(t.State.Player);
    }
    internal static void CorpseHands(Player p)
    {
        if (TemporaryHeld(p) && Native.Get(p, "_itemInHands") is { } binding) Native.Set(binding, "Value", null);
    }
    private static void CastSmoke(State s, float duration) =>
        AdvancedLegendaryActions.DeploySmokeAt(s.Player, s.Player.Position + Vector3.up * .15f, duration, instant: true);
    private static void PrepareGrenades(GameWorld world, State s)
    {
        if (M67World != world || M67Load is null || M67Load.IsFaulted)
        {
            M67World = world;
            M67Load = Load(Singleton<ItemFactory>.Instance.CreateItem(NewId(), M67, null));
        }
        Pending = s;
        world.StartCoroutine(ThrowSix(world, s, M67Load));
    }
    private static IEnumerator ThrowSix(GameWorld world, State s, Task load)
    {
        var deadline = Time.time + 10;
        while (Pending == s && RequestValid(world, s) && !load.IsCompleted && Time.time < deadline) yield return null;
        if (Pending != s) yield break;
        Pending = null;
        if (!RequestValid(world, s) || load.Status != TaskStatus.RanToCompletion || world.GrenadeFactory is not { } factory) yield break;
        var owner = s.Player; var position = owner.Position; var spawned = 0;
        for (var i = 0; i < 6; i++)
        {
            try
            {
                var item = (ThrowWeap)Singleton<ItemFactory>.Instance.CreateItem(NewId(), M67, null);
                var pooled = Singleton<ObjectsFactory>.Instance.CreateItem(item, true);
                GrenadeSettings settings;
                try { settings = UnityEngine.Object.Instantiate(pooled.GetComponent<GrenadePrefab>().GrenadeItself); }
                finally { AssetPoolObject.ReturnToPool(pooled); }
                var (start, velocity) = CrazyIvanToss(position, i);
                // Create applies its force as an impulse on the next physics step.
                // Use no impulse when setting the actual launch velocity ourselves.
                var g = factory.Create(settings, start, Quaternion.identity, Vector3.zero, item, owner.ProfileId);
                Native.Set(Native.Get(g, "Rigidbody")!, "velocity", velocity);
                Native.Set(g, "Velocity", velocity);
                GrenadeSafety[item.Id] = owner.ProfileId;
                world.RegisterGrenade(g);
                if (Singleton<GlobalEventDispatcher>.Instantiated) Native.Call(Singleton<GlobalEventDispatcher>.Instance, "ThrowGrenade", g, start, velocity, .5f);
                spawned++;
            }
            catch (Exception e) { Plugin.LogSource.LogWarning("EFB Crazy Ivan: " + e.Message); }
        }
        if (spawned > 0) s.Cool(600);
    }
    private static (Vector3 Start, Vector3 Velocity) CrazyIvanToss(Vector3 origin, int index)
    {
        var theta = index * Mathf.PI / 3f;
        var direction = new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta));
        // Low underhand arc, about 3 m to first contact on flat ground.
        return (origin + Vector3.up + direction * .4f, direction * 4.4f + Vector3.up * 1.5f);
    }
    private static readonly MethodInfo Linecast = AccessTools.Method(AccessTools.TypeByName("UnityEngine.Physics"), "Linecast",
        new[] { typeof(Vector3), typeof(Vector3), typeof(int) });
    private static bool Flash(Player p)
    {
        var world = Singleton<GameWorld>.Instance;
        if (world is null || Linecast is null) return false;
        var origin = p.PlayerBones.Head.position;
        foreach (var candidate in world.RegisteredPlayers)
        {
            if (candidate is not Player target || target.HealthController?.IsAlive != true || !AdvancedMeleeRuntime.Enemy(p, target) || Is(target, "HW046-3")) continue;
            var delta = origin - target.PlayerBones.Head.position; var range = delta.magnitude;
            if (range > 15 || Linecast.Invoke(null, new object[] { origin, target.PlayerBones.Head.position, (int)LayersMaskController.GrenadeObstaclesColliderMask }) is true) continue;
            var strength = Mathf.Max(.15f, (1 - range / 15) * Mathf.Max(.2f, Vector3.Dot(delta.normalized, target.LookDirection)));
            target.ActiveHealthController.DoStun(5, strength);
            target.ActiveHealthController.DoBurnEyes(origin, strength, strength, 5);
            if (target.AIData?.BotOwner is { BotState: EBotState.Active } bot) bot.FlashGrenade.AddBlindEffect(5 * strength, origin);
        }
        return true;
    }
    private static void Enlarge(string id) => Radius.Add(id);
    internal static float BlastRadius(Item item) => Radius.Contains(item.Id) ? 2 : 1;
    internal static (string?, bool) BeginExplosion(Grenade? grenade, Item? item, ref IExplosiveItem explosive)
    {
        var old = (SafeOwner, FlashExplosion);
        SafeOwner = item is null ? null : GrenadeSafety.GetValueOrDefault(item.Id);
        FlashExplosion = grenade is StunGrenade || explosive.Blindness.sqrMagnitude > 0 && explosive.MaxExplosionDistance <= 0;
        if (item is not null && Radius.Contains(item.Id)) explosive = new ChestRigExplosive(explosive, 1, 2);
        return old;
    }
    internal static void EndExplosion((string?, bool) old) { SafeOwner = old.Item1; FlashExplosion = old.Item2; }
    internal static bool FlashImmune(Player p) => SafeOwner == p.ProfileId || FlashExplosion && Is(p, "HW046-3");
    internal static bool Immune(Player p, DamageInfo hit) =>
        hit.Weapon is ThrowWeap g && GrenadeSafety.GetValueOrDefault(g.Id) == p.ProfileId ||
        SafeOwner == p.ProfileId && hit.DamageType is EDamageType.Explosion or EDamageType.GrenadeFragment or EDamageType.ThermobaricExplosion;
    internal static bool ImmuneArmor(ArmorComponent armor, DamageInfo hit) =>
        (SafeOwner is not null || hit.Weapon is ThrowWeap g && GrenadeSafety.ContainsKey(g.Id)) &&
        BackpackNative.ItemOwner(armor.Item) is { } p && Immune(p, hit);
}

[HarmonyPatch(typeof(InputPreset), nameof(InputPreset.UpdateInput))]
internal static class HatAbilityInput
{
    private static void Postfix(List<ECommand> commands, InputCombination[]? ____workingKeyCombinations) => Headwear.ProcessAbilityInput(commands, ____workingKeyCombinations);
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.CreateRocketShot))]
internal static class HatRocketFired
{
    private static void Postfix(Player.FirearmController __instance) => Headwear.RocketFired(__instance);
}
[HarmonyPatch(typeof(FirearmHandsInputTranslator), nameof(FirearmHandsInputTranslator.TranslateCommand))]
internal static class HatTemporaryInput
{
    private static bool Prefix(FirearmHandsInputTranslator __instance, ECommand __0, ref InputNode.ETranslateResult __result)
    {
        if (!Headwear.TemporaryHeld(__instance._player)) return true;
        var name = __0.ToString();
        if (!name.Contains("Reload") && !name.Contains("Unload") && !name.Contains("Chamber") && !name.Contains("Throw")) return true;
        __result = InputNode.ETranslateResult.Block; return false;
    }
}
[HarmonyPatch]
internal static class HatTemporaryLoot
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(GameWorld).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(m => m.Name == nameof(GameWorld.ThrowItem) && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Item));
    private static bool Prefix(Item __0) => !Headwear.Temporary(__0);
}
[HarmonyPatch(typeof(Player), nameof(Player.CreateCorpse), new Type[0])]
internal static class HatCorpseHands
{
    private static void Prefix(Player __instance) => Headwear.CorpseHands(__instance);
}
[HarmonyPatch(typeof(Grenade), nameof(Grenade.Explosion), new[] { typeof(Grenade), typeof(IExplosiveItem), typeof(Vector3), typeof(string), typeof(IBallisticsCalculator), typeof(Item), typeof(Vector3), typeof(bool) })]
internal static class HatExplosion
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Grenade grenade, Item originalWeaponItem, ref IExplosiveItem grenadeItem, out (string?, bool) __state) =>
        __state = Headwear.BeginExplosion(grenade, originalWeaponItem, ref grenadeItem);
    private static Exception? Finalizer((string?, bool) __state, Exception? __exception)
    { Headwear.EndExplosion(__state); return __exception; }
}
