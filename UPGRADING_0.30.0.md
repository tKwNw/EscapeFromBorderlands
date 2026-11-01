# Upgrade to Escape From Borderlands 0.30.0 — SPT 4.0.13

This guide is for upgrading an existing Escape From Borderlands installation to the published **0.30.0** release. Back up your profile and configuration before installing.

## What changes

- Client BepInEx GUID and server mod GUID: `com.tKwNw.escapefromborderlands`.
- Display name: `tKwNw-EscapeFromBorderlands`; server author: `tKwNw`.
- Both DLL and project version declarations: `0.30.0`.
- Automatic migration of BepInEx client settings from `com.tkw.escapefromborderlands.client.cfg`, plus optional intermediate `com.tkw.escapefromborderlands.cfg`.
- No planned changes to Legendary catalog template IDs, loot settings or gameplay mechanics. Old server `config/config.json` remains usable.

## Before upgrading

1. Close the SPT game, server, and launcher.
2. Back up your **SPT profile**, existing server `config/config.json`, and `BepInEx/config/com.tkw.escapefromborderlands.client.cfg`.
3. If a candidate using the intermediate GUID was installed, also back up `BepInEx/config/com.tkw.escapefromborderlands.cfg`.
4. Remove/replace the old EFB client and server DLLs. Do **not** leave duplicate EFB DLLs in different mod folders.
5. Install the **compiled 0.30.0 installation ZIP** for **SPT 4.0.13** from GitHub Releases. Do not extract the source repository ZIP as a game installation.

## First launch

- The new BepInEx config file will be `BepInEx/config/com.tKwNw.escapefromborderlands.cfg`.
- Missing settings are imported from legacy files before EFB binds HUD and grenade options.
- Values already present in the new config take priority; the old files are never deleted.
- A pre-existing target config receives a `.pre-0.30-migration.bak` file before changes.
- Look for `Migrated ... previous Escape From Borderlands client config entries` in the BepInEx log.
- Inspect HUD position/size, charge display, and grenade draw-volume options after launch.
- Verify preexisting Legendary items appear and work in an **existing backed-up profile**.

## Reverting

Restore the backed-up profile/server config and original 0.29.x DLLs. The migration preserves the old client `.cfg`, so restoring the original DLL also restores its previous settings. Keep a full backup if a later version writes to the saved profile.
