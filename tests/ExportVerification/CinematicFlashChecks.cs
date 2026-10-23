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
        var controller = new CinematicFlashController(99, 100, new(1, 0, 0), 1, 1, 2, .5f,
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
        if (timeline.Evaluate(.9f).Count != 0)
            throw new InvalidDataException("Inactive flash controller rendered outside its capability window.");
        var activeStates = timeline.Evaluate(1.5f);
        var appear = activeStates.Single();
        if (Math.Abs(appear.Opacity - .5f) > .001f || Vector3.Distance(appear.Position, new(11, 3, -2)) > .001f
            || appear.RayCount != 8 || appear.Color != controller.Color
            || appear.Seed != controller.OwnerKey
            || Math.Abs(appear.LengthRatio - .5f) > .001f)
            throw new InvalidDataException("Flash activation, fade-in, source world position or authored parameters were lost.");
        var disappearingStates = timeline.Evaluate(3.5f);
        var disappearing = disappearingStates.Single();
        if (!ReferenceEquals(activeStates, disappearingStates))
            throw new InvalidDataException("Flash evaluation allocated a new state collection instead of reusing it.");
        if (Math.Abs(disappearing.Opacity - .5f) > .001f
            || Vector3.Distance(disappearing.Position, appear.Position) > .001f)
            throw new InvalidDataException("Flash fade-out moved the native depth-sized GFX_Sun from its source position.");
        if (Math.Abs(timeline.Evaluate(4.5f).Single().Opacity - .5f) > .001f)
            throw new InvalidDataException("Flash reactivation did not start a new source-time fade-in.");
        for (int iteration = 0; iteration < 32; iteration++) _ = timeline.Evaluate(1.5f);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1024; iteration++) _ = timeline.Evaluate(1.5f);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (allocatedBytes != 0)
            throw new InvalidDataException($"Warmed flash playback allocated {allocatedBytes} bytes for 1024 updates.");
        VerifyNativeSunRayLayout();
        Console.WriteLine("PASS: cinematic flash activation, fades, allocation-free updates, snap position and ray parameters");
    }

    private static void VerifyNativeSunRayLayout()
    {
        var center = new System.Numerics.Vector2(20, 30);
        var firstLayout = CinematicSunRayLayout.Create(center, 8, 99,
            .5f, 4, 4, 1, 1, .2f, 2, 2, .5f, .5f, .5f, 2, 3, 1000);
        var repeatedLayout = CinematicSunRayLayout.Create(center, 8, 99,
            .5f, 4, 4, 1, 1, .2f, 2, 2, .5f, .5f, .5f, 2, 3, 1000);
        if (firstLayout.Length != 8 || !firstLayout.SequenceEqual(repeatedLayout)
            || firstLayout.Count(ray => ray.IsSmall) != 4)
            throw new InvalidDataException("Native sun-ray layout lost paired rays or deterministic jitter.");

        var bigRay = firstLayout[0];
        var forwardCenter = (bigRay.ForwardLeft + bigRay.ForwardRight) * .5f;
        if (Math.Abs(System.Numerics.Vector2.Distance(center, bigRay.Rear) - 8) > .001f
            || Math.Abs(System.Numerics.Vector2.Distance(center, forwardCenter) - 4) > .001f)
            throw new InvalidDataException("Native sun-ray triangle no longer uses the source length ratio.");

        var variedLayout = CinematicSunRayLayout.Create(center, 8, 99,
            .5f, 4, 6, 1, 2, .2f, 2, 4, .5f, 1, .5f, 2, 3, 1000);
        var bigRays = variedLayout.Where((_, index) => index % 2 == 0).ToArray();
        var firstDirection = center - bigRays[0].Rear;
        float firstAngle = MathF.Abs(MathF.Atan2(firstDirection.Y, firstDirection.X));
        float[] bigLengths = bigRays.Select(ray => System.Numerics.Vector2.Distance(center, ray.Rear)).ToArray();
        if (firstAngle > .5f || bigLengths.Any(length => length < 8 || length > 12)
            || bigLengths.Distinct().Count() < 2)
            throw new InvalidDataException($"Authored ray angle jitter and per-ray size ranges were not applied: angle={firstAngle}, lengths={string.Join(',', bigLengths)}.");

        var boundedLayout = CinematicSunRayLayout.Create(center, 8, 99,
            .5f, 150, 150, 150, 150, .2f, 100, 100, 100, 100, 3.2f, 2, 3, 100);
        float largestRadius = boundedLayout.SelectMany(ray => new[] { ray.Rear, ray.ForwardLeft, ray.ForwardRight })
            .Max(point => System.Numerics.Vector2.Distance(center, point));
        if (largestRadius > 100.01f)
            throw new InvalidDataException("Oversized cinematic flash rays exceeded the viewport preview radius.");
    }
}
