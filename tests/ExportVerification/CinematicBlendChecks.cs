using System.Numerics;
using JadeView;

internal static class CinematicBlendChecks
{
    internal static void Verify()
    {
        var root = new SceneObject { GaoKey = 1 };
        var bone = new SceneObject { GaoKey = 2, ActorNode = new() { Parent = 1 } };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        var idle = Action(0, 0);
        idle = idle with { Items = [idle.Items[0] with { Transitions = [new(1, 1, 0, 3)] }] };
        var moving = Action(10, MathF.PI / 2);
        var source = new AnimationClip
        {
            Loaded = true,
            Duration = 3,
            Tracks =
            [new() { Bone = -1, Target = 1, Keys =
                [CinematicTransitionChecks.Command(0, 0), CinematicTransitionChecks.Command(1, 1)] }]
        };
        actor.Clip = new()
        {
            Loaded = true,
            Cinematic = true,
            Duration = 3,
            CinematicBody = CinematicBodyTimeline.Create(source, actor,
                new Dictionary<uint, Dictionary<int, CinematicAction>> { [1] = new() { [0] = idle, [1] = moving } })
        };

        // Independent scalar recurrence from the native stock-pose blend at 60 Hz.
        Check(1, 2.5f, MathF.PI / 8);
        Check(1 + 1 / 60f, 6.25f, MathF.PI * .3125f);
        Check(1 + 2 / 60f, 9.0625f, MathF.PI * .453125f);
        Check(1 + 3 / 60f, 10, MathF.PI / 2);
        Check(.5f, 0, 0);
        Check(1 + 1 / 60f, 6.25f, MathF.PI * .3125f);
        Check(1 + .5f / 60, 4.375f, MathF.PI * .21875f);
        var exportTimes = AnimationExportSampling.Times(actor.Clip);
        if (!exportTimes.Contains(MathF.BitDecrement(1)) || !exportTimes.Contains(1 + 3 / 60f))
            throw new InvalidDataException("Export sampling omitted an action boundary or blend endpoint.");
        actor.Clip.Tracks.Add(new() { Bone = 0, Keys = [new() { Duration = 3, Position = new(100, 0, 0) }] });
        actor.Evaluate(0);
        if (Math.Abs(bone.PreviewMatrix!.Value.Translation.X - 100) > .001f)
            throw new InvalidDataException("Body blending overwrote an explicit cinematic bone track.");
        moving = moving with { Items = [moving.Items[0] with { Transitions = [new(2, 2, 0, 3)] }] };
        source.Tracks[0].Keys.Add(CinematicTransitionChecks.Command(1 + 1 / 60f, 2));
        actor.Clip = new()
        {
            Loaded = true,
            Cinematic = true,
            Duration = 3,
            CinematicBody = CinematicBodyTimeline.Create(source, actor,
                new Dictionary<uint, Dictionary<int, CinematicAction>>
                { [1] = new() { [0] = idle, [1] = moving, [2] = Action(20, 0) } })
        };
        actor.Time = 1 + 1 / 60f;
        actor.Evaluate(0);
        if (Math.Abs(bone.PreviewMatrix!.Value.Translation.X - 9.6875f) > .001f)
            throw new InvalidDataException("An interrupted blend did not start from the preceding blended pose.");
        Console.WriteLine("PASS: stock pose blend recurrence, quaternion interpolation, endpoints, fractional time, reverse seek and cinematic overrides");

        void Check(float time, float position, float angle)
        {
            actor.Time = time;
            actor.Evaluate(0);
            var matrix = bone.PreviewMatrix!.Value;
            Vector3 forward = Vector3.TransformNormal(Vector3.UnitX, matrix);
            if (Math.Abs(matrix.Translation.X - position) > .001f
                || Vector3.Distance(forward, new(MathF.Cos(angle), MathF.Sin(angle), 0)) > .001f)
                throw new InvalidDataException($"Stock blend mismatch at {time}: {matrix.Translation}, {forward}.");
        }
    }

    private static CinematicAction Action(float position, float angle) => new(0, [new(new AnimationClip
    {
        Loaded = true, Duration = 2, Tracks = [new() { Bone = 0, Keys = [new()
        {
            Duration = 2, Position = new(position, 0, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle)
        }] }]
    }, 0, 64, 1)]);
}
