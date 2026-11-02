# Updating Escape From Borderlands

Use this guide whenever you update EFB. Download the build that matches your installed **SPT version** from [Releases](https://github.com/tKwNw/EscapeFromBorderlands/releases), and check that release's notes for any additional instructions.

## Before updating

1. Close the game, launcher and SPT server.
2. Back up your profile, EFB server `config/config.json` and EFB client settings in `BepInEx/config/`.
3. Download the matching **Upgrade ZIP** if one is offered. It replaces the client and server DLLs while leaving your configuration in place. Otherwise, use the full installation ZIP and keep a copy of your existing `config.json` so you can restore it after extraction.

Use the compiled installation or upgrade ZIP attached to the release.

## Install the update

Extract the ZIP into your **SPT game root** and replace the existing EFB files. Update both the client and server from the same release.

| File or folder | SPT 4.0.13 | SPT 4.1.6 |
| --- | --- | --- |
| Client DLL | `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll` | Same path |
| Server folder | `SPT/user/mods/EscapeFromBorderlands/` | `SPT_Runtime/user/mods/EscapeFromBorderlands/` |
| Server settings | `SPT/user/mods/EscapeFromBorderlands/config/config.json` | `SPT_Runtime/user/mods/EscapeFromBorderlands/config/config.json` |

If you used the full installation ZIP, restore your saved server `config.json` before starting the server. Keep only 1 installed EFB client DLL and 1 matching server DLL; remove any duplicate copies left in older mod folders.

## Start and review your settings

Start the server, then the game. EFB adds missing server options automatically and keeps your existing values. Older EFB client settings are imported automatically when needed.

Review your [configuration settings](docs/configuration.md), including the status display and grenade options, and keep your backup until you are happy with the update.

If you need to revert, restore the matching previous client/server files, configuration and profile backup together.

This process updates EFB within your existing SPT installation. For a different SPT version, use its matching EFB download and follow that SPT version's profile-migration instructions.

[← EFB home](README.md) · [Release notes and downloads](https://github.com/tKwNw/EscapeFromBorderlands/releases)
