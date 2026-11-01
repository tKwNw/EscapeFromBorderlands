using System.Reflection;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EscapeFromBorderlands.Client;
using UnityEngine;

internal static class DrawAudioTests
{
    private static readonly Dictionary<string, VariantDefinition> Designs = LegendaryCatalog.Load().Variants.Where(v => v.GrenadeChargesPerRaid > 0).ToDictionary(v => v.RuntimeKey);
    private static ThrowWeap Item(string key = "GR15") => new() { StringTemplateId = Designs[key].TemplateId };
    private static readonly FieldInfo VolumeField = typeof(LegendaryGrenadeSettings).GetField("DrawVolume", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Volume(float value) => VolumeField.SetValue(null, new ConfigEntry<float> { Value = value });
    private static (Player Player, BetterAudio Audio) Scene()
    {
        LegendaryGrenadeDrawAudio.Dispose();
        Volume(.5f); AudioClip.RejectData = false; AudioClip.Created = 0;
        Time.time = Time.unscaledTime = 100;
        var player = new Player(); Singleton<GameWorld>.Instance = new() { MainPlayer = player };
        var audio = new BetterAudio(); MonoBehaviourSingleton<BetterAudio>.Instance = audio;
        return (player, audio);
    }
    private static Player.BaseGrenadeHandsController Draw(Player player, ThrowWeap? item = null)
    {
        item ??= Item(); player.InventoryController.Reachable.Add(item);
        var hands = new Player.BaseGrenadeHandsController { Owner = player, Item = item };
        player.HandsController = hands;
        // Invoke the exact production draw postfix at the native Spawn boundary.
        typeof(LegendaryGrenadeDrawPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [hands]);
        return hands;
    }
    internal static void Run(Action<string, Action> run, Action<bool, string> check, Action<float, float, string> near)
    {
        run("Bundled choir contains the opening 2.5 seconds as stereo PCM16 with a faded ending", () =>
        {
            Scene();
            using var stream = typeof(LegendaryGrenadeDrawAudio).Assembly.GetManifestResourceStream(LegendaryGrenadeDrawAudio.ResourceName)!;
            var pcm = WavePcm.Read(stream);
            check(pcm.Channels == 2 && pcm.SampleRate == 44100 && pcm.Frames == 110250, "asset format and length");
            near(pcm.Seconds, 2.5f, "duration");
            check(pcm.Samples.Any(x => Math.Abs(x) > .05f) && pcm.Samples.All(x => x >= -1f && x < 1f), "non-silent valid normalized PCM");
            check(pcm.Samples.Take(2).All(x => x == 0) && pcm.Samples.TakeLast(2).All(x => Math.Abs(x) < .0001f), "sampled fade endpoints");
        });
        run("Actual Holy Hand Grenade draw postfix plays 1 local nonspatial cue without spending a charge", () =>
        {
            var(p,a) = Scene(); var h = Draw(p); var s = a.Borrowed.Single();
            check(s.Group == BetterAudio.AudioSourceGroupType.Nonspatial && s.Active && s.NeedUpdate, "native nonspatial pool with volume updates");
            check(s.Stereo && s.ForceStereo && s.OneShot && !s.Loop && !s.Occlusion && s.Spatial == 0 && s.Pitch == 1, "local stereo playback without propagation/occlusion");
            check(ReferenceEquals(s.Mixer, a.SourceGroups[(int)BetterAudio.AudioSourceGroupType.Nonspatial].MixerGroup), "native nonspatial mixer routing");
            check(s.MaxDistance == a.SourceGroups[(int)BetterAudio.AudioSourceGroupType.Nonspatial].Preset.DefaultMaxDistance && s.source1.panStereo == 0, "native rolloff and centered pan reset pooled source settings");
            check(s.Plays == 1 && s.Clip!.Frames == 110250 && s.Clip.Data.Length == 220500, "real embedded samples reach playback");
            near(s.Volume, .5f, "default volume"); near(s.PlayVolume, .5f, "native Play volume");
            check(ReusableGrenadeRuntime.Remaining(h.Item!) == 1, "drawing never consumes grenade charge");
            LegendaryGrenadeDrawAudio.Drawn(h); check(a.Borrows == 1, "duplicate Spawn for same controller does not replay");
        });
        run("Ordinary grenades, other legendaries, bots and remote players cannot trigger the draw cue", () =>
        {
            var(p,a) = Scene();
            Draw(p, new() { StringTemplateId = Designs["GR15"].SourceTemplateId });
            Draw(p, Item("GR26")); Draw(new Player { IsAI = true }); Draw(new Player());
            check(a.Borrows == 0 && AudioClip.Created == 0, "only local Holy Hand Grenade draws load or borrow audio");
        });
        run("A spent Holy Hand Grenade cannot play a draw cue", () =>
        {
            var(p,a) = Scene(); var item = Item();
            check(ReusableGrenadeRuntime.Charges.Reserve(item.Id, 1, "spent", 300), "reserve final charge");
            ReusableGrenadeRuntime.Charges.Finish(item.Id, "spent", true); Draw(p,item);
            check(a.Borrows == 0 && AudioClip.Created == 0, "0-charge gate runs before audio loading");
        });
        run("Rapid draws restart the choir without overlapping or reloading the clip", () =>
        {
            var(p,a) = Scene(); Draw(p); var old = a.Borrowed.Single(); Draw(p);
            check(old.Releases == 1 && !old.Active && a.Borrowed.Count(x => x.Active) == 1, "old source returned before replacement playback");
            check(a.Borrowed.Last().Plays == 1 && AudioClip.Created == 1 && ReferenceEquals(old.Clip, a.Borrowed.Last().Clip), "single cached clip across copies");
        });
        run("Cue finishes using unscaled time and returns its native source exactly once", () =>
        {
            var(p,a) = Scene(); Draw(p); var s = a.Borrowed.Single();
            Time.unscaledTime += 2.49f; LegendaryGrenadeDrawAudio.Maintain(); check(s.Releases == 0, "cue remains active before end");
            Time.unscaledTime += .02f; LegendaryGrenadeDrawAudio.Maintain(); LegendaryGrenadeDrawAudio.Maintain();
            check(s.Releases == 1 && s.OnReleased == null && Time.time == 100, "unscaled completion cleans one source and callback");
        });
        run("Switching hands, dying, ending the raid and unloading stop the draw cue", () =>
        {
            foreach (var end in new[] { "switch", "death", "raid", "reset", "unload" })
            {
                var(p,a) = Scene(); Draw(p); var s = a.Borrowed.Single();
                if (end == "switch") p.HandsController = new();
                else if (end == "death") p.HealthController.IsAlive = false;
                else if (end == "raid") Singleton<GameWorld>.Instance = null!;
                else if (end == "reset") LegendaryGrenadeDrawAudio.Reset();
                else LegendaryGrenadeDrawAudio.Dispose();
                LegendaryGrenadeDrawAudio.Maintain(); check(s.Releases == 1 && s.OnReleased == null, end + " cleans source");
            }
        });
        run("Volume 0 mutes draws and stops a currently playing choir", () =>
        {
            var(p,a) = Scene(); Volume(0); Draw(p); check(a.Borrows == 0 && AudioClip.Created == 0, "muted draw creates no audio");
            Volume(.4f); Draw(p); var s = a.Borrowed.Single(); near(s.PlayVolume, .4f, "next draw takes new volume");
            Volume(0); LegendaryGrenadeDrawAudio.Maintain(); check(s.Releases == 1, "live mute stops active cue");
        });
        run("Draw cue volume clamps invalid and out-of-range configuration", () =>
        {
            Scene(); foreach (var(value,expected) in new[] { (-1f,0f),(2f,1f),(float.NaN,.5f),(float.PositiveInfinity,.5f),(.4f,.4f),(.75f,.75f) })
            { Volume(value); near(LegendaryGrenadeSettings.DrawSoundVolume,expected,"validated volume"); }
        });
        run("Unavailable audio initialization and empty pools leave grenade drawing unchanged", () =>
        {
            var(p,a) = Scene(); MonoBehaviourSingleton<BetterAudio>.Instance = null!; Draw(p); check(AudioClip.Created == 0,"unavailable controller is harmless");
            MonoBehaviourSingleton<BetterAudio>.Instance = a; a.ListenerTransform = null; Draw(p); check(a.Borrows == 0,"unavailable listener is harmless");
            a.ListenerTransform = new GameObject().transform; a.NoSources = true; Draw(p); check(a.Borrowed.Count == 0,"empty pool is harmless");
            a.NoSources = false; Draw(p); check(a.Borrowed.Single().Plays == 1,"later draw retries successfully");
        });
        run("Native source release and rejected sample upload cannot leak or replay the cue", () =>
        {
            var(p,a) = Scene(); Draw(p); var s = a.Borrowed.Single(); s.Release(); Time.unscaledTime += 3; LegendaryGrenadeDrawAudio.Maintain();
            check(s.Releases == 1 && s.OnReleased == null,"native release removes stale ownership");
            LegendaryGrenadeDrawAudio.Dispose(); AudioClip.RejectData = true; var count = a.Borrows;
            Draw(p); check(a.Borrows == count && !UnityEngine.Object.All.OfType<AudioClip>().Any(),"rejected samples destroy clip before borrowing source");
            AudioClip.RejectData = false; Draw(p); check(a.Borrows == count+1 && a.Borrowed.Last().Plays == 1,"failed upload retries on later draw");
        });
        run("PCM decoder rejects malformed, truncated and unsupported audio", () =>
        {
            Scene();
            using var stream = typeof(LegendaryGrenadeDrawAudio).Assembly.GetManifestResourceStream(LegendaryGrenadeDrawAudio.ResourceName)!;
            using var copy = new MemoryStream(); stream.CopyTo(copy); var bytes = copy.ToArray();
            var badHeader = bytes.ToArray(); badHeader[0] = 0;
            var badFormat = bytes.ToArray(); badFormat[20] = 3;
            foreach(var data in new[] { badHeader, badFormat, bytes.Take(30).ToArray() })
            {
                var rejected = false; try { WavePcm.Read(new MemoryStream(data)); } catch(Exception e) when (e is InvalidDataException or EndOfStreamException) { rejected = true; }
                check(rejected,"invalid audio rejected before Unity creation");
            }
        });
        LegendaryGrenadeDrawAudio.Dispose(); Volume(.5f);
    }
}
