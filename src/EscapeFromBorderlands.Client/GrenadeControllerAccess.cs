using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Keep the existing Native.Get property-before-field resolution, including
// hidden members on foreign controller types. Only proven native fields use
// compiled reads; failed/foreign access retains the original reflection path.
internal static class GrenadeControllerAccess
{
    private sealed class Readers
    {
        internal Func<Player.BaseGrenadeHandsController, Player?>? Player;
        internal Func<Player.BaseGrenadeHandsController, Transform[]?>? Parts;
        internal Func<Player.BaseGrenadeHandsController, GrenadeEmission?>? Emission;
    }
    private static readonly Dictionary<Type, Readers> Cache = new();
    private static Readers For(Player.BaseGrenadeHandsController controller)
    {
        var type = controller.GetType();
        if (Cache.TryGetValue(type, out var readers))
            return readers;
        Prepare(type);
        return Cache[type];
    }
    private static Func<Player.BaseGrenadeHandsController, T?>? Compile<T>(Type runtime, string name, Type declaring) where T : class
    {
        try
        {
            if (AccessTools.Property(runtime, name) is not null)
                return null;
            var field = AccessTools.Field(runtime, name);
            if (field is null || field.DeclaringType != declaring || field.FieldType != typeof(T) || field.IsStatic)
                return null;
            var method = new DynamicMethod("EfbGrenadeRead_" + name, typeof(T), new[] { typeof(Player.BaseGrenadeHandsController) }, typeof(GrenadeControllerAccess), true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<Player.BaseGrenadeHandsController, T?>)method.CreateDelegate(typeof(Func<Player.BaseGrenadeHandsController, T?>));
        }
        catch
        {
            return null;
        }
    }
    internal static Player? Owner(Player.BaseGrenadeHandsController controller)
    {
        var read = For(controller).Player;
        // A missing native owner still uses Actor's profile fallback.
        return read?.Invoke(controller) ?? BackpackRuntime.Actor(controller);
    }
    internal static Transform[]? Parts(Player.BaseGrenadeHandsController controller)
    {
        var read = For(controller).Parts;
        return read is null ? Native.Get(controller, "ThrowingParts") as Transform[] : read(controller);
    }
    internal static GrenadeEmission? Emission(Player.BaseGrenadeHandsController controller)
    {
        var read = For(controller).Emission;
        return read is null ? Native.Get(controller, "GrenadeEmission") as GrenadeEmission : read(controller);
    }
    internal static void Prepare()
    {
        // Resolve/compile known native controllers during menu warmup, not the
        // first throw. Custom types lazily take the same guarded path.
        Prepare(typeof(Player.GrenadeHandsController));
        Prepare(typeof(Player.QuickGrenadeThrowHandsController));
    }
    private static void Prepare(Type type)
    {
        if (Cache.ContainsKey(type))
            return;
        var readers = new Readers();
        try
        {
            // Actor() checks Player before _player; keep that property path intact.
            if (AccessTools.Property(type, "Player") is null && AccessTools.Field(type, "Player") is null)
                readers.Player = Compile<Player>(type, "_player", typeof(Player.ItemHandsController));
            readers.Parts = Compile<Transform[]>(type, "ThrowingParts", typeof(Player.BaseGrenadeHandsController));
            readers.Emission = Compile<GrenadeEmission>(type, "GrenadeEmission", typeof(Player.BaseGrenadeHandsController));
        }
        catch { /* Optional preparation; normal reflection stays available. */ }
        Cache[type] = readers;
    }
}
