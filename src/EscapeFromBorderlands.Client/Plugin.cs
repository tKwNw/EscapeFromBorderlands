using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.janky.hollywoodfx", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.tKwNw.escapefromborderlands";
    public const string PluginName = "tKwNw-EscapeFromBorderlands";
    public const string PluginVersion = "0.31.7";

    internal static ManualLogSource LogSource { get; private set; } = null!;
    internal static Plugin Instance { get; private set; } = null!;

    private Harmony? _harmony;
    private GameWorld? _world;

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
            Logger.LogInfo($"Migrated {imported} previous Escape From Borderlands client config entries; old settings remain backed up.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not migrate previous Escape From Borderlands client settings: {ex.Message}");
        }
    }

    private void Awake()
    {
        Instance = this;
        LogSource = Logger;
        MigrateLegacyClientConfig();
        StatusHudSettings.Bind(Config);
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
            catch (Exception e) { failures++; Logger.LogError($"Escape From Borderlands could not install {type.Name}: {e}"); }
        }
        if (failures > 0) Logger.LogError($"Escape From Borderlands: {failures} hook group(s) failed. The error above identifies each unavailable feature.");
        Logger.LogInfo($"Escape From Borderlands {PluginVersion} loaded with {RarityResolver.DefinitionCount} Legendary gun, melee, backpack, chest-rig, grenade and earpiece definitions from catalog revision {RarityResolver.CatalogRevision}. Effects remain restricted to Escape From Borderlands template IDs.");
    }

    private void OnDestroy()
    {
        ResetSession();
        ExpandedStorageNavigation.Reset();
        LegendaryGrenadeDrawAudio.Dispose();
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        var world = PlayerRefresh.ActiveWorld(Singleton<GameWorld>.Instance);
        if (_world != world) { ResetSession(); _world = world; }
        AdvancedLegendaryActions.Maintain(world);
        LegendaryExplosionFx.Maintain(world);
        ReusableGrenadeRuntime.Maintain();
        LegendaryGrenadeDrawAudio.Maintain();
        LegendaryRuntime.MaintainVolleys();
        LegendaryRuntime.ObserveHeldWeapon(world?.MainPlayer);
        EarpieceRuntime.Maintain(world);
        ServiceWindowPlayback.Maintain();
        ChestRigRuntime.Maintain(world);
        MeleeRuntime.Maintain(world);
        AdvancedMeleeRuntime.Maintain(world);
        BackpackRuntime.Maintain(world);
        BackpackVisitorExtraction.Maintain(world);
        EffectStatusHud.Maintain(world);
        PlayerRefresh.Maintain(world);
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
