using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class HeadwearSettings
{
    internal static ConfigEntry<KeyboardShortcut> AbilityKey = null!;
    private static readonly KeyboardShortcut DefaultKey = new(KeyCode.G, KeyCode.LeftShift);
    internal static KeyboardShortcut Shortcut => AbilityKey?.Value ?? DefaultKey;
    internal static void Bind(ConfigFile config)
    {
        AbilityKey = config.Bind("Headwear", "Ability key", DefaultKey,
            "Activate equipped headwear abilities. Changes apply immediately and update item descriptions. None disables the shortcut. Avoid keys used by other game actions.");
        AbilityKey.SettingChanged += (_, _) => ItemDescriptionPresentation.RefreshHeadwear();
    }
    internal static string Describe(string text) => text.Replace("Shift+G", Label);
    internal static string Label => Shortcut.MainKey == KeyCode.None ? "Unbound" :
        string.Join("+", Shortcut.Modifiers.Select(Name).Concat(new[] { Name(Shortcut.MainKey) }));
    private static string Name(KeyCode key) => key switch
    {
        KeyCode.LeftShift or KeyCode.RightShift => "Shift",
        KeyCode.LeftControl or KeyCode.RightControl => "Ctrl",
        KeyCode.LeftAlt or KeyCode.RightAlt => "Alt",
        _ => key.ToString()
    };
    private static readonly Func<KeyCode, bool>? Held = Input("GetKey"), Down = Input("GetKeyDown");
    private static Func<KeyCode, bool>? Input(string method) =>
        AccessTools.TypeByName("UnityEngine.Input") is { } type && AccessTools.Method(type, method, new[] { typeof(KeyCode) }) is { } info
            ? (Func<KeyCode, bool>)Delegate.CreateDelegate(typeof(Func<KeyCode, bool>), info) : null;
    internal static bool IsHeld(KeyCode key) => Held?.Invoke(key) == true;
    private static bool Modifier(KeyCode key) => key switch
    {
        KeyCode.LeftShift or KeyCode.RightShift => IsHeld(KeyCode.LeftShift) || IsHeld(KeyCode.RightShift),
        KeyCode.LeftControl or KeyCode.RightControl => IsHeld(KeyCode.LeftControl) || IsHeld(KeyCode.RightControl),
        KeyCode.LeftAlt or KeyCode.RightAlt => IsHeld(KeyCode.LeftAlt) || IsHeld(KeyCode.RightAlt),
        _ => IsHeld(key)
    };
    internal static bool Pressed => Shortcut.MainKey != KeyCode.None && Down?.Invoke(Shortcut.MainKey) == true && Shortcut.Modifiers.All(Modifier);
}
