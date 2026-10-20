using System.Numerics;
using JadeView;

internal static class CinematicFrequencyChecks
{
    internal static void Verify()
    {
        var root = new SceneObject { GaoKey = 1 };
        var pelvis = new SceneObject { GaoKey = 2, ActorNode = new() };
        var actor = new SceneActor { Root = root, Members = [root, pelvis], Bones = [pelvis] };
        var actionClip = new AnimationClip
        {
            Loaded = true,
            Duration = 10,
            Tracks =
        [new() { Bone = -1, Keys = [new() { Duration = 10, Position = Vector3.Zero },
            new() { Time = 10, Position = new(10, 0, 0) }] },
            new() { Bone = 0, Keys = [new() { Duration = 10, Position = Vector3.UnitZ },
                new() { Time = 10, Position = new(10, 0, 1) }] }]
        };
        var action = new CinematicAction(0, [new(actionClip, 0, 64, 1)]);
        var source = new AnimationClip
        {
            Loaded = true,
            Duration = 4,
            Tracks =
        [new() { Bone = -1, Target = 1, Keys =
        [CinematicTransitionChecks.Command(0, 0), CinematicTestEvents.Call(.5f, 1, 0x32c9, 0, 1),
            CinematicTestEvents.Call(1.5f, 1, 0x32c9, 0, 0),
            CinematicTestEvents.Call(2.5f, 1, 0x32c9, 0, 64),
            CinematicTestEvents.Call(3, 1, 0x339b, 0, 0)] }]
        };
        var timeline = CinematicBodyTimeline.Create(source, actor,
            new Dictionary<uint, Dictionary<int, CinematicAction>> { [1] = new() { [0] = action } });
        actor.Clip = new() { Loaded = true, Cinematic = true, Duration = 4, CinematicBody = timeline };
        Check(.5f, .5f);
        Check(1.5f, .5f + 1 / 60f);
        Check(2.5f, .5f + 1 / 60f);
        Check(3, 1 + 1 / 60f);
        Check(3.5f, 1.5f + 1 / 60f);
        Check(1, .5f + .5f / 60);
        if (timeline.ActionCount != 2) throw new InvalidDataException("Frequency changes restarted the body action.");
        var unrelated = CinematicTestEvents.Call(1, 1, 0x32c9, 1, 32);
        if (CinematicActionEvent.Decode(unrelated, 1, unrelated.OpaquePayloads[0]) != null)
            throw new InvalidDataException("A non-lead animation frequency changed the lead action.");
        var shortClip = new AnimationClip { Loaded = true, Duration = 1 };
        var twoItems = new CinematicAction(0, [new(shortClip, 0, 64, 1), new(shortClip, 0, 64, 1)]);
        float resetAtNextItem = CinematicActionClock.SourceElapsed(twoItems, 2, null, 0, [new(.5f, 32)]);
        var oneItem = new CinematicAction(0, [new(shortClip, 0, 64, 1)]);
        float retainedAtLoop = CinematicActionClock.SourceElapsed(oneItem, 3, null, 0, [new(.5f, 32)]);
        if (Math.Abs(resetAtNextItem - 1.5f) > .001f || Math.Abs(retainedAtLoop - 1.75f) > .001f)
            throw new InvalidDataException("Frequency override did not reset at a new item or persist on the same-item loop.");
        Console.WriteLine("PASS: in-place animation frequency changes, one-FPS snapshot, pause/resume, root/skeleton agreement, action reset and reverse seek");

        void Check(float time, float distance)
        {
            actor.Time = time;
            actor.Evaluate(0);
            if (Vector3.Distance(root.PreviewMatrix!.Value.Translation, new(distance, 0, 0)) > .001f
                || Vector3.Distance(pelvis.PreviewMatrix!.Value.Translation, new(distance, 0, 1)) > .001f)
                throw new InvalidDataException($"Frequency clock/root mismatch at {time}: {root.PreviewMatrix.Value.Translation}.");
        }
    }
}
