# EFB 0.29.1 / SPT 4.1.6 audit

## Baselines and merge

Upstream EFB 0.29.0 / SPT 4.0.13 Source and Validation archive SHA-256 values/sizes match its published manifest. The native base is corrected EFB 0.28.2 / SPT 4.1.6; legacy 0.28.0 is the common ancestor. This preserves the separately added compatibility/repair layers instead of recreating the native branch from upstream. Input hashes and source migration deltas are included in validation/.

Current catalogs, configuration defaults, effect/rule logic and immutable resources adopt the upstream release. All previous raw current/retired IDs remain present. The 1,340 expanded backpack layouts and all 48 saved 8-column layouts remain supported. The feature change adds shared airdrop configuration and adopts the approved status HUD/partial grenade charge behavior.

## Native adaptations

| Upstream API/path | SPT 4.1.6 adaptation |
| --- | --- |
| Services.AirdropService | Services.InRaid.AirdropService |
| RarityMod databaseService.GetItems | Injected TemplateTable.Items |
| Legacy server metadata/base contracts | Native IModMetadata and IOnLoad.OnLoadAsync(CancellationToken) |
| New status reader of night-vision IsOn | Existing repaired BackpackPresentation.NightVisionOn helper using Togglable.On |
| New medical-use status Bool_2 | Native MedEffect._interrupted |
| New SilencerItemClass check | Native Silencer type |
| Version declarations | 0.29.1 across plugin/server/project/file/product metadata; CLR representation 0.29.1.0 |

Native IL confirms config filtering runs before both random and forced loot generation, custom airdrops forward through the shared generator, and fitting reads final item size before placement/map fill. The native fitting algorithm itself remains unchanged. EFB changes the current root template before fitting and keeps the graph IDs, attachments/state and FIR flags. The shared percentage is independent of bot backend/category/flea rates.

## Required carried fixes

The native UIA adapter, backpack inventory composition, HollywoodFX/native explosion adapters, APBS/earpiece compatibility, inspection fallback, recoil/night-vision helper, grenade packing and client/server/shared quest protections retain exact previous native bytes. The last-priority quick-select filter remains intact. The typed medical rules retain their non-presentation tokens while adding a successful-surgery status receipt. The new status paths explicitly use the native night-vision and interruption repairs. All 17 carry-forward checks pass. PORTING.md records this as a requirement for future updates.

## Completed verification

- Client/server production builds: 0 warnings and 0 errors.
- 942 behavior/service cases across 19 suites: fixes 80, combat 43, fireball 37, storage 54, layout 7, navigation 29, visitor 18, rigs 140, grenades 199, spawns/config/airdrops 52, quests 23, earpieces 85, explosion FX 19, HollywoodFX 5, UIA ordering 11, inspection 10, presentation 16, melee 62 and native server integration 52.
- All 26 complete-run jobs pass, including binary semantic-version contracts and UIA-present/absent native audits. Exact source/test input hashes are captured after generation and verified again while packaging.
- Native binding audit: 281 groups. UIA absent: 354 targets, 648 arguments/fields; UIA present: 355 targets, 650 arguments/fields. Optional skips are recorded explicitly.
- Actual source selectors: 62 required selectors / 137 targets without UIA; 63 / 138 with UIA. The 64-selector inventory includes optional skips. All 36 native transpiler targets pass in each mode, and all 4 modeled UIA collector compositions pass. Independent native target maps agree.
- The supplied UIA 2.1.6 binary passes 15 GUID/signature/target/ordering/resource-caller contracts. Real Harmony ordering tests use production EFB code with controlled EFT/UIA-shaped boundaries and model the inspected UIA query rewrite.
- 199 reflected-member contracts, 89 native method-role checks, 54 export/catalog/API checks, 20 preservation checks, 17 carry-forward checks and 8 embedded-audio/API checks pass. Description-number style has 0 violations.
- The status inventory classifies all 1,508 named designs with current native source hashes. It is a static coverage inventory plus targeted behavior regressions, not a live test of every possible condition/item combination.
- The full native database layout audit covers 1,340 backpack layouts across 44 bases; the idle-layout audit records 0 size/layout/scroll-position/ancestor-lookup work over 300 controlled frames with 16 windows and 1 inventory view.
- Native airdrop tests cover default/migrated config, 0/100% and fractional boundaries, all categories/types, backend independence, request-local filtering, forced ranges, weighted variants, current storage layouts, IDs/attachments/FIR, fire modes, unusual stacks, exclusions and repeat-call prevention. They patch actual native service methods with controlled boundary bodies.

## Artifacts and execution limits

Only Source and fresh-install Runtime archives are emitted. Runtime contains exactly the client DLL, native server DLL and fresh config at their installation paths. Source includes current tests/tools/reports, status inventory and provenance, without game/reference/optional DLLs, SDKs, build output or caches.

No live Windows client, complete SPT startup, in-raid airdrop, actual UIA gameplay, fully modded APBS or Fika run was executed. Metadata/IL tests and controlled boundaries do not establish live rendering/audio/physics/animation or UI behavior. UIA's native transpiler is not executed in the metadata audit; its call substitution is modeled from inspected binary metadata/IL. The export lacks ItemTemplate.Types for direct native CLR execution of that path. No fabricated game assembly is used or shipped. Installed-game smoke tests remain necessary. Upstream Fika charge synchronization/reconnect limitations remain.
