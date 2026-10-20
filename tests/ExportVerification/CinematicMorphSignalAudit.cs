using System.Buffers.Binary;
using JadeView;

internal static class CinematicMorphSignalAudit
{
    internal static object[] Collect(SceneDocument document)
    {
        var helpers = document.Worlds.SelectMany(world => world.Objects)
            .Where(sceneObject => sceneObject.Name.Contains("Cinemorph_ACME", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(sceneObject => sceneObject.GaoKey);

        return document.CinematicClips.Values
            .SelectMany(clip => clip.Tracks
                .Where(track => helpers.ContainsKey(track.Target))
                .SelectMany(track => track.Keys.Select(key => CreateKeyReport(
                    clip.Key, track.Target, helpers[track.Target].Name, key))))
            .ToArray();
    }

    private static object CreateKeyReport(uint clipKey, uint targetKey, string helperName, AnimationKey key) => new
    {
        Clip = clipKey.ToString("X8"),
        Target = targetKey.ToString("X8"),
        Name = helperName,
        key.Time,
        key.Duration,
        Position = VectorReport(key.Position),
        Scale = VectorReport(key.Scale),
        key.Rotation,
        key.Format,
        key.Flags,
        Opaque = key.OpaquePayloads.Select(payload => new
        {
            payload.Kind,
            Length = payload.Data.Length,
            Rows = ReadSignalRows(payload)
        }).ToArray()
    };

    private static object? VectorReport(System.Numerics.Vector3? value) => value is { } vector
        ? new { vector.X, vector.Y, vector.Z }
        : null;

    private static Dictionary<string, uint>[] ReadSignalRows(AnimationOpaquePayload payload)
    {
        if (payload.Kind != "word-pair-list" || payload.Data.Length < sizeof(uint)) return [];
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Data);
        if (count > int.MaxValue || payload.Data.Length != sizeof(uint) + count * 8) return [];

        return Enumerable.Range(0, (int)count).Select(index =>
        {
            int offset = sizeof(uint) + index * 8;
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Data.AsSpan(offset));
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(payload.Data.AsSpan(offset + sizeof(uint)));
            return new Dictionary<string, uint>
            {
                ["Value"] = value,
                ["Category"] = tag >> 24,
                ["Selector"] = tag & 0xffff
            };
        }).ToArray();
    }
}
