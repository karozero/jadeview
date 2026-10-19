using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace JadeView;
internal static class PcSceneObjectEncoder
{
    internal static byte[] Write(SceneStructureChange change, Geometry geometry, uint geometryKey, uint? brainKey, byte[] header, PcSceneLoader.Record? copySource, byte[] original)
    {
        var matrix = JadeMatrix.Identity;
        matrix.SetTransform(change.Transform.ToTranslation(), change.Transform.ToRotation(), change.Transform.ToScale());
        if (!matrix.IsValid)
            throw new InvalidDataException($"Invalid transform for '{change.Name}'.");
        bool preserveBounds = copySource != null && JadeMatrix.Read(original.AsSpan(copySource.Offset + 22, JadeMatrix.SizeInBytes)).Scale == change.Transform.ToScale();
        uint flags = ObjectFlags(change, copySource, preserveBounds);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, true);
        writer.Write(0); // Framed payload length, filled after serialization.
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), flags);
        writer.Write(header);
        writer.Write(matrix.ToByteArray());
        WriteBounds(writer, change, geometry, copySource, original, preserveBounds);
        if (copySource != null)
            WriteCopiedExtensions(writer, copySource, original, geometryKey, brainKey);
        else
            WriteAppearance(writer, change, geometryKey);
        WriteName(writer, change.Name);
        byte[] frame = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length - 4);
        return frame;
    }

    private static uint ObjectFlags(SceneStructureChange change, PcSceneLoader.Record? copySource, bool preserveBounds)
    {
        if (copySource == null)
            return 0x5000u | (change.Modifiers.Count > 0 ? 0x2000u : 0u);
        return preserveBounds ? copySource.Flags : copySource.Flags & ~SceneLoader.IdentityFlagOBBox;
    }

    private static int BoundsSize(PcSceneLoader.Record record) => (record.Flags & SceneLoader.IdentityFlagOBBox) != 0 ? 48 : 24;
    private static void WriteBounds(BinaryWriter writer, SceneStructureChange change, Geometry geometry, PcSceneLoader.Record? copySource, byte[] original, bool preserveBounds)
    {
        if (preserveBounds)
        {
            writer.Write(original.AsSpan(copySource!.Offset + 90, BoundsSize(copySource)));
            return;
        }

        Vector3 scaledMin = geometry.Min * change.Transform.ToScale();
        Vector3 scaledMax = geometry.Max * change.Transform.ToScale();
        WriteVector(writer, Vector3.Min(scaledMin, scaledMax));
        WriteVector(writer, Vector3.Max(scaledMin, scaledMax));
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteCopiedExtensions(BinaryWriter writer, PcSceneLoader.Record copySource, byte[] original, uint geometryKey, uint? brainKey)
    {
        int offset = copySource.Offset + 90 + BoundsSize(copySource);
        byte[] extensions = original.AsSpan(offset, copySource.NameFieldOffset - offset).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(extensions, geometryKey);
        if (brainKey.HasValue)
            BinaryPrimitives.WriteUInt32LittleEndian(extensions.AsSpan(copySource.BrainFieldOffset - offset, 4), brainKey.Value);
        writer.Write(extensions);
    }

    private static void WriteAppearance(BinaryWriter writer, SceneStructureChange change, uint geometryKey)
    {
        writer.Write(geometryKey);
        writer.Write(change.MaterialKey == 0 ? uint.MaxValue : change.MaterialKey);
        writer.Write(change.DrawFlags);
        writer.Write(0u);
        writer.Write(change.RliColors.Length);
        foreach (uint color in change.RliColors)
            writer.Write(color);
        if (change.Modifiers.Count == 0)
            return;
        writer.Write(uint.MaxValue);
        writer.Write(1u);
        writer.Write(new byte[14]);
        foreach (var modifier in change.Modifiers)
        {
            writer.Write(modifier.Type);
            writer.Write(0u);
            writer.Write(modifier.Data);
        }

        writer.Write(uint.MaxValue);
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        bool isLatin1Name = Encoding.Latin1.GetString(Encoding.Latin1.GetBytes(name)) == name && !name.Contains('\0');
        if (!isLatin1Name)
            throw new InvalidDataException("Object names must use the game's Latin-1 character set without embedded nulls.");
        byte[] bytes = Encoding.Latin1.GetBytes(name + "\0");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
