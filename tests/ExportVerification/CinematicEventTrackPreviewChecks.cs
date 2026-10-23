using System.Numerics;
using JadeView;

internal static class CinematicEventTrackPreviewChecks
{
    internal static void Verify()
    {
        var owner = new SceneObject { Name = "EVE owner", GaoKey = 1 };
        var target = new SceneObject
        {
            Name = "EVE transform target",
            GaoKey = 2,
            Transform = new() { Translation = new(2, 0, 0) }
        };
        var secondTarget = new SceneObject
        {
            Name = "Second EVE transform target",
            GaoKey = 3,
            Transform = new() { Translation = new(3, 0, 0) }
        };
        var world = new WorldData { Key = 100 };
        world.Objects.AddRange([owner, target, secondTarget]);
        var sourceClip = new AnimationClip
        {
            Key = 50,
            CinematicWorldKey = world.Key,
            Cinematic = true,
            Loaded = true,
            Duration = 4,
            Tracks =
            [
                new() { Bone = -1, Target = owner.GaoKey, Keys = [new() { Duration = 4 }] },
                new() { Bone = -1, Target = target.GaoKey, Keys = [new() { Duration = 4, Position = new(5, 0, 0) }] },
                new() { Bone = -1, Target = secondTarget.GaoKey, Keys = [new() { Duration = 4, Position = new(9, 0, 0) }] }
            ]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(sourceClip.Key, sourceClip);
        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(.5f);

        var eventTrack = new AnimationTrack
        {
            Bone = -1,
            Target = target.GaoKey,
            Keys =
            [
                new() { Time = 0, Duration = 1, Scale = new(2) },
                new() { Time = 1, Duration = 1, Scale = new(.5f) }
            ]
        };
        if (!playback.PreviewEventTrack(target, eventTrack))
            throw new InvalidDataException("A decoded EVE transform channel was rejected for manual preview.");
        RequireScale(2, "EVE preview did not start at the current cinematic playhead.");
        if (!playback.IsPreviewingEventTrack(target, eventTrack))
            throw new InvalidDataException("The active EVE transform channel was not retained.");

        playback.Seek(1);
        RequireScale(1.25f, "EVE transform keys did not follow the shared cinematic clock.");
        playback.Seek(3);
        RequireScale(.5f, "EVE transform preview did not hold the final channel pose.");
        playback.Seek(.25f);
        RequireScale(1, "Seeking before the manual EVE start did not restore the source clip transform.");

        playback.ClearEventTrackPreview();
        RequireScale(1, "Clearing EVE preview did not restore the source clip transform.");
        playback.Seek(1);
        RequireScale(1, "Cleared EVE preview reappeared after a later seek.");

        var secondEventTrack = new AnimationTrack
        {
            Bone = -1,
            Target = secondTarget.GaoKey,
            Keys = [new() { Time = 0, Duration = 1, Scale = new(3) }]
        };
        if (!playback.PreviewEventTrack(target, eventTrack)
            || !playback.PreviewEventTrack(secondTarget, secondEventTrack))
            throw new InvalidDataException("Switching between manual EVE transform targets failed.");
        if (target.PreviewMatrix is not Matrix4x4 restoredTargetMatrix
            || Math.Abs(restoredTargetMatrix.Translation.X - 5) > .001f
            || secondTarget.PreviewMatrix is not Matrix4x4 secondTargetMatrix
            || Math.Abs(secondTargetMatrix.M11 - 3) > .001f)
            throw new InvalidDataException("Switching the EVE target left a stale transform preview on the previous object.");

        var boneTrack = new AnimationTrack { Bone = 0, Target = target.GaoKey, Keys = [new() { Scale = Vector3.One }] };
        if (CinematicEventTrackPreview.HasTransformData(boneTrack)
            || CinematicEventTrackPreview.HasTransformData(new AnimationTrack { Bone = -1 }))
            throw new InvalidDataException("EVE preview exposed unsupported bone or empty channels as root transforms.");

        playback.Deactivate();
        Console.WriteLine("PASS: manual EVE transform preview shares cinematic time, seeks deterministically and restores source motion");

        void RequireScale(float expected, string message)
        {
            float actual = target.PreviewMatrix is Matrix4x4 matrix ? matrix.M11 : 1;
            if (Math.Abs(actual - expected) > .001f) throw new InvalidDataException(message);
        }
    }
}
