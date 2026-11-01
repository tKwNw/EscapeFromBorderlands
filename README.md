# Escape From Borderlands (EFB)

**Borderlands-inspired Legendary equipment for Single Player Tarkov (SPT).**

Escape From Borderlands adds distinct Legendary versions of supported vanilla EFT guns, grenades, melee weapons, backpacks, chest rigs, and earpieces. Vanilla items remain available. Legendary equipment features signature effects and configurable spawn behavior.

## Supported SPT versions and downloads

| SPT version | EFB version | Installation ZIP | Source and item catalogs |
| --- | --- | --- | --- |
| **4.0.13** | **0.30.0** | [Download for SPT 4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.30.0-spt-4.0.13/EscapeFromBorderlands_0.30.0_SPT-4.0.13.zip) | [spt-4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.0.13) |
| **4.1.6** | **0.30.0** | [Download for SPT 4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/releases/download/v0.30.0-spt-4.1.6/EscapeFromBorderlands_0.30.0_SPT-4.1.6.zip) | [spt-4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.1.6) |

The [Releases page](https://github.com/tKwNw/EscapeFromBorderlands/releases) contains the compiled installation archives. Each branch contains the source, default configuration, and [Legendary item catalog](https://github.com/tKwNw/EscapeFromBorderlands/blob/spt-4.1.6/docs/README.md) for its supported SPT API version. Do not install GitHub's automatically generated source-code archive in place of the compiled ZIP.

## Download verification and VirusTotal

These SHA-256 hashes have been verified against the **published GitHub Release assets** (not merely the source archives):

| SPT version | Installation ZIP SHA-256 | VirusTotal |
| --- | --- | --- |
| **4.0.13** | `cef55996b1097cfa33f5882a16bbf0807ac3bcbf24507a96280eb88a742cde8b` | [VirusTotal report](https://www.virustotal.com/gui/file/cef55996b1097cfa33f5882a16bbf0807ac3bcbf24507a96280eb88a742cde8b) |
| **4.1.6** | `84d271146280ee52c20576c6f1e40173749f12bce81e27e4bf1313a35ab35b0e` | [VirusTotal report](https://www.virustotal.com/gui/file/84d271146280ee52c20576c6f1e40173749f12bce81e27e4bf1313a35ab35b0e/detection) |

Each report link identifies the exact SHA-256 hash of the corresponding published installation ZIP. Detection counts and the current verdict should be reviewed on VirusTotal; matching the hash confirms file identity, not that the file is malware-free. VirusTotal results do not guarantee safety.

## Installation

Close the game, launcher, and SPT server. Back up your profile and existing EFB settings before extracting the installation ZIP into your **SPT game root** (the folder containing `EscapeFromTarkov.exe`).

**SPT 4.0.13**
- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/`

**SPT 4.1.6**
- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT_Runtime/user/mods/EscapeFromBorderlands/`

The 0.30.0 release migrates older BepInEx settings to the updated plugin GUID. See each version's `UPGRADING_0.30.0.md` for precautions and migration details.

## Building from source

Select the branch for your SPT version and follow its `BUILDING.md`. Building requires the appropriate .NET SDK and assemblies from your own installed game. Proprietary EFT/SPT binaries and optional-mod dependencies are not bundled.

## Optional compatibility

The 4.1.6 version includes compatibility work for **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere**, and **HollywoodFX**. These are optional and not included in the downloads; results may depend on the particular versions installed.

## Issues and licensing

When reporting an issue, include your **SPT version**, **EFB version**, installed mods, steps to reproduce, and relevant log excerpts. Do not post private account details or tokens.

EFB source code is provided under the [MIT license](LICENSE.txt); see [third-party notices](THIRD_PARTY_NOTICES.md) for the separately licensed audio asset. Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.
