using System.Numerics;
using JadeView;

internal static class CinematicPhaseTransitionChecks
{
    internal static void Verify()
    {
        VerifyPhase(1, 0, 1.125f);
        VerifyPhase(2, 0, 1.875f);
        VerifyPhase(3, 0, 1.125f); // Native forward flag takes priority.
        VerifyPhase(1, 16, 16 / 60f);
        VerifyPhase(1, 0, 1.125f, 0);
        Console.WriteLine("PASS: forward/inverse native transition phases, fractional frames, differing durations/rates, explicit frame override and reverse seek");
    }

    private static void VerifyPhase(byte flags, int frame, float expected, int incomingFrequency = 32)
    {
        var outgoing = Action(2, 64);
        var incoming = Action(3, incomingFrequency);
        outgoing = outgoing with { Items = [outgoing.Items[0] with { Transitions = [new(1, 1, flags, 0)] }] };
        var root = new SceneObject { GaoKey = 7 };
        var request = frame == 0 ? CinematicTransitionChecks.Command(.75f, 1)
            : CinematicTestEvents.Call(.75f, 7, 13211, 1, (uint)frame);
        var source = new AnimationClip
        {
            Loaded = true,
            Duration = 3,
            Tracks =
            [new() { Bone = -1, Target = 7, Keys = [CinematicTransitionChecks.Command(0, 0), request] }]
        };
        var timeline = CinematicBodyTimeline.Create(source, new() { Root = root, Members = [root] },
            new Dictionary<uint, Dictionary<int, CinematicAction>> { [7] = new() { [0] = outgoing, [1] = incoming } });
        foreach (float time in new[] { .75f, .85f, .75f })
        {
            float actual = timeline.Sample(time).Single().Time;
            float reference = expected + (time - .75f) * (incomingFrequency / 64f);
            if (MathF.Abs(actual - reference) > .0001f)
                throw new InvalidDataException($"Transition phase mismatch: {actual} vs {reference}, flags={flags}, frame={frame}.");
        }
        if (Vector3.Distance(timeline.RootPose(.75f).Translation, new(.75f, 0, 0)) > .0001f)
            throw new InvalidDataException("Transition phase teleported the actor to its new animation's root offset.");
    }

    private static CinematicAction Action(float duration, int frequency) => new(0,
        [new(new() { Loaded = true, Duration = duration, Tracks =
            [new() { Bone = 0, Keys = [new() { Duration = duration, Position = Vector3.Zero }] },
             new() { Bone = -1, Target = 0, Keys = [new() { Duration = duration, Position = Vector3.Zero },
                 new() { Time = duration, Position = new(duration, 0, 0) }] }] }, 0, frequency, 1)]);
}
