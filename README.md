# Escape From Borderlands (EFB)

**Borderlands-inspired Legendary equipment for SPT.**

EFB adds Legendary versions of vanilla firearms, melee weapons, chest rigs, backpacks, grenades and headsets, each with a signature ability and random bonuses.

## Download and install

Choose the download that matches your installed SPT version.

| SPT version | EFB version | Download | Release notes |
| --- | --- | --- | --- |
| 4.0.13 | 0.31.7 | [Installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.7-spt-4.0.13/EscapeFromBorderlands_0.31.7_SPT-4.0.13.zip) · [Upgrade](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.7-spt-4.0.13/EscapeFromBorderlands_0.31.7_SPT-4.0.13_Upgrade.zip) | [What's changed](https://github.com/tKwNw/EscapeFromBorderlands/releases/tag/v0.31.7-spt-4.0.13) |
| 4.1.6 | 0.31.8 | [Installation ZIP](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.8-spt-4.1.6/EscapeFromBorderlands_0.31.8_SPT-4.1.6.zip) · [Upgrade](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.8-spt-4.1.6/EscapeFromBorderlands_0.31.8_SPT-4.1.6_Upgrade.zip) | [What's changed](https://github.com/tKwNw/EscapeFromBorderlands/releases/tag/v0.31.8-spt-4.1.6) |

Source branches: [SPT 4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.0.13) · [SPT 4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.1.6). Each includes its build instructions.

For a fresh installation, close the game, launcher and server, then extract the installation ZIP into your **SPT game root**. Use the compiled installation ZIP from [Releases](https://github.com/tKwNw/EscapeFromBorderlands/releases).

**Already using EFB?** Follow the [upgrade guide](UPGRADING.md) to update while keeping your settings. The same guide applies to every EFB release.

## Latest changes

- Extended `otherNonScavs` spawning to eligible custom factions registered through MoreBotsAPI, using the existing equipment-category chances with or without APBS.
- Reduced weapon-granted impact-explosion damage by **60 percentage points**, to a minimum of **10%**. RShG-2 effects and the Pocket Rocket rig ability are unchanged.
- Reduced repeated grenade, headset, audio and projectile maintenance, improved stale-player cleanup and made raid resets more resilient.

## Legendary equipment

Legendary items have a fixed signature ability and 4 compatible random bonuses. Inspect an item to see its effects and rolls. Vanilla items remain available, and Legendary firearms use their normal compatible attachments.

Generated Legendary flea offers hide their 4 random bonuses as `????` until purchase. The signature ability stays visible.

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

[Browse the complete item catalog](docs/README.md). Both supported SPT versions share this item catalog.

## Settings and compatibility

Use the [configuration guide](docs/configuration.md) to adjust Legendary spawn chances, flea availability, prices, Fence stock, grenade recharge times, draw-sound volume and the status display.

Both SPT versions include compatibility for **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere** and **HollywoodFX**. Eligible custom factions can use EFB spawning through optional **MoreBotsAPI** metadata. These mods are optional and are not bundled with EFB. Both releases also support optional **Stat Rewards** integration with a separate Legendary reward chance.

## Help and credits

Report problems through [GitHub Issues](https://github.com/tKwNw/EscapeFromBorderlands/issues). Include your SPT version, EFB version, installed mods and steps to reproduce the issue.

EFB source code uses the [MIT license](LICENSE.txt). See [third-party notices](THIRD_PARTY_NOTICES.md) for the separately licensed audio. Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.

