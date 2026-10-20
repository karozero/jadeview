using System.Numerics;
using JadeView;

internal static class CinematicFlashChecks
{
    internal static void Verify()
    {
        var owner = new SceneObject { GaoKey = 99, Name = "Controller carrier" };
        var target = new SceneObject
        {
            GaoKey = 100,
            Name = "Animated target",
            Transform = new() { Translation = new(10, 2, 3) }
        };
        var world = new WorldData();
        world.Objects.AddRange([owner, target]);
        var controller = new CinematicFlashController(99, 100, new(1, 0, 0), 1, 1, 2,
            0xff40c0ff, 8, .8f, 4, 6, .5f, 1, .4f, 2, 3, .25f, .5f, 0);
        var source = new AnimationClip
        {
            Loaded = true,
            CinematicOwnerKey = 99,
            Duration = 5,
            Tracks =
        [new() { Bone = -1, Keys =
        [CinematicTestEvents.Call(1, 0, 8133, 0x100, 0),
            CinematicTestEvents.Call(3, 0, 8133, 0, 0x100),
            CinematicTestEvents.Call(4, 0, 8133, 0x100, 0)] }]
        };
        var timeline = new CinematicFlashTimeline(source, world, new Dictionary<uint, CinematicFlashController>
        { [99] = controller });
        if (timeline.Evaluate(.9f).Length != 0)
            throw new InvalidDataException("Inactive flash controller rendered outside its capability window.");
        var appear = timeline.Evaluate(1.5f).Single();
        if (Math.Abs(appear.Opacity - .5f) > .001f || Vector3.Distance(appear.Position, new(11, 3, -2)) > .001f
            || appear.RayCount != 8 || appear.Color != controller.Color)
            throw new InvalidDataException("Flash activation, fade-in, snap position or authored parameters were lost.");
        if (Math.Abs(timeline.Evaluate(3.5f).Single().Opacity - .5f) > .001f)
            throw new InvalidDataException("Flash fade-out did not follow its authored delay.");
        if (Math.Abs(timeline.Evaluate(4.5f).Single().Opacity - .5f) > .001f)
            throw new InvalidDataException("Flash reactivation did not start a new source-time fade-in.");
        Console.WriteLine("PASS: cinematic flash activation, deterministic fades, snap position and ray parameters");
    }
}
