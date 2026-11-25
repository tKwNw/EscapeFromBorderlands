# Escape From Borderlands — SPT 4.0.13

**EFB 0.50.0 · Borderlands-inspired Legendary equipment for SPT.**

## Download and install

[Download the installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.50.0-spt-4.0.13/EscapeFromBorderlands_0.50.0_SPT-4.0.13.zip) · [Upgrade ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.50.0-spt-4.0.13/EscapeFromBorderlands_0.50.0_SPT-4.0.13_Upgrade.zip) · [Release notes](https://github.com/tKwNw/EscapeFromBorderlands/releases/tag/v0.50.0-spt-4.0.13)

For a fresh installation, close the game, launcher and server, then extract the installation ZIP into your **SPT game root**. The package installs:

- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/`

**Updating an existing installation?** Use the Upgrade ZIP to keep your existing server and client settings. The full installation ZIP includes a default server config. Follow the [shared upgrade guide](https://github.com/tKwNw/EscapeFromBorderlands/blob/main/UPGRADING.md) to keep your settings. [Other supported SPT versions](https://github.com/tKwNw/EscapeFromBorderlands#download-and-install).

## Latest changes

- Added **27 Legendary eyewear designs**, each with a signature ability and four per-instance random bonuses.
- Eyewear supports compatible bot combat effects while avoiding checks for player-only actions.
- Grand Vizier fires an independently scattered extra shotgun volley, including additional Legendary projectiles, with a 30% shotgun damage penalty.
- Added Director’s Cut readiness status and corrected Hot Work Permit grenade launch. Semtax no longer attaches to other grenades.
- Retained the previous performance, raid-cleanup, container, grenade-icon, NVG and compatibility improvements.
- Preserved Legendary IDs, existing rolls, settings and integrations. The collection now includes **1,641 Legendary designs**.

## Legendary equipment

Legendary items have a fixed signature ability and 4 compatible random bonuses. Inspect an item to see its effects and rolls. Vanilla items remain available, and Legendary firearms use their normal compatible attachments.

Generated Legendary flea offers show their signature ability and `????` for each of the 4 random bonuses. Each purchased copy reveals its own bonuses; existing owned items keep their rolls.

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

[Browse the complete item catalog](docs/README.md) for the Legendary names and signature effects in this SPT version.

## Settings and compatibility

The [configuration guide](docs/configuration.md) covers spawn chances, flea availability, prices, Fence stock, grenade recharge times, draw-sound volume and the status display.

Both SPT versions include compatibility for **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere** and **HollywoodFX**. Eligible custom factions can use EFB spawning through optional **MoreBotsAPI** metadata. These mods are optional and are not bundled with EFB. Both releases also support optional **Stat Rewards** integration with a separate Legendary reward chance.

## Building from source

See [build instructions](BUILDING.md) for this SPT branch.

## Help and credits

Report problems through [GitHub Issues](https://github.com/tKwNw/EscapeFromBorderlands/issues). Include your SPT version, EFB version, installed mods and steps to reproduce the issue.

EFB source code uses the [MIT license](LICENSE.txt). See [third-party notices](THIRD_PARTY_NOTICES.md) for the separately licensed audio. Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.
