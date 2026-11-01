from pathlib import Path
here=Path(__file__).resolve().parent
src=here.parents[2]/'src/EscapeFromBorderlands.Client'
def block(text,marker):
 start=text.index(marker);brace=text.index('{',start);depth=0
 for i in range(brace,len(text)):
  if text[i]=='{':depth+=1
  elif text[i]=='}':depth-=1
  if depth==0:return text[start:i+1]
 raise ValueError(marker)
s=(src/'ChestRigBallistics.cs').read_text()
apply=block(s,'internal static class ChestRigBallistics')
# The tests execute these production members unchanged, supplying controlled
# projectile/actor data rather than pretending to execute Unity physics.
(here/'ProductionBallistics.cs').write_text('using System;using System.Linq;using Comfort.Common;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using UnityEngine;\nnamespace EscapeFromBorderlands.Client;\n'+apply+'\n')
(here/'ProductionRigNative.cs').write_text('using System;using System.Linq;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;\nnamespace EscapeFromBorderlands.Client;\n'+block((src/'ChestRigPatches.cs').read_text(),'internal static class ChestRigNative')+'\n')

s=(src.parent/'EscapeFromBorderlands.Server/RarityMod.cs').read_text()
expression=s[s.index('    private static string RigPanelId('):];expression=expression[:expression.index(';')+1]
body='\n'.join([expression,block(s,'private void ApplyRigSoftArmor('),block(s,'private void RigPanelModPool(')])
(here/'ProductionRigArmor.cs').write_text('using System.Text.Json;using SPTarkov.Server.Core.Models.Common;using SPTarkov.Server.Core.Models.Eft.Common.Tables;using SPTarkov.Server.Core.Utils.Json.Converters;\nnamespace EscapeFromBorderlands.Server;\ninternal sealed class RigArmorFixture {\ninternal RigDatabase databaseService=new();internal RigTemplateTable templateTable;internal RigLocales localeTable=new();internal RigCloner cloner=new();internal RigLocale localeService=new();\ninternal RigArmorFixture(){templateTable=new(databaseService);}\ninternal void Apply(TemplateItem rig,VariantDefinition variant)=>ApplyRigSoftArmor(rig,variant);\ninternal void Pools(Dictionary<MongoId,Dictionary<string,HashSet<MongoId>>> pools,VariantDefinition variant)=>RigPanelModPool(pools,variant);\n'+body+'\n}\n')

text=(src/"MeleeWeaponSwitch.cs").read_text()
(here/"ProductionMeleeSwitch.cs").write_text("using System;using System.Collections.Generic;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using UnityEngine;\nnamespace EscapeFromBorderlands.Client;\n"+block(text,"internal static class MeleeWeaponSwitch")+"\n"+block(text,"internal static class LegendaryMeleeFirearmDrawPatch")+"\n")

text=(src/"BackpackRuntime.cs").read_text()
methods="\n".join(block(text,marker) for marker in ["internal static float Movement(","internal static float MotionFactor(","internal static float Value("])
ergo=next(line for line in (src/"BackpackNative.cs").read_text().splitlines() if "internal static float ErgonomicsFactor(" in line)
(here/"ProductionBackpackBonuses.cs").write_text("using System;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using UnityEngine;\nnamespace EscapeFromBorderlands.Client;\ninternal static partial class BackpackRuntime {\n"+methods+"\n}\ninternal static partial class BackpackNative {\n"+ergo+"\n}\n")

(here/'ProductionRigCombat.cs').write_text('using System;using System.Linq;using System.Collections.Generic;using Comfort.Common;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;using EFT.Interactive;using UnityEngine;\nnamespace EscapeFromBorderlands.Client;\n'+block((src/'ChestRigCombat.cs').read_text(),'internal static class ChestRigCombat')+'\n')
text=(src/'LegendaryRuntime.cs').read_text()
(here/'ProductionApplication.cs').write_text('using System;using UnityEngine;\nnamespace EscapeFromBorderlands.Client;\n'+block(text,'internal sealed class EffectApplication')+'\n'+block(text,'internal readonly struct StatModifiers')+'\n')

text=(src/'LegendaryHitTrackingPatch.cs').read_text()
(here/'ProductionReloadCompletion.cs').write_text('using System;using System.Linq;using System.Reflection;using EFT;using EFT.Ballistics;using EFT.InventoryLogic;\nnamespace EscapeFromBorderlands.Client;\ninternal static class RigReloadHook {\n'+block(text,'private static void Prefix(Player.FirearmController __instance, MethodBase __originalMethod, ref Callback callback, object[] __args)')+'\n}\n')
