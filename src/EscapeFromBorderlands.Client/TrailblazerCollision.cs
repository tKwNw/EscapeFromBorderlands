using System;
using HarmonyLib;
using EFT;

namespace EscapeFromBorderlands.Client;

internal static class TrailblazerCollision
{
    internal static void Ignore(Grenade parent, Grenade child)
    {
        var collider = AccessTools.TypeByName("UnityEngine.Collider");
        var ignore = AccessTools.Method(AccessTools.TypeByName("UnityEngine.Physics"), "IgnoreCollision", new[] { collider, collider, typeof(bool) })
            ?? throw new InvalidOperationException("Native grenade collision isolation is unavailable.");
        foreach (var a in parent.GetComponentsInChildren(collider, true))
            foreach (var b in child.GetComponentsInChildren(collider, true))
                ignore.Invoke(null, new object[] { a, b, true });
    }
}
