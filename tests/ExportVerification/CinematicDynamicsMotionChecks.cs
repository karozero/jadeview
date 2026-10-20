using System.Numerics;
using JadeView;

internal static class CinematicDynamicsMotionChecks
{
    internal static void Verify()
    {
        var clip = new AnimationClip
        {
            Loaded = true,
            Duration = 1,
            Tracks =
        [new() { Bone = -1, Keys =
            [new() { Duration = 1, Position = Vector3.Zero, Rotation = Quaternion.Identity },
             new() { Time = 1, Position = new(8, 0, 0),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) }] }]
        };
        var dynamics = new CinematicAction(0, [new(clip, 0, 64, 1) { Flags = 0x16 }]);
        var animated = new CinematicAction(0, [new(clip, 0, 64, 1)]);
        foreach (float elapsed in new[] { .5f, 2.25f, .125f })
        {
            var pose = dynamics.Motion(elapsed, null, 0);
            if (pose.Translation.Length() > .0001f)
                throw new InvalidDataException("SpeedFromDyna accumulated animation translation.");
            var expected = animated.Motion(elapsed, null, 0);
            expected.Translation = Vector3.Zero;
            if (pose != expected)
                throw new InvalidDataException("SpeedFromDyna removed animation rotation.");
        }
        // A dynamics item must not discard displacement from an earlier ordinary item.
        var mixed = new CinematicAction(byte.MaxValue,
            [new(clip, 0, 64, 1), new(clip, 0, 64, 1) { Flags = 6 }]);
        if (Vector3.Distance(mixed.Motion(1.5f, null, 0).Translation, new(8, 0, 0)) > .001f)
            throw new InvalidDataException("Dynamics item lost preceding action displacement.");
        if (dynamics.Motion(.3f, null, 17).Translation.Length() > .0001f)
            throw new InvalidDataException("Start-frame offset introduced dynamics root translation.");
        Console.WriteLine("PASS: dynamics-driven actions retain rotation, cancel root travel, preserve prior items and support seeking/start offsets");
    }
}
