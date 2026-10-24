namespace JadeView;

internal static class CinematicEventExportInventoryBuilder
{
    private const string TransformControlPayload = "extra-transform-controls";
    private const string DecodedNotExecuted = "decoded_not_executed_by_exporter";

    internal static CinematicEventExportInventory Build(AnimationClip clip)
    {
        var rawRecords = new List<CinematicExportSourceRecord>();
        var events = new List<CinematicExportSemanticEvent>();
        int decodedRecordCount = 0;
        int unknownRecordCount = 0;
        int nonEventRecordCount = 0;

        foreach (var (track, trackIndex) in clip.Tracks.Select((track, index) => (track, index)))
        foreach (var (key, keyIndex) in track.Keys.Select((key, index) => (key, index)))
        foreach (var payload in key.OpaquePayloads)
        {
            int sourceRecordIndex = rawRecords.Count;
            rawRecords.Add(CreateSourceRecord(track, trackIndex, key, keyIndex, payload, sourceRecordIndex));
            if (track.Bone >= 0)
            {
                CountNonEventPayload(payload, ref unknownRecordCount, ref nonEventRecordCount);
                continue;
            }

            uint[] contexts = CinematicOwnerContext.ForTrack(clip, track);

            if (TryAddMessageEvents(key, payload, contexts, sourceRecordIndex,
                trackIndex, keyIndex, events))
            {
                decodedRecordCount++;
                continue;
            }

            if (TryAddLiteralCallEvents(track, key, payload, contexts, sourceRecordIndex,
                trackIndex, keyIndex, events))
            {
                decodedRecordCount++;
                continue;
            }

            CountNonEventPayload(payload, ref unknownRecordCount, ref nonEventRecordCount);
        }

        return new(1, rawRecords.Count, decodedRecordCount, unknownRecordCount,
            nonEventRecordCount, rawRecords.ToArray(), events.ToArray());
    }

    private static void CountNonEventPayload(AnimationOpaquePayload payload,
        ref int unknownRecordCount, ref int nonEventRecordCount)
    {
        if (payload.Kind == TransformControlPayload)
        {
            nonEventRecordCount++;
            return;
        }

        unknownRecordCount++;
    }

    private static CinematicExportSourceRecord CreateSourceRecord(AnimationTrack track, int trackIndex,
        AnimationKey key, int keyIndex, AnimationOpaquePayload payload, int sourceRecordIndex) =>
        new(sourceRecordIndex, trackIndex, keyIndex, key.Time, key.Duration, track.Bone,
            track.Target, track.ChannelFlags, track.HeaderWord, key.Flags, key.Format,
            payload.Kind, payload.SourceOffset, Convert.ToHexString(payload.Data));

    private static bool TryAddMessageEvents(AnimationKey key,
        AnimationOpaquePayload payload, uint[] contexts, int sourceRecordIndex, int trackIndex, int keyIndex,
        List<CinematicExportSemanticEvent> events)
    {
        var decodedMessages = contexts.Select(context =>
                CinematicTimelineMessage.TryReadPayload(key, payload, context))
            .Where(message => message != null)
            .Cast<CinematicTimelineMessage>()
            .ToArray();
        if (decodedMessages.Length == 0) return false;

        foreach (var message in decodedMessages)
        {
            events.Add(new(sourceRecordIndex, trackIndex, keyIndex, key.Time, key.Duration,
                message.ExecutionObjectKey, "message", DecodedNotExecuted,
                MessageType: message.Type, Message: new(message.Gao, message.Gao2,
                    [BitConverter.SingleToUInt32Bits(message.Vector1.X),
                        BitConverter.SingleToUInt32Bits(message.Vector1.Y),
                        BitConverter.SingleToUInt32Bits(message.Vector1.Z)],
                    [BitConverter.SingleToUInt32Bits(message.Vector2.X),
                        BitConverter.SingleToUInt32Bits(message.Vector2.Y),
                        BitConverter.SingleToUInt32Bits(message.Vector2.Z)],
                    BitConverter.SingleToUInt32Bits(message.Float1),
                    BitConverter.SingleToUInt32Bits(message.Float2), message.Int1, message.Int2)));
        }
        return true;
    }

    private static bool TryAddLiteralCallEvents(AnimationTrack track, AnimationKey key,
        AnimationOpaquePayload payload, uint[] contexts, int sourceRecordIndex, int trackIndex, int keyIndex,
        List<CinematicExportSemanticEvent> events)
    {
        var decodedCalls = contexts.Select(context =>
                (Context: context, Call: CinematicLiteralCall.Decode(payload, track.Target, context)))
            .Where(item => item.Call != null)
            .ToArray();
        if (decodedCalls.Length == 0) return false;

        foreach (var (context, call) in decodedCalls)
        {
            events.Add(new(sourceRecordIndex, trackIndex, keyIndex, key.Time, key.Duration,
                context, "literal_call", DecodedNotExecuted, call!.Target,
                call.Selector, CinematicEngineFunctionCatalog.NameFor(call.Selector), call.Arguments));
        }
        return true;
    }
}
