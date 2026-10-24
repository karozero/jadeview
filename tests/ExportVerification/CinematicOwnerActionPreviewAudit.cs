using System.Text.Json;
using JadeView;

internal static class CinematicOwnerActionPreviewAudit
{
    internal static void Write(string archivePath, string containerId, uint clipKey,
        uint ownerKey, uint actorKey, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        Entry entry = archive.Entries.Single(candidate => candidate.Id.Equals(containerId,
            StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        AnimationClip sourceClip = document.CinematicClips.Values.Single(clip => clip.Key == clipKey);
        WorldData sourceWorld = document.Worlds.Single(world => world.Key == sourceClip.CinematicWorldKey);
        WorldData previewWorld = CinematicWorldAssembler.Assemble(document, sourceWorld);
        CinematicScriptOwner owner = document.CinematicScriptOwners.Values
            .Single(candidate => candidate.OwnerKey == ownerKey);

        CinematicPlayback? baseline = CinematicPlayback.DiscoverClip(sourceClip, document,
            previewWorld, new ResourceResolver(), null, null, null);
        CinematicPlayback? selected = CinematicPlayback.DiscoverClip(sourceClip, document,
            previewWorld, new ResourceResolver(), null, new HashSet<CinematicScriptOwnerId> { owner.Id }, null);
        if (baseline == null || selected == null || !selected.PreviewableSceneEventOwners.Contains(owner.Id))
            throw new InvalidDataException($"Owner {ownerKey:X8} is not a previewable SceneEvents owner for clip {clipKey:X8}.");
        string[] actionContributions = CinematicOwnerActionContributionResolver.FindResolvedRequests(
            baseline, document, previewWorld, owner.Id);
        CinematicInterruptionGateSnapshot[] exitGateSnapshots = ReadExitGateSnapshots(
            owner, selected, previewWorld);

        var report = new
        {
            Container = entry.Id,
            Clip = new { Key = sourceClip.Key.ToString("X8"), sourceClip.Name },
            Owner = new { Key = owner.OwnerKey.ToString("X8"), owner.Name,
                World = owner.WorldKey.ToString("X8"), Event = owner.SceneEventsKey.ToString("X8"),
                CallbackSlots = owner.CallbackSlots?.Select(slot => new
                {
                    slot.Index,
                    FunctionKey = slot.FunctionKey.ToString("X8"),
                    slot.FunctionName
                }),
                Lifecycle = CinematicOwnerStartLifecycleAnalyzer.Analyze(owner,
                    document.CinematicScriptEventTracks.GetValueOrDefault(owner.Id)).Phases
            },
            Actor = new
            {
                Key = actorKey.ToString("X8"),
                Name = previewWorld.Actors.FirstOrDefault(actor => actor.Root.GaoKey == actorKey)?.Root.Name,
                Baseline = BodyState(baseline, actorKey),
                WithOwnerPreview = BodyState(selected, actorKey),
                ActionContributions = actionContributions
            },
            StartGates = CinematicStartGateEvaluator.Evaluate(owner, previewWorld.Objects,
                selected.ScriptStateCalls, 0),
            ExitGateSnapshots = exitGateSnapshots,
            PreviewableOwnerCount = selected.PreviewableSceneEventOwners.Length,
            Limit = "Manual owner preview demonstrates the serialized EVE/action path. Exit-gate snapshots evaluate only timeline saved-flag writes and preview capabilities; collision/activity state, priority ownership, callback polling and actual dispatch remain runtime-dependent."
        };

        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory,
            $"{entry.Id}-{clipKey:X8}-{ownerKey:X8}-owner-action-preview.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CINEMATIC OWNER ACTION PREVIEW: clip={clipKey:X8}; owner={ownerKey:X8}; "
            + $"actor={actorKey:X8}; baseline={report.Actor.Baseline?.ActionCount ?? 0}; "
            + $"selected={report.Actor.WithOwnerPreview?.ActionCount ?? 0}; startGates={report.StartGates.Status}; "
            + $"exitGateSnapshots={report.ExitGateSnapshots.Length}; report={reportPath}");
    }

    private static CinematicInterruptionGateSnapshot[] ReadExitGateSnapshots(
        CinematicScriptOwner owner, CinematicPlayback playback, WorldData world)
    {
        var state = new CinematicObjectState(playback.SourceClip, world, playback.Actors,
            playback.ScriptStateCalls, playback.VideoPlayback);
        var snapshots = new List<CinematicInterruptionGateSnapshot>();
        string? priorState = null;
        float[] sampleTimes = playback.ScriptStateCalls.Select(item => item.Time)
            .Where(time => float.IsFinite(time) && time >= 0 && time <= playback.Duration)
            .Append(0)
            .Append(playback.Duration)
            .Distinct()
            .Order()
            .ToArray();

        foreach (float time in sampleTimes)
        {
            state.Evaluate(time);
            CinematicInterruptionGateEvaluation evaluation = CinematicInterruptionGateEvaluator.Evaluate(
                owner, world.Objects, playback.ScriptStateCalls, time);
            string stateKey = $"{evaluation.EnabledMask}:{evaluation.Status}:"
                + string.Join(',', evaluation.Gates.Select(gate => $"{gate.Mask}={gate.Status}"));
            if (stateKey != priorState)
            {
                snapshots.Add(new(time, evaluation.EnabledMask, evaluation.Status,
                    evaluation.Gates.Select(gate => new ExitGateState(gate.Mask, gate.Name,
                        gate.Status, gate.Evidence)).ToArray()));
                priorState = stateKey;
            }
        }

        state.Clear();
        return snapshots.ToArray();
    }

    private static BodyPreviewState? BodyState(CinematicPlayback? playback, uint actorKey)
    {
        if (playback == null) return null;
        CinematicBodyTimeline? body = playback.BodyTimelines
            .Where(item => item.Actor.Root.GaoKey == actorKey)
            .Select(item => item.Body).SingleOrDefault();
        if (body == null) return null;
        SegmentPreviewState[] segments = body.TimelineSegments(playback.Duration)
            .Select(segment => new SegmentPreviewState(segment.Start, segment.End, segment.Label))
            .ToArray();
        return new(body.ActionCount, segments, body.Diagnostics.ToArray());
    }

    private sealed record BodyPreviewState(int ActionCount,
        SegmentPreviewState[] Segments, string[] Diagnostics);

    private sealed record SegmentPreviewState(float Start, float End, string Label);
    private sealed record CinematicInterruptionGateSnapshot(float Time, int? EnabledMask,
        CinematicStartGateStatus Status, ExitGateState[] Gates);
    private sealed record ExitGateState(int Mask, string Name, CinematicStartGateStatus Status,
        string Evidence);
}
