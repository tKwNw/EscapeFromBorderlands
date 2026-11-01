# Build Escape From Borderlands 0.30.0 (SPT 4.0.13)

This branch holds the complete **0.30.0** source for **SPT 4.0.13**.

## Prerequisites

- Windows PowerShell.
- .NET SDK with .NET 9 targeting support (server targets `net9.0`; client targets `netstandard2.1`).
- A locally installed **SPT 4.0.13** game with the server and BepInEx/client reference assemblies.

Do **not** commit or redistribute proprietary SPT/Escape from Tarkov assemblies. These are referenced from your own installed game. The project also restores the `SemanticVersioning` NuGet dependency when building the server.

## Compile

From this branch's root directory, using a path to your SPT installation:

```powershell
.\build.ps1 -GameRoot 'C:\SPT MODDED'
```

The script builds both projects from `src/` and places outputs beneath `output/Server` and `output/Client`.

The client and server DLLs must both report version `0.30.0`. Do not substitute 4.1.6 references.

## Distribute

The **source repository** is not itself an installation ZIP. Build and package the two DLLs and default configuration under the correct SPT 4.0.13 installation folder structure, and attach that *installable* ZIP separately to the matching GitHub Release.

See `README.md` for the expected client/server destinations.

**Validation note:** Source inspection does not replace compiling against an installed SPT 4.0.13 game and verifying behavior in game.

## GUID migration regression test

Run `dotnet run --project tests/GuidMigration/GuidMigration.csproj` to exercise client settings migration without EFT/SPT references. This checks only the migration helper; the full client and server must still be compiled and tested against the installed game.
