from pathlib import Path

folder = Path(__file__).resolve().parent
source = folder.parents[2] / 'src/EscapeFromBorderlands.Client/BackpackProgressionPatches.cs'
text = source.read_text()
start = text.index('[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "ThrowGrenadeInner")]')
brace = text.index('{', start)
depth = 0
for end in range(brace, len(text)):
    depth += (text[end] == '{') - (text[end] == '}')
    if depth == 0:
        break
else:
    raise RuntimeError('Unbalanced production throw-distance adapter')
(folder / 'ProductionThrowDistance.cs').write_text(
    'using System;\nusing EFT;\nusing HarmonyLib;\nnamespace EscapeFromBorderlands.Client;\n'
    + text[start:end + 1] + '\n')

# Compile the unchanged smoke lifecycle methods with controlled engine doubles.
def member(text, marker):
    start = text.index(marker); brace = text.index('{', start); depth = 0
    for end in range(brace, len(text)):
        depth += (text[end] == '{') - (text[end] == '}')
        if depth == 0: return text[start:end + 1]
    raise RuntimeError(marker)
smoke = (folder.parents[2] / 'src/EscapeFromBorderlands.Client/AdvancedLegendaryActions.cs').read_text()
markers = ['private sealed class ShortSmoke', 'public static void Maintain(GameWorld?', 'private static void PreloadSmoke(',
           'public static void DeploySmokeAt(Player ', 'private static IEnumerator DeploySmokeWhenReady(',
           'private static void CreateSmoke(', 'private static float SmokeRadius(', 'private static void RemoveSmoke(']
# A profile-ID overload can be included once present in production.
if 'public static void DeploySmokeAt(string ' in smoke: markers.insert(4, 'public static void DeploySmokeAt(string ')
headers = 'using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using Comfort.Common;using EFT;using EFT.AssetsManager;using EFT.InventoryLogic;using HarmonyLib;using Systems.Effects;using UnityEngine;\nnamespace EscapeFromBorderlands.Client;\n'
fields = 'private const string SmokeGrenadeTemplateId="5a2a57cfc4a2826c6e06d44a"; private static readonly List<ShortSmoke> Smokes=new(); private static GameWorld? AssetWorld; private static Task? AssetLoad; private static CancellationTokenSource? AssetCancellation; internal static SmokeGrenade Last=>Smokes.Last().Grenade; internal static int Count=>Smokes.Count; internal static void Reset(){foreach(var s in Smokes.ToArray())RemoveSmoke(s);AssetWorld=null;AssetLoad=null;AssetCancellation?.Dispose();AssetCancellation=null;}\n'
(folder / 'ProductionSmokeLifecycle.cs').write_text(headers+'internal static class SmokeLifecycleFixture {\n'+fields+'\n'.join(member(smoke, m) for m in markers)+'\n}\n')
