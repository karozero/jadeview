using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    // Archival metadata only. A glTF importer must not execute these game records.
    private static void AddSourceAnimationRecords(Obj animation, AnimationClip clip)
    {
        var records = clip.Tracks.SelectMany((track, trackIndex) =>
            track.Keys.SelectMany((key, keyIndex) => key.OpaquePayloads.Select(payload =>
                D("trackIndex", trackIndex, "keyIndex", keyIndex,
                    "time", key.Time, "duration", key.Duration,
                    "bone", track.Bone, "target", track.Target.ToString("X8"),
                    "channelFlags", track.ChannelFlags, "headerWord", track.HeaderWord.ToString("X8"),
                    "flags", key.Flags, "format", key.Format,
                    "kind", payload.Kind, "sourceOffset", payload.SourceOffset,
                    "bytesHex", Convert.ToHexString(payload.Data))))).ToArray();
        if (records.Length == 0) return;

        animation["extras"] = D("jadeViewSourceRecords", D(
            "schemaVersion", 1, "clipKey", clip.Key.ToString("X8"),
            "encoding", "hexadecimal source bytes", "offsetOrigin", "clip start",
            "timeUnit", "seconds", "interpreted", false, "records", records));
    }
}
