using System.Numerics;

namespace JadeView;

internal sealed record CinematicCameraEvent(float Time, uint ReceiverKey, uint PositionKey,
    uint TargetKey, int Mode, float Transition, Vector3 PositionOffset, Vector3 TargetOffset,
    float FocalAngle = 0)
{
    internal static CinematicCameraEvent[] Read(AnimationClip clip) => clip.Tracks
        .Where(track => track.Bone < 0)
        .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads.SelectMany(payload =>
            CinematicOwnerContext.ForTrack(clip, track)
                .Select(context => Decode(key.Time, context, payload)))))
        .OfType<CinematicCameraEvent>().Distinct().OrderBy(item => item.Time).ToArray();

    private static CinematicCameraEvent? Decode(float time, uint trackTarget, AnimationOpaquePayload payload)
    {
        if (!CinematicFacialEvent.TryDecodeMessage(payload, out var message, allowLiteralFlags: true)
            || BitConverter.ToUInt32(message, 48) != 60) return null;
        uint receiver = BitConverter.ToUInt32(payload.Data, 4);
        if (receiver == 0) receiver = trackTarget;
        return new(time, receiver, BitConverter.ToUInt32(message, 0), BitConverter.ToUInt32(message, 4),
            BitConverter.ToInt32(message, 40), BitConverter.ToSingle(message, CinematicMessageFields.Float2),
            ReadVector(message, 8), ReadVector(message, 20), BitConverter.ToSingle(message, CinematicMessageFields.Float1));
    }

    internal bool HasLiteralPair => Mode is 1 or 2 && PositionKey is > 2 and < uint.MaxValue
        && TargetKey is > 2 and < uint.MaxValue;
    internal bool HasLiteralPosition => Mode is 1 or 2 && PositionKey is > 2 and < uint.MaxValue
        && (TargetKey == 0 || TargetKey is > 2 and < uint.MaxValue);

    private static Vector3 ReadVector(byte[] data, int offset) => new(
        BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));
}
