using System.Numerics;

namespace JadeView;

internal static class CinematicScriptTransformTracks
{
    private const int ScaleSetSelector = 0x1fbe;
    private const int VectorConstructorSelector = 0x1770;

    internal static IEnumerable<AnimationTrack> Read(AnimationClip source)
    {
        var commands = source.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.Select(payload =>
                Decode(key.Time, payload, track.Target == 0 ? source.CinematicOwnerKey : track.Target))))
            .OfType<ScaleCommand>().GroupBy(command => command.Target);
        foreach (var group in commands)
        {
            var commandsByTime = group.OrderBy(command => command.Time).ToArray();
            var track = new AnimationTrack { Bone = -1, Target = group.Key };
            for (int index = 0; index < commandsByTime.Length; index++)
            {
                var command = commandsByTime[index];
                float end = index + 1 < commandsByTime.Length ? commandsByTime[index + 1].Time : source.Duration;
                track.Keys.Add(new()
                {
                    Time = command.Time,
                    Duration = Math.Max(0, end - command.Time),
                    Flags = 0x880,
                    Scale = command.Scale
                });
            }
            yield return track;
        }
    }

    private static ScaleCommand? Decode(float time, AnimationOpaquePayload payload, uint inheritedTarget)
    {
        if (!CinematicWordPairList.TryRead(payload, out var rows) || rows.Length != 8) return null;
        bool supported = CinematicWordPairList.IsLiteral(rows[0], 0x28)
            && CinematicWordPairList.HasSelector(rows[1], 2, 2054)
            && rows[2..5].All(row => CinematicWordPairList.IsLiteral(row, 0x22))
            && CinematicWordPairList.HasSelector(rows[5], 3, VectorConstructorSelector)
            && CinematicWordPairList.HasSelector(rows[6], 3, ScaleSetSelector)
            && CinematicWordPairList.IsEnd(rows[7]);
        if (!supported) return null;
        uint target = rows[0].Value == 0 ? inheritedTarget : rows[0].Value;
        var scale = new Vector3(BitConverter.UInt32BitsToSingle(rows[2].Value),
            BitConverter.UInt32BitsToSingle(rows[3].Value), BitConverter.UInt32BitsToSingle(rows[4].Value));
        if (target is 0 or uint.MaxValue || !float.IsFinite(scale.LengthSquared())) return null;
        return new(time, target, scale);
    }

    private sealed record ScaleCommand(float Time, uint Target, Vector3 Scale);
}
