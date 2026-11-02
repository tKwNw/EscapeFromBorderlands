# Escape From Borderlands 0.31.2 — SPT 4.0.13

Service Window now provides its medical-use bonus from the first use, Holy Hand Grenade draw audio recovers across raids, and optional Stat Rewards integration controls Legendary equipment rewards.

## Service Window

- While the inventory menu is open, medical item use and ammunition loading and unloading are **50% faster**. This replaces its previous loot-window signature and Arm Stamina recovery bonus.
- Medical coverage includes surgery kits, painkillers, bandages, splints, hemostatics and injectors, alongside medkits.
- Apply the medical bonus to animation playback after initialization, including the first medical use. Closing the inventory or removing the rig updates the conditional bonus; the active status follows the inventory-menu condition.
- Preserve native speed, other EFB bonuses, existing item identities, storage and random rolls. Food and drink retain their existing behavior.

## Holy Hand Grenade

- Repair draw-audio recovery across raids, delayed audio initialization and audio-engine changes.
- Retain the existing 2.5-second cue, default volume **0.50**, 3-second fuse, radius, charges and recharge behavior.

## Optional Stat Rewards compatibility

- Prevent EFB's many cloned templates and presets from dominating random weapon/equipment reward pools.
- Add **`statRewardsLegendaryChancePercent`**, default **1%**, configurable from **0–100%**. Existing configs receive the missing option automatically.
- Roll once per successfully generated supported equipment reward. At 0%, EFB conversions are disabled; ordinary eligible rewards remain available.
- Preserve generated attachments, armor inserts, item IDs, durability and Found in Raid state. Explicit-template rewards and unrelated categories retain their authored behavior.
- Keep the setting independent of bot/APBS, airdrop and flea settings. Stat Rewards remains optional; no additional mod is bundled or required. Default preset creation may be enabled or disabled.

## Installation and compatibility

Target: **SPT 4.0.13**. EFB release/file/product versions are **0.31.2**, with equivalent CLR assembly version **0.31.2.0**.

Close the game and server before installing. Existing users can extract the matching Upgrade ZIP into the SPT root to replace the 2 EFB DLLs and preserve their config. The full installation ZIP includes config.json and license/notices. No config.default is shipped. See [upgrade instructions](UPGRADING_0.31.2.md).

The catalog remains **1,508 named designs** and **1,860 raw entries**, revision **54**. This release changes Service Window descriptions without adding designs or changing saved item IDs, random rolls or storage layouts. Documentation and item catalogs match the final source.

The SPT 4.1.6 release remains **0.30.0**, catalog revision **52**, until its separate port is prepared. Its previous compatibility fixes will be retained during that port.

## Validation

Both production builds have 0 warnings/errors. Release checks against the final server source also pass 13 Stat Rewards cases / 111 assertions and a separate startup check without Stat Rewards (1 case / 3 assertions). The 0.31.2 focused rig/headset/grenade suites pass 420 cases / 348,812 assertions, with 204 native contracts and 161 Harmony argument checks across 158 hook groups. The number-style audit has 0 violations across 126 source files.

The user confirmed the Service Window first-use repair in 0.31.2 and the Holy Hand Grenade audio repair across 3 consecutive raids. Automated medical coverage includes 44 medical-family template identities on controlled boundaries; it does not establish a live test of every item. Stat Rewards' full live milestone/mail flow has not been independently confirmed. Historical broader 0.31.0 results are retained separately from current focused results.
