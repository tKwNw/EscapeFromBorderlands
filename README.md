# Escape From Borderlands 0.31.2 — SPT 4.0.13

Service Window now speeds medical use from the first action, Holy Hand Grenade draw audio recovers across raids, and optional Stat Rewards integration controls Legendary equipment rewards. See [release notes](RELEASE_NOTES_0.31.2.md) and [upgrade instructions](UPGRADING_0.31.2.md).

## Legendary item catalog

Browse the complete EFB 0.31.2 Legendary item roster (**1,508 named designs; catalog revision 54**):

| Item category | Count | Catalog |
| --- | ---: | --- |
| Guns / firearms | 1,229 | [All Legendary guns](docs/weapons/README.md) |
| Melee weapons | 48 | [All Legendary melee weapons](docs/melee/README.md) |
| Vests / chest rigs | 88 | [All Legendary vests](docs/vests/README.md) · [440 storage identities](docs/vests/LAYOUTS.md) |
| Backpacks | 67 | [All Legendary backpacks](docs/backpacks/README.md) |
| Grenades | 37 | [All Legendary grenades](docs/grenades/README.md) |
| Headsets / earpieces | 39 | [All Legendary headsets](docs/headsets/README.md) |

[Open the complete catalog index](docs/README.md). Every entry includes its signature effect and template ID, taken directly from EFB's current Legendary definitions.

## Downloads and installation

- [Full installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.2-spt-4.0.13/EscapeFromBorderlands_0.31.2_SPT-4.0.13.zip)
- [Upgrade ZIP — 2 DLLs; preserves your config](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.2-spt-4.0.13/EscapeFromBorderlands_0.31.2_SPT-4.0.13_Upgrade.zip)
- Full ZIP SHA-256: `e2dddab4f0cb046bdb9f3c373fb5f4f06acdfc44288fb208645ddc6241075be2`

Close the game and server, then extract the appropriate ZIP into the SPT game root. Replace both matching DLLs:

- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll`

The full package includes config.json and license/notices. No config.default is shipped. [Read the upgrade instructions](UPGRADING_0.31.2.md).

## Service Window and Holy Hand Grenade

Service Window grants **50% faster medical item use and ammunition loading/unloading while the inventory menu is open**, including the first medical use, surgery kits, painkillers and injectors. It replaces the previous loot-window/Arm Stamina signature. Existing item IDs, storage and random rolls are retained.

The Holy Hand Grenade draw-audio repair is included, retaining its 0.50 default volume and existing grenade balance.

## Protect legendary gear from consuming quest objectives

EFB items cannot be handed in or placed for quests. This includes Gunsmith weapon turn-ins, explicit legendary quest targets, owned storage layouts and retired compatibility aliases. Ordinary containers holding legendary items are protected too, including nested contents.

- Hand-in selection excludes protected copies, even when a native category or exact-template match would normally accept them.
- The final client handover transaction rejects protected selections before any item discard. The server checks the whole request before native processing; a mixed ordinary/legendary selection is rejected as a whole, so an ordinary item listed first is not partially consumed.
- Gunsmith keeps its native receiver and build requirements and rejects protected weapons or weapons holding protected items.
- Placement selection excludes protected items. The completion callback rechecks contents and takes the native cancellation path if a legendary was added during the placement timer. Canceled placement neither removes the item nor advances quest progress.
- Ordinary eligible hand-ins, builds and placements retain native behavior. A legendary elsewhere in inventory does not block handing in an unrelated ordinary item.

Legendary Mosins and other EFB variants still count as their vanilla bases for non-consuming objectives, including weapon kills/hits, equipment requirements and item finding. Native restrictions such as FIR, distance, body part, time, role and attachments remain enforced. Existing use/sale matching and quest markers are retained. This protection is specific to quest hand-ins, Gunsmith turn-ins and placement; it does not change voluntary trader sales or ordinary item use.

## Stat Rewards compatibility

When Stat Rewards is detected, `statRewardsLegendaryChancePercent` controls the chance for each supported randomly generated weapon/equipment reward to become an EFB Legendary of the same vanilla base. Default 1%; range 0–100%. Its normal reward selection runs first, preserving weapon attachments, armor inserts and item state. EFB templates are excluded from its initial random equipment pools. This works without generated EFB default presets and does not alter explicit template rewards or other reward categories. The rate is independent of bot, APBS, airdrop and flea settings.
