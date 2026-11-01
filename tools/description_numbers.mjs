import assert from 'node:assert/strict';

const units={zero:0,one:1,two:2,three:3,four:4,five:5,six:6,seven:7,eight:8,nine:9};
const teens={ten:10,eleven:11,twelve:12,thirteen:13,fourteen:14,fifteen:15,sixteen:16,seventeen:17,eighteen:18,nineteen:19};
const tens={twenty:20,thirty:30,forty:40,fifty:50,sixty:60,seventy:70,eighty:80,ninety:90};
const ordinal={first:1,second:2,third:3,fourth:4,fifth:5,sixth:6,seventh:7,eighth:8,ninth:9,tenth:10,eleventh:11,twelfth:12,thirteenth:13,fourteenth:14,fifteenth:15,sixteenth:16,seventeenth:17,eighteenth:18,nineteenth:19,twentieth:20,thirtieth:30,fortieth:40,fiftieth:50,sixtieth:60,seventieth:70,eightieth:80,ninetieth:90,hundredth:100,thousandth:1000,millionth:1000000};
const u=Object.keys(units).join('|'),t=Object.keys(teens).join('|'),d=Object.keys(tens).join('|');
const small=`(?:(?:${d})(?:[- ](?:${u}))?|${t}|${u})`;
const hundred=`(?:(?:${u}) hundred(?: and)?(?: ${small})?|${small})`;
const thousand=`(?:${hundred} thousand(?:(?: and)? ${hundred})?|${hundred})`;
const cardinal=new RegExp(`\\b(?:${thousand} million(?:(?: and)? ${thousand})?|${thousand})\\b`,'gi');
const ord=new RegExp(`\\b(?:${Object.keys(ordinal).join('|')})\\b`,'gi');
function suffix(n){return n+(n%100>=11&&n%100<=13?'th':n%10===1?'st':n%10===2?'nd':n%10===3?'rd':'th');}
function parseCardinal(s){
 let total=0,chunk=0;
 for(const token of s.toLowerCase().split(/[ -]+/)){
  if(token==='and')continue;
  if(token==='hundred')chunk*=100;
  else if(token==='thousand'||token==='million'){total+=chunk*(token==='thousand'?1000:1000000);chunk=0;}
  else chunk+=(units[token]??teens[token]??tens[token]);
 }
 return total+chunk;
}
function secondIsUnit(s,index){
 const left=s.slice(0,index);
 if(/(?:\d(?:\.\d+)?[\s-]+|\b(?:per|each|every)\s+)$/i.test(left))return true;
 return false;
}

export function normalizeDescription(text){
 let s=text;
 s=s.replace(/\bhalf a second\b/gi,'0.5 seconds');
 s=s.replace(/\b(?:one|a) quarter of\b/gi,'25% of');
 s=s.replace(/\bquarter(?: of)?\b/gi,m=>m.toLowerCase().endsWith(' of')?'25% of':'25%');
 s=s.replace(/\bhalf\b/gi,'50%');
 s=s.replace(/\bOnce earned\b/g,'After activation');
 s=s.replace(/\bonce earned\b/g,'after activation');
 s=s.replace(/\bOnce per raid, striking\b/g,'1 use per raid: striking');
 s=s.replace(/fires once when the trigger is pressed and once when it is released/gi,'fires 1 shot when the trigger is pressed and 1 shot when it is released');
 s=s.replace(/\bonce\b/gi,'1 time');
 s=s.replace(/\btwice\b/gi,'2 times');
 s=s.replace(/Double-tap fire only\./g,'2-shot fire only.');
 s=s.replace(/Replaces Double-tap with /g,'Replaces the 2-shot firing mode with ');
 s=s.replace(/\bsingle-projectile\b/gi,'1-projectile');
 s=s.replace(/\bdoubles (its|the|your) ([^.]+)\./gi,(_,who,thing)=>`multiplies ${who} ${thing} by 2.`);
 s=s.replace(/\bdoubled\b/gi,'2×');
 s=s.replace(/\btripled\b/gi,'3×');
 s=s.replace(/\bquadrupled\b/gi,'4×');
 // Compound ordinals must be handled before cardinals (twenty-first -> 21st).
 const compoundOrd=new RegExp(`\\b(${d})[- ](first|second|third|fourth|fifth|sixth|seventh|eighth|ninth)\\b`,'gi');
 s=s.replace(compoundOrd,(_,a,b)=>suffix(tens[a.toLowerCase()]+ordinal[b.toLowerCase()]));
 s=s.replace(cardinal,match=>String(parseCardinal(match)));
 s=s.replace(ord,(match,index,whole)=>match.toLowerCase()==='second'&&secondIsUnit(whole,index)?match:suffix(ordinal[match.toLowerCase()]));
 return s;
}

export function remainingNumberWords(text){
 const candidates=new RegExp(`\\b(?:${u}|${t}|${d}|hundred|thousand|million|${Object.keys(ordinal).join('|')}|once|twice|single|double|doubles|doubled|triple|triples|tripled|quadruple|quadruples|quadrupled|half|quarter)\\b`,'gi');
 return [...text.matchAll(candidates)].filter(m=>!(m[0].toLowerCase()==='second'&&secondIsUnit(text,m.index))).map(m=>({word:m[0],index:m.index}));
}

export function verifyNumberRules(){
 const cases=[
  ['nine shots and six seconds','9 shots and 6 seconds'],
  ['twenty-one shots and one hundred and five rounds','21 shots and 105 rounds'],
  ['one thousand two hundred and thirty-four rounds','1234 rounds'],
  ['first shot, second shot, third shot, twenty-first shot','1st shot, 2nd shot, 3rd shot, 21st shot'],
  ['one cartridge per second; 30-second cooldown','1 cartridge per second; 30-second cooldown'],
  ['half a second; half capacity; one quarter of capacity','0.5 seconds; 50% capacity; 25% of capacity'],
  ['Once earned, it lasts until reload. Triggers once per magazine.','After activation, it lasts until reload. Triggers 1 time per magazine.'],
  ['Every third shell doubles its normal pellet count.','Every 3rd shell multiplies its normal pellet count by 2.'],
  ['Uses six charges. At zero, it stays in inventory.','Uses 6 charges. At 0, it stays in inventory.'],
  ['+25% damage for 8 seconds. 20-second cooldown.','+25% damage for 8 seconds. 20-second cooldown.'],
 ];
 for(const [input,expected]of cases){const actual=normalizeDescription(input);assert.equal(actual,expected);assert.deepEqual(remainingNumberWords(actual),[]);assert.equal(normalizeDescription(actual),actual);}
 return cases.length;
}
