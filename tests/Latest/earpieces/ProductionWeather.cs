using System;using System.Collections.Generic;using System.Linq;using Comfort.Common;using EFT;
namespace EscapeFromBorderlands.Client;
internal static class BackpackAudio
{
    internal static readonly Dictionary<object, float> Weather = new();
    internal static readonly Dictionary<BetterSource, Player> Searches = new();
    [ThreadStatic] internal static float InventoryFactor;
    internal static float WeatherFactor => (BackpackRuntime.Is(Singleton<GameWorld>.Instance?.MainPlayer, "BP21-3") ? .25f : 1f) * (EarpieceRuntime.Listener?.Key == "EP02-2" ? .4f : 1f);
    internal static void Reset()
    {
        Searches.Clear();
        // Cached crossfade volumes outlive a raid in some scenes. Restore them
        // while Resetting suppresses bag bonuses, before forgetting the sources.
        foreach (var blender in Weather.Where(p => p.Value != 1f).Select(p => p.Key).ToArray()) RefreshWeather(blender);
        Weather.Clear();
    }
    internal static void RefreshWeather(object blender)
    {
        if (blender.GetType().Name == "WindAmbientBlender") Native.Call(blender, "UpdateWindSound", true);
        else Native.Call(blender, "StartCrossfader", .25f, 1f);
    }
}
