from pathlib import Path
import argparse
here = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, default=here.parents[1] / 'src/EscapeFromBorderlands.Client/BackpackPresentation.cs')
args = parser.parse_args()
text = args.source.read_text()
def block(marker):
    start = text.index(marker); brace = text.index('{', start); depth = 0
    for index in range(brace, len(text)):
        if text[index] == '{': depth += 1
        elif text[index] == '}': depth -= 1
        if depth == 0: return text[start:index + 1]
    raise ValueError(marker)
header = '''using System; using System.Linq; using System.Reflection;
using System.Collections.Generic; using Comfort.Common; using EFT;
using EFT.Animations; using EFT.InventoryLogic; using HarmonyLib; using UnityEngine;
namespace EscapeFromBorderlands.Client;
'''
helper = 'internal static class BackpackPresentation {\n' + block('internal static bool FullCompartments(')
helper += '\n' + next((line.strip() for line in text.splitlines() if 'internal static bool NightVisionOn(' in line), '') + '\n}'
(here / 'ProductionPresentation.cs').write_text(header + helper + '\n' + block('internal static class BackpackRecoilSettlingPatch') + '\n' + block('internal static class BackpackBloomPatch') + '\n')
print('Extracted production recoil/compartment/bloom bodies from ' + str(args.source))
