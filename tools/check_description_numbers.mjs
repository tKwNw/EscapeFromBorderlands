import fs from 'node:fs/promises';
import path from 'node:path';
import {remainingNumberWords,verifyNumberRules} from './description_numbers.mjs';

// Usage: node check_description_numbers.mjs /path/to/EscapeFromBorderlands
const root=path.resolve(process.argv[2]??'EscapeFromBorderlands');
const source=path.join(root,'src');
const sides=['Client','Server'];
const catalogs=await Promise.all(sides.map(s=>fs.readFile(path.join(source,`EscapeFromBorderlands.${s}`,'LegendaryDefinitions.json'),'utf8')));
const issues=[];
if(catalogs[0]!==catalogs[1])issues.push({problem:'Client and server catalogs differ'});
let entries=0;
for(let i=0;i<catalogs.length;i++){
 const c=JSON.parse(catalogs[i]);
 for(const v of c.variants){
  entries++;
  for(const key of ['effectText',...(v.grenadeChargesPerRaid>0?['description']:[])]){
   const matches=remainingNumberWords(v[key]);
   if(matches.length)issues.push({side:sides[i],templateId:v.templateId,field:key,words:matches});
  }
 }
}
let sourceFiles=0;
async function inspect(dir){for(const item of await fs.readdir(dir,{withFileTypes:true})){
 const file=path.join(dir,item.name);
 if(item.isDirectory()){if(!['bin','obj'].includes(item.name))await inspect(file);}
 else if(item.name.endsWith('.cs')){
  sourceFiles++;
  const lines=(await fs.readFile(file,'utf8')).split('\n');
  for(let n=0;n<lines.length;n++){
   const line=lines[n];if(/^\s*\/\//.test(line))continue;
   for(const m of line.matchAll(/\$?@?"(?:""|\\.|[^"\\])*"/g)){
    const words=remainingNumberWords(m[0].replace(/\bThe Big One(?:'s)?\b/g,"[item name]"));if(!words.length)continue;
    // Preserve engine values and proper names. Diagnostic/log messages are
    // deliberately outside the effect-description copy policy.
    if(m[0]==='"single"'||m[0].includes('Second Breath')||m[0].includes('Expected '))continue;
    if(file.endsWith('ApbsCompatibility.cs')||file.endsWith('RarityMod.cs'))continue;
    issues.push({file:path.relative(root,file),line:n+1,text:m[0],words});
   }
  }
 }
}}
await inspect(source);
const checks=verifyNumberRules();
console.log(JSON.stringify({activeEffectsPerCatalog:entries/2,sourceFiles,normalizationChecks:checks,violations:issues.length,issues},null,2));
process.exitCode=issues.length?1:0;
