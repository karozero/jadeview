using System.Globalization;

namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const string CinematicStartCallbackForMessageGate = "Cine_Events_cine_tests_depart";
    private const string HelperTextStartCallbackForMessageGate = "helper_text_hlptxt_tests_depart";

    private static void AddCinematicMessageGateBehavior(List<string> diagnostics,
        CinematicScriptOwner owner, int testsMask)
    {
        const int messageGateMask = 0x020;
        if ((testsMask & messageGateMask) == 0) return;

        int? expectedMessageId = EffectiveInteger(owner, "des_i_msg_ID");
        int? expectedInt1Mask = EffectiveInteger(owner, "des_i_msg_INT1");
        string messageIdDescription = expectedMessageId is { } messageId
            ? messageId.ToString(CultureInfo.InvariantCulture)
            : "unavailable";
        string int1Description = expectedInt1Mask switch
        {
            0 => "Int1 is not filtered because the configured mask is zero",
            { } mask => $"Int1 must overlap mask 0x{unchecked((uint)mask):X8}",
            _ => "the optional Int1 mask is unavailable"
        };

        diagnostics.Add($"Message start gate reads the queue of {DescribeObjectReference(owner, "des_o_acteur_msg")}; "
            + $"it accepts queued message ID {messageIdDescription} when {int1Description}. "
            + "Iris checks at most the first four queued entries with MSGRead_C, which reads without consuming them. "
            + "A match is required for the gate to pass; the live queue is not available in this archive snapshot.");
    }

    internal static CinematicScriptMessageGateCandidate[]? FindMessageGateCandidates(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences)
        => FindMessageGateCandidates(gateOwner, eventTracks, namedReferences, requireQueueContext: true);

    internal static CinematicScriptMessageGateCandidate[]? FindMessageGateContextMismatches(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences)
        => FindMessageGateCandidates(gateOwner, eventTracks, namedReferences, requireQueueContext: false);

    internal static CinematicScriptEngineMessageGateCandidate[]? FindExtendedMessageGateCandidates(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences)
        => FindExtendedMessageGateCandidates(gateOwner, eventTracks, namedReferences,
            requireQueueTarget: true, requireInt1MaskCompatibility: true);

    internal static CinematicScriptEngineMessageGateCandidate[]? FindExtendedMessageGateTargetMismatches(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences)
        => FindExtendedMessageGateCandidates(gateOwner, eventTracks, namedReferences,
            requireQueueTarget: false, requireInt1MaskCompatibility: true);

    internal static CinematicScriptEngineMessageGateCandidate[]? FindExtendedMessageGateInt1MaskMismatches(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences)
        => FindExtendedMessageGateCandidates(gateOwner, eventTracks, namedReferences,
            requireQueueTarget: true, requireInt1MaskCompatibility: false);

    private static CinematicScriptMessageGateCandidate[]? FindMessageGateCandidates(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences, bool requireQueueContext)
    {
        bool hasCinematicMessageGate = HasCinematicFunctionReference(gateOwner,
                CinematicStartCallbackForMessageGate)
            && (DeriveStartGateMask(gateOwner) & 0x020) != 0;
        bool hasHelperTextMessageGate = CinematicScriptReader.TryDescribeCallbackReachability(gateOwner,
                HelperTextStartCallbackForMessageGate, out _)
            && HasObjectReference(gateOwner, "des_o_acteur_msg");
        if (!hasCinematicMessageGate && !hasHelperTextMessageGate)
            return null;

        uint? queueKey = EffectiveUnsigned(gateOwner, "des_o_acteur_msg");
        int? expectedMessageId = EffectiveInteger(gateOwner, "des_i_msg_ID");
        int? expectedInt1Mask = EffectiveInteger(gateOwner, "des_i_msg_INT1");
        if (queueKey is null or 0 or 1 or 2 or uint.MaxValue || expectedMessageId == null
            || expectedInt1Mask == null)
            return [];

        var referenceArray = namedReferences.ToArray();
        var queueObject = CinematicScriptReferenceNameResolver.Resolve(queueKey.Value,
            gateOwner.WorldKey, referenceArray);
        if (queueObject == null) return [];

        var resolvedCandidates = eventTracks
            .SelectMany(track => track.Messages
                .Where(entry => entry.Message.Type == expectedMessageId
                    && (expectedInt1Mask == 0 || (expectedInt1Mask.Value & entry.Message.Int1) != 0))
                .Select(entry => (Track: track, entry.Time, entry.Message)))
            .Select(candidate =>
            {
                var contextObject = CinematicScriptReferenceNameResolver.Resolve(
                    candidate.Message.ExecutionObjectKey, candidate.Track.WorldKey, referenceArray);
                bool contextMatchesQueue = contextObject is { } resolvedContext
                    && resolvedContext.WorldKey == queueObject.WorldKey
                    && resolvedContext.Key == queueObject.Key;
                return contextObject is { } resolvedExecutionContext
                    && contextMatchesQueue == requireQueueContext
                    ? new CinematicScriptMessageGateCandidate(candidate.Track, candidate.Time,
                        candidate.Message, queueObject, resolvedExecutionContext, [candidate.Track])
                    : null;
            })
            .OfType<CinematicScriptMessageGateCandidate>()
            .GroupBy(candidate => (candidate.Track.WorldKey, candidate.Track.EventKey, candidate.Time,
                candidate.Message))
            .Select(group =>
            {
                var sources = group.SelectMany(candidate => candidate.Sources).Distinct().ToArray();
                var primary = group.OrderBy(candidate => SourcePriority(candidate.Track))
                    .ThenBy(candidate => candidate.Track.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
                return primary with { Sources = sources };
            })
            .OrderBy(candidate => candidate.Track.WorldKey)
            .ThenBy(candidate => candidate.Track.OwnerKey)
            .ThenBy(candidate => candidate.Time)
            .ToArray();
        return resolvedCandidates;
    }

    private static CinematicScriptEngineMessageGateCandidate[]? FindExtendedMessageGateCandidates(
        CinematicScriptOwner gateOwner, IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences, bool requireQueueTarget,
        bool requireInt1MaskCompatibility)
    {
        if (!HasMessageStartGate(gateOwner)) return null;

        uint? queueKey = EffectiveUnsigned(gateOwner, "des_o_acteur_msg");
        int? expectedMessageId = EffectiveInteger(gateOwner, "des_i_msg_ID");
        int? expectedInt1Mask = EffectiveInteger(gateOwner, "des_i_msg_INT1");
        if (queueKey is null or 0 or 1 or 2 or uint.MaxValue || expectedMessageId == null
            || expectedInt1Mask is null)
            return [];

        // MSGSendEx zero-initializes its message and writes only sender, msgID, and f2.
        // A nonzero Int1 mask rejects it; a zero mask is not an Int1 mismatch.
        bool int1MaskCompatible = expectedInt1Mask.Value == 0;
        if (int1MaskCompatible != requireInt1MaskCompatibility) return [];

        var referenceArray = namedReferences.ToArray();
        var queueObject = CinematicScriptReferenceNameResolver.Resolve(queueKey.Value,
            gateOwner.WorldKey, referenceArray);
        if (queueObject == null) return [];

        var resolvedCandidates = eventTracks
            .SelectMany(track => track.EngineCalls
                .Where(entry => CinematicEngineFunctionCatalog.NameFor(entry.Call.Selector) == "MSGSendEx"
                    && entry.Call.Arguments.Length >= 2
                    && unchecked((int)entry.Call.Arguments[0]) == expectedMessageId)
                .Select(entry => (Track: track, entry.Time, entry.Call)))
            .Select(candidate =>
            {
                var targetObject = CinematicScriptReferenceNameResolver.Resolve(
                    candidate.Call.Target, candidate.Track.WorldKey, referenceArray);
                if (targetObject == null) return null;
                bool targetMatchesQueue = targetObject.WorldKey == queueObject.WorldKey
                    && targetObject.Key == queueObject.Key;
                return targetMatchesQueue == requireQueueTarget
                    ? new CinematicScriptEngineMessageGateCandidate(candidate.Track, candidate.Time,
                        candidate.Call, queueObject, targetObject, [candidate.Track])
                    : null;
            })
            .OfType<CinematicScriptEngineMessageGateCandidate>()
            .GroupBy(candidate => (candidate.Track.WorldKey, candidate.Track.EventKey,
                candidate.Time, candidate.Call.Target, Selector: candidate.Call.Selector,
                Arguments: string.Join(",", candidate.Call.Arguments)))
            .Select(group =>
            {
                var sources = group.SelectMany(candidate => candidate.Sources).Distinct().ToArray();
                var primary = group.OrderBy(candidate => SourcePriority(candidate.Track))
                    .ThenBy(candidate => candidate.Track.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
                return primary with { Sources = sources };
            })
            .OrderBy(candidate => candidate.Track.WorldKey)
            .ThenBy(candidate => candidate.Track.OwnerKey)
            .ThenBy(candidate => candidate.Time)
            .ToArray();
        return resolvedCandidates;
    }

    private static bool HasMessageStartGate(CinematicScriptOwner owner)
    {
        bool cinematicGate = HasCinematicFunctionReference(owner, CinematicStartCallbackForMessageGate)
            && (DeriveStartGateMask(owner) & 0x020) != 0;
        bool helperTextGate = CinematicScriptReader.TryDescribeCallbackReachability(owner,
                HelperTextStartCallbackForMessageGate, out _)
            && HasObjectReference(owner, "des_o_acteur_msg");
        return cinematicGate || helperTextGate;
    }

    private static int SourcePriority(CinematicScriptMessageTrack track) => track.ChannelIndex.HasValue ? 0
        : track.LinkName.Equals("SceneEvents", StringComparison.Ordinal) ? 1
        : track.LinkName.Equals("Cinematic", StringComparison.Ordinal) ? 2 : 3;
}

internal sealed record CinematicScriptMessageGateCandidate(CinematicScriptMessageTrack Track, float Time,
    CinematicTimelineMessage Message, CinematicScriptNamedReference QueueObject,
    CinematicScriptNamedReference ExecutionContext, CinematicScriptMessageTrack[] Sources);

internal sealed record CinematicScriptEngineMessageGateCandidate(CinematicScriptMessageTrack Track,
    float Time, CinematicLiteralCall Call, CinematicScriptNamedReference QueueObject,
    CinematicScriptNamedReference TargetObject, CinematicScriptMessageTrack[] Sources);

internal sealed record CinematicScriptMessageGateAnalysis(
    CinematicScriptMessageGateCandidate[] Matches,
    CinematicScriptMessageGateCandidate[] DifferentContexts,
    CinematicScriptEngineMessageGateCandidate[] EngineMatches,
    CinematicScriptEngineMessageGateCandidate[] EngineDifferentTargets,
    CinematicScriptEngineMessageGateCandidate[] EngineInt1MaskMismatches);
