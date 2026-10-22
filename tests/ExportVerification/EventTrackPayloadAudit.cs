using System.Text.Json;
using JadeView;

internal static class EventTrackPayloadAudit
{
    internal static void Write(string archivePath, string containerId, uint ownerKey,
        uint eventKey, int trackIndex, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var eventList = document.CinematicEmbeddedEventTracks.Values.SelectMany(lists => lists)
            .Single(candidate => candidate.OwnerKey == ownerKey && candidate.EventKey == eventKey
                && candidate.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase));
        var track = eventList.Tracks.Single(candidate => candidate.Index == trackIndex);
        if (track.SourceTrack == null)
            throw new InvalidDataException("Source event track data was not retained.");

        var payloads = track.SourceTrack.Keys.SelectMany((key, keyIndex) => key.OpaquePayloads
            .Select(payload => new
            {
                KeyIndex = keyIndex,
                key.Time,
                key.Duration,
                key.Flags,
                key.Format,
                payload.Kind,
                Bytes = payload.Data.Length,
                Rows = CinematicWordPairList.TryRead(payload, out var rows)
                    ? rows.Select((row, rowIndex) => new
                    {
                        Index = rowIndex,
                        row.Value,
                        Tag = $"0x{row.Tag:X8}",
                        Category = row.Tag >> 24,
                        Metadata = (row.Tag >> 16) & 0xff,
                        Type = row.Tag & 0xffff,
                        CandidateFunction = row.Value == 0 && row.Tag >> 24 == 3
                            ? CinematicEngineFunctionCatalog.NameFor((int)(row.Tag & 0xffff))
                            : null
                    }).ToArray()
                    : []
            })).ToArray();

        string path = Path.Combine(output, $"{containerId}-{eventKey:X8}-{ownerKey:X8}-EVE{trackIndex}-payloads.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Container = containerId,
            Owner = eventList.OwnerName,
            OwnerKey = ownerKey.ToString("X8"),
            Link = eventList.LinkName,
            Event = eventKey.ToString("X8"),
            Track = trackIndex,
            track.ChannelFlags,
            track.Bone,
            track.KeyCount,
            track.Duration,
            DecodedCalls = track.Calls.Select(item => new
            {
                item.Time,
                Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                Target = item.Call.Target.ToString("X8"),
                item.Call.Arguments
            }),
            DecodedMessages = track.Messages.Select(item => new { item.Time, item.Message.Type }),
            Payloads = payloads
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"EVENT TRACK PAYLOADS: {track.KeyCount} keys, {payloads.Length} payloads -> {path}");
    }
}
