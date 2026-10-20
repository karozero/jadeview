using System.Numerics;
using JadeView;

internal static class CinematicShutterChecks
{
    internal static void Verify()
    {
        var owner = new SceneObject
        {
            GaoKey = 99,
            Name = "Arbitrary controller name",
            ControlFlags = CinematicObjectState.Inactive
        };
        var world = new WorldData();
        world.Objects.Add(owner);
        var document = new SceneDocument();
        document.CinematicShutterControllers.Add(99, new(.1f, .2f, .1f, new(.4f, .6f)));
        var source = new AnimationClip
        {
            Loaded = true,
            CinematicOwnerKey = 99,
            Duration = 6,
            Tracks =
        [new() { Bone = -1, Keys =
        [CinematicTestEvents.Call(1, 0, 8133, 0x300, 0),
            CinematicTestEvents.Call(2, 0, 8153, 0),
            CinematicTestEvents.Call(3, 0, 8133, 0x200, 0x100),
            CinematicTestEvents.Call(4, 0, 8113),
            CinematicTestEvents.Call(4.5f, 0, 8133, 0x300, 0),
            CinematicTestEvents.Call(5, 0, 8153, 0)] }]
        };
        var timeline = new CinematicShutterTimeline(source, world, document);
        if (timeline.TriggerCount != 3 || timeline.Evaluate(1.5f) != null || timeline.Evaluate(2.5f) != null
            || timeline.Evaluate(4.5f) != null)
            throw new InvalidDataException("Shutter activation ignored capabilities, inactive/destroy or its duration.");
        Check(2.05f, .5f);
        Check(2.2f, 1);
        Check(2.35f, .5f);
        Check(5.05f, .5f);
        Check(2.05f, .5f);
        Console.WriteLine("PASS: source shutter activation, controller capabilities, inactive/reactivation, close/hold/open and reverse seek");

        void Check(float time, float closure)
        {
            if (timeline.Evaluate(time) is not { } state || Math.Abs(state.Closure - closure) > .001f
                || Vector2.Distance(state.Center, new(.4f, .6f)) > .001f)
                throw new InvalidDataException($"Shutter phase mismatch at {time}.");
        }
    }
}
