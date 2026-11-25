# Building Escape From Borderlands 0.51.0 — SPT 4.1.6

This branch contains the complete source code and Legendary item definitions for **SPT 4.1.6**. The GitHub Releases page provides compiled, ready-to-install downloads.

## Prerequisites

- Windows PowerShell and the **.NET 10 SDK**.
- Your own installed **SPT 4.1.6** game, including its client, BepInEx, and server reference assemblies.
- Do not copy proprietary EFT/SPT DLLs or optional third-party mod DLLs into this repository.

## Build

From the repository root, run this command using your SPT installation root:

```powershell
.\build.ps1 -GameRoot 'C:\YOUR-SPT-4.1.6-INSTALLATION'
```

The build script produces `output/Client/EscapeFromBorderlands.Client.dll` and `output/Server/EscapeFromBorderlands.Server.dll`. The client targets **.NET Standard 2.1** and the server targets **.NET 10**.

For an installation package, use the SPT-specific paths described in [README.md](README.md), together with `config/config.json`, `LICENSE.txt`, and `THIRD_PARTY_NOTICES.md`.

**Important:** Always build against the correct SPT version. Never reuse binary outputs after switching branches; stale DLLs from another SPT version are not compatible.

The public source contains the release gameplay code and inert compatibility interfaces. Private recorders, capture tools and test evidence are excluded. Upgrade packages omit the default config so existing settings can be retained.
