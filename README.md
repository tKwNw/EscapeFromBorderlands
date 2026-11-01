# Escape From Borderlands 0.30.0 — SPT 4.0.13

## Legendary item catalog

Browse the complete EFB 0.30.0 Legendary item roster (**1,508 named designs; catalog revision 52**):

| Item category | Count | Catalog |
| --- | ---: | --- |
| Guns / firearms | 1,229 | [All Legendary guns](docs/weapons/README.md) |
| Melee weapons | 48 | [All Legendary melee weapons](docs/melee/README.md) |
| Vests / chest rigs | 88 named designs | [All Legendary vests](docs/vests/README.md) · [440 storage identities](docs/vests/LAYOUTS.md) |
| Backpacks | 67 | [All Legendary backpacks](docs/backpacks/README.md) |
| Grenades | 37 | [All Legendary grenades](docs/grenades/README.md) |
| Headsets / earpieces | 39 | [All Legendary headsets](docs/headsets/README.md) |

[Open the complete catalog index](docs/README.md). Every entry includes its signature effect and template ID, taken directly from EFB's current Legendary definitions.


Adds 1 shared server setting for legendary items in airdrops, carrying forward all 0.28.3 status-effect and partial grenade charge display fixes.

## Airdrop chance

In `SPT/user/mods/EscapeFromBorderlands/config/config.json`:

```json
"airdropLegendaryChancePercent": 1
```

- Default **1%**, adjustable from **0% to 100%**, including fractional percentages.
- A roll is made for each supported item generated in an airdrop. **0%** produces no EFB legendary conversions. **100%** converts every supported individual loot item to a current legendary variant of that same vanilla base.
- The same setting covers guns, melee, backpacks, chest/armored rigs, grenades and earpieces across every native airdrop type and custom/forced airdrop requests. Food, medical supplies, barter goods, attachments and other unsupported items remain as generated. A crate with no supported gear keeps its normal contents.
- This is a chance per eligible item, not a chance that the entire crate contains a legendary. It does not add extra items or alter how often airdrops occur.
- The airdrop rate is independent of APBS/standalone bot settings and the flea toggle. There are no separate airdrop category, drop-type or backend options.
- Current concrete backpack layouts and existing signature/layout weights are respected. Mystery offers and retired layouts are excluded. Item IDs, attachments, Found in Raid state and normal loot counts are preserved. A restricted fire-mode signature receives a supported mode if the source preset carried an incompatible mode.
- Unusual stacks introduced by other mods retain their original template and quantity, preserving validity for legendary items that require a maximum stack size of 1.

EFB database clones are excluded from the native airdrop's temporary random/preset pool before the single percentage is applied. Forced EFB entries are mapped to their vanilla bases first. Conversion runs before native crate fitting and does not change shared loot databases or other reward generators. Restart the server after editing the setting.

Existing configs receive the missing setting automatically, preserving custom rates, explicit zeroes, pricing, flea settings and unrelated fields. Values outside the range are clamped to 0–100. The master `enabled` switch still disables the mod.

## Install

Close the game and server. Existing users: extract **Upgrade** into the SPT game root and replace both matching DLLs. It preserves your existing configuration and adds the missing airdrop setting during normal config migration.

- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll`

For a fresh installation use **Full**, which also contains `SPT/user/mods/EscapeFromBorderlands/config/config.json`. Only `config.json` is shipped, with no `config.default`. Target: **SPT 4.0.13**; the separate SPT 4.1.6 branch is unchanged.

## Versioning and validation

763 behavior cases / 770,662 assertions across 14 suites pass, with 192 native contracts and 159 Harmony argument bindings across 156 hook groups. Both builds have 0 warnings/errors. Number audit: 0 violations across 122 source files. Live in-raid airdrop testing remains outstanding.

Release version **0.29.0** follows **MAJOR.MINOR.PATCH**, matching client/server declarations, file/product metadata and archive names. Source and compiled-metadata checks enforce agreement. The Source and Validation archives include the complete implementation, status inventory and verification evidence. Production-logic tests and real Harmony boundary tests use matching SPT 4.0.13 references; the build host has no running Tarkov client.

## Legendary earpieces

| ID | Native base | Name | Signature |
| --- | --- | --- | --- |
| EP01-1 | ComTac II | Dead Channel | After remaining still for at least 1 second, environmental ambient noise is 75% quieter to you. Moving ends the effect. |
| EP01-2 | ComTac II | Inside Voice | Your own footsteps and equipment-handling sounds are 50% quieter to you. |
| EP01-4 | ComTac II | Check 1-2 | Completing a magazine check grants +40% aiming speed for 6 seconds. 15-second cooldown. |
| EP01-5 | ComTac II | Cold Open | After 10 seconds without firing, your next 3 firearm shots deal 25% more damage. Firing resets the 10-second timer. |
| EP02-1 | Sordin | Moss Code | While on Woods, Shoreline, or Reserve, sounds from other characters can be heard from 50% farther away. |
| EP02-2 | Sordin | Rain Check | Rain and wind are 60% quieter to you. |
| EP02-3 | Sordin | Shaggy Special | Walking 20 metres without sprinting restores 20% of maximum Leg Stamina. Sprinting resets your distance progress. |
| EP02-5 | Sordin | Grounded | After remaining prone for 3 seconds, gain +50% Arm Stamina regeneration and 25% less weapon sway. Leaving prone ends both bonuses. |
| EP03-1 | GSSh-01 | Tin Foil Crown | Grenade-induced deafness and contusion last 75% less time. |
| EP03-2 | GSSh-01 | Budget Cuts | Fully exhausting your Arm Stamina instantly restores 50% of its maximum. 45-second cooldown. |
| EP03-3 | GSSh-01 | Loose Wire | After an enemy bullet damages you, gain +35% reload speed and +25% aiming speed for 8 seconds. 25-second cooldown. |
| EP04-1 | Tactical Sport | PACER Test | Stopping a sprint grants +50% aiming speed for 5 seconds. 10-second cooldown. |
| EP04-2 | Tactical Sport | Stutter Step | While moving sideways, firearm recoil is reduced by 25%. |
| EP04-3 | Tactical Sport | Steady Cadence | Aiming slows your walking speed 50% less. |
| EP04-4 | Tactical Sport | Time Out | After remaining still for 3 seconds, medical items are used 50% faster. Moving ends the bonus. |
| EP04-5 | Tactical Sport | Photo Finish | If an enemy bullet damages you and leaves you at 30% total health or below, gain +30% sprint speed for 6 seconds. 45-second cooldown. |
| EP05-3 | Razor | Low-Profile | Killing an enemy within 30 metres grants +50% hearing range for 20 seconds. Further qualifying kills refresh the duration without stacking. |
| EP05-4 | Razor | Garrote Wire | While holding a melee weapon, gain +20% walking speed and +25% melee attack speed. |
| EP06-1 | XCEL | Matamata Turtle | Hearing range is increased by 100%. Movement speed is reduced by 20%. |
| EP06-2 | XCEL | Crossfade | Switching from a melee weapon to a firearm grants +30% firearm damage for 4 seconds. 20-second cooldown. |
| EP06-3 | XCEL | Tempo Shift | Firing 5 shots within 2 seconds grants +20% fire rate for 4 seconds. 12-second cooldown. |
| EP06-4 | XCEL | Feedback Loop | Each firearm kill cuts 15 seconds from 1 reusable grenade's recharge in your pockets or rig. Chooses the copy with the shortest active timer. 10-second cooldown. |
| EP06-5 | XCEL | 2nd Verse | Clearing a weapon malfunction grants +40% fire rate and 50% less recoil for 10 seconds. 30-second cooldown. |
| EP07-1 | M32 | I Knocked | Successfully kicking open a door grants +25% firearm damage and +30% walking speed for 10 seconds. 20-second cooldown. |
| EP07-2 | M32 | Fast Forward | Grenade draw speed and throw animation speed are increased by 60%. |
| EP07-5 | M32 | Commercial Break | Finishing food or drink grants +20% walking speed and +50% Leg Stamina regeneration for 20 seconds. 45-second cooldown. |
| EP08-1 | ComTac IV | 4 on the Floor | While crouched and stationary, firearm recoil is reduced by 40%. |
| EP08-2 | ComTac IV | Mute Point | Footstep noise emitted while crouch-walking is reduced by 40%. |
| EP08-3 | ComTac IV | Quiet Hours | Between 20:00 and 06:00 raid time, aiming consumes 35% less Arm Stamina and weapon sway is reduced by 35%. |
| EP09-2 | Liberator | Fast Clock | You are immune to grenade-induced contusion. |
| EP09-3 | Liberator | Ear Amnesty | Pain and tremors have 50% less effect on your weapon sway. |
| EP09-5 | Liberator | Safe Word | While using a medical item, incoming firearm damage is reduced by 30%. |
| EP10-1 | ComTac V | High Fidelity | After aiming continuously for 2 seconds, gain +20% projectile velocity and 25% less firearm dispersion. Stopping aiming ends both bonuses. |
| EP10-2 | ComTac V | 5 Second Rule | A firearm kill grants +50% reload speed for 5 seconds. Further firearm kills refresh the duration. |
| EP10-3 | ComTac V | Band Hopper | Switching directly between different firearms is 75% faster. |
| EP10-4 | ComTac V | Double Take | Completing a chamber check grants +25% damage to your next 2 firearm shots. 20-second cooldown. |
| EP11-1 | ComTac VI | 6th Sense | Footsteps from other characters can be heard from 25% farther away. |
| EP11-2 | ComTac VI | Deep Listening | While prone, footsteps from other characters are 75% louder to you and gunfire is 50% quieter to you. |
| EP11-3 | ComTac VI | Clear Reception | At 80% Arm Stamina or above, firearm recoil recovers 40% faster. |

## Random earpiece bonuses

All numerical rolls use 5% steps. Roll 4 distinct compatible types.

| Bonus | Range |
| --- | --- |
| Far Signal | +5–15% hearing distance |
| Noise Gate | −10–25% environmental ambient volume |
| Self Muffle | −10–25% own footstep and equipment volume, local only |
| Clear Head | −15–30% grenade contusion duration; excluded from Fast Clock |
| Steady Aim | −10–20% aiming Arm Stamina cost |
| Fast Hands | +10–20% firearm draw speed |
| 2nd Wind | +10–25% Leg Stamina regeneration |
| Quick Focus | +10–20% aiming speed |

## Protect legendary gear from consuming quest objectives

EFB items cannot be handed in or placed for quests. This includes Gunsmith weapon turn-ins, explicit legendary quest targets, owned storage layouts and retired compatibility aliases. Ordinary containers holding legendary items are protected too, including nested contents.

- Hand-in selection excludes protected copies, even when a native category or exact-template match would normally accept them.
- The final client handover transaction rejects protected selections before any item discard. The server checks the whole request before native processing; a mixed ordinary/legendary selection is rejected as a whole, so an ordinary item listed first is not partially consumed.
- Gunsmith keeps its native receiver and build requirements and rejects protected weapons or weapons holding protected items.
- Placement selection excludes protected items. The completion callback rechecks contents and takes the native cancellation path if a legendary was added during the placement timer. Canceled placement neither removes the item nor advances quest progress.
- Ordinary eligible hand-ins, builds and placements retain native behavior. A legendary elsewhere in inventory does not block handing in an unrelated ordinary item.

Legendary Mosins and other EFB variants still count as their vanilla bases for non-consuming objectives, including weapon kills/hits, equipment requirements and item finding. Native restrictions such as FIR, distance, body part, time, role and attachments remain enforced. Existing use/sale matching and quest markers are retained. This protection is specific to quest hand-ins, Gunsmith turn-ins and placement; it does not change voluntary trader sales or ordinary item use.


## Current grenade catalog

| ID | Native source | Name | Charges | Recharge seconds | Signature |
| --- | --- | --- | ---: | ---: | --- |
| GR01 | RGD-5 | Danger Zone | 3 | 180 | Blast damage is increased by 75% against targets within 3 metres of the detonation. |
| GR02 | RGD-5 | Street Sweeper | 3 | 180 | Every detonation creates 75% more fragments. |
| GR03 | RGD-5 | Lunch Bell | 3 | 180 | After a successful throw, gain 40% reload speed for 5 seconds. |
| GR04 | RGD-5 | Semtax | 3 | 180 | Sticks to the 1st object it hits, including enemies and scenery, and remains attached until its fuse expires. |
| GR06 | F-1 | Little Lemon | 3 | 180 | Every detonation creates 100% more fragments. |
| GR07 | F-1 | Red Notice | 3 | 180 | Fragment hits that damage flesh also cause a heavy bleed. |
| GR09 | F-1 | Iron Curtain | 3 | 180 | After a successful throw, take 90% less damage from grenade blasts and fragments for 8 seconds. |
| GR11 | M67 | Blast Em | 3 | 180 | The entire blast radius deals full blast damage. |
| GR12 | M67 | Side Business | 3 | 180 | The explosion also creates an instant smoke cloud at the detonation point for 15 seconds. |
| GR13 | M67 | M.I.R.V. | 3 | 180 | On detonation, releases 3 normal F-1 grenades in a wide, unpredictable spread. |
| GR14 | M67 | Baseball | 3 | 180 | +100% throwing distance and +50% explosion radius. |
| GR15 | M67 | Holy Hand Grenade | 1 | 300 | The fuse lasts 3 seconds. Explosion radius is increased by 300%. |
| GR18 | VOG-17 | Reset | 3 | 180 | After a successful throw, gain 75% magazine loading and unloading speed for 10 seconds. |
| GR20 | VOG-17 | Rapid Response | 3 | 180 | After a successful throw, your next firearm draw is 100% faster. |
| GR21 | VOG-25 | Juggernaut | 3 | 180 | The thrower takes no damage from this grenade’s blast or fragments. |
| GR24 | VOG-25 | Ankle Biter | 3 | 180 | Blast damage to legs is increased by 75%. |
| GR25 | VOG-25 | Dirty Work | 3 | 180 | After a successful throw, hip-fire dispersion is reduced by 40% for 6 seconds. |
| GR26 | RGN | Encore | 3 | 180 | Detonates on impact, then repeats the full explosion at the same location after 1 second. |
| GR27 | RGN | Point Blank | 3 | 180 | An enemy directly struck by the grenade takes 100% more blast damage from its detonation. |
| GR35 | RGO | Big Bang | 3 | 180 | +30% explosion radius. Every detonation also produces a flash effect within 25 metres. |
| GR36 | V40 | Small Claims | 5 | 180 | Gain 60% more blast damage when a pistol is your only equipped firearm at release. |
| GR40 | V40 | Shagbag | 15 | 180 | Maximum 15 charges per copy and +50% throwing distance. |
| GR47 | Zarya | Sun Staring Champion | 3 | 180 | The thrower is immune to this grenade’s flash, concussion and deafening effects. |
| GR49 | Zarya | Play Maker | 3 | 180 | After a successful throw, gain 20% movement speed for 5 seconds. |
| GR50 | Zarya | Daylight Robbery | 3 | 180 | Enemies successfully flashed take 30% more damage from your firearm shots for 8 seconds. |
| GR51 | M7290 | Neuralyzer | 3 | 180 | +100% effective flash radius. |
| GR54 | M7290 | Flash Push | 3 | 180 | Successfully flashing an enemy grants 40% less firearm recoil for 8 seconds. |
| GR56 | RDG-2B | Fog of War | 3 | 180 | Smoke cloud width, height and depth are increased by 100%. |
| GR57 | RDG-2B | Safe Passage | 3 | 180 | While within 8 metres of this grenade’s active smoke source, the thrower gains 20% movement speed. |
| GR58 | RDG-2B | Still Waters | 3 | 180 | While crouched within 8 metres of this grenade’s active smoke source, the thrower has 60% less firearm recoil. |
| GR59 | RDG-2B | Pop Smoke | 3 | 180 | Smoke instantly reaches its full area when the fuse expires. |
| GR62 | M18 Green | FOB | 3 | 180 | While within 8 metres of this grenade’s active smoke source, the thrower loads and unloads magazines 100% faster. |
| GR63 | M18 Green | Green Room | 3 | 180 | While within 8 metres of this grenade’s active smoke source, the thrower uses medical items 50% faster. |
| GR64 | M18 Green | Breathing Room | 3 | 180 | While within 8 metres of this grenade’s active smoke source, the thrower gains +75% Arm Stamina and Leg Stamina regeneration. |
| GR65 | M18 Green | Trailblazer | 3 | 180 | Drops 1 M18 Green smoke grenade immediately and 1 every 5 metres of horizontal travel. Drops stop on landing, where the parent deploys its normal cloud. Dropped grenades deploy smoke after landing. |

## Random bonuses

Each copy has 4 distinct compatible bonuses, using 5% numerical steps. Encore retains its original 4 bonus types and seed sequence. Instant Pop Smoke replaces only its obsolete Rapid Screen bonus with an unused compatible type; its other bonuses are preserved.

| Bonus | Range | Compatible types |
| --- | --- | --- |
| Blast damage | +15–30% | Fragmentation |
| Explosion radius | +10–20% | Fragmentation |
| Fragment count | +20–40% | Fragmentation |
| Throwing distance | +15–30% | All |
| Fragment damage | +10–25% | Fragmentation |
| Grenade draw speed | +20–40% | All |
| Throw animation speed | +15–30% | All |
| Flash duration | +15–30% | Flash |
| Effective visual flash radius | +10–25% | Flash |
| Smoke duration | +20–40% | Smoke |
| Smoke cloud size | +15–30% | Smoke |
| Smoke deployment speed | +20–40% | Smoke except Pop Smoke |
| Door Breaching | Fixed 3-metre trait | Fragmentation |

Grenade weight reduction, throwing Leg Stamina cost reduction and fragment penetration are excluded. Matching grenade buffs refresh or use the strongest active value without stacking.


See [FORGE_COMPLIANCE.md](FORGE_COMPLIANCE.md) for the 0.30.0 metadata, configuration-migration, licensing and release requirements.

For players upgrading from 0.29.x, see [UPGRADING_0.30.0.md](UPGRADING_0.30.0.md) for GUID/config migration instructions.
