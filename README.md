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

## Install

- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll`

## Protect legendary gear from consuming quest objectives

EFB items cannot be handed in or placed for quests. This includes Gunsmith weapon turn-ins, explicit legendary quest targets, owned storage layouts and retired compatibility aliases. Ordinary containers holding legendary items are protected too, including nested contents.

- Hand-in selection excludes protected copies, even when a native category or exact-template match would normally accept them.
- The final client handover transaction rejects protected selections before any item discard. The server checks the whole request before native processing; a mixed ordinary/legendary selection is rejected as a whole, so an ordinary item listed first is not partially consumed.
- Gunsmith keeps its native receiver and build requirements and rejects protected weapons or weapons holding protected items.
- Placement selection excludes protected items. The completion callback rechecks contents and takes the native cancellation path if a legendary was added during the placement timer. Canceled placement neither removes the item nor advances quest progress.
- Ordinary eligible hand-ins, builds and placements retain native behavior. A legendary elsewhere in inventory does not block handing in an unrelated ordinary item.

Legendary Mosins and other EFB variants still count as their vanilla bases for non-consuming objectives, including weapon kills/hits, equipment requirements and item finding. Native restrictions such as FIR, distance, body part, time, role and attachments remain enforced. Existing use/sale matching and quest markers are retained. This protection is specific to quest hand-ins, Gunsmith turn-ins and placement; it does not change voluntary trader sales or ordinary item use.
