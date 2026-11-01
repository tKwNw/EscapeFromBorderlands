using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;
internal static class BackpackLegDamageScope { [ThreadStatic] internal static Player? Player; }
[HarmonyPatch]
internal static class BackpackLegMovementDamagePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Player), "MovementContextOnStateChanged");
        yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Player), "InflictSelfDamage"));
    }
    private static void Prefix(object __instance, out Player? __state)
    {
        __state = BackpackLegDamageScope.Player;
        BackpackLegDamageScope.Player = __instance as Player ?? __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => typeof(Player).IsAssignableFrom(f.FieldType)).Select(f => f.GetValue(__instance) as Player).FirstOrDefault();
    }
    private static Exception? Finalizer(Player? __state, Exception? __exception) { BackpackLegDamageScope.Player = __state; return __exception; }
}
