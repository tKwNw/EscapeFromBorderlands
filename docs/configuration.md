# EFB configuration

## Server settings

On/off toggles are grouped at the top of `config.json`, followed by chances and pricing. Close the server before editing `config.json`, save your changes, then restart it.

`SPT/user/mods/EscapeFromBorderlands/config/config.json`

| Setting | Default | What it controls |
| --- | --- | --- |
| `enabled` | `true` | Enables EFB's server features. |
| `addToPmcWeapons` | `true` | Enables Legendary equipment on PMCs across the supported categories. |
| `addToBossWeapons` | `true` | Enables Legendary equipment on bosses. |
| `addToNonScavFactions` | `true` | Enables Legendary equipment on raiders, rogues, followers, cultists, arena fighters and other non-Scav factions. |
| `spawnChances.pmcs` | `1` per category | PMC Legendary chance for each equipment category. |
| `spawnChances.scavs` | `0` except `unarmoredHeadwear: 1` | Separate chances for supported ordinary and sniper Scav equipment. |
| `lootContainerSpawnChances` | `1` per category | Legendary chances for eligible items in supported loot containers. |
| `spawnChances.bosses` | `10` per category | Boss Legendary chance for each equipment category. |
| `factionSpawnChances` | `1` per category | Separate equipment chances for each other supported non-Scav faction group. |
| `autoDetectApbs` | `true` | Detects APBS automatically. The same spawn settings are used with or without APBS. |
| `airdropLegendaryChancePercent` | `1` | 0–100% chance per supported loot item, shared across all airdrop types. |
| `statRewardsLegendaryChancePercent` | `1` | 0–100% chance per supported random equipment reward when Stat Rewards is installed. |
| `allowFleaMarketTrading` | `false` | Allows Legendary items on the flea market. Generated stock hides its 4 random bonuses until purchase in every category. |
| `allowFenceToSellEfbItems` | `false` | Allows active EFB items in Fence's offers under normal stocking rules. With `false`, blocks generated and resold EFB offers in normal and maximum-reputation stock. Players can still sell EFB items to Fence. |
| `priceMultipliers.Legendary` | `10.0` | Legendary price multiplier relative to the vanilla base. |
| `createDefaultPresets` | `true` | Creates ready-to-use Legendary weapon presets. |

Bot spawn percentages accept **0–99**. Each category has its own value: `guns`, `melee`, `backpacks`, `chestRigs`, `grenades`, `earpieces`, `armoredHeadwear` and `unarmoredHeadwear`. A value of 0 disables Legendary selection for that category. These settings affect supported equipment; they do not give every bot a fixed number of Legendary items. Scavs use their own `spawnChances.scavs` settings.

`factionSpawnChances.otherNonScavs` also covers eligible custom factions registered through MoreBotsAPI, including supported ISB and Black Division roles. Recognized bosses retain the boss settings. Ordinary/sniper Scavs and recognized custom Scav roles use the Scav settings. Infected and special excluded roles remain unchanged. Custom roles without enough faction metadata are left unchanged.

For supported native and custom roles, EFB applies the chance after their loadout is assembled. Only supported vanilla item bases can become Legendary; mod-added item bases remain unchanged. Attachments, ammunition, item identities and container contents are preserved. A signature is skipped if it would invalidate the generated firing mode or storage layout. Each generated inventory is evaluated once, so cached bots do not receive extra rolls. These chances work with or without APBS, including when APBS modded gear is disabled. Empty slots and unsupported item bases are not filled or replaced.

The airdrop percentage is a chance for each eligible item, rather than a chance for the entire crate. At 0%, airdrop Legendary conversions are disabled. At 100%, every eligible supported item is converted; unrelated loot remains available.

Stat Rewards integration is optional. Its reward chance is separate from bot, airdrop and flea settings.

Fence's setting is independent of the flea-market toggle and Legendary spawn chances. Set `allowFenceToSellEfbItems` to `true` to permit otherwise eligible active EFB stock; it does not guarantee Legendary offers. Retired compatibility aliases and mystery purchase-preview templates stay excluded. Existing EFB offers, including an offer with a nested EFB item, are removed when blocking is enabled. Completed player sales retain their normal payment and reputation behavior. Restart the server after changing this option.

Missing server options are added automatically when EFB starts. Keep existing values when updating; see the [upgrade guide](https://github.com/tKwNw/EscapeFromBorderlands/blob/main/UPGRADING.md).

## Client settings

EFB creates `BepInEx/config/com.tKwNw.escapefromborderlands.cfg`. Edit it with the game closed, or use an installed BepInEx configuration manager.

| Section | Settings |
| --- | --- |
| Headwear | Rebind the ability key (default **Shift+G**). Changes apply immediately and update in-game descriptions. |
| Status icons | Enable or hide the display, toggle equipment categories including headwear, adjust horizontal/vertical position and change its size. Hiding the display leaves gameplay effects and timers active. |
| Legendary grenades | Set seconds to restore 1 charge: **180** by default, with separate **300** defaults for Holy Hand Grenade and 9-Banger. Minimum 1 second; changes apply next raid. |
| Holy Hand Grenade draw sound | Volume **0.50** by default. Use **0** to mute or **1** for full volume. Changes apply on the next draw; muting also stops the current cue. |

The status display shows triggered and conditional effects while active, including conditions such as aiming or going prone. Permanent effects do not need an active-status entry. Recharge timers can appear while grenades are unequipped; unequipped charge counts appear only between empty and full, such as **1/3** or **2/3**.

[← EFB home](../README.md) · [Legendary item catalog](README.md)
