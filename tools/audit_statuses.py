from pathlib import Path
import json,re,collections,hashlib,argparse

parser=argparse.ArgumentParser(description='Classify status visibility for every named design using the current catalog and source gates.')
parser.add_argument('--source',required=True,type=Path)
parser.add_argument('--output',required=True,type=Path)
args=parser.parse_args()
client=args.source/'src/EscapeFromBorderlands.Client'
out=args.output;out.mkdir(exist_ok=True,parents=True)
catalog=json.loads((client/'LegendaryDefinitions.json').read_text())
designs=list({v['shortName']:v for v in reversed(catalog['variants'])}.values())
assert len(designs)==1508
head_gates={
'EP01-1':'Stationary for at least 1 second; moving ends it.',
'EP02-1':'On Woods, Shoreline or Reserve, including indoors.',
'EP02-5':'Prone continuously for at least 3 seconds; leaving prone ends it.',
'EP04-2':'Sideways movement input exceeds the existing threshold.',
'EP04-3':'Currently aiming down sights.',
'EP04-4':'Stationary for at least 3 seconds; movement ends readiness.',
'EP05-4':'A melee weapon is drawn.',
'EP08-1':'Crouched and stationary.',
'EP08-2':'Crouch-walking with movement input and no sprint.',
'EP08-3':'Raid clock is at least 20:00 or before 06:00.',
'EP09-5':'Currently using an actual medkit item.',
'EP10-1':'Continuous ADS for at least 2 seconds; lowering the weapon ends it.',
'EP11-2':'Currently prone.',
'EP11-3':'Arm Stamina is at least 80% of its current maximum.'}
head_permanent={'EP01-2','EP02-2','EP03-1','EP06-1','EP07-2','EP09-2','EP09-3','EP10-3','EP11-1'}
bag_switch=set(re.findall(r'case "(BP[0-9]+-[0-9]+)"', (client/'BackpackEffectStatus.cs').read_text()))
bag_cooldowns={'BP03-3','BP07-1','BP10-3','BP20-3','BP29-2','BP34-2','BP43-3'}
bag_proc={'BP18-2','BP36-1','BP37-3'}
bag_use={'BP14-3','BP23-1','BP42-1'}
rig_new={'TR13-1','TR14-1','TR16-2','TR17-2','TR33-1','TR33-2','AR01-2','AR04-2','AR05-2','AR05-1','AR14-2','AR16-1','AR16-2','AR11-2','TR18-1','TR24-2'}
rig_permanent={'TR04-1','TR05-1','TR07-2','TR12-1','TR19-2','AR07-1','AR09-2','AR13-2','AR20-1','AR21-1','AR21-2','AR25-2'}
rig_proc={'TR17-1','TR05-2','AR23-1'}
gun_permanent={'bunker_buster','split_decision','mp7a2_volks_tribute','ak545_wall_pass','wave015_core_and_jacket','wave015_echo'}
melee_ones=['DamageMultiplier','SlashDamageMultiplier','StabDamageMultiplier','AttackSpeedMultiplier','SwapSpeedMultiplier','WeaponSwapSpeedMultiplier','ReachMultiplier','MovementMultiplier','IncomingDamageMultiplier','SprintStaminaMultiplier','JumpStaminaMultiplier','JumpHeightMultiplier','NoiseMultiplier','RearDamageMultiplier','BleedingTargetDamageMultiplier','MovingDamageMultiplier','LegDamageMultiplier','MaximumHealthFraction','IntervalDamageMultiplier','ChargedDamageMultiplier','ChargedReachMultiplier','WeightMultiplier','CrouchMovementMultiplier','HeadSlashDamageMultiplier','VaultSpeedMultiplier','VaultLegStaminaMultiplier','CrouchExplosionMultiplier','MarkedAttackerDamageMultiplier','LongStabDamageMultiplier','FirstPartStabDamageMultiplier','TargetLowHealthDamageMultiplier']
def lower(k):return k[0].lower()+k[1:]
def melee_permanent(e):
 def n(k):return e.get(lower(k),1 if k in melee_ones else 0)
 return not e.get('ability') and not any(e.get(lower(k)) for k in ['RequiresAk74','RequiresEmptyPrimaries','RequiresSniper','RequiresNight','RequiresBleeding','RequiresProne','UnlocksDoorOncePerRaid','KillStopsBleeding','KillClearsTremor','HeavyBleedOnStab','FractureOnLegHit']) and n('MaximumHealthFraction')>=1 and all(n(k)<=0 for k in ['StationarySeconds','DamagePerFleshHit','AttackInterval','ExtraJumpHeightMultiplier','SlashSpeedPerAttack','NearbyEnemyRadius','ChainPenetrationPerHit','BleedingHitLegStaminaFraction','HealOnFleshHit','ChestSlashStomachDamage','KillPainkillerSeconds','HealOnKill','KillLegStaminaFraction','KillArmStaminaFraction','TargetLowHealthFraction']) and n('MarkedAttackerDamageMultiplier')>=1 and all(n(k)==1 for k in ['CrouchMovementMultiplier','CrouchExplosionMultiplier','MovingDamageMultiplier','BleedingTargetDamageMultiplier','RearDamageMultiplier','FirstPartStabDamageMultiplier','LongStabDamageMultiplier','HeadSlashDamageMultiplier'])
def melee_proc(e):return any(e.get(lower(k),0)>0 for k in ['HealOnFleshHit','HealOnKill','KillLegStaminaFraction','KillArmStaminaFraction','BleedingHitLegStaminaFraction','ChestSlashStomachDamage','TargetLowHealthFraction']) or any(e.get(lower(k)) for k in ['KillStopsBleeding','KillClearsTremor','HeavyBleedOnStab','FractureOnLegHit']) or any(e.get(lower(k),1)!=1 for k in ['RearDamageMultiplier','BleedingTargetDamageMultiplier','FirstPartStabDamageMultiplier','LongStabDamageMultiplier','HeadSlashDamageMultiplier'])
rows=[]
source_runtime=(client/'LegendaryRuntime.cs').read_text()
for d in sorted(designs,key=lambda v:(v['weaponClass'],v['sourceShortName'],v['shortName'])):
 key=d.get('runtimeKey') or d['templateId'];category=d['weaponClass'];source='';gate='';change='Existing behavior checked';mode='';visibility=''
 if category=='headphones':
  source='EarpieceRuntime.cs / EarpieceEffects.cs'
  if key in head_permanent:mode='Permanent';visibility='Hidden';gate='Unconditional modifier while worn.'
  elif key in head_gates:mode='Conditional';visibility='While the condition holds';gate=head_gates[key];change='Missing conditional row restored'
  elif key=='EP01-5':mode='Charged';visibility='When the next cartridges are empowered';gate='10-second firing pause readies 3 cartridges; actual firing spends them.';change='Initial and replenished ready cartridges now visible without spending them'
  elif key=='EP02-3':mode='Instant trigger';visibility='Brief Triggered receipt';gate='After the actual 20-metre walking recovery trigger.';change='Previously invisible proc acknowledged'
  else:mode='Timed / charges / cooldown';visibility='During active window, ready charges or real cooldown';gate=d['effectText']
 elif category=='backpack':
  source='BackpackEffectStatus.cs / EffectStatusModel.cs / BackpackNative.cs'
  if key=='BP36-2':mode='Extraction result';visibility='Native post-raid result; no lasting in-raid buff window';gate=d['effectText']
  elif key in bag_switch:mode='Conditional / timed';visibility='While the actual condition or stored charge/window holds';gate=d['effectText'];change='Existing gate checked'
  elif key in bag_cooldowns:mode='Cooldown / deployed effect';visibility='During actual cooldown; Sling Away/deployed smoke retain their real active window';gate=d['effectText']
  elif key in bag_proc:mode='Instant trigger';visibility='Brief Triggered receipt after actual successful proc';gate=d['effectText'];change='Previously invisible successful proc acknowledged'
  elif key in bag_use:mode='Conditional item use';visibility='While the actual accelerated item-use snapshot exists';gate=d['effectText'];change='Active accelerated use now visible'
  else:mode='Permanent / access rule';visibility='Hidden';gate='Permanent equipped stat, inventory/access rule, or native extraction-only result. '+d['effectText']
  if key in bag_use and key in bag_switch:visibility+='; accelerated use remains visible after its charge is spent';change='Charge and continuing item-use phases checked'
  if key=='BP40-2':change='First paid action now also shows actual distinct-action refund readiness'
 elif category=='chestRig':
  source='ChestRigRuntime.cs / ChestRigMedical.cs / ChestRigInventory.cs / ChestRigCombat.cs'
  if key in rig_permanent:mode='Permanent';visibility='Hidden';gate='Unconditional equipped modifier or access/storage rule.'
  elif key in rig_proc:mode='Instant trigger';visibility='Brief Triggered receipt after confirmed action';gate=d['effectText'];change='Actual successful action now acknowledged'
  else:mode='Conditional / timed / charges';visibility='While condition, timed window, pending charge or actual cooldown holds';gate=d['effectText']
  if key in rig_new:change='Missing condition/readiness row restored or corrected'
  if key=='AR03-2':change='Stored next-shot damage remains visible before a firearm is drawn'
 elif category=='melee':
  source='MeleeEffectStatus.cs / MeleeRuntime.cs / AdvancedMeleeRuntime.cs';e=d['meleeEffect']
  if melee_permanent(e):mode='Permanent';visibility='Hidden';gate='Unconditional held-item modifier.';change='Permanent status suppressed'
  elif e.get('killPainkillerSeconds',0)>0:mode='Persistent triggered effect';visibility='Real native pain-relief lifetime, including after stowing';gate=d['effectText']
  elif melee_proc(e) and not e.get('ability') and not e.get('damagePerFleshHit') and not e.get('markedAttackerDamageMultiplier',1)<1:mode='Instant target/kill trigger';visibility='Brief Triggered receipt after an actual qualifying flesh hit or kill';gate=d['effectText'];change='Always-ready placeholder replaced by actual trigger receipt'
  else:mode='Conditional / charged';visibility='While real eligibility gate, buildup or ready charge holds';gate=d['effectText']
 elif category=='grenade':
  source='ReusableGrenadeRuntime.cs / GrenadeEffectRules.cs / GrenadeBuffs.cs / LegendaryGrenadeEffects.cs';mode='Per-copy charges / recharge';visibility='Charges when held or partially charged while stowed; recharge while held or stowed; empty stowed counts and full stowed copies hidden';gate=d['effectText']
  if key in {'GR03','GR09','GR18','GR20','GR25','GR49','GR54'}:visibility+='; actual triggered buff/charge has its own row'
  if key in {'GR57','GR58','GR62','GR63','GR64'}:visibility+='; smoke buff only for owner inside the active radius'+(' and crouched' if key=='GR58' else '')
  if key=='GR50':visibility+='; live marked-target duration is now displayed';change='Missing owned mark window restored; dead targets no longer keep the row alive'
 else:
  category='firearm';source='WeaponEffectStatus.cs / LegendaryRuntime.cs';custom=d.get('customEffect');special=d.get('specialId','')
  if (custom and custom.get('trigger')=='always') or (not custom and special in gun_permanent):mode='Permanent';visibility='Hidden';gate='Unconditional signature; compatible random stats remain gameplay-active.';change='Permanent-only status suppressed'
  elif special=='wave015_dont_stop':mode='Instant trigger';visibility='Brief Triggered receipt only after a successful ammunition transfer';gate=d['effectText'];change='Always-ready placeholder replaced by actual transfer receipt'
  elif special=='wave015_like_a_ghost':mode='Deployed effect';visibility='Actual deployed smoke lifetime';gate=d['effectText']
  else:mode='Conditional / charged';visibility='Held item only, while real upcoming-shot or continuous-condition evaluation qualifies';gate=('custom:'+custom['trigger'] if custom else 'special:'+special if special else d['effectKind']+':trigger '+str(d.get('triggerIndex')))+'; '+d['effectText']
  if special in {'kabans_kabab','requiem'}:change='Permanent baseline no longer hides which conditional cartridge is ready'
  if custom:assert '"'+custom['trigger']+'"' in source_runtime,custom['trigger']
 rows.append(dict(name=d['shortName'],nativeBase=d['sourceShortName'],category=category,signatureKey=key,templateId=d['templateId'],signature=d['effectText'],statusType=mode,shownWhen=visibility,gameplayGate=gate,source=source,auditChange=change))
summary=dict(version='0.30.0',sptVersion='4.1.6',baseline='0.28.2 native / 0.29.0 upstream',catalogRevision=52,namedDesigns=len(rows),categories=dict(collections.Counter(r['category'] for r in rows)),statusTypes=dict(collections.Counter(r['statusType'] for r in rows)),newConditionalHeadsetRows=len(head_gates),permanentHeadsetSignatures=len(head_permanent),note='Static classification of every named design and targeted production-logic regression tests. It is not a live test of every item or state combination. Category HUD toggles remain authoritative. Permanent random modifiers do not get individual rows. Instant receipts use a 2-second display lifetime without claiming a 2-second gameplay buff.')
report=dict(summary=summary,rows=rows,sourceSha256={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in client.glob('*.cs') if p.name in {'BackpackEffectStatus.cs','BackpackNative.cs','ChestRigRuntime.cs','EarpieceRuntime.cs','EarpieceEffects.cs','MeleeEffectStatus.cs','MeleeRuntime.cs','WeaponEffectStatus.cs','EffectStatusModel.cs','EffectStatusHud.cs','GrenadeBuffs.cs','GrenadeEffectRules.cs','ReusableGrenadeRuntime.cs'}})
(out/'Status_Effects_Inventory.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(summary,indent=2))
