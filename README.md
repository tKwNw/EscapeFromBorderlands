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

## Protect legendary gear from consuming quest objectives

EFB items cannot be handed in or placed for quests. This includes Gunsmith weapon turn-ins, explicit legendary quest targets, owned storage layouts and retired compatibility aliases. Ordinary containers holding legendary items are protected too, including nested contents.

- Hand-in selection excludes protected copies, even when a native category or exact-template match would normally accept them.
- The final client handover transaction rejects protected selections before any item discard. The server checks the whole request before native processing; a mixed ordinary/legendary selection is rejected as a whole, so an ordinary item listed first is not partially consumed.
- Gunsmith keeps its native receiver and build requirements and rejects protected weapons or weapons holding protected items.
- Placement selection excludes protected items. The completion callback rechecks contents and takes the native cancellation path if a legendary was added during the placement timer. Canceled placement neither removes the item nor advances quest progress.
- Ordinary eligible hand-ins, builds and placements retain native behavior. A legendary elsewhere in inventory does not block handing in an unrelated ordinary item.

Legendary Mosins and other EFB variants still count as their vanilla bases for non-consuming objectives, including weapon kills/hits, equipment requirements and item finding. Native restrictions such as FIR, distance, body part, time, role and attachments remain enforced. Existing use/sale matching and quest markers are retained. This protection is specific to quest hand-ins, Gunsmith turn-ins and placement; it does not change voluntary trader sales or ordinary item use.

See [FORGE_COMPLIANCE.md](FORGE_COMPLIANCE.md) for the 0.30.0 metadata, configuration-migration, licensing and release requirements.

For players upgrading from 0.29.x, see [UPGRADING_0.30.0.md](UPGRADING_0.30.0.md) for GUID/config migration instructions.
