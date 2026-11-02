# Escape From Borderlands (EFB)

**Borderlands-inspired Legendary equipment for SPT.**

Escape From Borderlands adds distinct Legendary versions of supported vanilla weapons, grenades, melee weapons, backpacks, chest rigs, and earpieces. Vanilla items remain available. Legendary equipment features signature effects and configurable spawn behavior.
## Legendary equipment catalog

Explore **1,508 named Legendary designs** in each supported SPT version. Each item listing includes its Legendary name, signature effect, and template ID.

| Equipment category | Designs | Complete catalog |
| --- | ---: | --- |
| Firearms | **1,229** | [Legendary guns](docs/weapons/README.md) |
| Melee weapons | **48** | [Legendary melee weapons](docs/melee/README.md) |
| Vests & chest rigs | **88** | [Legendary vests and rigs](docs/vests/README.md) |
| Backpacks | **67** | [Legendary backpacks](docs/backpacks/README.md) |
| Grenades | **37** | [Legendary grenades](docs/grenades/README.md) |
| Headsets & earpieces | **39** | [Legendary headsets](docs/headsets/README.md) |

[**Browse the full Legendary catalog**](docs/README.md) · [View all 440 vest/rig storage identities](docs/vests/LAYOUTS.md)

The catalog below documents **EFB 0.31.2 / SPT 4.0.13**, revision **54**. SPT 4.1.6 remains on **EFB 0.30.0**, revision **52**, with the same design counts and its earlier Service Window signature. [Browse the 4.1.6 catalog](https://github.com/tKwNw/EscapeFromBorderlands/blob/spt-4.1.6/docs/README.md). Item tables document fixed signature abilities; Legendary items can also roll additional random modifiers.



## Supported SPT versions and downloads

| SPT version | EFB version | Installation ZIP | Source and item catalogs |
| --- | --- | --- | --- |
| **4.0.13** | **0.31.2** | [Download for SPT 4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.31.2-spt-4.0.13/EscapeFromBorderlands_0.31.2_SPT-4.0.13.zip) | [spt-4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.0.13) |
| **4.1.6** | **0.30.0** | [Download for SPT 4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.30.0-spt-4.1.6/EscapeFromBorderlands_0.30.0_SPT-4.1.6.zip) | [spt-4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.1.6) |

The [Releases page](https://github.com/tKwNw/EscapeFromBorderlands/releases) contains the compiled installation archives. Each branch contains the source, default configuration, and [Legendary item catalog](docs/README.md) for its supported SPT API version. Do not install GitHub's automatically generated source-code archive in place of the compiled ZIP.

## Changes in EFB 0.31.2 — SPT 4.0.13

- **Service Window:** 50% faster medical item use and ammunition loading/unloading while the inventory menu is open, including the first medical use, surgery kits, painkillers and injectors.
- **Holy Hand Grenade:** draw-sound recovery across raids and delayed audio initialization; default volume remains 0.50.
- **Optional Stat Rewards compatibility:** independent `statRewardsLegendaryChancePercent` setting, default 1%, range 0–100%, per supported random equipment reward.

[Read the complete 4.0.13 release notes](https://github.com/tKwNw/EscapeFromBorderlands/blob/spt-4.0.13/RELEASE_NOTES_0.31.2.md). The 4.1.6 port will follow separately and retain its existing compatibility fixes.

## Download verification and VirusTotal

These SHA-256 hashes have been verified against the **published GitHub Release assets** (not merely the source archives):

| SPT version | Installation ZIP SHA-256 | VirusTotal |
| --- | --- | --- |
| **4.0.13** | `e2dddab4f0cb046bdb9f3c373fb5f4f06acdfc44288fb208645ddc6241075be2` | [Look up this hash](https://www.virustotal.com/gui/file/e2dddab4f0cb046bdb9f3c373fb5f4f06acdfc44288fb208645ddc6241075be2) |
| **4.1.6** | `84d271146280ee52c20576c6f1e40173749f12bce81e27e4bf1313a35ab35b0e` | [VirusTotal report](https://www.virustotal.com/gui/file/84d271146280ee52c20576c6f1e40173749f12bce81e27e4bf1313a35ab35b0e/detection) |

Each report link identifies the exact SHA-256 hash of the corresponding published installation ZIP. Detection counts and the current verdict should be reviewed on VirusTotal; matching the hash confirms file identity, not that the file is malware-free. VirusTotal results do not guarantee safety.

## Installation

Close the game, launcher, and SPT server. Back up your profile and existing EFB settings before extracting the installation ZIP into your **SPT game root** (the main installation folder).

**SPT 4.0.13**
- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/`

**SPT 4.1.6**
- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT_Runtime/user/mods/EscapeFromBorderlands/`

The 0.30.0 release migrates older BepInEx settings to the updated plugin GUID. See each version's `UPGRADING_0.30.0.md` for precautions and migration details.

## Building from source

Select the branch for your SPT version and follow its `BUILDING.md`. Building requires the appropriate .NET SDK and assemblies from your own installed game. Proprietary game and SPT binaries and optional-mod dependencies are not bundled.

## Optional compatibility

Both version include compatibility work for **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere**, and **HollywoodFX**. These are optional and not included in the downloads; results may depend on the particular versions installed.

## Issues and licensing

When reporting an issue, include your **SPT version**, **EFB version**, installed mods, steps to reproduce, and relevant log excerpts. Do not post private account details or tokens.

EFB source code is provided under the [MIT license](LICENSE.txt); see [third-party notices](THIRD_PARTY_NOTICES.md) for the separately licensed audio asset. Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.
