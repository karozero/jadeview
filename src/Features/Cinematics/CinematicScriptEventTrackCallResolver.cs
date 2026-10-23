namespace JadeView;

internal static class CinematicScriptEventTrackCallResolver
{
    internal static CinematicScriptEventTrackCall[] Resolve(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall> sourceCalls,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]> eventLists) =>
        Analyze(owner, sourceCalls, eventLists).Resolved;

    internal static CinematicScriptEventTrackCallAnalysis Analyze(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall> sourceCalls,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]> eventLists)
    {
        var resolvedCalls = new List<CinematicScriptEventTrackCall>();
        var unresolvedCalls = new List<CinematicScriptUnresolvedEventTrackCall>();
        foreach (var sourceCall in sourceCalls)
        {
            if (!TryGetOperation(sourceCall.Api, out string operation, out bool affectsAllTracks))
                continue;
            if (sourceCall.Arguments.Length == 0)
            {
                unresolvedCalls.Add(new(sourceCall, "The source call has no receiver argument."));
                continue;
            }
            if (!TryResolveEventLists(owner, sourceCall, eventLists, out var sceneEventLists,
                    out string targetBinding, out string targetFailure))
            {
                unresolvedCalls.Add(new(sourceCall, targetFailure));
                continue;
            }

            int[] trackIndices = [];
            string? indexBinding = null;
            if (!affectsAllTracks)
            {
                if (CinematicScriptTrackIndexResolver.TryResolveIndices(owner, sourceCall,
                        out trackIndices, out string resolvedBinding))
                {
                    indexBinding = resolvedBinding;
                }
                else
                {
                    string reason = sourceCall.LoopContexts is { Length: > 0 }
                        ? $"The indexed call is inside a {sourceCall.LoopContexts[0].Kind} loop whose bounds or iteration behavior cannot be safely reduced."
                        : "The index cannot be reduced to a literal or a uniquely assigned scalar OVA expression.";
                    unresolvedCalls.Add(new(sourceCall, reason));
                    continue;
                }
            }

            foreach (var eventList in sceneEventLists)
            {
                int?[] addressedIndices = affectsAllTracks ? [null] : trackIndices.Select(index => (int?)index).ToArray();
                foreach (int? trackIndex in addressedIndices)
                {
                    CinematicEmbeddedEventTrack[] tracks = trackIndex == null
                        ? eventList.Tracks
                        : eventList.Tracks.Where(track => track.Index == trackIndex).ToArray();
                    if (tracks.Length == 0)
                    {
                        string reason = trackIndex == null
                            ? "The linked SceneEvents list has no decoded channels."
                            : $"Resolved index {trackIndex} is not present in the linked SceneEvents list.";
                        unresolvedCalls.Add(new(sourceCall, reason));
                        continue;
                    }

                    resolvedCalls.Add(new(sourceCall, operation, trackIndex, eventList, tracks,
                        targetBinding, indexBinding));
                }
            }
        }

        return new(resolvedCalls.ToArray(), unresolvedCalls.ToArray());
    }

    private static bool TryResolveEventLists(CinematicScriptOwner owner,
        CinematicScriptReachableEngineCall sourceCall,
        IReadOnlyDictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]> eventLists,
        out CinematicEmbeddedEventTrackList[] resolvedLists, out string targetBinding,
        out string failureReason)
    {
        string receiver = Normalize(sourceCall.Arguments[0]);
        if (IsNullReceiver(receiver))
        {
            resolvedLists = [];
            targetBinding = string.Empty;
            failureReason = "The source call passes a literal null receiver, so no event-list owner is selected.";
            return false;
        }

        if (IsCurrentOwnerReceiver(receiver, sourceCall))
        {
            resolvedLists = eventLists.GetValueOrDefault((owner.WorldKey, owner.OwnerKey), [])
                .Where(list => list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase)
                    && list.EventKey == owner.SceneEventsKey)
                .ToArray();
            targetBinding = "current owner";
            if (resolvedLists.Length == 1)
            {
                failureReason = string.Empty;
                return true;
            }

            failureReason = owner.SceneEventsKey is <= 2 or uint.MaxValue
                ? "The owner has no serialized SceneEvents EVE reference."
                : "The owner's SceneEvents reference did not resolve to exactly one decoded list.";
            return false;
        }

        string[] referencedFields = ObjectReferenceFieldsForReceiver(receiver, sourceCall, owner);
        var referencedTargets = referencedFields.Select(fieldName =>
                (FieldName: fieldName, Reference: owner.EffectiveObjectReference(fieldName)))
            .Where(item => item.Reference is { TargetKey: > 2 and < uint.MaxValue })
            .DistinctBy(item => (item.FieldName, item.Reference!.TargetKey))
            .ToArray();
        if (referencedTargets.Length != 1)
        {
            resolvedLists = [];
            targetBinding = string.Empty;
            string[] explicitlyNullFields = referencedFields.Where(fieldName =>
                    owner.EffectiveVariable(fieldName) is { } variable && HasZeroValue(variable.Value))
                .ToArray();
            failureReason = explicitlyNullFields.Length == 1
                ? $"OVA object reference {explicitlyNullFields[0]} is unset (effective value 0); the source does not select an event-list owner for this call."
                : "The receiver does not resolve to one serialized OVA object reference.";
            return false;
        }

        var referencedTarget = referencedTargets[0];
        var candidates = eventLists.Where(entry => entry.Key.OwnerKey == referencedTarget.Reference!.TargetKey)
            .SelectMany(entry => entry.Value)
            .Where(list => list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(list => (list.WorldKey, list.OwnerKey, list.EventKey))
            .ToArray();
        var localCandidates = candidates.Where(list => list.WorldKey == owner.WorldKey).ToArray();
        var uniqueCandidates = localCandidates.Length > 0 ? localCandidates : candidates;
        if (uniqueCandidates.Length != 1)
        {
            resolvedLists = [];
            targetBinding = string.Empty;
            failureReason = $"OVA reference {referencedTarget.FieldName} ({referencedTarget.Reference!.TargetKey:X8}) has no unique decoded SceneEvents list.";
            return false;
        }

        resolvedLists = uniqueCandidates;
        targetBinding = $"{referencedTarget.FieldName} OVA reference ({referencedTarget.Reference!.TargetKey:X8})";
        failureReason = string.Empty;
        return true;
    }

    private static bool IsCurrentOwnerReceiver(string receiver, CinematicScriptReachableEngineCall sourceCall)
    {
        if (receiver.Equals("AI_gpst_CurrentGameObject", StringComparison.Ordinal)) return true;

        return sourceCall.ArgumentAssignments.Any(assignment =>
                assignment.Variable.Equals(receiver, StringComparison.Ordinal)
                && Normalize(assignment.Expression).Equals("AI_gpst_CurrentGameObject", StringComparison.Ordinal));
    }

    private static string[] ObjectReferenceFieldsForReceiver(string receiver,
        CinematicScriptReachableEngineCall sourceCall, CinematicScriptOwner owner)
    {
        var fields = sourceCall.ArgumentFields.Where(field =>
                field.AliasVariable?.Equals(receiver, StringComparison.Ordinal) == true)
            .Select(field => field.Name);
        var assignedFields = owner.ObjectReferences.Where(reference => reference.ArrayIndex == null)
            .Where(reference => sourceCall.ArgumentAssignments.Any(assignment =>
                assignment.Variable.Equals(receiver, StringComparison.Ordinal)
                && ReferencesMemberField(assignment.Expression, reference.VariableName)))
            .Select(reference => reference.VariableName);
        return fields.Concat(assignedFields).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ReferencesMemberField(string expression, string fieldName) =>
        expression.Contains($"->{fieldName}", StringComparison.OrdinalIgnoreCase);

    private static bool IsNullReceiver(string receiver) => receiver is "0" or "NULL" or "nullptr";

    private static bool HasZeroValue(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint hexadecimal) && hexadecimal == 0;
        return long.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long decimalValue) && decimalValue == 0;
    }

    private static bool TryGetOperation(string api, out string operation, out bool affectsAllTracks)
    {
        affectsAllTracks = api.EndsWith("All", StringComparison.Ordinal) || api == "TRASetTime";
        string operationCore = affectsAllTracks ? api[..^3] : api;
        operation = api;
        return operationCore is "TRAPlay" or "TRAStop" or "TRAPause" or "TRAUnPause"
            or "TRAReinit" or "TRAIsStop" or "TRASetEnd" or "TRASetTime";
    }

    private static string Normalize(string expression) =>
        expression.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Trim('(', ')');
}
