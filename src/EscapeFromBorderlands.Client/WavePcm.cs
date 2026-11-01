using System;
using System.IO;
using System.Text;

namespace EscapeFromBorderlands.Client;

// The bundled cue is little-endian PCM16 WAV. Decode in memory without a
// platform codec, a temporary file, or a runtime networking dependency.
internal sealed class WavePcm
{
    internal readonly int Channels, SampleRate;
    internal readonly float[] Samples;
    internal int Frames => Samples.Length / Channels;
    internal float Seconds => (float)Frames / SampleRate;
    private WavePcm(int channels, int sampleRate, float[] samples)
    { Channels = channels; SampleRate = sampleRate; Samples = samples; }

    internal static WavePcm Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("Expected RIFF audio.");
        var end = (long)reader.ReadUInt32() + 8;
        if (end > stream.Length || reader.ReadUInt32() != 0x45564157) throw new InvalidDataException("Invalid WAVE container.");
        int channels = 0, rate = 0, block = 0, bits = 0, format = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= end)
        {
            var chunk = reader.ReadUInt32(); var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (next > end || size > int.MaxValue) throw new InvalidDataException("Truncated WAVE chunk.");
            if (chunk == 0x20746d66)
            {
                if (size < 16) throw new InvalidDataException("Truncated WAVE format.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadUInt32(); block = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (chunk == 0x61746164) data = reader.ReadBytes((int)size);
            stream.Position = next + (size & 1);
        }
        if (format != 1 || channels < 1 || channels > 2 || rate <= 0 || bits != 16 || block != channels * 2 ||
            data is null || data.Length == 0 || data.Length % block != 0)
            throw new InvalidDataException("The draw cue must be mono/stereo PCM16 audio.");
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
        return new WavePcm(channels, rate, samples);
    }
}
