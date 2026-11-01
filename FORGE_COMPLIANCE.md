# Forge / SP-Mod compliance — Escape From Borderlands 0.30.0

This candidate updates the **existing** EFB mod for The Forge's current client/server metadata rules. It does **not** add gameplay content.

## Identifiers

- GitHub maintainer and server author: `tKwNw`.
- **Client BepInEx GUID and server ModGuid:** `com.tKwNw.escapefromborderlands`.
- **BepInEx display name:** `tKwNw-EscapeFromBorderlands`.
- **Server metadata name:** `EscapeFromBorderlands`.
- **Client, server, and project versions:** `0.30.0`.
- **Server SPT constraint:** unchanged and limited to this branch's supported SPT version.

SPT 4.1.6 uses its native `IModMetadata` contract, rather than assuming the SPT 4.0.x abstract base class is available. Both satisfy the version/ID fields relevant to the target version.

## Legacy configuration migration

The client/server GUID is now **`com.tKwNw.escapefromborderlands`**. The previous published version used client GUID `com.tkw.escapefromborderlands.client` and server GUID `com.tkw.escapefromborderlands`. The new client imports settings from both older client config names in `BepInEx/config/`, preferring the intermediate `com.tkw.escapefromborderlands.cfg` if present. It merges **only missing section/key pairs**, so user settings already under the new GUID win. This also covers the case in which BepInEx has created an empty destination file before `Awake`.

Migration runs **before any Config.Bind**, and a successful merge calls `Config.Reload()`. The old files remain untouched; any pre-existing destination gets a `.pre-0.30-migration.bak` backup before modification. Migration is idempotent: later runs do not change already imported keys. A migration failure is logged as a warning without blocking the mod.

Server `config/config.json`, 0.29.x item template IDs, and the static Legendary definitions remain unchanged. Harmony patch-owner identifiers remain unchanged intentionally; they are not metadata GUIDs and renaming them can affect optional patches/test assumptions. External mods referencing the old **mod GUID** may need updating. Do not install old and new client/server DLLs simultaneously. Test existing saves and startup after upgrade.

## Build and publication

1. Build against the exact SPT version noted in the matching branch's `BUILDING.md`. Both compiled DLLs must be **rebuilt from this source**; the older 0.29.x binaries are not compliant merely by repacking them.
2. Run `tools/package_forge_release.ps1` after a successful build to generate the installable archive with both DLLs, default config, `LICENSE.txt` and `THIRD_PARTY_NOTICES.md`.
3. Verify DLL assembly and file/product metadata are 0.30.0, client plugin GUID/name/version and server metadata GUID/name/author/version/SPT range. The packaging script checks the DLL assembly versions, but **does not replace** inspection of plugin/server metadata or in-game testing.
4. Validate a fresh SPT instance, including gameplay status effects, grenades, startup, and optional mod integrations. Existing automated test claims in historical reports apply to **previous** builds and must be rerun for this candidate.
5. Publish **new 0.30.0 tags** for each SPT version only after verification. Keep the existing 0.29.x tags immutable. Update VirusTotal links and Forge submissions for the newly published binaries.

## Licensing

The original EFB code is distributed under MIT (see `LICENSE.txt`); third-party Holy Hand Grenade audio remains under the separate Pixabay Content License as documented in `THIRD_PARTY_NOTICES.md`.

## Regression test

Run `dotnet run --project tests/GuidMigration/GuidMigration.csproj`. It exercises the exact production helper and covers 0.29.x first-time migration, BepInEx-created destination files, user overrides, existing backups, intermediate-GUID settings, and repeat starts. Runtime Config.Reload, native gameplay, and optional integrations still require local testing.

## Pending validation

This source change was prepared without the proprietary EFT/SPT reference assemblies. Full .NET compilation and live client/server game execution must be completed locally before publishing it as a ready-to-install release.
