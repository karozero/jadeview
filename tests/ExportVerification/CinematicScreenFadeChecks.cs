using JadeView;

internal static class CinematicScreenFadeChecks
{
    internal static void Verify()
    {
        var world = new WorldData();
        world.Objects.Add(new() { GaoKey = 7, Name = "Arbitrary global controller" });
        world.Objects.Add(new() { GaoKey = 8, Name = "Other script" });
        var document = new SceneDocument();
        document.CinematicFadeReceivers.Add(7);
        var source = new AnimationClip
        {
            Loaded = true,
            CinematicOwnerKey = 7,
            Duration = 3,
            Tracks =
        [new() { Bone = -1, Keys =
        [CinematicTestEvents.Message(1, 0, 50, new(.1f, .2f, .3f), new(.4f, .1f, 0), 0, uint.MaxValue),
         CinematicTestEvents.Message(1.1f, 8, 50, new(0, 0, 1), new(1, 0, 0), 0, uint.MaxValue),
         CinematicTestEvents.Message(1.5f, 0, 50, new(), new(.2f, 0, 0), 0, uint.MaxValue),
         CinematicTestEvents.Message(2, 0, 50, new(float.NaN, 0, 1), new(), 0, uint.MaxValue),
         CinematicTestEvents.Message(2.1f, 0, 50, new(0, 0, 1), new(), 0, uint.MaxValue, scalar2: 1)] }]
        };
        var timeline = new CinematicScreenFadeTimeline(source, world, document);
        if (timeline.Count != 2 || timeline.Evaluate(.5f) != null || timeline.Evaluate(1.71f) != null)
            throw new InvalidDataException("Fade receiver validation, replacement or malformed timing failed.");
        Check(1.2f, .5f);
        Check(1.4f, 1);
        Check(1.55f, .75f);
        Check(1.2f, .5f);
        Console.WriteLine("PASS: source-qualified screen fades, delay/rise/hold/fall, replacement, reverse seek and malformed input");

        void Check(float time, float expected)
        {
            if (timeline.Evaluate(time) is not { } color || Math.Abs(color.W - expected) > .001f
                || Math.Abs(color.X - expected) > .001f)
                throw new InvalidDataException($"Screen fade mismatch at {time}.");
        }
    }
}
