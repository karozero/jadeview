using JadeView;

internal static class CinematicAudioChecks
{
    internal static void Verify()
    {
        var modifier = new byte[132];
        Put(0, 273); Put(4, 0x800); Put(8, 0); Put(12, 77); Put(120, 2);
        BitConverter.GetBytes(1f).CopyTo(modifier, 16);
        var owner = new SceneObject { GaoKey = 44, Modifiers = [new(8, modifier)] };
        var world = new WorldData();
        world.Objects.Add(owner);
        var document = new SceneDocument();
        document.CinematicSoundBanks.Add(77, [0, uint.MaxValue, 55]);
        var source = new AnimationClip
        {
            Duration = 2,
            CinematicOwnerKey = 44,
            Tracks = [new() { Bone = -1, Target = 0, Keys =
            [
                CinematicTestEvents.Call(.5f, 0, 12058, 0),
                CinematicTestEvents.Call(1.5f, 0, 12060, 0)
            ] }]
        };
        var cues = CinematicAudioTimeline.Build(source, world, document);
        Require(cues.Length == 1 && cues[0].SoundKey == 55 && cues[0].Start == .5f && cues[0].End == 1.5f,
            "Owner context, preserved bank slot or start/stop timing was lost");
        var audio = AudioAsset.FromSamples(Enumerable.Repeat((short)1000, 24000).ToArray(), 1, 24000);
        var mix = CinematicAudioMixer.Mix(cues, 2, key => key == 55 ? audio : null);
        Require(Sample(.25f) == 0 && Sample(.75f) == 1000 && Sample(1.75f) == 0,
            "Mixed audio did not honor source cue boundaries");
        Require(mix.Audio.Samples[36000 * 2] == mix.Audio.Samples[36000 * 2 + 1], "Mono source was not mapped to stereo");
        var overlapping = CinematicAudioMixer.Mix([cues[0], cues[0] with { Volume = .5f }], 2, _ => audio);
        Require(overlapping.Audio.Samples[36000 * 2] == 1500, "Simultaneous audio cues were not mixed");
        var ramp = AudioAsset.FromSamples([0, 1000, 2000, 3000], 1, 24000);
        var resampled = CinematicAudioMixer.Mix([cues[0]], 2, _ => ramp);
        Require(resampled.Audio.Samples[24001 * 2] == 500, "PCM sample-rate conversion lost interpolation");
        Console.WriteLine("PASS: source sound-bank slots, owner context, timed audio start/stop, mixing and PCM resampling");

        short Sample(float time) => mix.Audio.Samples[(int)(time * CinematicAudioMixer.SampleRate) * 2];
        void Put(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(modifier, offset);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
