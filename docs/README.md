# Escape From Borderlands — Legendary item catalog

This documentation lists every **named Legendary design** in the EFB **0.31.2** source catalogs for **SPT 4.0.13** (catalog revision **54**). The SPT 4.1.6 port is maintained separately and has not yet received this release.

| Category | Named Legendary designs | Full item list |
| --- | ---: | --- |
| Guns / firearms | 1,229 | [Weapons](weapons/README.md) |
| Melee weapons | 48 | [Melee](melee/README.md) |
| Vests and chest rigs | 88 | [Vests & rigs](vests/README.md) |
| Backpacks | 67 | [Backpacks](backpacks/README.md) |
| Grenades | 37 | [Grenades](grenades/README.md) |
| Headsets / earpieces | 39 | [Headsets](headsets/README.md) |
| **Total** | **1,508** | |

The vests/rigs represent **440 distinct storage-template identities** across 88 designs: [see the complete vest/rig layout registry](vests/LAYOUTS.md). All categories together contain **1,860 raw Legendary template entries**. These totals include the catalog's non-spawning rig mystery/flea identities where indicated; they are not all additional independently spawning designs.

**About the tables:** The listed signature is the fixed special ability; each Legendary item can also roll 4 compatible random bonuses. The 24-character template IDs help identify exact records for troubleshooting. Names, signature effects, and IDs were generated from `src/EscapeFromBorderlands.Server/LegendaryDefinitions.json` (also embedded in the client), rather than inferred from screenshots or memory. The underlying vanilla items remain separate and playable.

[← Project README](../README.md)
