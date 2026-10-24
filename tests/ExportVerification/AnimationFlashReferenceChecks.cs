using System.Numerics;
using JadeView;

internal static class AnimationFlashReferenceChecks
{
    internal static void Verify()
    {
        byte[] bytes = BuildFlashTracks();
        AnimationClip clip = AnimationReader.Read(bytes, 0, bytes.Length);
        if (clip.Tracks.Count != 4)
            throw new InvalidDataException("Animation reader lost a Flash-reference track.");

        AnimationTrack firstTargetPosition = clip.Tracks[0];
        AnimationTrack sameTargetRotation = clip.Tracks[1];
        AnimationTrack ordinaryTarget = clip.Tracks[2];
        AnimationTrack compactRotation = clip.Tracks[3];
        if (!firstTargetPosition.UsesFlashReference || !sameTargetRotation.UsesFlashReference)
            throw new InvalidDataException("Flash reference was not inherited by a later channel for the same target.");
        if (ordinaryTarget.UsesFlashReference)
            throw new InvalidDataException("A Flash channel leaked its reference state to a different target.");
        if (!compactRotation.UsesFlashReference || !compactRotation.Keys[0].FlashCompressedRotation
            || MathF.Abs(compactRotation.Keys[0].Duration - 5f / 60f) > .00001f
            || compactRotation.Keys[0].Rotation is not { } rotation
            || MathF.Abs(rotation.X) + MathF.Abs(rotation.Y) + MathF.Abs(rotation.Z)
                + MathF.Abs(1 - MathF.Abs(rotation.W)) > .0001f)
            throw new InvalidDataException("Compact Flash quaternion encoding was not decoded with its masked frame duration.");

        Console.WriteLine("PASS: EVE Flash reference scope, inherited target channels and compact quaternion timing");
    }

    private static byte[] BuildFlashTracks()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)4);
        writer.Write((ushort)0);

        WriteTrackHeader(70);
        writer.Write((ushort)1);
        writer.Write((ushort)0x84);
        writer.Write((ushort)12);
        writer.Write((ushort)1);
        WriteVector(1, 0, 0);

        WriteTrackHeader(70);
        writer.Write((ushort)1);
        writer.Write((ushort)0x80);
        writer.Write((ushort)16);
        writer.Write((ushort)16);
        writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);

        WriteTrackHeader(71);
        writer.Write((ushort)1);
        writer.Write((ushort)0x80);
        writer.Write((ushort)12);
        writer.Write((ushort)1);
        WriteVector(3, 0, 0);

        WriteTrackHeader(72);
        writer.Write((ushort)0x8005);
        writer.Write((short)0); writer.Write((short)0); writer.Write((short)0);
        return stream.ToArray();

        void WriteTrackHeader(uint target)
        {
            writer.Write((ushort)0);
            writer.Write((short)-1);
            writer.Write(target);
            writer.Write(target);
            writer.Write(1u);
        }

        void WriteVector(float x, float y, float z)
        {
            writer.Write(x); writer.Write(y); writer.Write(z);
        }
    }
}
