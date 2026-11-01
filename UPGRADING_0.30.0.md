# Upgrade to Escape From Borderlands 0.30.0 — SPT 4.1.6

This is a **candidate source release** pending native compilation and live SPT testing. Do not replace a working 0.29.x public release until the new binaries have been verified.

## What changes

- Client BepInEx GUID and server mod GUID: `com.tKwNw.escapefromborderlands`.
- Display name: `tKwNw-EscapeFromBorderlands`; server author: `tKwNw`.
- Both DLL and project version declarations: `0.30.0`.
- Automatic migration of BepInEx client settings from `com.tkw.escapefromborderlands.client.cfg`, plus optional intermediate `com.tkw.escapefromborderlands.cfg`.
- No planned changes to Legendary catalog template IDs, loot settings or gameplay mechanics. Old server `config/config.json` remains usable.

## Before upgrading

1. Close Tarkov, SPT server and the launcher.
2. Back up your **SPT profile**, existing server `config/config.json`, and `BepInEx/config/com.tkw.escapefromborderlands.client.cfg`.
3. If a candidate using the intermediate GUID was installed, also back up `BepInEx/config/com.tkw.escapefromborderlands.cfg`.
4. Remove/replace the old EFB client and server DLLs. Do **not** leave duplicate EFB DLLs in different mod folders.
5. Install the **compiled** and tested 0.30.0 archive for **SPT 4.1.6** when available. Do not extract this source archive as a game installation.

## First launch

- The new BepInEx config file will be `BepInEx/config/com.tKwNw.escapefromborderlands.cfg`.
- Missing settings are imported from legacy files before EFB binds HUD and grenade options.
- Values already present in the new config take priority; the old files are never deleted.
- A pre-existing target config receives a `.pre-0.30-migration.bak` file before changes.
- Look for `Migrated ... previous Escape From Borderlands client config entries` in the BepInEx log.
- Inspect HUD position/size, charge display, and grenade draw-volume options after launch.
- Verify preexisting Legendary items appear and work in an **existing backed-up profile**.

## Developer checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify_forge_identity.ps1 -SptVersion '4.1.6'
dotnet run --project .\tests\GuidMigration\GuidMigration.csproj
.\build.ps1 -GameRoot 'C:\YOUR SPT INSTALLATION'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\package_forge_release.ps1
```

The standalone migration test covers file semantics but does not replace loading both DLLs into a real SPT installation. Test startup, existing profile, APBS and Use Items Anywhere (when installed), and BepInEx settings migration before publishing.

## Reverting

Restore the backed-up profile/server config and original 0.29.x DLLs. The migration preserves the old client `.cfg`, so restoring the original DLL also restores its previous settings. Keep a full backup if a later version writes to the saved profile.
