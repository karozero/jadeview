using System.Text;
using System.Numerics;
using JadeView;

internal static class CinematicHierarchyTrackAudit
{
    internal static void Write(string archivePath, string container, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        var objectsByKey = document.Worlds.SelectMany(world => world.Objects)
            .GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var tracks = new List<TrackSource>();

        foreach (var clip in document.CinematicClips.Values)
        foreach (var (track, index) in clip.Tracks.Select((track, index) => (track, index)))
            tracks.Add(new($"clip {clip.Key:X8}/{clip.Name} channel {index}", track,
                clip.CinematicOwnerKey, track.Target == 0 ? clip.CinematicOwnerKey : track.Target,
                track.UsesFlashReference));

        foreach (var eventLists in document.CinematicEmbeddedEventTracks.Values)
        foreach (var eventList in eventLists)
        foreach (var eventTrack in eventList.Tracks)
            if (eventTrack.SourceTrack is { } track)
            {
                uint effectiveTarget = eventTrack.TargetKey == 0 ? eventList.OwnerKey : eventTrack.TargetKey;
                tracks.Add(new($"{eventList.LinkName} owner {eventList.OwnerKey:X8} event {eventList.EventKey:X8} channel {eventTrack.Index}",
                    track, eventList.OwnerKey, effectiveTarget,
                    CinematicNestedEventTransformTracks.UsesFlashReference(eventList, eventTrack,
                        effectiveTarget)));
            }

        var rows = tracks.Where(item => item.EffectiveTargetKey is > 2 and < uint.MaxValue
                && objectsByKey.TryGetValue(item.EffectiveTargetKey, out var matches) && matches.Length == 1
                && !string.IsNullOrWhiteSpace(matches[0].ParentId))
            .Select(item =>
            {
                SceneObject sceneObject = objectsByKey[item.EffectiveTargetKey][0];
                SceneObject? parent = TryResolve(objectsByKey, sceneObject.ParentId);
                AnimationKey? firstTransformKey = item.Track.Keys.FirstOrDefault(key =>
                    key.Position.HasValue || key.Rotation.HasValue || key.Scale.HasValue);
                return new
                {
                    Source = item.Source,
                    OwnerKey = item.OwnerKey.ToString("X8"),
                    TargetKey = item.EffectiveTargetKey.ToString("X8"),
                    TargetName = sceneObject.Name,
                    TargetFlags = $"0x{sceneObject.Flags:X8}",
                    ParentKey = sceneObject.ParentId,
                    ParentName = parent?.Name,
                    HasSerializedLocal = sceneObject.SerializedHierarchyLocalMatrix.HasValue,
                    HasReferenceMatrix = (sceneObject.Flags & 0x04000000) != 0,
                    item.UsesFlashReference,
                    UsesFlashFlag = item.Track.Keys.Any(key => (key.Flags & 4) != 0),
                    FlashEventTimes = item.Track.Keys.Where(key => (key.Flags & 4) != 0)
                        .Select(key => key.Time).ToArray(),
                    HasFlashCompressedRotation = item.Track.Keys.Any(key => key.FlashCompressedRotation),
                    ChannelFlags = $"0x{item.Track.ChannelFlags:X4}",
                    item.Track.Bone,
                    Keys = item.Track.Keys.Count,
                    FirstKeyFlags = firstTransformKey is null ? null : $"0x{firstTransformKey.Flags:X4}",
                    FirstKeyTime = firstTransformKey?.Time,
                    FirstPosition = firstTransformKey?.Position is Vector3 firstPosition
                        ? new[] { firstPosition.X, firstPosition.Y, firstPosition.Z } : null,
                    FirstRotation = firstTransformKey?.Rotation is Quaternion firstRotation
                        ? new[] { firstRotation.X, firstRotation.Y, firstRotation.Z, firstRotation.W } : null,
                    FirstScale = firstTransformKey?.Scale is Vector3 firstScale
                        ? new[] { firstScale.X, firstScale.Y, firstScale.Z } : null
                };
            })
            .ToArray();

        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, $"{entry.Id}-hierarchy-transform-tracks.json");
        File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(rows,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"HIERARCHY EVE TRACK AUDIT: {entry.Id}; matchedTracks={rows.Length}; report={reportPath}");
        Console.WriteLine($"HIERARCHY EVE TRACK FLAGS: flashMarked={rows.Count(row => row.UsesFlashFlag)}; "
            + $"flashReferenceContext={rows.Count(row => row.UsesFlashReference)}; "
            + $"withLocalMatrix={rows.Count(row => row.HasSerializedLocal)}; "
            + $"uniqueParent={rows.Count(row => row.ParentName != null)}");
    }

    private static SceneObject? TryResolve(IReadOnlyDictionary<uint, SceneObject[]> objectsByKey, string? keyText)
    {
        if (!uint.TryParse(keyText, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint key)
            || !objectsByKey.TryGetValue(key, out var matches) || matches.Length != 1) return null;
        return matches[0];
    }

    private sealed record TrackSource(string Source, AnimationTrack Track, uint OwnerKey,
        uint EffectiveTargetKey, bool UsesFlashReference);
}
