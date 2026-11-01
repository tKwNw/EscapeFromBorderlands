using System;
using System.Reflection;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class BackpackMedicalRestoration
{
    internal sealed class Scope
    {
        internal BackpackNative.Use? Use;
        internal bool Rollback;
        internal Scope? Previous;
    }
    [ThreadStatic] private static Scope? Current;
    internal static bool InScope => Current is not null;
    internal static void Reset() => Current = null;
    internal static Scope? Enter(object effect, bool rollback)
    {
        var previous = Current;
        BackpackNative.Uses.TryGetValue(effect, out var use);
        Current = new Scope { Use = use, Rollback = rollback, Previous = previous };
        return previous;
    }
    internal static void Exit(Scope? previous) => Current = previous;
    internal static float Adjust(ActiveHealthController health, float value, bool hydration)
    {
        var scope = Current; var use = scope?.Use;
        if (use is null || use.Player != health.Player) return value;
        if (scope!.Rollback)
        {
            if (value >= 0f) return value;
            // Native early cancellation reverses its unmodified accumulator.
            // Reverse only the extra amount actually granted to this use,
            // including after removing or changing the backpack.
            var extra = hydration ? use.HydrationBonus : use.EnergyBonus;
            if (hydration) use.HydrationBonus = 0f; else use.EnergyBonus = 0f;
            return value - extra;
        }
        var state = BackpackRuntime.Get(health.Player);
        if (value <= 0f || state?.Key != "BP06-1" || !use.From(state) || Native.Component<FoodDrinkComponent>(use.Item) is null) return value;
        var resource = hydration ? health.Hydration : health.Energy;
        var adjusted = Math.Max(value, Math.Min(value * 1.5f, Math.Max(0f, resource.Maximum - resource.Current)));
        var bonus = adjusted - value;
        if (hydration) use.HydrationBonus += bonus; else use.EnergyBonus += bonus;
        return adjusted;
    }
}

[HarmonyPatch]
internal static class BackpackMedRestorationScopePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController).GetNestedType("MedEffect", BindingFlags.NonPublic), "RegularUpdate");
    private static void Prefix(object __instance, out BackpackMedicalRestoration.Scope? __state) => __state = BackpackMedicalRestoration.Enter(__instance, false);
    private static Exception? Finalizer(BackpackMedicalRestoration.Scope? __state, Exception? __exception)
    { BackpackMedicalRestoration.Exit(__state); return __exception; }
}
