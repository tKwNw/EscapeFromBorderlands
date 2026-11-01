from pathlib import Path
here=Path(__file__).resolve().parent
project=here.parent.parent/'EscapeFromBorderlands/src'
old=here.parents[2]/'backpacks_0187/EscapeFromBorderlands/src/EscapeFromBorderlands.Client/RarityResolver.cs'
s=old.read_text().replace('namespace EscapeFromBorderlands.Client;', 'using EscapeFromBorderlands.Client;\nnamespace EscapeFromBorderlands.Legacy;').replace('internal static class RarityResolver', 'internal static class LegacyRarityResolver')
(here/'LegacyRarityResolver.cs').write_text(s)
def block(s,marker):
 start=s.index(marker);brace=s.index('{',start);depth=0
 for i in range(brace,len(s)):
  if s[i]=='{':depth+=1
  if s[i]=='}':depth-=1
  if depth==0:return s[start:i+1]
 raise ValueError(marker)
s=(project/'EscapeFromBorderlands.Server/ApbsCompatibility.cs').read_text()
(here/'BossFixture.cs').write_text('using System.Collections;\nusing System.Globalization;\nnamespace EscapeFromBorderlands.Server;\ninternal static class BossFixture {\n'+block(s,'private static bool AddSourceRestrictedBossVariants(')+'\n'+block(s,'private static object? CreateCompatiblePoolKey(')+'\ninternal static bool Apply(IDictionary pool, IReadOnlyDictionary<string, VariantDefinition[]> variants, double fraction, out int added)=>AddSourceRestrictedBossVariants(pool, variants, fraction, out added);\n}\n')
print('Extracted original roll algorithm and unchanged current APBS boss weighting bodies.')
