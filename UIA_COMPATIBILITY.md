# Use Items Anywhere 2.1.6 compatibility — EFB 0.29.1

This release carries forward the reviewed UIA integration from native 0.28.2 and produces compiled client/server DLLs for SPT 4.1.6. The original integration was reviewed against UIA 2.1.6. Current validation is regenerated after merging the 0.29.0 feature update.

## Review and changes

Both mods replace typed inventory searches in PlayerInputTranslator.vmethod_1, FirearmHandsInputTranslator.ReloadExternalMagazine, BotReload.GetMagazineForReload and BotReload.AddAmmoToMagazines. EFB now runs after the actual UIA inventory-query Harmony owner and recognizes its typed collector calls. The optional BepInEx dependency ensures UIA initializes first when installed; EFB also works alone.

| Concern | Integrated behavior |
| --- | --- |
| Configured grenade slots | Delegate to UIA CollectGrenades, then add eligible legendary-backpack items and filter EFB charge availability |
| Player reload slots | Delegate to UIA CollectReloadItems before the BP02-2 backpack extension |
| Bot reload policy | Route by the actual bot target method to CollectNativeItems, even if the incoming call is still native |
| Reflection overloads | Validate static generic void(InventoryController, IList<T>, Predicate<T>) before selecting a collector |
| Optional collector failure | Commit results only after successful collection; native fallback receives no partial UIA additions |
| Instruction rewriting | Rewrite only call/callvirt and retain an explicit three-argument adapter signature |
| Quick-use wheel | Patch the exact static bool HasResource(Item) predicate and reject spent EFB throwable instances while preserving native rejections |

The wheel resource predicate is called from UIA IsItemStillUsable, CreateWheelItem and GetAvailabilityRank in the supplied DLL. Filtering at this shared boundary supports grouped-item and last-item availability paths. Each real item retains its own charge ledger; no templates, charge limits, recharge rates or config defaults changed. The optional mod's assemblies and assets are not redistributed.

## Completed verification

- Both DLLs compile against native SPT 4.1.6 references with 0 warnings/errors.
- 942 behavior/service cases pass. The 199-case grenade suite includes 27 new UIA cases for native fallback, configured candidates, predicates, separate bot slots, same-template copies, recharge, backpack extensions, overload selection and failure recovery.
- 11 real-Harmony tests install the production EFB patches against controlled EFT/UIA boundaries in both installation orders. The UIA rewrite in these tests models the inspected 2.1.6 query substitution and one-query guard.
- Native patch/argument and actual selector/transpiler audits run with the supplied UIA 2.1.6 binary present and absent. All 4 overlapping native IL paths also pass EFB composition checks after a modeled UIA substitution using its actual method metadata.
- 15 metadata/IL contracts inspect the actual supplied UIA binary, including GUID/version, collector signatures, target table, patch owner/priority, bot rules and shared wheel resource callers.
- The UIA adapter and inventory-query composition files retain exact native 0.28.2 bytes. The wider feature update adds airdrop configuration and status changes; its input hashes and carry-forward checks are in validation/.

These checks do not execute a live game or UIA's native transpiler/gameplay code. Harmony installation tests use explicit controlled domain boundaries; the native IL composition audit models UIA's call substitution rather than executing it. Actual Windows client behavior remains to be confirmed.

## Installation and source build

For a fresh installation, extract the Runtime ZIP into the SPT root containing EscapeFromTarkov.exe. It contains the EFB client DLL, server DLL and default server config. For an existing installation, preserve your custom config when replacing the DLLs. Leave UIA files unchanged.

To build the Source ZIP, install the .NET 10 SDK and run build.ps1 -GameRoot 'C:\SPT MODDED'. Outputs are under output/Client and output/Server. No UIA assembly is needed to compile EFB.

## Installed-game checklist

1. Without UIA, deplete the first of 2 legendary grenades, select the other, then confirm charge regeneration.
2. With UIA, repeat in pockets and in configured extra grenade slots such as armband/belt.
3. Use the wheel and tap-last-item with charged, depleted and recharged copies.
4. Test 2 copies of the same legendary, one depleted and one charged; verify the charged instance is selected.
5. Mix vanilla and legendary grenades; vanilla items are consumed normally and legendary originals remain carried.
6. Check BP35-1 throwables, BP02-2 player/bot magazines and BP07-1 backpack grenade behavior.
7. Confirm player and bot reloads work and the Harmony log has no one-inventory-query errors.

Fika gameplay and charge synchronization were not tested and are not added by this integration.
