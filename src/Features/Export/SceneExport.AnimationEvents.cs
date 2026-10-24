using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    // Archival metadata only. A glTF importer must not execute these game records.
    private static void AddSourceAnimationRecords(Obj animation, AnimationClip clip)
    {
        var inventory = CinematicEventExportInventoryBuilder.Build(clip);
        if (inventory.RawRecordCount == 0) return;

        var rawRecords = inventory.SourceRecords.Select(record => D(
            "sourceRecordIndex", record.SourceRecordIndex,
            "trackIndex", record.TrackIndex, "keyIndex", record.KeyIndex,
            "time", record.Time, "duration", record.Duration,
            "bone", record.Bone, "target", record.Target.ToString("X8"),
            "channelFlags", record.ChannelFlags, "headerWord", record.HeaderWord.ToString("X8"),
            "flags", record.Flags, "format", record.Format,
            "kind", record.Kind, "sourceOffset", record.SourceOffset,
            "bytesHex", record.BytesHex)).ToArray();
        animation["extras"] = D(
            "jadeViewSourceRecords", D(
                "schemaVersion", 2, "clipKey", clip.Key.ToString("X8"),
                "encoding", "hexadecimal source bytes", "offsetOrigin", "clip start",
                "timeUnit", "seconds", "interpreted", false, "records", rawRecords),
            "jadeViewCinematicEvents", SerializeCinematicEvents(inventory, clip));
    }

    private static Obj SerializeCinematicEvents(CinematicEventExportInventory inventory, AnimationClip clip) =>
        D("schemaVersion", inventory.SchemaVersion, "clipKey", clip.Key.ToString("X8"),
            "rawRecordCount", inventory.RawRecordCount,
            "decodedRecordCount", inventory.DecodedRecordCount,
            "unknownRecordCount", inventory.UnknownRecordCount,
            "nonEventRecordCount", inventory.NonEventRecordCount,
            "interpretation", "Decoded source data only; callbacks and runtime effects are not executed by this export.",
            "events", inventory.Events.Select(SerializeCinematicEvent).ToArray());

    private static Obj SerializeCinematicEvent(CinematicExportSemanticEvent item)
    {
        var data = D("sourceRecordIndex", item.SourceRecordIndex,
            "trackIndex", item.TrackIndex, "keyIndex", item.KeyIndex,
            "time", item.Time, "duration", item.Duration,
            "executionContextKey", item.ExecutionContextKey.ToString("X8"),
            "kind", item.Kind, "status", item.Status);
        if (item.TargetKey is { } targetKey) data["targetKey"] = targetKey.ToString("X8");
        if (item.Selector is { } selector) data["selector"] = $"{selector:X4}";
        if (item.FunctionName is { } functionName) data["functionName"] = functionName;
        if (item.Arguments is { } arguments)
            data["arguments"] = arguments.Select(argument => $"{argument:X8}").ToArray();
        if (item.MessageType is { } messageType) data["messageType"] = messageType;
        if (item.Message is { } message)
            data["message"] = D("gao", message.Gao.ToString("X8"), "gao2", message.Gao2.ToString("X8"),
                "vector1Bits", message.Vector1Bits.Select(value => $"{value:X8}").ToArray(),
                "vector2Bits", message.Vector2Bits.Select(value => $"{value:X8}").ToArray(),
                "float1Bits", $"{message.Float1Bits:X8}", "float2Bits", $"{message.Float2Bits:X8}",
                "int1", message.Int1, "int2", message.Int2);
        return data;
    }
}
