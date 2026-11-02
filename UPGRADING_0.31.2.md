# Upgrade to Escape From Borderlands 0.31.2 — SPT 4.0.13

1. Close the game, launcher and SPT server. Back up your profile and EFB settings.
2. For an existing EFB installation, extract `EscapeFromBorderlands_0.31.2_SPT-4.0.13_Upgrade.zip` into the SPT game root and replace both DLLs:
   - `BepInEx/plugins/EscapeFromBorderlands/EscapeFromBorderlands.Client.dll`
   - `SPT/user/mods/EscapeFromBorderlands/EscapeFromBorderlands.Server.dll`
3. Retain the existing `SPT/user/mods/EscapeFromBorderlands/config/config.json`. The new `statRewardsLegendaryChancePercent` option is added automatically with a default of 1; your existing values are preserved. Restart the server after changing settings.
4. For a fresh installation, use `EscapeFromBorderlands_0.31.2_SPT-4.0.13.zip`. It includes config.json and the license/notices. No config.default is included.

Both DLLs must come from the matching SPT 4.0.13 release. The SPT 4.1.6 installation uses a separate build and server path.

Existing item/template IDs, saved random rolls and storage layouts are preserved. Service Window's signature now uses the inventory-menu condition and gives +50% medical-use and ammunition loading/unloading speed. The earlier 0.30.0 plugin-GUID migration remains included; see [0.30.0 migration notes](UPGRADING_0.30.0.md) when upgrading from an older release.
