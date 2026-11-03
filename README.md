# Escape From Borderlands — SPT 4.0.13

**EFB 0.31.5 · Borderlands-inspired Legendary equipment for SPT.**

## Download and install

[Download the installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.5-spt-4.0.13/EscapeFromBorderlands_0.31.5_SPT-4.0.13.zip) · [Upgrade ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.5-spt-4.0.13/EscapeFromBorderlands_0.31.5_SPT-4.0.13_Upgrade.zip) · [Release notes](https://github.com/tKwNw/EscapeFromBorderlands/releases/tag/v0.31.5-spt-4.0.13)

For a fresh installation, close the game, launcher and server, then extract the installation ZIP into your **SPT game root**. The package installs:

- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/`

**Updating an existing installation?** Follow the [shared upgrade guide](https://github.com/tKwNw/EscapeFromBorderlands/blob/main/UPGRADING.md) to keep your settings. [Other supported SPT versions](https://github.com/tKwNw/EscapeFromBorderlands#download-and-install).

## Latest changes

- Improved equipment effect updates and cleanup between raids to avoid repeated player-state errors.
- Added `allowFenceToSellEfbItems`, default **false**. Fence excludes EFB stock by default; players can still sell EFB items to him. Enable it to permit eligible EFB offers under his normal stocking rules.
- Fixed a text-material error that could stop the effect status display. The display now waits for usable text assets and retries after a display failure.

## Legendary equipment

Legendary items have a fixed signature ability and 4 compatible random bonuses. Inspect an item to see its effects and rolls. Vanilla items remain available, and Legendary firearms use their normal compatible attachments.

Generated Legendary flea offers show their signature ability and `????` for each of the 4 random bonuses. Each purchased copy reveals its own bonuses; existing owned items keep their rolls.

Find Legendaries on PMCs, bosses and other non-Scav factions, or among supported airdrop loot. Spawn chances are configurable by faction and equipment category. Flea market availability can also be enabled in the settings.

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
| **Total** | **1,508** | |

[Browse the complete item catalog](docs/README.md) for the Legendary names and signature effects in this SPT version.

## Settings and compatibility

The [configuration guide](docs/configuration.md) covers spawn chances, flea availability, prices, Fence stock, grenade recharge times, draw-sound volume and the status display.

Both SPT versions include compatibility for **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere** and **HollywoodFX**. These mods are optional and are not bundled with EFB. The SPT 4.0.13 release also supports optional **Stat Rewards** integration with a separate Legendary reward chance.

## Building from source

See [build instructions](BUILDING.md) for this SPT branch.

## Help and credits

Report problems through [GitHub Issues](https://github.com/tKwNw/EscapeFromBorderlands/issues). Include your SPT version, EFB version, installed mods and steps to reproduce the issue.

EFB source code uses the [MIT license](LICENSE.txt). See [third-party notices](THIRD_PARTY_NOTICES.md) for the separately licensed audio. Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.
