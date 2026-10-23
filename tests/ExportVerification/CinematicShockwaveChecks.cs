using JadeView;

internal static class CinematicShockwaveChecks
{
    internal static void Verify()
    {
        const uint ownerKey = 0x3500c01f;
        const float radius = 12;
        var owner = new SceneObject { GaoKey = ownerKey };
        var world = new WorldData();
        world.Objects.Add(owner);
        var timeline = new CinematicShockwaveTimeline(new AnimationClip(), world,
            new Dictionary<uint, CinematicShockwaveController>
            {
                [ownerKey] = new(ownerKey, radius)
            });

        var active = timeline.Evaluate(.5f);
        Require(active.Count == 1 && Math.Abs(active[0].Zoom - radius * MathF.Sqrt(.5f)) < .0001f
            && !owner.CinematicExcluded,
            "Active shockwave preview must preserve its deterministic radius and visibility.");
        var finished = timeline.Evaluate(1.5f);
        Require(ReferenceEquals(active, finished) && finished.Count == 1 && finished[0].Finished
            && finished[0].Zoom == 0 && owner.CinematicExcluded,
            "Shockwave updates should reuse the frame buffer and exclude completed effects.");

        for (int iteration = 0; iteration < 32; iteration++)
            _ = timeline.Evaluate(.5f);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1024; iteration++)
            _ = timeline.Evaluate(.5f);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(allocatedBytes == 0,
            $"Shockwave evaluation allocated {allocatedBytes} bytes for 1024 warmed frames.");
        timeline.Clear();
        Require(owner.CinematicZoom == null && !owner.CinematicExcluded,
            "Clearing shockwave preview must restore its transient object state.");
        Console.WriteLine("PASS: shockwave playback buffer reuse, allocation-free evaluation and preview cleanup");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
