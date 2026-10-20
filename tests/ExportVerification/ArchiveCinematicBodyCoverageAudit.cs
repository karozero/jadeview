using System.Text.Json;
using JadeView;

internal static class ArchiveCinematicBodyCoverageAudit
{
    private sealed record ActorCoverage(string Container, string World, string Clip,
        string ClipName, string ClipOwnerKey, string Actor, string ActorKey, int BoneCount, int ActionSegments,
        int BoneTracks, bool HasRootTrack, bool HasUnresolvedActionRequests,
        int? BaseKitSlotCount, string[] RequestedSlotsAbsentFromBaseKit,
        string[] ActionRequests, string[] Diagnostics);
    private sealed record ClipIndex(string Container, string World, string Clip,
        string ClipOwnerKey, string ClipOwnerName, float Duration, int TrackCount, string[] TrackTargets,
        string[] ClipOwnerCandidateDetails, string OwnerBrainKey, string ScriptModelKey, string[] ScriptFunctionKeys,
        string[] BodyActionEvents, string[] OwnerInheritedPayloads);

    internal static void Write(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var entries = archive.Entries
            .Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var coverage = new List<ActorCoverage>();
        var clipIndex = new List<ClipIndex>();
        var failures = new List<object>();
        int containersWithCinematicWorlds = 0;
        int clipCount = 0;

        for (int index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            SceneDocument document;
            byte[] sourceBytes;
            PcSceneLoader.Result replay;
            try
            {
                sourceBytes = archive.Read(entry);
                document = SceneLoader.Load(sourceBytes, entry.Name, archive.PathName);
                replay = PcSceneLoader.Read(sourceBytes, archive.PathName);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException
                or OverflowException or IndexOutOfRangeException)
            {
                failures.Add(new { entry.Id, Error = error.Message });
                continue;
            }

            var cinematicWorlds = document.Worlds
                .Where(world => world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (cinematicWorlds.Length > 0) containersWithCinematicWorlds++;

            foreach (var sourceWorld in cinematicWorlds)
            {
                var world = CinematicWorldAssembler.Assemble(document, sourceWorld);
                var clips = document.CinematicClips.Values
                    .Where(clip => clip.CinematicWorldKey == sourceWorld.Key || clip.CinematicWorldKey == 0)
                    .ToArray();
                foreach (var clip in clips)
                {
                    clipCount++;
                    clipIndex.Add(ReadClipIndex(entry.Id, sourceWorld.Name, sourceWorld.Key, clip,
                        document, replay.Records, sourceBytes));
                    var sourceTracks = clip.Tracks.Concat(CinematicScriptTransformTracks.Read(clip)).ToArray();
                    foreach (var actor in world.Actors)
                    {
                        CinematicBodyTimeline timeline;
                        try
                        {
                            timeline = CinematicBodyTimeline.Create(clip, actor, document.CinematicActionKits);
                        }
                        catch (Exception error) when (error is InvalidDataException or ArgumentException
                            or OverflowException or IndexOutOfRangeException)
                        {
                            bool hasMatchingSourceTracks = sourceTracks.Any(track => track.Bone >= 0
                                ? track.Target == actor.Root.GaoKey
                                : actor.Members.Any(member => member.GaoKey == track.Target));
                            if (!hasMatchingSourceTracks) continue;
                            coverage.Add(new(entry.Id, sourceWorld.Name, clip.Key.ToString("X8"), clip.Name,
                                clip.CinematicOwnerKey.ToString("X8"), actor.Root.Name, actor.Root.GaoKey.ToString("X8"),
                                actor.Bones.Count, 0, 0, false, true,
                                null, [], [],
                                [$"Audit could not build the action timeline: {error.Message}"]));
                            continue;
                        }

                        bool hasActorTracks = sourceTracks.Any(track => track.Bone >= 0
                            ? track.Target == actor.Root.GaoKey
                            : actor.Members.Any(member => member.GaoKey == track.Target));
                        if (!hasActorTracks && timeline.ActionCount == 0) continue;

                        int boneTrackCount = sourceTracks.Count(track => track.Bone >= 0
                            && track.Target == actor.Root.GaoKey);
                        bool hasRootTrack = sourceTracks.Any(track => track.Bone < 0
                            && track.Target == actor.Root.GaoKey);
                        bool hasUnresolvedRequests = timeline.Diagnostics.Any(message =>
                            message.StartsWith("Missing body action", StringComparison.Ordinal)
                            || message.StartsWith("No cinematic skeletal action resolved", StringComparison.Ordinal));
                        document.CinematicActionKits.TryGetValue(actor.Root.GaoKey, out var baseKit);
                        var actionRequests = ReadActionRequests(clip, actor);
                        var requestedSlotsAbsentFromBaseKit = actionRequests
                            .Where(request => request.Selector == 0x3391 && request.Action >= 0
                                && (baseKit == null || !baseKit.ContainsKey(request.Action)))
                            .Select(request => $"{request.Action} at {request.Time:F3}s")
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();

                        coverage.Add(new(entry.Id, sourceWorld.Name, clip.Key.ToString("X8"), clip.Name,
                            clip.CinematicOwnerKey.ToString("X8"), actor.Root.Name, actor.Root.GaoKey.ToString("X8"), actor.Bones.Count,
                            timeline.ActionCount, boneTrackCount, hasRootTrack, hasUnresolvedRequests,
                            baseKit?.Count, requestedSlotsAbsentFromBaseKit,
                            actionRequests.Select(request => DescribeActionRequest(request, document.CinematicActionKits))
                                .Distinct(StringComparer.Ordinal).ToArray(),
                            timeline.Diagnostics.ToArray()));
                    }
                }
            }

            if ((index + 1) % 10 == 0 || index + 1 == entries.Length)
                Console.WriteLine($"ARCHIVE CINEMATIC BODY COVERAGE: {index + 1}/{entries.Length} containers");
        }

        var skeletalActors = coverage.Where(item => item.BoneCount > 0).ToArray();
        var report = new
        {
            Archive = archive.PathName,
            ContainersScanned = entries.Length,
            ContainersWithCinematicWorlds = containersWithCinematicWorlds,
            CinematicClips = clipCount,
            ActorClipBindings = coverage.Count,
            SkeletalActorClipBindings = skeletalActors.Length,
            WithResolvedBodyActions = skeletalActors.Count(item => item.ActionSegments > 0),
            WithoutResolvedBodyActions = skeletalActors.Count(item => item.ActionSegments == 0),
            WithDirectBoneTracks = skeletalActors.Count(item => item.BoneTracks > 0),
            WithDirectRootTracks = skeletalActors.Count(item => item.HasRootTrack),
            WithUnresolvedActionRequests = skeletalActors.Count(item => item.HasUnresolvedActionRequests),
            TotalBodyActionSegments = coverage.Sum(item => item.ActionSegments),
            RequestedSlotsAbsentFromBaseKit = coverage.Sum(item => item.RequestedSlotsAbsentFromBaseKit.Length),
            Diagnostics = coverage.SelectMany(item => item.Diagnostics)
                .GroupBy(message => message, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .Select(group => new { Message = group.Key, Occurrences = group.Count() })
                .ToArray(),
            ClipIndex = clipIndex,
            LoadFailures = failures,
            ActorCoverage = coverage,
            Limits = "Static reconstruction using each FF container's serialized actor action kits and clip events. It does not execute brain callbacks, conditional gameplay handoffs, runtime action queues, or actor visibility/collision state. Reused cross-container defaults are not inferred."
        };

        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "archive-cinematic-body-coverage.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static CinematicActionEvent[] ReadActionRequests(AnimationClip clip, SceneActor actor) =>
        clip.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads
                .Select(payload => CinematicActionEvent.Decode(key,
                    track.Target == 0 ? clip.CinematicOwnerKey : track.Target, payload))))
            .Where(request => request?.Target == actor.Root.GaoKey && !request.FrequencyOnly && !request.Release)
            .Cast<CinematicActionEvent>()
            .ToArray();

    private static string DescribeActionRequest(CinematicActionEvent request,
        IReadOnlyDictionary<uint, Dictionary<int, CinematicAction>> kits)
    {
        string providerStatus = "";
        if (request.Selector is 0x3398 or 0x3399)
        {
            if (!kits.TryGetValue(request.KitOwner, out var provider))
                providerStatus = " provider=missing";
            else
            {
                int availableInRange = provider.Keys.Count(slot => slot >= request.Action && slot <= request.LastAction);
                providerStatus = $" providerSlots={provider.Count} rangeSlots={availableInRange}";
            }
        }

        return $"selector=0x{request.Selector:X4} action={request.Action} kit={request.KitOwner:X8} "
            + $"range={request.Action}-{request.LastAction} at {request.Time:F3}s{providerStatus}";
    }

    private static ClipIndex ReadClipIndex(string container, string world, uint worldKey,
        AnimationClip clip, SceneDocument document, IReadOnlyList<PcSceneLoader.Record> records,
        byte[] sourceBytes)
    {
        var owners = records.Where(record => record.Kind == 'l' && record.LoadWorldKey == worldKey
            && (record.SceneEvents == clip.Key || record.Events == clip.Key)).ToArray();
        var owner = owners.FirstOrDefault(record => record.Key == clip.CinematicOwnerKey);
        uint brainKey = owner?.BrainFieldOffset is int brainOffset && brainOffset >= 0
            ? Preview.U32(sourceBytes, brainOffset) : 0;
        var brain = records.FirstOrDefault(record => record.Kind == 'q' && record.Key == brainKey);
        uint scriptModelKey = brain is { Size: >= 4 } ? Preview.U32(sourceBytes, brain.Offset) : 0;
        var scriptModel = records.FirstOrDefault(record => record.Kind == 's' && record.Key == scriptModelKey);
        string[] functionKeys = scriptModel?.References.Select(reference => reference.Key)
            .Distinct().Select(key => key.ToString("X8")).ToArray() ?? [];
        string[] ownerInheritedPayloads = clip.Tracks.SelectMany((track, trackIndex) => track.Keys
                .SelectMany(key => key.OpaquePayloads.Select(payload => (track, trackIndex, key, payload))))
            .Where(item => item.track.Target == 0
                && CinematicWordPairList.TryRead(item.payload, out var rows)
                && rows.Length >= 4
                && CinematicWordPairList.IsLiteral(rows[0], 0x28)
                && rows[0].Value == 0
                && CinematicWordPairList.HasSelector(rows[1], 2, 2054))
            .Select(item =>
            {
                CinematicWordPairList.TryRead(item.payload, out var rows);
                string calls = string.Join(",", rows.Select((row, rowIndex) => (row, rowIndex))
                    .Where(value => value.row.Tag >> 24 == 3)
                    .Select(value =>
                    {
                        string arguments = string.Join(" ", rows.Skip(2).Take(value.rowIndex - 2)
                            .Select(argument => $"{argument.Value:X8}"));
                        return $"0x{value.row.Tag & 0xffff:X4}({arguments})";
                    }));
                return $"track={item.trackIndex} at={item.key.Time:F3}s calls=[{calls}] kind={item.payload.Kind}";
            })
            .Distinct(StringComparer.Ordinal).ToArray();
        var actions = clip.Tracks.Where(track => track.Bone < 0)
            .SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads
                .Select(payload => CinematicActionEvent.Decode(key,
                    track.Target == 0 ? clip.CinematicOwnerKey : track.Target, payload))))
            .Where(action => action != null && action.Selector is 0x3391 or 0x3398 or 0x3399)
            .Cast<CinematicActionEvent>()
            .Select(action => $"target={action.Target:X8} selector=0x{action.Selector:X4} "
                + $"action={action.Action} kit={action.KitOwner:X8} end={action.LastAction} at={action.Time:F3}s")
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(container, world, clip.Key.ToString("X8"), clip.CinematicOwnerKey.ToString("X8"),
            document.Worlds.SelectMany(sourceWorld => sourceWorld.Objects)
                .FirstOrDefault(sceneObject => sceneObject.GaoKey == clip.CinematicOwnerKey)?.Name ?? "",
            clip.Duration, clip.Tracks.Count,
            clip.Tracks.Select(track => track.Target.ToString("X8")).Distinct(StringComparer.Ordinal).ToArray(),
            owners.Select(record => $"{record.Key:X8}:{record.Name}:"
                + $"events={(record.Events == clip.Key ? 1 : 0)}:sceneEvents={(record.SceneEvents == clip.Key ? 1 : 0)}")
                .ToArray(),
            brainKey.ToString("X8"), scriptModelKey.ToString("X8"), functionKeys, actions,
            ownerInheritedPayloads);
    }
}
