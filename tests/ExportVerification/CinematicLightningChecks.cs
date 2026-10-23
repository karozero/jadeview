using System.Numerics;
using JadeView;

internal static class CinematicLightningChecks
{
    internal static void Verify()
    {
        var owner = new SceneObject { GaoKey = 99, Name = "Lightning owner" };
        var destination = new SceneObject
        {
            GaoKey = 100,
            Name = "Lightning target",
            Transform = new() { Translation = new(10, 2, 3) }
        };
        var world = new WorldData();
        world.Objects.AddRange([owner, destination]);
        var controller = new CinematicLightningController(99, 99, 100, 0, 0, 1,
            .5f, .5f, .5f, .5f, 1, 1, .1f, .2f, 8, 0,
            0, 0xff40c0ff, 0xff40c0ff, 1);
        var source = new AnimationClip
        {
            Loaded = true,
            CinematicOwnerKey = 99,
            Duration = 5,
            Tracks =
        [new() { Bone = -1, Keys =
        [CinematicTestEvents.Call(1, 99, 8133, 1, 0),
            CinematicTestEvents.Call(3, 99, 8133, 0, 1),
            CinematicTestEvents.Call(4, 99, 8133, 1, 0)] }]
        };
        var timeline = new CinematicLightningTimeline(source, world,
            new Dictionary<uint, CinematicLightningController> { [99] = controller });
        if (timeline.Evaluate(.9f).Count != 0)
            throw new InvalidDataException("Inactive lightning capability rendered before activation.");
        var activeStates = timeline.Evaluate(1.5f);
        var fadeIn = activeStates.Single();
        if (Math.Abs(fadeIn.Opacity - .5f) > .001f || fadeIn.Color != controller.OnColorFirst
            || Vector3.Distance(fadeIn.Start, Vector3.Zero) > .001f
            || Vector3.Distance(fadeIn.End, new(10, 3, -2)) > .001f)
            throw new InvalidDataException($"Lightning activation/fade/endpoints mismatch: opacity={fadeIn.Opacity}, color={fadeIn.Color:X8}, start={fadeIn.Start}, end={fadeIn.End}.");
        var inactiveStates = timeline.Evaluate(3.5f);
        if (!ReferenceEquals(activeStates, inactiveStates))
            throw new InvalidDataException("Lightning evaluation allocated a new state collection instead of reusing it.");
        if (Math.Abs(inactiveStates.Single().Opacity - .5f) > .001f
            || Math.Abs(timeline.Evaluate(4.5f).Single().Opacity - .5f) > .001f)
            throw new InvalidDataException("Lightning fade-out or capability reactivation was not repeatable.");

        owner.InitialCapabilities = 1;
        var timedController = controller with
        {
            Mode = 2,
            CapabilityMask = 3,
            OnDelayMin = .5f,
            OnDelayMax = .5f,
            OffDelayMin = .5f,
            OffDelayMax = .5f,
            FadeIn = .1f,
            FadeOut = .1f
        };
        var timedSource = new AnimationClip
        {
            Loaded = true,
            CinematicOwnerKey = 99,
            Duration = 4,
            Tracks =
        [new() { Bone = -1, Keys =
        [CinematicTestEvents.Call(1, 99, 8133, 0, 1),
            CinematicTestEvents.Call(2, 99, 8133, 2, 0)] }]
        };
        var timedTimeline = new CinematicLightningTimeline(timedSource, world,
            new Dictionary<uint, CinematicLightningController> { [99] = timedController });
        if (Math.Abs(timedTimeline.Evaluate(2.05f).Single().Opacity - .5f) > .001f
            || Math.Abs(timedTimeline.Evaluate(2.55f).Single().Opacity - .5f) > .001f)
            throw new InvalidDataException("Capability-gated lightning timers lost ordering or treated a multi-bit mask as all-bits-required.");
        Console.WriteLine("PASS: scripted lightning capability masks, timed transitions, deterministic fades and world endpoints");
    }
}

