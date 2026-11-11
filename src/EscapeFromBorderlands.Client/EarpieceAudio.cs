using Physical = PlayerPhysicalClass;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.ActiveHeadphones;
using EFT.InventoryLogic;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class EarpieceAudio
{
    internal sealed class Source
    {
        internal Player? Owner;
        internal string Kind = "";
        internal float RawRange, AppliedRange, VolumeFactor = 1f, RangeFactor = 1f, BakedLocalVolume = 1f;
        internal bool HasRange, CachedLocalInput, Retired;
        internal float RetryAt, ReportAt;
    }
    internal readonly struct Scope
    {
        internal readonly Player? Owner;
        internal readonly string Kind;
        internal readonly float BaseRolloff;
        internal Scope(Player? owner, string kind, float baseRolloff = 0f) { Owner = owner; Kind = kind; BaseRolloff = baseRolloff; }
    }
    private static readonly Dictionary<BetterSource, Source> Sources = new();
    private static readonly Dictionary<GClass890, Player> Queues = new();
    private static readonly List<GClass890> StaleQueues = new();
    private static readonly List<KeyValuePair<BetterSource, Source>> SourceSnapshot = new();
    private static readonly HashSet<string> Reports = new(StringComparer.Ordinal);
    private static readonly HashSet<(string Item, string Stage, string Kind, bool Own)> FactorReports = new();
    private static readonly Dictionary<Type, MethodInfo> VolumeUpdates = new();
    [ThreadStatic] internal static Scope Current;
    private static float LastAmbient = 1f;
    private static void RefreshHeadphones() => MonoBehaviourSingleton<BetterAudio>.Instance.ApplyCurrentHeadphonesTemplate();
    internal static float Ambient => EarpieceRuntime.Listener is { } s
        ? EarpieceEffectRules.Ambient(s.Key, s.Roll.Earpiece.Ambient, EarpieceRuntime.Moving(s.Player), s.Effect.StillSeconds) : 1f;
    internal static Player? Owner(BaseSoundPlayer sound) => Native.Get(Native.Get(sound, "playersBridge"), "iPlayer") as Player;
    internal static Scope Begin(Player? owner, string kind, float baseRolloff = 0f) { var old = Current; Current = new Scope(owner, kind, baseRolloff); return old; }
    internal static Scope BeginQueue(GClass890 queue)
    {
        var old = Current;
        if (Queues.TryGetValue(queue, out var owner)) { Current = new Scope(owner, "gun"); RegisterQueue(queue); }
        return old;
    }
    internal static void Capture(BetterSource source)
    {
        // WeaponSoundPlayer can emit unrelated sounds inside its firing call.
        // Only RegisterQueue identifies gunshot sources; other actor scopes
        // still identify equipment, movement and speech independently.
        if (Current.Owner is { } owner && Current.Kind != "gun") Register(source, owner, Current.Kind);
    }
    internal static float Filter(float volume, BetterSource source) => volume * SourceVolume(source);
    internal static void RefreshOutput(BetterSource source)
    {
        // Reflection dispatch enters the patched concrete method even if an
        // earlier native caller has cached/inlined its original body on Mono.
        var type = source.GetType();
        if (!VolumeUpdates.TryGetValue(type, out var update))
            VolumeUpdates[type] = update = AccessTools.Method(type, "UpdateSourceVolume", new[] { typeof(float) });
        update.Invoke(source, new object[] { 1f });
    }
    internal static void GunStarted(SuperSource source)
    {
        Capture(source);
        if (Sources.TryGetValue(source, out var state) && Volume(state.Owner, state.Kind) != 1f) RefreshOutput(source);
    }
    internal static void PrepareScheduledGun(BetterSource source)
    {
        if (Sources.TryGetValue(source, out var state) && state.Kind == "gun" && Volume(state.Owner, "gun") != 1f)
            RefreshOutput(source);
    }
    internal static float RawRange(BetterSource source) => Sources.TryGetValue(source, out var s) && s.HasRange ? s.RawRange : source.MaxDistance;
    internal static void Register(BetterSource? source, Player? owner, string kind)
    {
        if (source == null || owner is null) return;
        if (Sources.TryGetValue(source, out var known) && known.Owner == owner && known.Kind == kind) return;
        Forget(source, true);
        Sources[source] = new Source { Owner = owner, Kind = kind };
        if (source.MaxDistance > 0f) source.SetRolloff(source.MaxDistance);
    }
    private static GameWorld? MapWorld;
    private static string? MapName;
    private static string? Map(EarpieceRuntime.State? listener)
    {
        if (listener?.Key != "EP02-1" || Singleton<GameWorld>.Instance is not { } world) return null;
        if (!ReferenceEquals(MapWorld, world) || MapName is null)
        {
            MapWorld = world;
            MapName = Native.Get(world, "LocationId")?.ToString()?.ToLowerInvariant();
        }
        return MapName;
    }
    internal static float Hearing(Player? owner, string kind)
    {
        var listener = EarpieceRuntime.Listener;

        return Hearing(owner, kind, listener, Map(listener));
    }
    private static float Hearing(Player? owner, string kind, EarpieceRuntime.State? listener, string? map)
    {
        var hat = Headwear.Listener;
        var hearing = hat?.Player != owner ? Headwear.Factor(hat, HatStat.Hearing) : 1f;
        return hearing * (listener is null || owner == listener.Player ? 1f :
            EarpieceEffectRules.Hearing(listener.Key, listener.Roll.Earpiece.Hearing, listener.Effect.Hearing, false, kind, map));
    }
    internal static float Volume(Player? owner, string kind) => Volume(owner, kind, EarpieceRuntime.Listener);
    private static float Volume(Player? owner, string kind, EarpieceRuntime.State? listener)
    {
        var emitted = kind == "step" ? EarpieceRuntime.EmittedStep(owner) : 1f;
        if (kind == "step" && owner != Headwear.Local)
            emitted *= Headwear.Factor(owner, HatStat.Noise) * Headwear.Factor(Headwear.Listener, HatStat.ExternalSteps);
        var factor = listener is null ? emitted : EarpieceEffectRules.Volume(listener.Key, listener.Roll.Earpiece.SelfNoise,
            owner == listener.Player, kind, listener.Key == "EP11-2" && listener.Player.MovementContext.IsInPronePose, emitted);
        return kind == "gun" ? factor * Headwear.Factor(owner, HatStat.Gunshot) : factor;
    }
    internal static float SourceVolume(BetterSource source)
    {
        Capture(source);
        if (!Sources.TryGetValue(source, out var s)) return 1f;
        var factor = s.VolumeFactor = TargetVolume(s);
        Report("volume", s, factor);
        return factor;
    }
    private static float TargetVolume(Source state) => Volume(state.Owner, state.Kind) / state.BakedLocalVolume;
    internal static float LocalVolume(Player owner, string kind)
    {
        var listener = EarpieceRuntime.Listener;
        if (listener is null || owner != listener.Player || kind is not ("step" or "item")) return 1f;
        return listener.Roll.Earpiece.SelfNoise * (listener.Key == "EP01-2" ? .5f : 1f);
    }
    internal static void BakeLocal(BetterSource source, Player owner, string kind, ref float volume, bool oneShot = true)
    {
        var local = Singleton<GameWorld>.Instance?.MainPlayer;
        if (owner != local) return;
        Register(source, owner, kind);
        var factor = LocalVolume(owner, kind);
        Sources[source].BakedLocalVolume = factor;
        Sources[source].CachedLocalInput = source is not SimpleSource || !oneShot;
        volume *= factor;
        if (factor != 1f && EarpieceRuntime.Listener is { } s && FactorReports.Add((s.Item.Id, "input", kind, true)))
            Plugin.LogSource.LogInfo($"EFB local audio {s.Roll.Definition.ShortName}: native {kind} input multiplier {factor:F3}.");
    }
    internal static void StepInput(BetterSource source, ref float volume, bool oneShot)
    {
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player is null) return;
        var known = Sources.TryGetValue(source, out var s) && s.Owner == player && s.Kind == "step";
        var secondary = Native.Get(Native.Get(player, "_specificStepAudioController"), "BetterSource_0");
        if (Current.Owner == player && Current.Kind == "step" || known ||
            ReferenceEquals(source, Native.Get(player, "NestedStepSoundSource")) || ReferenceEquals(source, secondary))
            BakeLocal(source, player, "step", ref volume, oneShot);
    }
    internal static void Rolloff(BetterSource source, ref float distance)
    {
        Capture(source);
        if (!Sources.TryGetValue(source, out var s)) return;
        if (Current.BaseRolloff > 0f) distance = Current.BaseRolloff;
        // Native occlusion reapplies MaxDistance. Recover the original instead
        // of multiplying an already-expanded range on every occlusion update.
        if (s.HasRange && Math.Abs(distance - s.AppliedRange) < .0001f) distance = s.RawRange;
        s.RawRange = distance; s.HasRange = true; s.RangeFactor = Hearing(s.Owner, s.Kind);
        distance *= s.RangeFactor; s.AppliedRange = distance;
        Report("range", s, s.RangeFactor);
    }
    private static void Report(string stage, Source source, float factor)
    {
        if (factor == 1f || EarpieceRuntime.Listener is not { } listener) return;
        var own = source.Owner == listener.Player;
        if (FactorReports.Add((listener.Item.Id, stage, source.Kind, own)))
            Plugin.LogSource.LogInfo($"EFB headset audio {listener.Roll.Definition.ShortName}: {stage} {source.Kind}, {(own ? "own" : "other")} source, multiplier {factor:F2}.");
    }
    internal static void Forget(BetterSource source, bool restore)
    {
        if (!Sources.TryGetValue(source, out var s)) return;
        // Drop the lease first, even if a native restoration fails. Reused pool
        // sources must not inherit ownership from a previous actor or raid.
        Sources.Remove(source!);
        if (restore && source != null && s.CachedLocalInput && s.BakedLocalVolume != 1f)
        {
            // A native looping/stereo source stores its input volume in these
            // caches. Remove only our baked factor before forgetting the lease.
            Cleanup("input volume", () => Native.Set(source, "PreOcclusionVolume", Native.Number(source, "PreOcclusionVolume") / s.BakedLocalVolume));
            Cleanup("stereo volume", () => { if (Native.Get(source, "float_0") is float second) Native.Set(source, "float_0", second / s.BakedLocalVolume); });
        }
        if (!restore || source == null) return;
        var old = Current; Current = default;
        try
        {
            Cleanup("output volume", () => RefreshOutput(source));
            if (s.HasRange) Cleanup("source range", () => source.SetRolloff(s.RawRange));
        }
        finally { Current = old; }
    }
    private static void Cleanup(string stage, Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            if (Reports.Add("cleanup:" + stage)) Plugin.LogSource?.LogWarning($"EFB audio {stage} restoration failed; remaining cleanup continues. {error}");
        }
    }
    internal static void RegisterQueue(GClass890 queue)
    {
        if (Current.Owner is not { } owner) return;
        Queues[queue] = owner;
        foreach (var source in queue.AudioSources) Register(source, owner, "gun");
    }
    internal static void Maintain(RaidPlayerSnapshot live)
    {
        var listener = EarpieceRuntime.Listener;
        // Playback hooks still capture newly playing sounds. Background
        // discovery only serves an affected listener or step emitter.
        if (listener is not null || EarpieceRuntime.States.Count != 0)
            foreach (var p in live)
            {
                if (listener is null && (!EarpieceRuntime.States.TryGetValue(p, out var active) || active.Key != "EP08-2")) continue;
                if (!PlayerRefresh.CanObserve(p)) continue;
                if (listener is not null)
                {
                    Register(p.SpeechSource, p, "voice");
                    Register(Native.Get(p, "_gearSource") as BetterSource, p, "item");
                    Register(Native.Get(p, "_searchSource") as BetterSource, p, "item");
                }
                Register(Native.Get(p, "NestedStepSoundSource") as BetterSource, p, "step");
            }
        try
        {
            foreach (var pair in Queues)
                if (!live.Contains(pair.Value)) StaleQueues.Add(pair.Key);
            foreach (var queue in StaleQueues) Queues.Remove(queue);
            SourceSnapshot.AddRange(Sources);
            var map = Map(listener);
            var now = UnityEngine.Time.unscaledTime;
            foreach (var pair in SourceSnapshot)
            {
                if (pair.Key == null) { Sources.Remove(pair.Key!); continue; }
                var s = pair.Value;
                // Keep playing death sounds, tails and corpse-looting audio.
                if (s.Owner is not null && !live.Contains(s.Owner)) { s.Owner = null; s.Retired = true; }
                if (s.Retired && pair.Key.PlayBackState != BetterSource.EPlayBackState.Playing)
                { Forget(pair.Key, true); continue; }
                if (now < s.RetryAt) continue;
                try
                {
                    if (s.VolumeFactor != Volume(s.Owner, s.Kind, listener) / s.BakedLocalVolume) RefreshOutput(pair.Key);
                    if (s.HasRange && s.RangeFactor != Hearing(s.Owner, s.Kind, listener, map)) pair.Key.SetRolloff(s.RawRange);
                }
                catch (Exception error)
                {
                    s.RetryAt = now + 5f;
                    if (now >= s.ReportAt)
                    { s.ReportAt = now + 30f; Plugin.LogSource?.LogWarning($"EFB audio refresh failed; retrying with backoff. {error}"); }
                }
            }
        }
        finally { StaleQueues.Clear(); SourceSnapshot.Clear(); }
        var ambient = Ambient;
        if (ambient != LastAmbient && MonoBehaviourSingleton<BetterAudio>.Instantiated)
        { RefreshHeadphones(); LastAmbient = ambient; }
    }
    internal static void Reset()
    {
        Current = default;
        try
        {
            foreach (var source in Sources.Keys.ToArray()) Cleanup("source lease", () => Forget(source, true));
            if (LastAmbient != 1f && MonoBehaviourSingleton<BetterAudio>.Instantiated && MonoBehaviourSingleton<BetterAudio>.Instance != null)
                Cleanup("ambient volume", RefreshHeadphones);
        }
        finally { Sources.Clear(); Queues.Clear(); Reports.Clear(); FactorReports.Clear(); MapWorld = null; MapName = null; Current = default; LastAmbient = 1f; }
    }
}

[HarmonyPatch(typeof(GClass2590), nameof(GClass2590.Configure))]
internal static class EarpieceAmbientPatch
{
    private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
    private static void Prefix(ref HeadphonesTemplateClass template)
    {
        var factor = EarpieceAudio.Ambient;
        if (template is null || factor == 1f) return;
        template = (HeadphonesTemplateClass)Clone.Invoke(template, null)!;
        template.AmbientVolume += 20f * (float)Math.Log10(Math.Max(.001f, factor));
    }
}
[HarmonyPatch]
internal static class EarpieceStepScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(Player), "PlayStepSound"), AccessTools.Method(typeof(Player), "DefaultPlay"), AccessTools.Method(typeof(Player), "method_61", new[] { typeof(SoundBank), typeof(float) }) };
    private static void Prefix(Player __instance, MethodBase __originalMethod, out EarpieceAudio.Scope __state) =>
        __state = EarpieceAudio.Begin(__instance, __originalMethod.Name == "method_61" ? "item" : "step");
    private static Exception? Finalizer(EarpieceAudio.Scope __state, Exception? __exception) { EarpieceAudio.Current = __state; return __exception; }
}
[HarmonyPatch]
internal static class EarpieceGunScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(WeaponSoundPlayer), "FireBullet"), AccessTools.Method(typeof(WeaponSoundPlayer), "StopFiringLoop") };
    private static void Prefix(WeaponSoundPlayer __instance, out EarpieceAudio.Scope __state)
    {
        __state = EarpieceAudio.Begin(EarpieceAudio.Owner(__instance), "gun");
        if (Native.Get(__instance, "_queue") is GClass890 queue) EarpieceAudio.RegisterQueue(queue);
    }
    private static void Postfix(WeaponSoundPlayer __instance)
    { if (Native.Get(__instance, "_queue") is GClass890 queue) EarpieceAudio.RegisterQueue(queue); }
    private static Exception? Finalizer(EarpieceAudio.Scope __state, Exception? __exception) { EarpieceAudio.Current = __state; return __exception; }
}
[HarmonyPatch]
internal static class EarpieceQueueSourcePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(GClass890), "SetRolloff"), AccessTools.Method(typeof(GClass890), "Enqueue") };
    private static void Prefix(GClass890 __instance) => EarpieceAudio.RegisterQueue(__instance);
}
[HarmonyPatch(typeof(GClass890), nameof(GClass890.Update))]
internal static class EarpieceQueuePlaybackPatch
{
    private static void Prefix(GClass890 __instance, out EarpieceAudio.Scope __state) => __state = EarpieceAudio.BeginQueue(__instance);
    private static Exception? Finalizer(EarpieceAudio.Scope __state, Exception? __exception) { EarpieceAudio.Current = __state; return __exception; }
}
[HarmonyPatch(typeof(GClass893), nameof(GClass893.PlayOn))]
internal static class EarpieceGunPlaybackOutputPatch
{
    // Enqueued body/automatic/tail clips write channel volumes and start on
    // this native boundary. Refresh their output targets after those writes.
    private static void Postfix(SuperSource source) => EarpieceAudio.GunStarted(source);
}
[HarmonyPatch(typeof(SuperSource), nameof(SuperSource.PlayScheduled))]
internal static class EarpieceScheduledGunSourcePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Name == "PlayScheduled" && method.DeclaringType?.FullName == "UnityEngine.AudioSource")
            {
                // Native scheduling has already captured both RAW channel
                // caches here. Reassert their filtered targets before either
                // physical channel starts, including a bypassed native update.
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EarpieceAudio), nameof(EarpieceAudio.PrepareScheduledGun)));
                count++;
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException($"Expected 2 physical gun-channel starts, found {count}.");
    }
}
[HarmonyPatch]
internal static class EarpieceSourcePlaybackPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(BetterSource), "Play"), AccessTools.Method(typeof(BetterSource), "PlayScheduled") };
    private static void Prefix(BetterSource __instance) => EarpieceAudio.Capture(__instance);
}
[HarmonyPatch(typeof(SoundBank), nameof(SoundBank.Play))]
internal static class EarpieceLocalStepInputPatch
{
    private static void Prefix(BetterSource source, ref float volume, bool oneShot) => EarpieceAudio.StepInput(source, ref volume, oneShot);
}
[HarmonyPatch(typeof(BaseSoundPlayer), "PlayClipInternal")]
internal static class EarpieceLocalItemInputPatch
{
    private static void Prefix(BaseSoundPlayer __instance, ref float volume)
    {
        if (EarpieceAudio.Owner(__instance) is { } owner && Native.Get(__instance, "_clipsSource") is BetterSource source)
            EarpieceAudio.BakeLocal(source, owner, "item", ref volume);
    }
}
[HarmonyPatch(typeof(SoundBank), nameof(SoundBank.PickClipsByDistance))]
internal static class EarpieceClipDistancePatch
{
    // The native clip selection has its own distance blend/cutoff, independent
    // of AudioSource.maxDistance. Physical propagation delays remain unchanged.
    private static void Prefix(ref float distance)
    { if (EarpieceAudio.Current.Owner is { } owner) distance /= EarpieceAudio.Hearing(owner, EarpieceAudio.Current.Kind); }
}
[HarmonyPatch(typeof(Player), "method_68")]
internal static class EarpieceActorSourcePatch
{
    private static void Prefix(Player __instance, BetterSource source, out EarpieceAudio.Scope __state)
    {
        var kind = ReferenceEquals(source, __instance.SpeechSource) ? "voice"
            : ReferenceEquals(source, Native.Get(__instance, "_gearSource")) || ReferenceEquals(source, Native.Get(__instance, "_searchSource")) ? "item" : "step";
        __state = EarpieceAudio.Begin(__instance, kind);
        EarpieceAudio.Capture(source);
    }
    private static Exception? Finalizer(EarpieceAudio.Scope __state, Exception? __exception) { EarpieceAudio.Current = __state; return __exception; }
}
[HarmonyPatch(typeof(Player), "method_66")]
internal static class EarpieceSecondaryStepRangePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(BetterSource), nameof(BetterSource.MaxDistance));
        var count = 0;
        foreach (var instruction in instructions)
        {
            // The secondary step controller receives the primary source's
            // ALREADY expanded range. Pass its raw range so its own sources
            // receive the listener multiplier exactly once.
            if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(EarpieceAudio), nameof(EarpieceAudio.RawRange)); count++; }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Expected 1 secondary-step range transfer, found {count}.");
    }
}
[HarmonyPatch(typeof(BaseSoundPlayer), "PlayClip")]
internal static class EarpieceItemAudioScopePatch
{
    private static void Prefix(BaseSoundPlayer __instance, ref int rolloff, out EarpieceAudio.Scope __state)
    {
        var owner = EarpieceAudio.Owner(__instance);
        __state = EarpieceAudio.Begin(owner, "item", rolloff);
        if (owner is not null) rolloff = (int)Math.Ceiling(rolloff * EarpieceAudio.Hearing(owner, "item"));
    }
    private static Exception? Finalizer(EarpieceAudio.Scope __state, Exception? __exception) { EarpieceAudio.Current = __state; return __exception; }
}
// Scale the raw target at each native channel's load, before interpolation,
// occlusion and fades. Do not mutate shared source caches or rely on a nested
// prefix/finalizer scope. Scheduled gunfire and ordinary playback converge here.
[HarmonyPatch]
internal static class EarpieceSourceVolumePatch
{
    private static readonly FieldInfo First = AccessTools.Field(typeof(BetterSource), "PreOcclusionVolume");
    private static readonly FieldInfo Second = AccessTools.Field(typeof(SuperSource), "float_0");
    private static readonly FieldInfo Third = AccessTools.Field(typeof(SuperSourceDistant), "float_0");
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(BetterSource), "UpdateSourceVolume"), AccessTools.Method(typeof(SuperSource), "UpdateSourceVolume"), AccessTools.Method(typeof(SuperSourceDistant), "UpdateSourceVolume") };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!instruction.LoadsField(First) && !instruction.LoadsField(Second) && !instruction.LoadsField(Third)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EarpieceAudio), nameof(EarpieceAudio.Filter)));
            count++;
        }
        if (count != 1) throw new InvalidOperationException($"Expected 1 native channel volume load, found {count}.");
    }
}
[HarmonyPatch]
internal static class EarpieceSourceRangePatch
{
    [ThreadStatic] private static int Depth;
    private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(SimpleSource), "SetRolloff"), AccessTools.Method(typeof(SuperSource), "SetRolloff"), AccessTools.Method(typeof(SuperSourceDistant), "SetRolloff") };
    private static void Prefix(BetterSource __instance, ref float __0, out int __state)
    { __state = Depth++; if (__state == 0) EarpieceAudio.Rolloff(__instance, ref __0); }
    private static Exception? Finalizer(int __state, Exception? __exception) { Depth = __state; return __exception; }
}
[HarmonyPatch(typeof(BetterSource), "Release")]
internal static class EarpieceSourceReleasePatch
{
    private static void Prefix(BetterSource __instance) => EarpieceAudio.Forget(__instance, true);
}
