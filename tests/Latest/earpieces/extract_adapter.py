from pathlib import Path
root=Path(__file__).resolve().parents[3]
s=(root/'src/EscapeFromBorderlands.Client/BackpackRuntime.cs').read_text()
s=s[s.index('internal static class Native'):]
Path(__file__).with_name('ProductionNative.cs').write_text('using System;using System.Reflection;using System.Linq;using System.Collections;using System.Collections.Generic;using EFT.InventoryLogic;using HarmonyLib;\nnamespace EscapeFromBorderlands.Client;\n'+s)

s=(root/'src/EscapeFromBorderlands.Client/WeaponCombatStatePatch.cs').read_text()
s=s[:s.index('[HarmonyPatch')]
Path(__file__).with_name('ProductionCombatCache.cs').write_text(s)

s=(root/'src/EscapeFromBorderlands.Client/BackpackAudioPatches.cs').read_text()
s=s[s.index('internal static class BackpackAudio'):s.index('[HarmonyPatch')]
Path(__file__).with_name('ProductionWeather.cs').write_text('using System;using System.Collections.Generic;using System.Linq;using Comfort.Common;using EFT;\nnamespace EscapeFromBorderlands.Client;\n'+s)

# Preserve the source class annotation as well as its unchanged hook body.
s=(root/'src/EscapeFromBorderlands.Client/BackpackAudioPatches.cs').read_text()
blocks=[]
for name in ['BackpackInventorySoundScopePatch','BackpackInventoryVolumePatch']:
 start=s.rfind('[HarmonyPatch',0,s.index('internal static class '+name));brace=s.index('{',start);depth=0
 for end in range(brace,len(s)):
  depth+=(s[end]=='{')-(s[end]=='}')
  if depth==0:break
 blocks.append(s[start:end+1])
Path(__file__).with_name('ProductionInventoryAudio.cs').write_text('using System;using System.Linq;using System.Reflection;using System.Collections.Generic;using Comfort.Common;using EFT;using EFT.UI;using HarmonyLib;\nnamespace EscapeFromBorderlands.Client;\n'+'\n'.join(blocks))
