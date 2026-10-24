using System.Text.Json;
using JadeView;

internal static class CinematicNestedEventTransformAudit
{
    internal static void Write(string archivePath, string container, uint? requestedClipKey, string outputDirectory)
    {
        Console.WriteLine($"CINEMATIC EVE AUDIT: indexing archive {archivePath}");
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate =>
            candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"CINEMATIC EVE AUDIT: reading {entry.Id} ({entry.StoredSize:N0} stored bytes)");
        byte[] containerBytes = archive.Read(entry);
        Console.WriteLine($"CINEMATIC EVE AUDIT: decoded {containerBytes.Length:N0} bytes; parsing records");
        var document = SceneLoader.Load(containerBytes, entry.Name, archivePath);
        Console.WriteLine($"CINEMATIC EVE AUDIT: parsed {document.CinematicClips.Count} clips, "
            + $"{document.Worlds.Count} worlds, {document.CinematicEmbeddedEventTracks.Count} event owners");
        var clips = document.CinematicClips.Values
            .Where(clip => requestedClipKey == null || clip.Key == requestedClipKey)
            .OrderBy(clip => clip.Key).ToArray();
        var results = clips.Select(clip =>
        {
            WorldData sourceWorld = document.Worlds.FirstOrDefault(world =>
                clip.CinematicWorldKey == 0 || world.Key == clip.CinematicWorldKey)
                ?? throw new InvalidDataException($"No source world resolved for clip {clip.Key:X8}.");

            Console.WriteLine($"CINEMATIC EVE AUDIT: assembling world {sourceWorld.Key:X8} for {clip.Key:X8}");
            var world = CinematicWorldAssembler.Assemble(document, sourceWorld);
            Console.WriteLine($"CINEMATIC EVE AUDIT: assembled {world.Objects.Count} objects, "
                + $"{world.Actors.Count} actors; evaluating event routes");
            var serializedRunningInitTracks = document.CinematicEmbeddedEventTracks.Values.SelectMany(lists => lists)
                .Where(list => list.WorldKey == world.Key
                    && world.Objects.Any(sceneObject => sceneObject.GaoKey == list.OwnerKey))
                .SelectMany(list => list.Tracks)
                .Where(track => (track.ChannelFlags & CinematicEventTrackFlags.RunningInit) != 0).ToArray();
            var loopTracks = document.CinematicEmbeddedEventTracks.Values.SelectMany(lists => lists)
                .Where(list => list.WorldKey == world.Key
                    && world.Objects.Any(sceneObject => sceneObject.GaoKey == list.OwnerKey))
                .SelectMany(list => list.Tracks)
                .Where(track => (track.ChannelFlags & CinematicEventTrackFlags.AutoLoop) != 0)
                .OrderBy(track => track.Duration).ToArray();
            Console.WriteLine($"CINEMATIC EVE AUDIT: {serializedRunningInitTracks.Length} serialized RunningInit tracks, "
                + $"{serializedRunningInitTracks.Count(track => (track.ChannelFlags & CinematicEventTrackFlags.AutoLoop) != 0)} looped; "
                + $"shortest loop {serializedRunningInitTracks.Where(track => (track.ChannelFlags & CinematicEventTrackFlags.AutoLoop) != 0)
                    .Select(track => track.Duration).DefaultIfEmpty().Min():F4}s");
            Console.WriteLine($"CINEMATIC EVE AUDIT: {loopTracks.Length} loop tracks in loaded world; "
                + $"shortest {loopTracks.FirstOrDefault()?.Duration:F6}s, "
                + $"longest {loopTracks.LastOrDefault()?.Duration:F3}s");
            var directCalls = CinematicLiteralCall.ReadTimeline(clip)
                .Where(item => CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) is
                    "TRAPlay" or "TRAPlayAll" or "TRASetTime")
                .Select(item => new
                {
                    item.Time,
                    Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                    Owner = item.Call.Target.ToString("X8"),
                    Arguments = item.Call.Arguments
                }).ToArray();
            var timeline = CinematicNestedEventTransformTracks.ReadTimeline(clip, world, document);
            var absolutePositionTargets = clip.Tracks.Concat(timeline.TransformTracks)
                .Where(track => track.Bone < 0 && track.Keys.Any(key => key.Position.HasValue))
                .Select(track => track.Target).ToHashSet();
            var moves = new CinematicGameScriptMoveTimeline(world, timeline.MoveSegments,
                CinematicLiteralCall.ReadTimeline(clip).Concat(timeline.Commands),
                absolutePositionTargets, timeline.MoveDiagnostics);
            var previewableOwners = timeline.PreviewableSceneEventOwners.Select(ownerId =>
            {
                var owner = document.CinematicScriptOwners[ownerId];
                var eventList = document.CinematicEmbeddedEventTracks[(ownerId.WorldKey, ownerId.OwnerKey)]
                    .First(list => list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase)
                        && list.EventKey == owner.SceneEventsKey);
                return new
                {
                    Owner = owner.Name,
                    OwnerKey = owner.OwnerKey.ToString("X8"),
                    OwnerWorld = ownerId.WorldKey.ToString("X8"),
                    EventKey = owner.SceneEventsKey.ToString("X8"),
                    ChannelCount = eventList.Tracks.Length,
                    CallbackNames = owner.CallbackNames,
                    ModelFunctionNames = owner.ModelFunctionNames
                };
            }).ToArray();
            var previewedOwnerEffects = timeline.PreviewableSceneEventOwners.Select(ownerId =>
            {
                var preview = CinematicNestedEventTransformTracks.ReadTimeline(clip, world, document,
                    previewedSceneEventOwners: new HashSet<CinematicScriptOwnerId> { ownerId });
                var owner = document.CinematicScriptOwners[ownerId];
                var baselineTargets = timeline.TransformTracks.Select(track => track.Target).ToHashSet();
                var addedTargets = preview.TransformTracks.Select(track => track.Target)
                    .Where(target => !baselineTargets.Contains(target)).Distinct()
                    .Select(target => new
                    {
                        Key = target.ToString("X8"),
                        Name = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == target)?.Name
                    }).ToArray();
                var baselineCommands = timeline.Commands.Select(ToCommandIdentity).ToHashSet();
                var addedCommands = preview.Commands
                    .Where(command => !baselineCommands.Contains(ToCommandIdentity(command)))
                    .Select(command => new
                    {
                        command.Time,
                        Function = CinematicEngineFunctionCatalog.NameFor(command.Call.Selector),
                        Target = command.Call.Target.ToString("X8"),
                        TargetName = world.Objects.FirstOrDefault(sceneObject =>
                            sceneObject.GaoKey == command.Call.Target)?.Name,
                        command.Call.Arguments
                    }).ToArray();
                return new
                {
                    Owner = owner.Name,
                    OwnerKey = owner.OwnerKey.ToString("X8"),
                    EVE = owner.SceneEventsKey.ToString("X8"),
                    AddedTransformTargets = addedTargets,
                    AddedCommands = addedCommands
                };
            }).ToArray();
            var tracks = timeline.TransformTracks
                .Select(track => new
                {
                    Target = track.Target.ToString("X8"),
                    TargetName = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == track.Target)?.Name,
                    KeyCount = track.Keys.Count,
                    FirstTime = track.Keys.FirstOrDefault()?.Time,
                    LastTime = track.Keys.LastOrDefault()?.Time,
                    Components = string.Join(",", new[]
                    {
                        track.Keys.Any(key => key.Position.HasValue) ? "translation" : null,
                        track.Keys.Any(key => key.Rotation.HasValue) ? "rotation" : null,
                        track.Keys.Any(key => key.Scale.HasValue) ? "scale" : null
                    }.Where(value => value != null))
                }).ToArray();
            var stateCommands = timeline.Commands
                .Where(item => item.Call.Selector is 8113 or 8115 or 8133 or 8152 or 8153 or 15300 or 15301)
                .Select(item => new
                {
                    item.Time,
                    Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                    Target = item.Call.Target.ToString("X8"),
                    TargetName = world.Objects.FirstOrDefault(sceneObject =>
                        sceneObject.GaoKey == item.Call.Target)?.Name,
                    item.Call.Arguments
                }).ToArray();
            return new
            {
                ClipKey = clip.Key.ToString("X8"),
                clip.Name,
                clip.Duration,
                DirectPlayCalls = directCalls,
                PreviewableSceneEventOwners = previewableOwners,
                PreviewedOwnerEffects = previewedOwnerEffects,
                GeneratedTracks = tracks,
                TransformInputs = world.Objects.Where(target => timeline.TransformTracks.Any(track => track.Target == target.GaoKey))
                    .Select(target => new
                    {
                        Key = target.GaoKey.ToString("X8"), target.Name, target.ParentId,
                        Baseline = new[] { target.Transform.Translation.X, target.Transform.Translation.Y, target.Transform.Translation.Z },
                        HierarchyLocal = target.SerializedHierarchyLocalMatrix is { } local
                            ? new[] { local.M11, local.M12, local.M13, local.M14, local.M21, local.M22, local.M23, local.M24,
                                local.M31, local.M32, local.M33, local.M34, local.M41, local.M42, local.M43, local.M44 } : null,
                        Tracks = timeline.TransformTracks.Where(track => track.Target == target.GaoKey)
                            .Select(track => track.Keys.Select(key => new
                            {
                                key.Time, key.Duration, key.Flags,
                                Position = key.Position is { } position ? new[] { position.X, position.Y, position.Z } : null
                            }).ToArray()).ToArray()
                    }).ToArray(),
                GeneratedStateCommands = stateCommands,
                MoveTargets = moves.Targets.Select(target => new
                {
                    Key = target.GaoKey.ToString("X8"), target.Name, target.ParentId
                }).ToArray(),
                MoveDiagnostics = moves.Diagnostics,
                MoveSegments = timeline.MoveSegments.Select(segment => new
                {
                    Target = segment.Command.TargetKey.ToString("X8"),
                    segment.TimelineStartSeconds, segment.TimelineEndSeconds,
                    segment.EventElapsedSeconds, segment.Command.EventDurationSeconds,
                    segment.Command.Origin
                }).ToArray(),
                GeneratedLocalRotations = timeline.RotationCommands.Select(command => new
                {
                    Target = command.TargetKey.ToString("X8"),
                    TargetName = world.Objects.FirstOrDefault(sceneObject =>
                        sceneObject.GaoKey == command.TargetKey)?.Name,
                    command.Start,
                    command.End,
                    command.Radians
                }).ToArray()
            };
        }).ToArray();

        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, $"{entry.Id}-nested-eve-transforms.json");
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Container = entry.Id,
            Clips = results,
            Limits = "Playback includes source-reachable callback starts, selected-clip literal TRAPlay/TRAPlayAll roots, and serialized RunningInit channels for uncontrolled owners or enabled callback owners. Script-owned SceneEvents preview switches default off; callback-linked Include switches default on. Their runtime callback gates are not executed. AutoLoop/AutoStop and literal stop/pause/play/reinitialize/TRASetTime cursor timing use serialized channel flags and event calls. TRASetTime seeks every track in the addressed EVE list while retaining run/pause state and clamps past-end cursors to the last event start. Seeks beyond 65535/60 seconds are unresolved because the engine's 16-bit frame wrap is not reconstructed. Runtime object-processing state, callback gates, gameplay-carried cursors, and unsupported command-only EVE state are not simulated."
        }, new JsonSerializerOptions { WriteIndented = true }));
        int generatedCount = results.Sum(result => result.GeneratedTracks.Length);
        Console.WriteLine($"NESTED EVE TRANSFORM AUDIT: {entry.Id}: {clips.Length} clips, {generatedCount} generated transform tracks -> {output}");

        static string ToCommandIdentity((float Time, CinematicLiteralCall Call) command) =>
            $"{command.Time:R}|{command.Call.Target:X8}|{command.Call.Selector:X4}|{string.Join(",", command.Call.Arguments)}";
    }
}
