using System.Numerics;

namespace JadeView;

internal static class CinematicScriptTransformTracks
{
    private const int ScaleSetSelector = 0x1fbe;
    private const int ZoomSetSelector = 0x1fc0;

    internal static IEnumerable<AnimationTrack> Read(AnimationClip source)
    {
        var commands = CinematicLiteralCall.ReadTimeline(source)
            .Select(item => Decode(item.Time, item.Call))
            .OfType<ScaleCommand>()
            .GroupBy(command => command.Target);

        foreach (var group in commands)
        {
            var commandsByTime = group.OrderBy(command => command.Time).ToArray();
            var stateChanges = commandsByTime.GroupBy(command => command.Time)
                .Select(timeGroup => timeGroup.Last()).ToArray();
            var track = new AnimationTrack { Bone = -1, Target = group.Key };
            for (int index = 0; index < stateChanges.Length; index++)
            {
                var command = stateChanges[index];
                float nextTime = index + 1 < stateChanges.Length
                    ? stateChanges[index + 1].Time
                    : source.Duration;
                track.Keys.Add(new()
                {
                    Time = command.Time,
                    Duration = Math.Max(0, nextTime - command.Time),
                    Flags = 0x880,
                    Scale = command.Scale
                });
            }
            yield return track;
        }
    }

    private static ScaleCommand? Decode(float time, CinematicLiteralCall call)
    {
        if (call.Target is 0 or uint.MaxValue) return null;

        if (call.Selector == ScaleSetSelector && call.Arguments.Length == 3)
        {
            var scale = new Vector3(
                BitConverter.UInt32BitsToSingle(call.Arguments[0]),
                BitConverter.UInt32BitsToSingle(call.Arguments[1]),
                BitConverter.UInt32BitsToSingle(call.Arguments[2]));
            return float.IsFinite(scale.LengthSquared()) ? new(time, call.Target, scale) : null;
        }

        if (call.Selector == ZoomSetSelector && call.Arguments.Length == 1)
        {
            float zoom = BitConverter.UInt32BitsToSingle(call.Arguments[0]);
            return float.IsFinite(zoom) ? new(time, call.Target, new(zoom)) : null;
        }

        return null;
    }

    private sealed record ScaleCommand(float Time, uint Target, Vector3 Scale);
}
