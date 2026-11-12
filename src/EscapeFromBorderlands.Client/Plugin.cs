using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace EscapeFromBorderlands.Client;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", "4.1.6")]
[BepInDependency("com.janky.hollywoodfx", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.cj.useFromAnywhere", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.tKwNw.escapefromborderlands";
    public const string PluginName = "tKwNw-EscapeFromBorderlands";
    public const string PluginVersion = "0.40.5";

    internal static ReleaseLog LogSource { get; private set; } = new ReleaseLog();
    internal static Plugin Instance { get; private set; } = null!;

    private Harmony? _harmony;
    private GameWorld? _world;
    private float _nextHeadwearError;

    private void MigrateLegacyClientConfig()
    {
        try
        {
            // BepInEx may create a blank config file before Awake. Merge missing
            // legacy entries, respecting any settings already present in the new file.
            // ConfigFilePath is available across the BepInEx versions used by both
            // supported SPT branches; Paths.ConfigPath is not exposed by every build.
            var destination = System.IO.Path.GetFullPath(Config.ConfigFilePath);
            var configDirectory = System.IO.Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(configDirectory))
                throw new InvalidOperationException("Cannot locate the BepInEx config directory.");
            var imported = LegacyClientConfigMigration.Migrate(configDirectory, destination);
            if (imported <= 0) return;
            Config.Reload();
            LogSource.LogInfo($"Migrated {imported} previous Escape From Borderlands client config entries; old settings remain backed up.");
        }
        catch (Exception ex)
        {
            LogSource.LogWarning($"Could not migrate previous Escape From Borderlands client settings: {ex.Message}");
        }
    }

    private void Awake()
    {
        Instance = this;
        LogSource = new ReleaseLog(Logger);
        MigrateLegacyClientConfig();
        StatusHudSettings.Bind(Config);
        HeadwearSettings.Bind(Config);
        LegendaryGrenadeSettings.Bind(Config);
        _harmony = new Harmony(PluginGuid);
        // Install presentation first and name any failed gameplay hook, so one
        // rejected patch cannot prevent unrelated descriptions and UI loading.
        var failures = 0;
        var patchTypes = typeof(Plugin).Assembly.GetTypes()
            .Where(t => t.IsDefined(typeof(HarmonyPatch), false))
            .OrderBy(t => t == typeof(ItemDescriptionPatch) || t == typeof(ItemDescriptionLabelPatch) || t == typeof(InspectItemNameColorPatch) || t == typeof(GridItemNameColorPatch) ? 0 : 1)
            .ThenBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in patchTypes)
        {
            try { _harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { failures++; LogSource.LogError($"Escape From Borderlands could not install {type.Name}: {e}"); }
        }
        if (failures > 0) LogSource.LogError($"Escape From Borderlands: {failures} hook group(s) failed. The error above identifies each unavailable feature.");
        SceneManager.sceneUnloaded += GrenadeBreachDoors.SceneUnloaded;
        LogSource.LogInfo($"Escape From Borderlands {PluginVersion} loaded for SPT {typeof(SPT.Core.SPTCorePlugin).Assembly.GetName().Version} with {RarityResolver.DefinitionCount} Legendary gun, melee, backpack, chest-rig, grenade, earpiece and headwear definitions from catalog revision {RarityResolver.CatalogRevision}. Early access testing build. Effects remain restricted to Escape From Borderlands template IDs.");
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= GrenadeBreachDoors.SceneUnloaded;
        GrenadeBreachDoors.Dispose();
        ResetSession();
        EffectStatusHud.ReleasePanel();
        ExpandedStorageNavigation.Reset();
        LegendaryGrenadeDrawAudio.Dispose();
        _harmony?.UnpatchSelf();
    }

    private readonly RaidPlayerSnapshot _raidPlayers = new();

    private void Update()
    {
        var world = PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance);
        if (!ReferenceEquals(_world, world)) { ResetSession(); _world = world; }
        if (world is null) return; // Menu storage still updates in LateUpdate.
        EffectStatusHud.PrepareDuringSetup(world);
        var trace = RaidUpdateTrace.Begin();
        var completed = false;
        try
        {
            try { Headwear.Maintain(world); }
            catch (Exception e)
            {
                if (UnityEngine.Time.unscaledTime >= _nextHeadwearError)
                { _nextHeadwearError = UnityEngine.Time.unscaledTime + 30; LogSource.LogWarning($"EFB headwear update will retry: {e.Message}"); }
            }
            trace.Phase("headwear");
            AdvancedLegendaryActions.Maintain(world);
            trace.Phase("short smoke");
            LegendaryExplosionFx.Maintain(world);
            trace.Phase("explosion visuals");
            ReusableGrenadeRuntime.Maintain(world, _raidPlayers);
            trace.Phase("grenade payloads and buffs");
            LegendaryGrenadeDrawAudio.Maintain();
            trace.Phase("grenade draw sound");
            LegendaryRuntime.MaintainVolleys();
            LegendaryRuntime.ObserveHeldWeapon(world.MainPlayer);
            trace.Phase("firearm outcomes");
            EarpieceRuntime.Maintain(world, _raidPlayers);
            trace.Phase("headsets and audio");
            ServiceWindowPlayback.Maintain();
            trace.Phase("medical playback");
            ChestRigRuntime.Maintain(world, _raidPlayers);
            trace.Phase("chest rigs");
            MeleeRuntime.Maintain(world);
            trace.Phase("melee");
            BackpackRuntime.Maintain(world, _raidPlayers);
            BackpackVisitorExtraction.Maintain(world);
            trace.Phase("backpacks");
            EffectStatusHud.Maintain(world);
            trace.Phase("effect HUD and grenade charges");
            PlayerRefresh.Maintain(world);
            trace.Phase("deferred player refresh");
            completed = true;
        }
        finally
        {
            _raidPlayers.Clear();
            trace.Phase(completed ? "roster cleanup" : "interrupted update");
            trace.Finish();
        }
    }

    private void LateUpdate() => ExpandedStorageNavigation.Maintain();

    internal static void ResetSession()
    {
        if (PlayerRefresh.Resetting) return;
        PlayerRefresh.Resetting = true;
        try
        {
            SessionCleanup.Begin();
            PlayerRefresh.Reset();
            SessionCleanup.Run(nameof(Headwear), Headwear.Reset);
            SessionCleanup.Run(nameof(ServiceWindowPlayback), ServiceWindowPlayback.Reset);
            SessionCleanup.Run("earpiece suspend", EarpieceRuntime.Suspend);
            SessionCleanup.Run(nameof(EffectStatusHud), EffectStatusHud.Reset);
            SessionCleanup.Run(nameof(ChestRigRuntime), ChestRigRuntime.Reset);
            SessionCleanup.Run(nameof(BackpackRuntime), BackpackRuntime.Reset);
            SessionCleanup.Run(nameof(BackpackVisitorExtraction), BackpackVisitorExtraction.Reset);
            SessionCleanup.Run(nameof(MasterKeyRuntime), MasterKeyRuntime.Reset);
            SessionCleanup.Run(nameof(MeleeRuntime), MeleeRuntime.Reset);
            SessionCleanup.Run(nameof(MeleeAnimation), MeleeAnimation.Reset);
            SessionCleanup.Run(nameof(ProjectileEffectSpawner), ProjectileEffectSpawner.ResetSession);
            SessionCleanup.Run(nameof(LegendaryExplosionFx), LegendaryExplosionFx.Reset);
            SessionCleanup.Run(nameof(AdvancedLegendaryActions), AdvancedLegendaryActions.ResetSession);
            SessionCleanup.Run(nameof(LegendaryFireModePatch), LegendaryFireModePatch.ResetSession);
            SessionCleanup.Run(nameof(WeaponCombatState), WeaponCombatState.ResetSession);
            SessionCleanup.Run(nameof(LegendaryRuntime), LegendaryRuntime.ResetSession);
            SessionCleanup.Run(nameof(RarityResolver), RarityResolver.ResetSession);
            SessionCleanup.Run(nameof(ReusableGrenadeRuntime), ReusableGrenadeRuntime.Reset);
            SessionCleanup.Run(nameof(GrenadeSpikeTrace), GrenadeSpikeTrace.Reset);
            SessionCleanup.Run(nameof(RaidUpdateTrace), RaidUpdateTrace.Reset);
            SessionCleanup.Run(nameof(WeaponPerformanceTrace), WeaponPerformanceTrace.Reset);
            SessionCleanup.Run(nameof(LegendaryGrenadeDrawAudio), LegendaryGrenadeDrawAudio.ResetSession);
            SessionCleanup.Run(nameof(LegendaryGrenadeSettings), LegendaryGrenadeSettings.Reset);
            SessionCleanup.Run(nameof(EarpieceRuntime), EarpieceRuntime.Reset);
        }
        finally { PlayerRefresh.Resetting = false; }
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnDestroy))]
internal static class LegendaryWorldCleanupPatch
{
    private static void Prefix(GameWorld __instance)
    {
        if ((Singleton<GameWorld>.Instance == __instance || !Singleton<GameWorld>.Instantiated) && PlayerRefresh.CloseWorld(__instance)) Plugin.ResetSession();
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Dispose))]
internal static class LegendaryWorldDisposalPatch
{
    private static void Prefix(GameWorld __instance)
    {
        if ((Singleton<GameWorld>.Instance == __instance || !Singleton<GameWorld>.Instantiated) && PlayerRefresh.CloseWorld(__instance)) Plugin.ResetSession();
    }
}
