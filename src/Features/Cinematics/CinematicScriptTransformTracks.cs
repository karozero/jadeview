using System.Numerics;

namespace JadeView;

internal static class CinematicScriptTransformTracks
{
    private const int ScaleSetSelector = 0x1fbe;
    private const int ZoomSetSelector = 0x1fc0;
    private const int PositionSetSelector = 0x1fa6;
    private const int PositionTeleportSetSelector = 0x1ffb;

    internal static IEnumerable<AnimationTrack> Read(AnimationClip source,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? additionalCalls = null)
    {
        var calls = CinematicLiteralCall.ReadTimeline(source).Concat(additionalCalls ?? [])
            .Where(item => float.IsFinite(item.Time))
            .OrderBy(item => item.Time);
        return calls.Select(item => Decode(item.Time, item.Call))
            .OfType<TransformCommand>()
            .GroupBy(command => (command.Target, command.Component))
            .Select(group => BuildTrack(group.Key.Target, group.Key.Component, group, source.Duration));
    }

    private static AnimationTrack BuildTrack(uint target, TransformComponent component,
        IEnumerable<TransformCommand> commands, float clipDuration)
    {
        TransformCommand[] stateChanges = commands.OrderBy(command => command.Time)
            .GroupBy(command => command.Time).Select(timeGroup => timeGroup.Last()).ToArray();
        var track = new AnimationTrack { Bone = -1, Target = target };
        for (int index = 0; index < stateChanges.Length; index++)
        {
            TransformCommand command = stateChanges[index];
            float nextTime = index + 1 < stateChanges.Length
                ? stateChanges[index + 1].Time
                : clipDuration;
            track.Keys.Add(new()
            {
                Time = command.Time,
                Duration = Math.Max(0, nextTime - command.Time),
                Flags = 0x880,
                Position = component == TransformComponent.Position ? command.Value : null,
                Scale = component == TransformComponent.Scale ? command.Value : null
            });
        }
        return track;
    }

    private static TransformCommand? Decode(float time, CinematicLiteralCall call)
    {
        if (call.Target is 0 or uint.MaxValue) return null;

        if (call.Selector == ScaleSetSelector && call.Arguments.Length == 3)
        {
            Vector3 scale = ReadVector(call.Arguments);
            return float.IsFinite(scale.LengthSquared())
                ? new(time, call.Target, TransformComponent.Scale, scale) : null;
        }

        if (call.Selector == ZoomSetSelector && call.Arguments.Length == 1)
        {
            float zoom = BitConverter.UInt32BitsToSingle(call.Arguments[0]);
            return float.IsFinite(zoom)
                ? new(time, call.Target, TransformComponent.Scale, new(zoom)) : null;
        }

        if (call.Selector is PositionSetSelector or PositionTeleportSetSelector
            && call.Arguments.Length == 3)
        {
            Vector3 position = ReadVector(call.Arguments);
            return IsFinite(position)
                ? new(time, call.Target, TransformComponent.Position, position) : null;
        }

        return null;
    }

    private static Vector3 ReadVector(uint[] arguments) => new(
        BitConverter.UInt32BitsToSingle(arguments[0]),
        BitConverter.UInt32BitsToSingle(arguments[1]),
        BitConverter.UInt32BitsToSingle(arguments[2]));

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private enum TransformComponent { Position, Scale }
    private sealed record TransformCommand(float Time, uint Target, TransformComponent Component, Vector3 Value);
}
