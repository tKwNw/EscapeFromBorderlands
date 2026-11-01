from pathlib import Path
import argparse

here = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, default=here.parents[2] / 'src/EscapeFromBorderlands.Client')
args = parser.parse_args()
source = args.source.resolve()

def block(text, marker):
    start = text.index(marker)
    brace = text.index('{', start)
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == '{': depth += 1
        elif text[i] == '}': depth -= 1
        if depth == 0: return text[start:i+1]
    raise ValueError(marker)

def cls(filename, name, patch):
    text = (source / filename).read_text()
    return patch + '\n' + block(text, 'internal static class ' + name)

headers = '''using System;
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
'''
stamina_attr = '[HarmonyPatch(typeof(Stamina), nameof(Stamina.Consume))]'
if (source / 'LegendaryStaminaCostPatch.cs').exists():
    stamina = cls('LegendaryStaminaCostPatch.cs', 'LegendaryStaminaCostPatch', stamina_attr)
else:
    stamina = cls('BackpackPatches.cs', 'BackpackTraversalCostPatch', stamina_attr) + '\n' + cls('MeleePatches.cs', 'LegendaryMeleeJumpCostPatch', stamina_attr)
parts = [stamina, cls('BackpackAudioPatches.cs', 'BackpackFoliageSoundPatch', '[HarmonyPatch(typeof(TreeInteractive), "method_0")]')]
for name in ['BackpackSurgeryPatch', 'BackpackEnergyPatch', 'BackpackHydrationPatch']:
    parts.append(cls('BackpackPatches.cs', name, ''))
(here / 'ProductionHooks.cs').write_text(headers + '\n\n'.join(parts) + '\n')

text = (source / 'BackpackNative.cs').read_text()
members = [block(text, 'internal sealed class Use'), block(text, 'internal static IEnumerable<EffectStatusModel.Status> UseStatuses(')]
for line in text.splitlines():
    if 'Dictionary<object, Use> Uses ' in line or 'Dictionary<string, Use> ItemUses ' in line or 'internal static bool InBag(' in line:
        members.append(line.strip())
for marker in ['internal static Use? Begin(', 'internal static float Remaining(', 'internal static float UseFactor(', 'internal static void End(', 'private static void HealOther(']:
    members.append(block(text, marker))
(here / 'ProductionMedical.cs').write_text(headers + 'internal static class BackpackNative\n{\n' + '\n'.join(members) + '\n}\n')
print('Extracted unchanged production class/member bodies from ' + str(source))

text=(source / "BackpackRuntime.cs").read_text()
(here / "ProductionTraversal.cs").write_text(headers+"internal static partial class BackpackRuntime\n{\n"+block(text,"internal static void Traversal(")+"\n"+block(text,"internal static float TraversalFactor(")+"\n}\n")

parts=[cls('BackpackPatches.cs','BackpackWeightScope',''),cls('BackpackPatches.cs','BackpackPhysicalWeightPatch',''),cls('BackpackProgressionPatches.cs','BackpackBreachRewardPatch','')]
(here/'ProductionReleaseHooks.cs').write_text(headers+'\n\n'.join(parts)+'\n')
