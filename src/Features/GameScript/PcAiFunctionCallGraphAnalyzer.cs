namespace JadeView;

/// <summary>
/// Follows linked AIExecute calls and AITrackCurChangeNow transitions without executing branches or VM expressions.
/// At most one shortest expansion is made for each function key; edge paths remain listed.
/// </summary>
internal static class PcAiFunctionCallGraphAnalyzer
{
    private const int MaximumDepth = 64;
    private const int MaximumPaths = 4096;

    private sealed record PendingFunction(uint Key, string Name, PcSerializedAiFunction Function,
        uint[] PathKeys, string[] PathNames, int[] CallNodeIndexes, string[] TransferKinds);

    internal static PcAiFunctionCallPath[] Analyze(uint rootKey, string rootName,
        PcSerializedAiFunction root, IReadOnlyDictionary<uint, string> linkedFunctionNames,
        Func<uint, PcSerializedAiFunction?> resolveFunction)
    {
        var paths = new List<PcAiFunctionCallPath>();
        var pending = new Queue<PendingFunction>();
        var expandedKeys = new HashSet<uint> { rootKey };
        pending.Enqueue(new(rootKey, rootName, root, [rootKey], [rootName], [], []));

        while (pending.TryDequeue(out PendingFunction? current) && paths.Count < MaximumPaths)
        {
            foreach (PcAiFunctionReference call in PcAiFunctionReferenceAnalyzer.Analyze(
                         current.Function, linkedFunctionNames))
            {
                if (paths.Count >= MaximumPaths) break;
                if (call.TargetKey is not { } targetKey)
                {
                    paths.Add(new(current.PathKeys, current.PathNames,
                        [.. current.CallNodeIndexes, call.NodeIndex],
                        [.. current.TransferKinds, call.Operation], call.Status));
                    continue;
                }

                string targetName = call.TargetName ?? $"OFC {targetKey:X8}";
                uint[] pathKeys = [.. current.PathKeys, targetKey];
                string[] pathNames = [.. current.PathNames, targetName];
                int[] callNodeIndexes = [.. current.CallNodeIndexes, call.NodeIndex];
                string[] transferKinds = [.. current.TransferKinds, call.Operation];
                if (call.Status != "ResolvedLinkedFunction")
                {
                    paths.Add(new(pathKeys, pathNames, callNodeIndexes, transferKinds, call.Status));
                    continue;
                }

                if (call.Operation == "AITrackCurChangeNow" && targetKey == current.Key)
                {
                    paths.Add(new(pathKeys, pathNames, callNodeIndexes, transferKinds, "SelfTrackNoOp"));
                    continue;
                }

                if (current.PathKeys.Contains(targetKey))
                {
                    paths.Add(new(pathKeys, pathNames, callNodeIndexes, transferKinds, "Cycle"));
                    continue;
                }

                if (current.CallNodeIndexes.Length >= MaximumDepth)
                {
                    paths.Add(new(pathKeys, pathNames, callNodeIndexes, transferKinds, "DepthLimit"));
                    continue;
                }

                paths.Add(new(pathKeys, pathNames, callNodeIndexes, transferKinds,
                    call.Operation == "AITrackCurChangeNow" ? "TrackTransition" : "Linked"));
                if (!expandedKeys.Add(targetKey)) continue;

                PcSerializedAiFunction? target = resolveFunction(targetKey);
                if (target == null)
                {
                    paths[^1] = paths[^1] with { Status = "TargetReadFailure" };
                    continue;
                }

                pending.Enqueue(new(targetKey, targetName, target, pathKeys, pathNames, callNodeIndexes, transferKinds));
            }
        }

        if (pending.Count > 0 && paths.Count >= MaximumPaths)
            paths.Add(new([], [], [], [], "PathLimit"));
        return paths.ToArray();
    }
}
