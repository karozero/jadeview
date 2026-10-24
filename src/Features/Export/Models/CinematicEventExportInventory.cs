namespace JadeView;

internal sealed record CinematicEventExportInventory(
    int SchemaVersion,
    int RawRecordCount,
    int DecodedRecordCount,
    int UnknownRecordCount,
    int NonEventRecordCount,
    CinematicExportSourceRecord[] SourceRecords,
    CinematicExportSemanticEvent[] Events);

internal sealed record CinematicExportSourceRecord(
    int SourceRecordIndex,
    int TrackIndex,
    int KeyIndex,
    float Time,
    float Duration,
    int Bone,
    uint Target,
    int ChannelFlags,
    uint HeaderWord,
    int Flags,
    int Format,
    string Kind,
    int SourceOffset,
    string BytesHex);

internal sealed record CinematicExportSemanticEvent(
    int SourceRecordIndex,
    int TrackIndex,
    int KeyIndex,
    float Time,
    float Duration,
    uint ExecutionContextKey,
    string Kind,
    string Status,
    uint? TargetKey = null,
    int? Selector = null,
    string? FunctionName = null,
    uint[]? Arguments = null,
    int? MessageType = null,
    CinematicExportMessageFields? Message = null);

internal sealed record CinematicExportMessageFields(
    uint Gao,
    uint Gao2,
    uint[] Vector1Bits,
    uint[] Vector2Bits,
    uint Float1Bits,
    uint Float2Bits,
    int Int1,
    int Int2);
