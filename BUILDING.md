# Building Escape From Borderlands 0.30.0 — SPT 4.0.13

This branch contains the complete source code and Legendary item definitions for **SPT 4.0.13**. The GitHub Releases page provides compiled, ready-to-install downloads.

## Prerequisites

- Windows PowerShell and a .NET SDK with **.NET 9 targeting support**.
- Your own installed **SPT 4.0.13** game, including its client, BepInEx, and server reference assemblies.
- Do not copy proprietary EFT/SPT DLLs or optional third-party mod DLLs into this repository.

## Build

From the repository root, run this command using the folder that contains `EscapeFromTarkov.exe`:

```powershell
.\build.ps1 -GameRoot 'C:\YOUR-SPT-4.0.13-INSTALLATION'
```

The build script produces `output/Client/EscapeFromBorderlands.Client.dll` and `output/Server/EscapeFromBorderlands.Server.dll`. The client targets **.NET Standard 2.1** and the server targets **.NET 9**.

For an installation package, use the SPT-specific paths described in [README.md](README.md), together with `config/config.json`, `LICENSE.txt`, and `THIRD_PARTY_NOTICES.md`.

**Important:** Always build against the correct SPT version. Never reuse binary outputs after switching branches; stale DLLs from another SPT version are not compatible.

The production source and build scripts are included. Historical test suites and internal validation tools are preserved in Git history rather than tracked at the current branch head.
