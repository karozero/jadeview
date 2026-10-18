using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace JadeView;

sealed record AudioAsset(byte[] Wav, short[] Samples, int Channels, int Rate)
{
    public double Seconds => (double)Samples.Length / Channels / Rate;
}

