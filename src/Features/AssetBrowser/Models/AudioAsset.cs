using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace JadeView;

sealed record AudioAsset(byte[] Wav, short[] Samples, int Channels, int Rate)
{
    public double Seconds => (double)Samples.Length / Channels / Rate;

    internal static AudioAsset FromSamples(short[] samples, int channels, int rate)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8); writer.Write(36 + samples.Length * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)channels); writer.Write(rate);
        writer.Write(rate * channels * 2); writer.Write((ushort)(channels * 2)); writer.Write((ushort)16);
        writer.Write("data"u8); writer.Write(samples.Length * 2);
        foreach (short sample in samples) writer.Write(sample);
        return new(output.ToArray(), samples, channels, rate);
    }
}

