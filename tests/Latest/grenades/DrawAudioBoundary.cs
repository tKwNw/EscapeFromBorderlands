// Explicit Unity/native audio boundary. The production cue, decoder and draw
// postfix execute unchanged; no real audio mixer is run by this fixture.
using UnityEngine;

public static class MonoBehaviourSingleton<T> where T : class { public static T Instance = null!; }
public class BetterAudio
{
    public enum AudioSourceGroupType { Gunshots, Nonspatial }
    public sealed class SourceGroup { public object MixerGroup { get; } = new(); public EFT.AudioGroupPreset Preset { get; } = new(); }
    public Transform? ListenerTransform = new GameObject().transform;
    public SourceGroup[] SourceGroups = [new(), new()];
    public readonly List<BetterSource> Borrowed = [];
    public bool NoSources; public int Borrows;
    public BetterSource? GetSource(AudioSourceGroupType group, bool activateSource, bool needUpdate)
    {
        Borrows++;
        if (NoSources) return null;
        var source = new BetterSource { Group = group, Active = activateSource, NeedUpdate = needUpdate };
        Borrowed.Add(source); return source;
    }
}
public class BetterSource : UnityEngine.Object
{
    public sealed class Channel { public float panStereo = -.8f; }
    public Channel source1 = new();
    public BetterAudio.AudioSourceGroupType Group;
    public bool Active, NeedUpdate, Stereo, Occlusion = true, Loop;
    public float Spatial = 1, Pitch, Volume, MaxDistance;
    public Vector3 Position; public object? Mixer;
    public Action<BetterSource>? OnReleased;
    public AudioClip? Clip; public float PlayVolume, Balance;
    public bool ForceStereo, OneShot; public int Plays, Releases;
    public void EnableStereo(bool value) => Stereo = value;
    public void SetSpatialBlend(float value) => Spatial = value;
    public void SetPitch(float value) => Pitch = value;
    public void IncludeInOcclusionProcess(bool value) => Occlusion = value;
    public void SetMixerGroup(object? mixer) => Mixer = mixer;
    public void SetBaseVolume(float volume) => Volume = volume;
    public void SetRolloff(float distance) => MaxDistance = distance;
    public virtual void Play(AudioClip clip1, AudioClip? clip2, float balance, float volume, bool forceStereo, bool oneShot)
    { Clip = clip1; Balance = balance; PlayVolume = volume; ForceStereo = forceStereo; OneShot = oneShot; Plays++; }
    public void Release() { Releases++; Active = false; OnReleased?.Invoke(this); }
}
namespace EFT { public sealed class AudioGroupPreset { public float DefaultMaxDistance = 60f; } }
namespace UnityEngine
{
    public sealed class AudioClip : Object
    {
        public static bool RejectData;
        public static int Created;
        public string Name = ""; public int Frames, Channels, Rate;
        public bool Stream; public float[] Data = [];
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)
        { Created++; return new() { Name = name, Frames = lengthSamples, Channels = channels, Rate = frequency, Stream = stream }; }
        public bool SetData(float[] data, int offsetSamples) { Data = data.ToArray(); return !RejectData; }
    }
}
