using System.Buffers.Binary;
using System.Numerics;

namespace JadeView;

internal sealed record CinematicTimelineMessage(
    float Time,
    uint ExecutionObjectKey,
    uint Gao,
    uint Gao2,
    Vector3 Vector1,
    Vector3 Vector2,
    float Float1,
    float Float2,
    int Int1,
    int Int2,
    int Type)
{
    internal static (float Time, CinematicTimelineMessage Message)[] ReadTimeline(AnimationClip clip,
        uint fallbackOwnerKey = 0)
    {
        return clip.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key =>
                CinematicOwnerContext.ForTrack(clip, track, fallbackOwnerKey)
                    .Select(contextKey => Decode(key, contextKey))))
            .Where(item => item.Message != null)
            .Select(item => (item.Time, item.Message!))
            .OrderBy(item => item.Time)
            .ToArray();
    }

    private static (float Time, CinematicTimelineMessage? Message) Decode(AnimationKey key, uint contextKey)
    {
        foreach (var payload in key.OpaquePayloads)
            if (TryReadPayload(key, payload, contextKey) is { } message)
                return (key.Time, message);

        return (key.Time, null);
    }

    internal static CinematicTimelineMessage? ReadMessage(AnimationKey key, uint contextKey) =>
        Decode(key, contextKey).Message;

    internal static CinematicTimelineMessage? TryReadPayload(AnimationKey key,
        AnimationOpaquePayload payload, uint contextKey)
    {
        if (!CinematicFacialEvent.TryDecodeMessage(payload, out byte[] message, allowLiteralFlags: true))
            return null;

        return new(key.Time, contextKey,
            ReadUInt32(message, CinematicMessageFields.Gao),
            ReadUInt32(message, CinematicMessageFields.Gao2),
            ReadVector3(message, CinematicMessageFields.Vector1),
            ReadVector3(message, CinematicMessageFields.Vector2),
            ReadSingle(message, CinematicMessageFields.Float1),
            ReadSingle(message, CinematicMessageFields.Float2),
            ReadInt32(message, CinematicMessageFields.Int1),
            ReadInt32(message, CinematicMessageFields.Int2),
            ReadInt32(message, CinematicMessageFields.Type));
    }

    private static uint ReadUInt32(byte[] source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset, sizeof(uint)));

    private static int ReadInt32(byte[] source, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, sizeof(int)));

    private static float ReadSingle(byte[] source, int offset) =>
        BitConverter.Int32BitsToSingle(ReadInt32(source, offset));

    private static Vector3 ReadVector3(byte[] source, int offset) => new(
        ReadSingle(source, offset), ReadSingle(source, offset + sizeof(float)),
        ReadSingle(source, offset + sizeof(float) * 2));
}
