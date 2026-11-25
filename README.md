# Escape From Borderlands (EFB)

**Borderlands-inspired Legendary equipment for SPT.**

EFB adds Legendary versions of vanilla firearms, melee weapons, chest rigs, backpacks, grenades, headsets, headwear and eyewear, each with a signature ability and random bonuses.

## Download and install

Choose the download that matches your installed SPT version.

| SPT version | EFB version | Full install | Upgrade | Release notes |
| --- | --- | --- | --- | --- |
| 4.0.13 | 0.50.0 | [Installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.50.0-spt-4.0.13/EscapeFromBorderlands_0.50.0_SPT-4.0.13.zip) | [Upgrade ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.50.0-spt-4.0.13/EscapeFromBorderlands_0.50.0_SPT-4.0.13_Upgrade.zip) | [What's changed](https://github.com/tKwNw/EscapeFromBorderlands/releases/tag/v0.50.0-spt-4.0.13) |
| 4.1.6 | 0.51.0 | [Installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.51.0-spt-4.1.6/EscapeFromBorderlands_0.51.0_SPT-4.1.6.zip) | [Upgrade ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.51.0-spt-4.1.6/EscapeFromBorderlands_0.51.0_SPT-4.1.6_Upgrade.zip) | [What's changed](https://github.com/tKwNw/EscapeFromBorderlands/releases/tag/v0.51.0-spt-4.1.6) |

Source branches: [SPT 4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.0.13) · [SPT 4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.1.6). Each includes its build instructions.

For a fresh installation, close the game, launcher and server, then extract the installation ZIP into your **SPT game root**. Use the compiled installation ZIP from [Releases](https://github.com/tKwNw/EscapeFromBorderlands/releases).

**Already using EFB?** Follow the [upgrade guide](UPGRADING.md) to update while keeping your settings. The same guide applies to every EFB release.

## Latest changes

- Added **27 Legendary eyewear designs**, each with a signature ability and four per-instance random bonuses.
- Eyewear supports compatible bot combat effects while avoiding checks for player-only actions.
- Grand Vizier fires an independently scattered extra shotgun volley, including additional Legendary projectiles, with a 30% shotgun damage penalty.
- Added Director’s Cut readiness status and corrected Hot Work Permit grenade launch. Semtax no longer attaches to other grenades.
- Retained the previous performance, raid-cleanup, container, grenade-icon, NVG and compatibility improvements.
- Preserved Legendary IDs, existing rolls, settings and integrations. The collection now includes **1,641 Legendary designs**.

## Legendary equipment

Legendary items have a fixed signature ability and 4 compatible random bonuses. Inspect an item to see its effects and rolls. Vanilla items remain available, and Legendary firearms use their normal compatible attachments.

Generated Legendary flea offers hide their 4 random bonuses as `????` until purchase. The signature ability stays visible.

Find Legendaries on configured bot factions, including Scavs, and among supported airdrop and container loot. Spawn chances are configurable by faction and equipment category. Flea market availability can also be enabled in the settings.

Legendary grenades are reusable: each copy has its own charges, bonuses and recharge timer. Empty copies stay in your inventory and are skipped by grenade quick select until a charge returns.

Legendary equipment counts as its vanilla base for objectives such as weapon kills and equipment requirements. Quest hand-ins, Gunsmith turn-ins and placement objectives exclude Legendary items to protect them from accidental loss.

| Category | Legendary designs | Item list |
| --- | ---: | --- |
| Firearms | 1,229 | [Browse](docs/weapons/README.md) |
| Melee weapons | 48 | [Browse](docs/melee/README.md) |
| Vests and chest rigs | 88 | [Browse](docs/vests/README.md) |
| Backpacks | 67 | [Browse](docs/backpacks/README.md) |
| Grenades | 37 | [Browse](docs/grenades/README.md) |
| Headsets and earpieces | 39 | [Browse](docs/headsets/README.md) |
| Headwear | 106 | [Browse](docs/headwear/README.md) |
| Eyewear | 27 | [Browse](docs/eyewear/README.md) |
| **Total** | **1,641** | |

[Browse the complete item catalog](docs/README.md). Both supported SPT versions share this item catalog.

## Settings and compatibility

Use the [configuration guide](docs/configuration.md) to adjust Legendary spawn chances, flea availability, prices, Fence stock, grenade recharge times, draw-sound volume and the status display.

Both SPT versions include compatibility for **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere** and **HollywoodFX**. Eligible custom factions can use EFB spawning through optional **MoreBotsAPI** metadata. These mods are optional and are not bundled with EFB. Both releases also support optional **Stat Rewards** integration with a separate Legendary reward chance.

## Help and credits

Report problems through [GitHub Issues](https://github.com/tKwNw/EscapeFromBorderlands/issues). Include your SPT version, EFB version, installed mods and steps to reproduce the issue.

EFB source code uses the [MIT license](LICENSE.txt). See [third-party notices](THIRD_PARTY_NOTICES.md) for the separately licensed audio. Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.
