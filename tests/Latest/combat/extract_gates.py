from pathlib import Path
here=Path(__file__).resolve().parent
source=here.parents[2]/'src/EscapeFromBorderlands.Client'
def block(text,marker):
 start=text.index(marker);brace=text.index('{',start);depth=0
 for i in range(brace,len(text)):
  if text[i]=='{':depth+=1
  elif text[i]=='}':depth-=1
  if depth==0:return text[start:i+1]
 raise ValueError(marker)
advanced=block((source/'AdvancedMeleeRuntime.cs').read_text(),'public static bool Enabled(')
text=(source/'MeleeRuntime.cs').read_text();start=text.index('public static bool Enabled(');end=text.index(';',start)+1
melee=text[start:end]
(here/'ProductionMeleeGates.cs').write_text('using System;\nusing System.Linq;\nusing Comfort.Common;\nusing EFT;\nnamespace EscapeFromBorderlands.Client;\ninternal static partial class MeleeRuntime {\n'+melee+'\n}\ninternal static partial class AdvancedMeleeRuntime {\n'+advanced+'\n}\n')
print('Extracted unchanged melee eligibility gates; inventory/clock/health are controlled doubles.')
