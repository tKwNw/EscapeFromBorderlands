using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;

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
        if (blender.GetType().Name == "WindAmbientBlender") Native.Call(blender, "method_0", true);
        else Native.Call(blender, "method_2", .25f, 1f);
    }
}
[HarmonyPatch(typeof(TreeInteractive), "method_0")]
internal static class BackpackFoliageSoundPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var volume = AccessTools.Field(typeof(TreeInteractive), "float_1");
        var replacements = 0;
        for (var i = 0; i < code.Count; i++)
        {
            yield return code[i];
            // Scale the final local copied after BOTH native player branches.
            // The nonlocal branch overwrites float_1 with its occluded volume.
            if (!code[i].LoadsField(volume) || i + 1 >= code.Count) continue;
            var next = code[i + 1].opcode;
            if (next != OpCodes.Stloc && next != OpCodes.Stloc_S && next != OpCodes.Stloc_0 && next != OpCodes.Stloc_1 && next != OpCodes.Stloc_2 && next != OpCodes.Stloc_3) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_3);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BackpackFoliageSoundPatch), nameof(Volume)));
            replacements++;
        }
        if (replacements != 1) throw new InvalidOperationException($"Expected one final native foliage volume load; found {replacements}.");
    }
    internal static float Volume(float volume, object player) => Native.Get(player, "iPlayer") is Player p ? volume * BackpackRuntime.Noise(p, "foliage") : volume;
}
[HarmonyPatch(typeof(BaseSoundPlayer), "PlayClipInternal")]
internal static class BackpackItemUseSoundPatch
{
    private static void Prefix(BaseSoundPlayer __instance, ref float volume)
    {
        if (Native.Get(Native.Get(__instance, "playersBridge"), "iPlayer") is not Player p) return;
        volume *= BackpackRuntime.Noise(p, p.HandsController is Player.MedsController ? "medical" : "item");
    }
}
[HarmonyPatch(typeof(Player), "method_46")]
internal static class BackpackSearchSourcePatch
{
    private static void Postfix(Player __instance, BetterSource __result) { if (__result != null) BackpackAudio.Searches[__result] = __instance; }
}
[HarmonyPatch(typeof(BetterSource), "SetBaseVolume")]
internal static class BackpackSearchVolumePatch
{
    private static void Prefix(BetterSource __instance, ref float volume)
    { if (BackpackAudio.Searches.TryGetValue(__instance, out var p)) volume *= BackpackRuntime.Noise(p, "inventory"); }
}
[HarmonyPatch(typeof(BetterSource), "Release")]
internal static class BackpackReleasedSourcePatch
{
    private static void Prefix(BetterSource __instance) => BackpackAudio.Searches.Remove(__instance);
}
[HarmonyPatch]
internal static class BackpackInventorySoundScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(GUISounds).GetMethods().Where(m => m.Name == "PlayItemSound");
    private static void Prefix(out float __state) { __state = BackpackAudio.InventoryFactor; var p = Singleton<GameWorld>.Instance?.MainPlayer; BackpackAudio.InventoryFactor = p is null ? 1f : BackpackRuntime.Noise(p, "inventory") * EarpieceAudio.Volume(p, "item"); }
    private static Exception? Finalizer(float __state, Exception? __exception) { BackpackAudio.InventoryFactor = __state; return __exception; }
}
[HarmonyPatch(typeof(GUISounds), "method_13")]
internal static class BackpackInventoryVolumePatch
{
    private static void Postfix(ref float __result) { if (BackpackAudio.InventoryFactor > 0f) __result *= BackpackAudio.InventoryFactor; }
}
[HarmonyPatch]
internal static class BackpackWeatherTargetVolumePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.PropertyGetter(AccessTools.TypeByName("Audio.AmbientSubsystem.PrecipitationAmbientBlender"), "Single_0"),
        AccessTools.Method(AccessTools.TypeByName("Audio.AmbientSubsystem.WindAmbientBlender"), "method_7") };
    private static void Postfix(ref float __result) => __result *= BackpackAudio.WeatherFactor;
}
[HarmonyPatch]
internal static class BackpackWeatherRefreshPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(AccessTools.TypeByName("Audio.AmbientSubsystem.PrecipitationAmbientBlender"), "ManualUpdate"),
        AccessTools.Method(AccessTools.TypeByName("Audio.AmbientSubsystem.WindAmbientBlender"), "ManualUpdate") };
    private static void Prefix(object __instance)
    {
        var factor = BackpackAudio.WeatherFactor;
        if (!BackpackAudio.Weather.TryGetValue(__instance, out var old)) old = 1f;
        BackpackAudio.Weather[__instance] = factor;
        if (factor == old) return;
        BackpackAudio.RefreshWeather(__instance);
    }
}
[HarmonyPatch]
internal static class BackpackWeatherOneShotPatch
{
    private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(
        AccessTools.Method(AccessTools.TypeByName("Audio.AmbientSubsystem.BaseAmbientSoundPlayer"), "method_1"));
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var ownerType = AccessTools.TypeByName("Audio.AmbientSubsystem.BaseAmbientSoundPlayer");
        var owner = original.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(f => f.FieldType == ownerType);
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Name == "set_volume" && method.DeclaringType?.FullName == "UnityEngine.AudioSource")
            {
                // Scale the fader's final value on every native update, rather
                // than modifying a transient value before a delayed clip starts.
                var nativeOpcode = instruction.opcode; var nativeOperand = instruction.operand;
                // Mutate the original instruction so its labels/exception markers
                // remain on the first instruction of the replacement sequence.
                instruction.opcode = OpCodes.Ldarg_0; instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldfld, owner);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BackpackWeatherOneShotPatch), nameof(Volume)));
                yield return new CodeInstruction(nativeOpcode, nativeOperand);
                replacements++;
                continue;
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException($"Expected one final ambient volume assignment in {original.DeclaringType?.Name}; found {replacements}.");
    }
    internal static float Volume(float volume, object player) => player.GetType().Name == "WeatherRandomAmbientSoundPlayer" ? volume * BackpackAudio.WeatherFactor : volume;
}
