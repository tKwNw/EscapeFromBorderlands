# Escape From Borderlands (EFB)

**Borderlands-inspired Legendary equipment for Single Player Tushonka (SPT).**

Escape From Borderlands adds distinct Legendary versions of supported vanilla EFT weapons, grenades, melee weapons, backpacks, chest rigs, and earpieces. Vanilla items remain available. Legendary equipment features its own special effects and configurable spawn behavior.

## Supported SPT versions

| SPT version | EFB version | Source branch |
| --- | --- | --- |
| **4.0.13** | **0.30.0** | [spt-4.0.13](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.0.13) |
| **4.1.6** | **0.30.0** | [spt-4.1.6](https://github.com/tKwNw/EscapeFromBorderlands/tree/spt-4.1.6) |

Each branch contains the source for its own SPT API version. Fixes and API adaptations for 4.1.6 are not automatically interchangeable with 4.0.13.

## Downloads and installation

**Compiled installation packages will be provided on the [Releases page](https://github.com/tKwNw/EscapeFromBorderlands/releases).** The source branches are intended for developers; cloning a branch does not install the mod.

When releases are published, select the installation archive matching **your SPT version** and extract it into your SPT game root. Back up your existing EFB configuration when upgrading.

For SPT 4.0.13:
- Client plugin: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server mod: `SPT/user/mods/EscapeFromBorderlands/`

For SPT 4.1.6:
- Client plugin: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server mod: `SPT_Runtime/user/mods/EscapeFromBorderlands/`

## Building from source

Open the branch for your SPT version and follow its `BUILDING.md` instructions. Building requires the correct .NET SDK and assemblies from your own installed SPT game. Proprietary game binaries and optional-mod dependencies are not provided here.

## Optional compatibility

The 4.1.6 source documents compatibility work involving **Acid's Progressive Bot System (APBS)**, **Use Items Anywhere**, and **HollywoodFX**. Their files are not included, and compatibility may depend on the installed versions. See the 4.1.6 branch's `UIA_COMPATIBILITY.md` and `PORTING.md` for implementation details.

## Releases and issue reports

Please report issues with the **SPT version**, **EFB version**, other installed mods, steps to reproduce, and relevant logs. Never post private account data or tokens.

Escape From Borderlands is a community-made SPT mod and is not affiliated with Battlestate Games or Gearbox Software.
