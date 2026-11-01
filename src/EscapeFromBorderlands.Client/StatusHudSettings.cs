using BepInEx.Configuration;

namespace EscapeFromBorderlands.Client;

internal static class StatusHudSettings
{
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<bool> Guns = null!, Melee = null!, Backpacks = null!, ChestRigs = null!, Grenades = null!, Earpieces = null!;
    internal static ConfigEntry<float> HorizontalOffset = null!, VerticalOffset = null!, Scale = null!;
    internal static void Bind(ConfigFile config)
    {
        const string section = "Status icons";
        Enabled = config.Bind(section, "Enabled", true,
            "Show Escape From Borderlands status icons and countdowns. Disabling this display does not disable effects or reset their timers.");
        Guns = config.Bind(section, "Show guns", true, "Show firearm status and cooldown text. Gameplay effects continue when hidden.");
        Melee = config.Bind(section, "Show melee", true, "Show melee status and cooldown text. Gameplay effects continue when hidden.");
        Backpacks = config.Bind(section, "Show backpacks", true, "Show backpack status and cooldown text. Gameplay effects continue when hidden.");
        ChestRigs = config.Bind(section, "Show chest rigs", true, "Show chest-rig status and cooldown text. Gameplay effects continue when hidden.");
        Grenades = config.Bind(section, "Show grenades", true, "Show each reusable grenade's remaining charges. Hiding the display does not reset charges.");
        Earpieces = config.Bind(section, "Show earpieces", true, "Show earpiece bonuses and cooldown timers. Gameplay effects continue when hidden.");
        HorizontalOffset = config.Bind(section, "Horizontal offset", 0f,
            new ConfigDescription("Move the display horizontally. Positive moves right; negative moves left. Changes apply immediately.", new AcceptableValueRange<float>(-3000f, 3000f)));
        VerticalOffset = config.Bind(section, "Vertical offset", 0f,
            new ConfigDescription("Move the display vertically. Positive moves down; negative moves up. Changes apply immediately.", new AcceptableValueRange<float>(-3000f, 3000f)));
        Scale = config.Bind(section, "Size", 1f,
            new ConfigDescription("Resize the icons, text and spacing together. 1 is normal size. Changes apply immediately.", new AcceptableValueRange<float>(.5f, 3f)));
    }
    internal static bool Show(EffectStatusModel.Status status) => StatusVisibility.Allows(status.Key,
        Guns?.Value != false, Melee?.Value != false, Backpacks?.Value != false, ChestRigs?.Value != false, Grenades?.Value != false, Earpieces?.Value != false);
}
