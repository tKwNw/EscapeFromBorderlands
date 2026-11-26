using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EscapeFromBorderlands.Client;

// Prepare EFB code during menus/loading, without firing synthetic shots,
// consuming RNG, creating game items or invoking effect callbacks. Hosts that
// cannot prepare a method retain normal first-use compilation. One per frame.
internal static class CombatCodeWarmup
{
    private static int _next;
    private static readonly (Type type, string method)[] Methods =
    {
        (typeof(LegendaryQuestItems), nameof(LegendaryQuestItems.Hit)),
        (typeof(QuestItemEquivalence), nameof(QuestItemEquivalence.ExpandedSources)),
        (typeof(QuestItemEquivalence), nameof(QuestItemEquivalence.WithSources)),
        (typeof(BallisticEffectPatch), "Postfix"),
        (typeof(LegendaryRuntime), "GetLiveStats"),
        (typeof(ProjectileEffectSpawner), nameof(ProjectileEffectSpawner.Spawn)),
        (typeof(ProjectileEffectSpawner), "SpawnOne"),
        (typeof(NativeShallowClone), nameof(NativeShallowClone.Copy)),
        (typeof(ChestRigCartridgePatch), "Prefix"),
        (typeof(WeaponCartridgePreparationPatch), "Prefix"),
        (typeof(ChestRigRuntime), nameof(ChestRigRuntime.Begin)),
        (typeof(ChestRigRuntime), nameof(ChestRigRuntime.Preview)),
        (typeof(ChestRigRuntime), "TrackShot"),
        (typeof(ChestRigRuntime), "FinishVolley"),
        (typeof(ChestRigRuntime), "FlushOutcomes"),
        (typeof(ChestRigBallistics), nameof(ChestRigBallistics.Apply)),
        (typeof(ChestRigBallistics), nameof(ChestRigBallistics.Impact)),
        (typeof(LegendaryHitTrackingPatch), "Postfix"),
        (typeof(ProjectileImpactExplosion), nameof(ProjectileImpactExplosion.Trigger)),
        (typeof(LegendaryRuntime), nameof(LegendaryRuntime.RecordImpact)),
        (typeof(ChestRigFleshHitPatch), "Prefix"),
        (typeof(ChestRigFleshHitPatch), "Postfix"),
        (typeof(LegendaryRuntime), nameof(LegendaryRuntime.BeginShot)),
        (typeof(LegendaryRuntime), nameof(LegendaryRuntime.RegisterShot)),
        (typeof(LegendaryRuntime), nameof(LegendaryRuntime.Associate)),
        (typeof(HatDeath), "Prefix"),
        (typeof(Headwear), nameof(Headwear.Dead)),
        (typeof(Headwear), "Killed"),
        (typeof(Headwear), "KillEffect"),
        (typeof(BackpackFreeGrenadeReleasePatch), "Prefix"),
        (typeof(ReusableGrenadeRuntime), nameof(ReusableGrenadeRuntime.Release)),
        (typeof(ReusableGrenadeRuntime), nameof(ReusableGrenadeRuntime.ProjectileItem)),
        (typeof(GrenadeInventoryPath), nameof(GrenadeInventoryPath.Contains)),
        (typeof(BackpackGrenadeAction), nameof(BackpackGrenadeAction.Release)),
        (typeof(LegendaryGrenadeEffects), nameof(LegendaryGrenadeEffects.Projectile)),
        (typeof(LegendaryGrenadeEffects), nameof(LegendaryGrenadeEffects.Created)),
        (typeof(LegendaryGrenadeEffects), nameof(LegendaryGrenadeEffects.BindSmoke)),
        (typeof(GrenadeBuffs), nameof(GrenadeBuffs.Thrown)),
        (typeof(GrenadeChargeLedger), nameof(GrenadeChargeLedger.Reserve)),
        (typeof(GrenadeChargeLedger), nameof(GrenadeChargeLedger.Finish)),
        (typeof(GrenadeChargeLedger), nameof(GrenadeChargeLedger.ReserveToken)),
        (typeof(GrenadeChargeLedger), nameof(GrenadeChargeLedger.FinishToken)),
        (typeof(GrenadeThrowSkill), nameof(GrenadeThrowSkill.For)),
        // Prepare native code only: never invoke factories/AI or manufacture a
        // projectile during loading. Mono hosts may decline this optional hint.
        (typeof(EFT.Player.BaseGrenadeHandsController), "method_10"),
        (typeof(ItemFactoryClass), "CreateItem"),
        (typeof(GrenadeFactoryClass), "Create"),
        (typeof(BotEventHandler), "ThrowGrenade"),
    };
    internal static void Maintain()
    {
        if (_next >= Methods.Length)
            return;
        if (_next == 0)
            GrenadeControllerAccess.Prepare();
        var entry = Methods[_next++];
        try
        {
            foreach (var method in entry.type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name == entry.method && !method.ContainsGenericParameters && method.GetMethodBody() is not null)
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }
        catch (Exception) { /* Optional runtime facility; normal invocation remains available. */ }
    }
}
