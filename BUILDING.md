# Build Escape From Borderlands 0.30.0 (SPT 4.1.6)

This branch holds the complete **0.30.0** source for **SPT 4.1.6**, including separate compatibility fixes and regression tests.

## Prerequisites

- Windows PowerShell.
- .NET **10 SDK**.
- A locally installed **SPT 4.1.6** game containing `SPT_Runtime/SPTarkov.Server.Core.dll`, the EFT managed assemblies and BepInEx.

Build from your own installed game. Do **not** upload or redistribute proprietary game, SPT, or optional-mod DLLs.

## Compile

From this branch's root directory, point `-GameRoot` at the directory containing `EscapeFromTarkov.exe`:

```powershell
.\build.ps1 -GameRoot 'C:\SPT 4.1'
```

The script checks the installed native SPT 4.1.6 server assembly, builds server and client, and writes DLL outputs under `output/Server` and `output/Client`.

The compiled client and server must both report version `0.30.0`.

## Validation

The `tests/`, `tools/`, `AUDIT.md`, `PORTING.md`, `UIA_COMPATIBILITY.md`, and `validation/` content from the release source are retained. The included logs and JSON reports are historical validation evidence, **not new test runs performed for this GitHub preparation**.

Read `tests/README.md` for the reference-dependent tests. Optional mods are not bundled.

## Distribute

The **source repository** is not itself an installation ZIP. A fresh-install release should place compiled DLLs and default config at the paths specified in `README.md`, including `SPT_Runtime/user/mods` for server files. Publish that installable ZIP as a separate asset on the matching GitHub Release.

**Validation note:** Source inspection does not replace a fresh compilation against a working SPT 4.1.6 installation or live-game compatibility testing.

## GUID migration regression test

Run `dotnet run --project tests/GuidMigration/GuidMigration.csproj` to exercise client settings migration without EFT/SPT references. This checks only the migration helper; the full client and server must still be compiled and tested against the installed game.
