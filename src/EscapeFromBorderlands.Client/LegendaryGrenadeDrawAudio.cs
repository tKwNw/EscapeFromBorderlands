using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static class LegendaryGrenadeDrawAudio
{
    internal const string ResourceName = "EscapeFromBorderlands.Client.Audio.HolyHandGrenade.wav";
    private static UnityEngine.Object? Clip;
    private static MethodInfo? Play;
    private static float Duration, Until;
    private static BetterSource? Source;
    private static BetterAudio? ClipOwner;
    private static Player.BaseGrenadeHandsController? Controller;
    private static bool ReportedFailure, ReportedPlayback, Pending, WasHeld;
    private static float RetryUntil, RetryAt;

    internal static void Drawn(Player.BaseGrenadeHandsController controller)
    {
        var actor = BackpackRuntime.Actor(controller);
        var world = Singleton<GameWorld>.Instance;
        var item = controller.Item;
        if (item is null || actor is null || actor.IsAI || actor.HealthController?.IsAlive != true ||
            !ReferenceEquals(actor, world?.MainPlayer) || !ReusableGrenadeRuntime.IsReusable(item) ||
            !RarityResolver.TryGetDefinition(item, out var definition) || definition.RuntimeKey != "GR15" ||
            ReusableGrenadeRuntime.Remaining(item) <= 0 || ReferenceEquals(Controller, controller)) return;
        Stop();
        Controller = controller;
        WasHeld = ReferenceEquals(actor.HandsController, controller);
        var volume = LegendaryGrenadeSettings.DrawSoundVolume;
        if (volume <= 0f) return;
        Pending = true;
        RetryUntil = Time.unscaledTime + 2f;
        RetryAt = Time.unscaledTime;
        if (WasHeld) TryPlay();
    }

    private static void TryPlay()
    {
        if (!Pending || Time.unscaledTime < RetryAt) return;
        var volume = LegendaryGrenadeSettings.DrawSoundVolume;
        if (volume <= 0f) { Pending = false; return; }
        if (Time.unscaledTime > RetryUntil)
        {
            Pending = false;
            Report(new InvalidOperationException("Audio listener or nonspatial source pool did not become ready within 2 seconds of the draw."));
            return;
        }
        RetryAt = Time.unscaledTime + .1f;
        var audio = MonoBehaviourSingleton<BetterAudio>.Instance;
        if (audio == null || audio.ListenerTransform == null) return;
        try
        {
            if (!ReferenceEquals(ClipOwner, audio)) { UnloadClip(); ClipOwner = audio; }
            LoadClip();
            var source = audio.GetSource(BetterAudio.AudioSourceGroupType.Nonspatial, true, true);
            if (source == null) return;
            Source = source;
            source.OnReleased += Released;
            source.Position = audio.ListenerTransform.position;
            source.EnableStereo(true);
            source.SetSpatialBlend(0f);
            source.SetPitch(1f);
            source.Loop = false;
            source.IncludeInOcclusionProcess(false);
            var group = audio.SourceGroups[(int)BetterAudio.AudioSourceGroupType.Nonspatial];
            Native.Call(source, "SetMixerGroup", Native.Get(group, "MixerGroup"));
            source.SetBaseVolume(volume);
            source.SetRolloff(group.Preset.DefaultMaxDistance);
            Native.Set(Native.Get(source, "source1")!, "panStereo", 0f);
            Play!.Invoke(source, new object?[] { Clip, null, 1f, volume, true, true });
            Pending = false;
            Until = Time.unscaledTime + Duration;
            if (!ReportedPlayback)
            {
                ReportedPlayback = true;
                Plugin.LogSource.LogInfo($"Holy Hand Grenade draw cue: {Duration:F2} seconds, local volume {volume:F2}.");
            }
        }
        catch (Exception e)
        {
            Stop();
            Report(e);
        }
    }

    private static void LoadClip()
    {
        if (Clip != null)
        {
            var state = Native.Get(Clip, "loadState");
            if (state is null || state.ToString() == "Loaded") return;
            UnloadClip();
        }
        using var stream = typeof(LegendaryGrenadeDrawAudio).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Bundled Holy Hand Grenade audio is missing.");
        var pcm = WavePcm.Read(stream);
        // The AudioModule is loaded by Tarkov. Reflection keeps the pinned
        // reference kit's omitted optional Unity modules out of the build.
        var type = AccessTools.TypeByName("UnityEngine.AudioClip") ?? throw new InvalidOperationException("Unity AudioClip is unavailable.");
        var create = AccessTools.Method(type, "Create", new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) })
            ?? throw new MissingMethodException(type.FullName, "Create");
        var clip = create.Invoke(null, new object[] { "EFB Holy Hand Grenade", pcm.Frames, pcm.Channels, pcm.SampleRate, false }) as UnityEngine.Object
            ?? throw new InvalidOperationException("Unity could not create the draw cue.");
        try
        {
            var setData = AccessTools.Method(type, "SetData", new[] { typeof(float[]), typeof(int) })
                ?? throw new MissingMethodException(type.FullName, "SetData");
            if (!Equals(setData.Invoke(clip, new object[] { pcm.Samples, 0 }), true)) throw new InvalidOperationException("Unity rejected the draw cue samples.");
            Play = AccessTools.Method(typeof(BetterSource), "Play", new[] { type, type, typeof(float), typeof(float), typeof(bool), typeof(bool) })
                ?? throw new MissingMethodException(typeof(BetterSource).FullName, "Play");
            Clip = clip;
            Duration = pcm.Seconds;
        }
        catch { UnityEngine.Object.Destroy(clip); throw; }
    }

    internal static void Maintain()
    {
        if (Controller is null) return;
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player?.HealthController?.IsAlive != true) { Reset(); return; }
        if (!ReferenceEquals(player.HandsController, Controller))
        {
            // Spawn can precede the native hands-controller assignment.
            if (Pending && !WasHeld && Time.unscaledTime <= RetryUntil) return;
            Reset(); return;
        }
        WasHeld = true;
        TryPlay();
        if (Source != null && (Time.unscaledTime >= Until || LegendaryGrenadeSettings.DrawSoundVolume <= 0f)) Stop();
    }

    private static void Released(BetterSource source)
    {
        source.OnReleased -= Released;
        if (ReferenceEquals(Source, source)) Source = null;
    }
    private static void Stop()
    {
        Pending = false;
        var source = Source; Source = null;
        if (source == null) return;
        source.OnReleased -= Released;
        try { source.Release(); }
        catch (Exception e) { Report(e); }
    }
    private static void Report(Exception e)
    {
        if (ReportedFailure) return;
        ReportedFailure = true;
        Plugin.LogSource.LogWarning("Holy Hand Grenade draw sound unavailable: " + (e.InnerException ?? e).Message);
    }
    internal static void Reset() { Stop(); Controller = null; Until = RetryUntil = RetryAt = 0f; WasHeld = false; }
    private static void UnloadClip()
    {
        if (Clip != null) UnityEngine.Object.Destroy(Clip);
        Clip = null; Play = null; Duration = 0f;
    }
    internal static void ResetSession()
    {
        Reset(); UnloadClip(); ClipOwner = null;
        ReportedFailure = ReportedPlayback = false;
    }
    internal static void Dispose() => ResetSession();
}
