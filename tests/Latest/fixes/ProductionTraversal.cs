using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
namespace EscapeFromBorderlands.Client;
internal static partial class BackpackRuntime
{
internal static void Traversal(Player p, string action)
    {
        var s = Get(p); if (s?.Key != "BP40-2") return;
        if (s.Record.Traversal != action && Time.time - s.Record.LastTraversal <= 5f) s.Record.Arm("traversal:" + action, action == "sprint" ? 5f : .5f);
        s.Record.Traversal = action; s.Record.LastTraversal = Time.time;
    }
internal static float TraversalFactor(Player p, string action)
    {
        var s = Get(p);
        return s?.Key == "BP40-2" && s.Record.Traversal.Length > 0 && s.Record.Traversal != action &&
            Time.time - s.Record.LastTraversal <= 5f ? .8f : 1f;
    }
}
