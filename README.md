# Escape From Borderlands 0.29.1 — SPT 4.1.6

Native port of upstream EFB 0.29.0 / SPT 4.0.13, merged into corrected native 0.28.2 using legacy 0.28.0 as the common ancestor. All separately added native fixes and compatibility integrations carry forward. Both DLLs declare release version 0.29.1; CLR assembly versions are 0.29.1.0 and file/product metadata use 0.29.1.

## New in this update

- One shared airdropLegendaryChancePercent setting, default 1, range 0–100. Each supported generated item receives its own chance to become a current Legendary variant of the same vanilla base. The setting covers all supported categories and normal/custom/forced drop types, independently of bot/APBS/flea rates.
- Airdrops retain native generation/fitting, unsupported loot, item IDs, attachments, quantity/state and Found in Raid flags. Legacy/mystery EFB pool entries are filtered or normalized. Unusual stacks remain native rather than becoming invalid max-stack-1 clones. Existing configs gain the missing setting while preserving explicit values and unrelated settings.
- Conditional status rows now follow actual gameplay gates, including ADS, prone, movement, environment, readiness, pending charges and cooldowns. Permanent effects remain hidden. Successful instant procs receive a brief visual receipt rather than an invented gameplay duration.
- Carried, non-equipped legendary grenades display partial charge progress. Held grenades retain their charge display; empty stowed counts and full stowed copies are hidden according to the upstream status rules.
- Catalog revision 52 is unchanged: 1,860 raw entries, 1,508 named designs, 3,133 expanded active identities and 777 retired aliases. Existing rolls, layouts and saved IDs are retained.

## Included fixes and compatibility

Use Items Anywhere 2.1.6 remains optional. Its configured grenade/player reload slots, separate bot rules, wheel availability, explicit patch ordering, signature validation and clean native fallback remain included. HollywoodFX remains optional with explosion scaling/deferred dispatch and normal pooled reuse. APBS detection and final-load integration remain included.

Native inspection/backpack presentation, recoil/night vision, typed medical callbacks, grenade packing, quest equivalence and protection against destructive hand-ins/placement are retained. The new status readers use the repaired native night-vision and medical interruption paths. See PORTING.md for the required carry-forward policy and UIA_COMPATIBILITY.md for optional integration details.

All prior features remain: Curtain Call, 9-Banger, slug-aware Elephant Gun, finalized headset stats/audio, 7-column backpack display wrapping with old capacity/layout preservation, 0.50 default embedded Holy Hand Grenade draw volume, shared non-Scav/boss spawning and economy controls. The separate EFB Classes mod is not bundled.

## Fresh installation

Close the game and server. Extract Runtime into the SPT 4.1.6 root containing EscapeFromTarkov.exe. The archive contains exactly:

- BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll
- SPT_Runtime/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll
- SPT_Runtime/user/mods/EscapeFromBorderlands/config/config.json

Runtime includes fresh defaults. Preserve your custom config if replacing an existing installation. No loose audio, config.default, third-party mod DLL or SDK is required from this package.

SPT 4.1.6 is required. Use Items Anywhere, APBS and HollywoodFX are optional and not redistributed. Fresh bot rates remain 1% per eligible category and 10% for bosses; Scavs and flea trading remain excluded by default. The airdrop setting is separate from those rates. Restart the server after changing server config.

## Source and build

Source includes production code, default config, embedded audio/provenance, build scripts, regression suites, carry-forward policy and current validation evidence. It excludes game/reference/optional DLLs, SDKs, binaries and caches. Install the .NET 10 SDK and build against the actual patched 4.1.6 installation:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -GameRoot "C:\SPT 4.1"
```

The client targets netstandard2.1 with a hard com.SPT.core 4.1.6 dependency. The server targets net10.0 and native table/IModMetadata contracts.

## Verification and limits

Both production builds have 0 warnings/errors. All 942 behavior/service cases pass across 19 suites. The complete runner has 26 jobs, including semantic-version checks and native audits with UIA present/absent. Carry-forward, status inventory, native method/API, reflection, layout, audio and preservation checks pass. Exact tested input hashes and detailed scope are included in validation/ and AUDIT.md.

No live Windows EFT/Unity session, complete SPT host startup, real UIA gameplay, fully modded APBS or Fika session was run. Airdrop tests invoke patched native services with controlled generation/fitting boundaries; they do not exercise an in-raid drop. Native IL composition models UIA's collector substitution using the actual supplied binary metadata. Installed-game UI/audio/rendering/physics/animation and airdrop confirmation remain outstanding. Fika charge synchronization/reconnect restoration remain upstream limitations.
