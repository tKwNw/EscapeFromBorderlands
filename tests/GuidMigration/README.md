# EFB GUID migration regression test

This small standalone console project tests the **actual production migration helper** without proprietary EFT/SPT assemblies.

Requirements: .NET SDK with net9.0 targeting support. In this branch root, run:

```powershell
dotnet run --project tests/GuidMigration/GuidMigration.csproj
```

Cases include first-run import, pre-created new-GUID config with conflicting values, older and intermediate file precedence, backup creation, and idempotency. This does **not** validate BepInEx's own `Config.Reload()` implementation or full game startup; verify those in a real client.
