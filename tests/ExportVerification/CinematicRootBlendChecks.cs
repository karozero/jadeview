using System.Numerics;
using JadeView;

internal static class CinematicRootBlendChecks
{
    internal static void Verify()
    {
        var blended = Timeline(0);
        Check(blended, 1, 22);
        Check(blended, 1 + 1 / 60f, 22 + 4 / 60f);
        Check(blended, 1 + 2 / 60f, 22 + 9 / 60f);
        Check(blended, 1 + 3 / 60f, 22.25f);
        Check(blended, 1.1f, 22.55f); // Keep the blended offset after the transition.
        Check(blended, .5f, 21);
        Check(blended, 1 + 1 / 60f, 22 + 4 / 60f);
        Check(Timeline(0x08), 1 + 1 / 60f, 22.1f);
        Check(Timeline(0, dynamics: true), 1 + 1 / 60f, 20.1f);

        var rotated = Timeline(0, rotate: true).RootPose(1 + 1 / 60f);
        var forward = Vector3.TransformNormal(Vector3.UnitX, rotated);
        float angle = MathF.PI / 2 + MathF.PI / 120;
        if (Vector3.Distance(forward, new(MathF.Cos(angle), MathF.Sin(angle), 0)) > .001f)
            throw new InvalidDataException("Root blending changed the incoming action's rotation.");
        var placed = Timeline(0, absolute: true).RootPose(1 + 1 / 60f);
        if (Vector3.Distance(placed.Translation, new(100, 50, 0)) > .001f)
            throw new InvalidDataException("Root blending overwrote an authored cinematic placement.");
        Console.WriteLine("PASS: root translation blending, persistent offsets, rotated actors, lead rotation, freeze/dynamics bypass, seek and absolute placement");
    }

    private static void Check(CinematicBodyTimeline timeline, float time, float expectedY)
    {
        var position = timeline.RootPose(time).Translation;
        if (Vector3.Distance(position, new(10, expectedY, 0)) > .001f)
            throw new InvalidDataException($"Blended root at {time}: {position}, expected Y {expectedY}.");
    }

    private static CinematicBodyTimeline Timeline(byte flags, bool dynamics = false, bool rotate = false, bool absolute = false)
    {
        var outgoing = Action(2);
        outgoing = outgoing with
        {
            Items = [outgoing.Items[0] with
            { Flags = dynamics ? (byte)6 : (byte)0, Transitions = [new(1, 1, flags, 3)] }]
        };
        var incoming = Action(6);
        if (rotate) incoming.Items[0].Clip.Tracks.Add(new()
        {
            Bone = -1,
            Keys =
            [new() { Duration = 1, Rotation = Quaternion.Identity },
                new() { Time = 1, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) }]
        });
        var root = new SceneObject
        {
            GaoKey = 1,
            Transform = new()
            {
                Translation = new(10, 20, 0),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)
            }
        };
        var source = new AnimationClip
        {
            Loaded = true,
            Duration = 2,
            Tracks =
            [new() { Bone = -1, Target = 1, Keys =
                [CinematicTransitionChecks.Command(0, 0), CinematicTransitionChecks.Command(1, 1)] }]
        };
        if (absolute) source.Tracks.Add(new()
        {
            Bone = -1,
            Target = 1,
            Keys = [new() { Time = 1 + 1 / 60f, Duration = 1, Position = new(100, 50, 0) }]
        });
        return CinematicBodyTimeline.Create(source, new() { Root = root, Members = [root] },
            new Dictionary<uint, Dictionary<int, CinematicAction>> { [1] = new() { [0] = outgoing, [1] = incoming } });
    }

    private static CinematicAction Action(float speed) => new(0, [new(new AnimationClip
    {
        Loaded = true, Duration = 1, Tracks = [new() { Bone = -1, Keys =
            [new() { Duration = 1, Position = Vector3.Zero }, new() { Time = 1, Position = new(speed, 0, 0) }] }]
    }, 0, 64, 1)]);
}
