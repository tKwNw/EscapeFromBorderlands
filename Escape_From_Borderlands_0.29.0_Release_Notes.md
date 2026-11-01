# Escape From Borderlands 0.29.0 — SPT 4.0.13

Adds the shared legendary-airdrop setting and includes every status-effect and partial grenade charge display fix from 0.28.3.

## Airdrop configuration

Edit `SPT/user/mods/EscapeFromBorderlands/config/config.json`:

```json
"airdropLegendaryChancePercent": 1
```

| Setting | Meaning |
| --- | --- |
| Default 1 | 1% chance per supported generated item |
| 0 | No EFB legendary conversions in airdrops |
| 100 | Every supported individual item becomes a current legendary variant of the same vanilla base |

The percentage applies to each eligible item, not to the whole crate. It covers all 6 supported categories—guns, melee, backpacks, chest/armored rigs, grenades and earpieces—and every native airdrop type, including custom/forced drop requests. There are no separate category, crate-type or backend options. APBS/standalone bot rates and the flea toggle do not control this setting.

Food, medicine, barter goods, attachments and other unsupported loot stay as generated. A crate containing only unsupported items keeps its normal contents, even at 100%. This setting does not change airdrop frequency or add extra loot. Unusual stacks created by other mods retain their native template and quantity so they cannot become invalid max-stack-1 legendary items. Fractional percentages are supported; values outside 0–100 are clamped. Restart the server after changing the value.

## How generation works

EFB clones and retired/mystery stock are filtered out of the native airdrop's temporary random/preset pool. Forced EFB entries are mapped to their vanilla bases first, so they receive the same configured roll. If forced entries map to the same vanilla base, their minimum/maximum quantity ranges are combined.

The shared chance selects a current variant of that same base, respecting signature weights, active backpack layouts and the existing column cap. Conversion happens before native crate fitting and preserves item IDs, attachment graphs, Found in Raid flags and ordinary item state. If a signature restricts fire modes, an incompatible preset mode is changed to a supported mode without editing shared preset state.

Shared loot databases and unrelated reward generators are unchanged. Existing status HUD behavior, saved item identities, per-copy random bonuses, storage layouts, bot spawning, economy, audio, explosions and quest protections carry forward from 0.28.3.

## Install

Close the game and server. Existing installations: extract **Upgrade** into the SPT game root and replace both matching DLLs. Your configuration is preserved; the missing airdrop setting is added automatically with the 1% default during normal configuration migration. Existing custom values, explicit zeroes, bot/flea/pricing settings and unrelated fields remain respected.

- Client: `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
- Server: `SPT/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll`

For a fresh installation, **Full** also supplies `SPT/user/mods/EscapeFromBorderlands/config/config.json`. Only `config.json` is shipped; there is no `config.default`.

Release version **0.29.0** follows MAJOR.MINOR.PATCH and matches the client/server declarations, file/product metadata and package names. This feature increments the minor version. Target: **SPT 4.0.13**; the separate SPT 4.1.6 branch is unchanged.

## Verification

The final source passes **763 behavior cases / 770,662 assertions across 14 suites**, plus **192 native contracts** and **159 Harmony argument bindings across 156 hook groups**. Both production builds have 0 warnings/errors. Source and binary version checks pass. The number-style audit has 0 violations across 122 source files. Optional HollywoodFX checks still cover 2 targets.

The 18 new airdrop cases cover default/config migration, 0/100% boundaries, fractional rates, backend independence, request-local filtering, forced quantity ranges, all supported categories and native drop types, weighted variants, active storage layouts, root IDs/attachments/FIR state, fire-mode compatibility, unusual stacks, exclusions and repeat-call protection. The actual SPT methods are patched and invoked with controlled native boundary bodies; the native fitting algorithm itself remains unchanged.

Source and Validation archives include current evidence and clearly labelled historical records. The build host has no running Tarkov client, so live in-raid airdrop testing remains outstanding. The existing 0.28.3 status changes retain the user's earlier approval.
