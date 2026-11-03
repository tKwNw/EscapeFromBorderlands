# EFB configuration

## Server settings

Close the server before editing `config.json`, save your changes, then restart it.

- SPT 4.0.13: `SPT/user/mods/EscapeFromBorderlands/config/config.json`
- SPT 4.1.6: `SPT_Runtime/user/mods/EscapeFromBorderlands/config/config.json`

| Setting | Default | What it controls |
| --- | --- | --- |
| `enabled` | `true` | Enables EFB's server features. |
| `addToPmcWeapons` | `true` | Enables Legendary equipment on PMCs across the supported categories. |
| `addToBossWeapons` | `true` | Enables Legendary equipment on bosses. |
| `addToNonScavFactions` | `true` | Enables Legendary equipment on raiders, rogues, followers, cultists, arena fighters and other non-Scav factions. |
| `spawnChances.pmcs` | `1` per category | PMC Legendary chance for each equipment category. |
| `spawnChances.bosses` | `10` per category | Boss Legendary chance for each equipment category. |
| `factionSpawnChances` | `1` per category | Separate equipment chances for each other supported non-Scav faction group. |
| `autoDetectApbs` | `true` | Detects APBS automatically. The same spawn settings are used with or without APBS. |
| `airdropLegendaryChancePercent` | `1` | 0–100% chance per supported loot item, shared across all airdrop types. |
| `statRewardsLegendaryChancePercent` | `1` | 0–100% chance per supported random equipment reward when Stat Rewards is installed. |
| `allowFleaMarketTrading` | `false` | Allows Legendary items on the flea market. Generated stock hides its 4 random bonuses until purchase in every category. |
| `allowFenceToSellEfbItems` | `false` | Allows active EFB items in Fence stock under his normal stocking rules. When false, excludes EFB items from generated, normal, maximum-reputation and resale stock. Players can still sell EFB items to Fence. |
| `priceMultipliers.Legendary` | `10.0` | Legendary price multiplier relative to the vanilla base. |
| `createDefaultPresets` | `true` | Creates ready-to-use Legendary weapon presets. |

Bot spawn percentages accept **0–99**. Each category has its own value: `guns`, `melee`, `backpacks`, `chestRigs`, `grenades` and `earpieces`. A value of 0 disables Legendary selection for that category. These settings affect supported equipment; they do not give every bot a fixed number of Legendary items. Ordinary Scavs are excluded.

The airdrop percentage is a chance for each eligible item, rather than a chance for the entire crate. At 0%, airdrop Legendary conversions are disabled. At 100%, every eligible supported item is converted; unrelated loot remains available.

Stat Rewards integration is optional in both supported SPT versions. Its reward chance is separate from bot, airdrop and flea settings.

Fence stock is controlled separately from flea, bot, airdrop and Stat Rewards settings. Restart the server after changing `allowFenceToSellEfbItems`. Enabling it permits otherwise eligible active EFB items; it does not guarantee offers. Retired aliases and mystery purchase previews remain excluded.

Missing server options are added automatically when EFB starts. Keep existing values when updating; see the [upgrade guide](https://github.com/tKwNw/EscapeFromBorderlands/blob/main/UPGRADING.md).

## Client settings

EFB creates `BepInEx/config/com.tKwNw.escapefromborderlands.cfg`. Edit it with the game closed, or use an installed BepInEx configuration manager.

| Section | Settings |
| --- | --- |
| Status icons | Enable or hide the display, toggle equipment categories, adjust horizontal/vertical position and change its size. Hiding the display leaves gameplay effects and timers active. |
| Legendary grenades | Set seconds to restore 1 charge: **180** by default, with separate **300** defaults for Holy Hand Grenade and 9-Banger. Minimum 1 second; changes apply next raid. |
| Holy Hand Grenade draw sound | Volume **0.50** by default. Use **0** to mute or **1** for full volume. Changes apply on the next draw; muting also stops the current cue. |

The status display shows triggered and conditional effects while active, including conditions such as aiming or going prone. Permanent effects do not need an active-status entry. Recharge timers can appear while grenades are unequipped; unequipped charge counts appear only between empty and full, such as **1/3** or **2/3**.

[← EFB home](../README.md) · [Legendary item catalog](README.md)
