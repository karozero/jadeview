using System.Numerics;
using JadeView;

internal static class CinematicLocalRotationChecks
{
    internal static void Verify()
    {
        VerifyNativeLocalAxisRotation();
        VerifyTimeBasedCommandSampling();
        VerifyPauseAndResumeMapping();
        VerifyLiteralCallDuration();
        Console.WriteLine("PASS: cinematic OBJRotateLocalY source-time interpolation, EVE pause/resume mapping and seek reset");
    }

    private static void VerifyNativeLocalAxisRotation()
    {
        Matrix4x4 rotated = CinematicLocalRotationTimeline.RotateLocalYAxis(
            Matrix4x4.Identity, MathF.PI / 2);
        if (MathF.Abs(rotated.M11) > .001f || MathF.Abs(rotated.M13 + 1) > .001f
            || MathF.Abs(rotated.M31 - 1) > .001f || MathF.Abs(rotated.M33) > .001f
            || rotated.Translation != Vector3.Zero)
            throw new InvalidDataException("OBJRotateLocalY no longer matches the Jade row-basis rotation or changed translation.");
    }

    private static void VerifyTimeBasedCommandSampling()
    {
        var target = new SceneObject { GaoKey = 0x100, Transform = new() };
        var world = new WorldData();
        world.Objects.Add(target);
        var timeline = new CinematicLocalRotationTimeline(world,
        [
            new(0x100, 0, 1, MathF.PI / 2),
            new(0x100, 1, 2, MathF.PI / 2)
        ]);

        timeline.Evaluate(.5f);
        Matrix4x4 half = target.PreviewMatrix!.Value;
        if (MathF.Abs(half.M11 - MathF.Cos(MathF.PI / 4)) > .001f
            || MathF.Abs(half.M31 - MathF.Sin(MathF.PI / 4)) > .001f)
            throw new InvalidDataException("OBJRotateLocalY did not interpolate proportionally through its event duration.");

        timeline.ResetTargets();
        timeline.Evaluate(1.5f);
        Matrix4x4 threeQuarter = target.PreviewMatrix!.Value;
        if (MathF.Abs(threeQuarter.M11 - MathF.Cos(3 * MathF.PI / 4)) > .001f
            || MathF.Abs(threeQuarter.M31 - MathF.Sin(3 * MathF.PI / 4)) > .001f)
            throw new InvalidDataException("Seeking backward and forward accumulated rotation instead of rebuilding from the source pose.");
    }

    private static void VerifyPauseAndResumeMapping()
    {
        var clock = new CinematicTrackPlaybackClock(0, 2, 3, 0,
            [(0.5f, "TRAPause"), (1.5f, "TRAUnPause")]);
        var intervals = clock.TimelineIntervalsForLocalRange(0, 1).ToArray();
        if (intervals.Length != 2
            || MathF.Abs(intervals[0].TimelineStart) > .001f
            || MathF.Abs(intervals[0].TimelineEnd - .5f) > .001f
            || MathF.Abs(intervals[0].LocalDuration - .5f) > .001f
            || MathF.Abs(intervals[1].TimelineStart - 1.5f) > .001f
            || MathF.Abs(intervals[1].TimelineEnd - 2) > .001f
            || MathF.Abs(intervals[1].LocalDuration - .5f) > .001f)
            throw new InvalidDataException("EVE local command duration did not pause and resume on the cinematic timeline.");

        var target = new SceneObject { GaoKey = 0x200, Transform = new() };
        var world = new WorldData();
        world.Objects.Add(target);
        var timeline = new CinematicLocalRotationTimeline(world, intervals.Select(interval =>
            new CinematicLocalRotationCommand(0x200, interval.TimelineStart, interval.TimelineEnd,
                MathF.PI * interval.LocalDuration)));
        timeline.Evaluate(1);
        if (MathF.Abs(target.PreviewMatrix!.Value.M31 - 1) > .001f)
            throw new InvalidDataException("Paused EVE rotation continued changing while its playback clock was frozen.");
        timeline.ResetTargets();
        timeline.Evaluate(1.75f);
        if (MathF.Abs(target.PreviewMatrix!.Value.M31 - MathF.Sin(3 * MathF.PI / 4)) > .001f)
            throw new InvalidDataException("Resumed EVE rotation did not continue from the saved local-event phase.");
    }

    private static void VerifyLiteralCallDuration()
    {
        uint radians = BitConverter.SingleToUInt32Bits(MathF.PI);
        var key = CinematicTestEvents.Call(.25f, 0x300, 0x1FAA, radians);
        key.Duration = .75f;
        var direct = CinematicLiteralCall.Decode(key.OpaquePayloads.Single(), 0x300, 0x300);
        var clip = new AnimationClip { Tracks = [new() { Target = 0x300, Bone = -1, Keys = [key] }] };
        var decoded = CinematicLiteralCall.ReadTimelineWithDuration(clip);
        if (decoded.Length != 1)
            throw new InvalidDataException($"Expected one source literal, found {decoded.Length}; direct={(direct != null)}, "+
                $"ownerContexts={CinematicOwnerContext.ForTrack(clip, clip.Tracks[0]).Length}; function={CinematicEngineFunctionCatalog.NameFor(0x1FAA)}.");
        var command = CinematicLocalRotationTimeline.ReadSourceCommands(clip).Single();
        if (command.TargetKey != 0x300 || MathF.Abs(command.Start - .25f) > .001f
            || MathF.Abs(command.End - 1) > .001f || MathF.Abs(command.Radians - MathF.PI) > .001f)
            throw new InvalidDataException("OBJRotateLocalY literal decoding lost the authored event duration or inherited target.");
    }
}
