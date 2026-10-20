using System.Numerics;
using JadeView;

internal static class CinematicScaleChecks
{
    internal static void Verify()
    {
        var root = new SceneObject { GaoKey = 1 };
        var bone = new SceneObject
        {
            GaoKey = 3,
            ActorNode = new()
            { Local = Matrix4x4.CreateTranslation(Vector3.UnitX) }
        };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        root.Actor = bone.Actor = actor;
        var prop = new SceneObject { GaoKey = 2 };
        var world = new WorldData();
        world.Objects.AddRange([root, bone, prop]);
        world.Actors.Add(actor);
        var source = new AnimationClip
        {
            Key = 10,
            Loaded = true,
            Cinematic = true,
            Duration = 3,
            CinematicOwnerKey = 1,
            Tracks =
        [new() { Bone = -1, Target = 1, Keys = [new() { Duration = 3, Position = new(10, 0, 0) }] },
            new() { Bone = -1, Target = 2, Keys = [new() { Duration = 3, Position = Vector3.UnitY }] },
            new() { Bone = -1, Target = 0, Keys =
            [CinematicTestEvents.VectorCall(1, 0, 0x1fbe, new(2.5f)),
                CinematicTestEvents.VectorCall(1, 2, 0x1fbe, new(.5f)),
                CinematicTestEvents.VectorCall(2, 0, 0x1fbe, Vector3.One),
                CinematicTestEvents.VectorCall(2.5f, 0, 0x1fbe, new(float.NaN))] }]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(10, source);
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        Check(.5f, 1);
        Check(1.5f, 2.5f);
        if (Math.Abs(prop.PreviewMatrix!.Value.M11 - .5f) > .001f)
            throw new InvalidDataException("A scripted prop scale was lost.");
        Check(2.75f, 1);
        Check(.5f, 1);
        if (source.Tracks.Count != 3 || root.Transform.Scale != Vector3.One)
            throw new InvalidDataException("Script transform evaluation modified source data.");
        Console.WriteLine("PASS: literal vector scale calls, owner inheritance, actor/prop scale, STEP/reset, reverse seek and nonfinite rejection");

        void Check(float time, float scale)
        {
            playback.Seek(time);
            if (Math.Abs(root.PreviewMatrix!.Value.M11 - scale) > .001f
                || Vector3.Distance(bone.PreviewMatrix!.Value.Translation, new(10 + scale, 0, 0)) > .001f)
                throw new InvalidDataException($"Scripted scale/hierarchy mismatch at {time}.");
        }
    }
}
