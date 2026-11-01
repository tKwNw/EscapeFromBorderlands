# EFB 0.30.0 — SPT 4.0.13 (Release candidate notes)

This release aligns **Escape From Borderlands** client and server metadata with The Forge's public submission rules.

## Changes
- Shared client/server GUID `com.tKwNw.escapefromborderlands` (full GitHub username).
- Compliant BepInEx display name `tKwNw-EscapeFromBorderlands`.
- Same semantic version `0.30.0` across client, server and project declarations.
- Automatic safe migration of legacy BepInEx settings; pre-existing new settings always take precedence.
- Added standalone migration regression tests, a source metadata check and licensing information for compiled archives.
- No intentional changes to game effects, item identities, templates, spawn defaults or the previous version's optional integration code.

## Installation

Use the compiled **SPT 4.0.13** installation ZIP after it has been built and tested, not the source code ZIP. Make a backup of existing profiles, client settings and server config first. See `UPGRADING_0.30.0.md`.

**Validation pending:** Full DLL rebuild using SPT 4.0.13 references, client/server startup, first-run migration, existing save files and optional integration checks. Do not publish this release candidate as a finished mod until those tests pass.
