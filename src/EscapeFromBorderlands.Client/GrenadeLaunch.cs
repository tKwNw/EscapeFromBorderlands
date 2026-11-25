using EFT;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Only used when releasing a duplicate or handling a Semtax collision.
// Native grenade Awake binds the root Collider; no raid-wide polling is needed.
internal static class GrenadeLaunch
{
    internal static bool IsGrenade(Collider collider) => collider.GetComponentInParent<Grenade>() != null;

    internal static void IgnoreSibling(Grenade first, Grenade second)
    {
        var a = first.GetComponent<Collider>();
        var b = second.GetComponent<Collider>();
        if (a != null && b != null) Physics.IgnoreCollision(a, b, true);
    }
}
