using System.Text.Json;
using JadeView;

internal static class WorldScriptGraphAudit
{
    internal static void Write(string archivePath, string containerId, string worldSelector, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate => candidate.Id.Equals(containerId,
            StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        WorldData? world;
        if (uint.TryParse(worldSelector, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint worldKey))
            world = document.Worlds.FirstOrDefault(candidate => candidate.Key == worldKey);
        else
            world = document.Worlds.FirstOrDefault(candidate => candidate.Name.Equals(worldSelector,
                StringComparison.OrdinalIgnoreCase));
        if (world == null) throw new InvalidDataException($"World '{worldSelector}' was not decoded from {containerId}.");
        var worldScriptOwners = document.CinematicScriptOwners.Values.Where(owner => owner.WorldKey == world.Key)
            .ToArray();
        var assembledWorld = CinematicWorldAssembler.Assemble(document, world);
        var allLoadedScriptOwners = document.CinematicScriptOwners.Values.ToArray();
        var scriptEventTracks = document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks).ToArray();
        var allEventTracks = document.CinematicMessageTracks
            .Concat(CinematicScriptMessageTrack.FromScriptEventTracks(scriptEventTracks))
            .Concat(CinematicScriptMessageTrack.FromCinematicClips(document.CinematicClips.Values))
            .Concat(CinematicScriptMessageTrack.FromEmbeddedEventTracks(
                document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists)))
            .Concat(CinematicScriptMessageTrack.FromActorClips(document.Worlds))
            .ToArray();
        var namedReferences = CinematicScriptNamedReferenceCatalog.ForDocument(document);
        string? ResolveName(uint key, uint preferredWorldKey) =>
            CinematicScriptReferenceNameResolver.Resolve(key, preferredWorldKey, namedReferences)?.Name;
        var owners = worldScriptOwners
            .OrderBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
            .Select(owner => new
            {
                Owner = owner.OwnerKey.ToString("X8"),
                owner.Name,
                Brain = owner.BrainKey.ToString("X8"),
                Model = owner.ScriptModelKey.ToString("X8"),
                Events = owner.EventsKey.ToString("X8"),
                SceneEvents = owner.SceneEventsKey.ToString("X8"),
                owner.CallbackNames,
                owner.ModelFunctionNames,
                CallbackDrivenSceneEvents = CinematicScriptEventTrackCallResolver.Resolve(owner,
                        CinematicScriptReader.ReachableEngineCalls(owner), document.CinematicEmbeddedEventTracks)
                    .Select(invocation => new
                    {
                        invocation.Operation,
                        invocation.TrackIndex,
                        invocation.IndexBinding,
                        invocation.TargetBinding,
                        Event = invocation.EventList.EventKey.ToString("X8"),
                        EventName = invocation.EventList.OwnerName,
                        Tracks = invocation.Tracks.Select(track => new
                        {
                            track.Index,
                            Target = track.TargetKey.ToString("X8"),
                            track.Bone,
                            track.KeyCount,
                            track.Duration
                        }),
                        Source = invocation.SourceCall.Source,
                        invocation.SourceCall.Line,
                        invocation.SourceCall.FunctionName,
                        invocation.SourceCall.CallbackPath,
                        Arguments = invocation.SourceCall.Arguments,
                        BranchConditions = invocation.SourceCall.BranchConditions
                    }).ToArray(),
                UnresolvedSceneEventCalls = GetUnresolvedSceneEventCalls(owner, document),
                SourceEngineCallSummary = CinematicScriptReader.SummarizeReachableEngineCalls(owner),
                SourceMessageReaders = CinematicScriptReader.ReachableMessageReaders(owner),
                SourceMessageRoutes = CinematicScriptMessageFlowResolver.Resolve(owner,
                    document.CinematicScriptOwners.Values),
                SourceSavedFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(owner),
                SourceOvaFieldUsages = CinematicScriptOvaUsageResolver.Analyze(owner),
                CallbackSlots = owner.CallbackSlots?.Select(slot => new
                {
                    slot.Index,
                    FunctionKey = slot.FunctionKey.ToString("X8"),
                    slot.FunctionName
                }),
                DialogueLines = (owner.DialogueLines ?? []).Select(line => new
                {
                    line.Slot,
                    TextBank = line.TextBankKey.ToString("X8"),
                    line.TextId,
                    line.SoundId,
                    line.SoundVariantMax,
                    line.TextBox,
                    line.Volume,
                    line.Near,
                    line.Far
                }),
                TriggerAnalysis = CinematicScriptBehaviorAnalyzer.Analyze(owner, allLoadedScriptOwners),
                MessageGateCandidates = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(owner,
                    allEventTracks, namedReferences)?.Select(candidate => new
                {
                    EventOwner = candidate.Track.OwnerKey.ToString("X8"),
                    candidate.Track.LinkName,
                    Event = candidate.Track.EventKey.ToString("X8"),
                    SourceLinks = candidate.Sources.Select(source => new
                    {
                        source.Name,
                        source.LinkName,
                        Channel = source.ChannelIndex
                    }),
                    candidate.Time,
                    candidate.Message.Type,
                    candidate.Message.Int1,
                    QueueObject = candidate.QueueObject.Key.ToString("X8"),
                    QueueObjectWorld = candidate.QueueObject.WorldKey.ToString("X8"),
                    candidate.QueueObject.Name
                }).ToArray(),
                MessageGateContextMismatches = CinematicScriptBehaviorAnalyzer
                    .FindMessageGateContextMismatches(owner, allEventTracks, namedReferences)?.Select(candidate => new
                    {
                        Source = candidate.Track.Name,
                        candidate.Track.LinkName,
                        Event = candidate.Track.EventKey.ToString("X8"),
                        SourceLinks = candidate.Sources.Select(source => new
                        {
                            source.Name,
                            source.LinkName,
                            Channel = source.ChannelIndex
                        }),
                        candidate.Track.ChannelIndex,
                        candidate.Time,
                        candidate.Message.Type,
                        candidate.Message.Int1,
                        Context = candidate.ExecutionContext.Key.ToString("X8"),
                        ContextWorld = candidate.ExecutionContext.WorldKey.ToString("X8"),
                        ContextName = candidate.ExecutionContext.Name,
                        QueueObject = candidate.QueueObject.Key.ToString("X8"),
                        QueueName = candidate.QueueObject.Name
                    }).ToArray(),
                ExtendedMessageGateCandidates = CinematicScriptBehaviorAnalyzer
                    .FindExtendedMessageGateCandidates(owner, allEventTracks, namedReferences)?.Select(candidate => new
                    {
                        Source = candidate.Track.Name,
                        candidate.Track.LinkName,
                        Event = candidate.Track.EventKey.ToString("X8"),
                        candidate.Track.ChannelIndex,
                        candidate.Time,
                        Target = candidate.Call.Target.ToString("X8"),
                        TargetName = candidate.TargetObject.Name,
                        MessageId = candidate.Call.Arguments[0],
                        PayloadFloat = unchecked((float)(int)candidate.Call.Arguments[1]),
                        QueueObject = candidate.QueueObject.Key.ToString("X8"),
                        QueueName = candidate.QueueObject.Name,
                        SourceLinks = candidate.Sources.Select(source => new
                        {
                            source.Name,
                            source.LinkName,
                            Channel = source.ChannelIndex
                        })
                    }).ToArray(),
                ExtendedMessageGateTargetMismatches = CinematicScriptBehaviorAnalyzer
                    .FindExtendedMessageGateTargetMismatches(owner, allEventTracks, namedReferences)?.Select(candidate => new
                    {
                        Source = candidate.Track.Name,
                        candidate.Track.LinkName,
                        Event = candidate.Track.EventKey.ToString("X8"),
                        candidate.Track.ChannelIndex,
                        candidate.Time,
                        Target = candidate.Call.Target.ToString("X8"),
                        TargetName = candidate.TargetObject.Name,
                        MessageId = candidate.Call.Arguments[0],
                        PayloadFloat = unchecked((float)(int)candidate.Call.Arguments[1]),
                        QueueObject = candidate.QueueObject.Key.ToString("X8"),
                        QueueName = candidate.QueueObject.Name
                    }).ToArray(),
                ExtendedMessageGateInt1MaskMismatches = CinematicScriptBehaviorAnalyzer
                    .FindExtendedMessageGateInt1MaskMismatches(owner, allEventTracks, namedReferences)?.Select(candidate => new
                    {
                        Source = candidate.Track.Name,
                        candidate.Track.LinkName,
                        Event = candidate.Track.EventKey.ToString("X8"),
                        candidate.Track.ChannelIndex,
                        candidate.Time,
                        Target = candidate.Call.Target.ToString("X8"),
                        TargetName = candidate.TargetObject.Name,
                        MessageId = candidate.Call.Arguments[0],
                        PayloadFloat = unchecked((float)(int)candidate.Call.Arguments[1]),
                        QueueObject = candidate.QueueObject.Key.ToString("X8"),
                        QueueName = candidate.QueueObject.Name
                    }).ToArray(),
                Variables = owner.Variables.Select(variable => new
                {
                    variable.Name,
                    variable.Type,
                    variable.Source,
                    variable.Value
                }),
                References = owner.ObjectReferences.Select(reference => new
                {
                    reference.VariableName,
                    reference.ArrayIndex,
                    reference.Source,
                    Target = reference.TargetKey.ToString("X8"),
                    reference.TargetName
                })
            }).ToArray();
        var linkedEventKeys = owners.SelectMany(owner => new[] { owner.Events, owner.SceneEvents })
            .Select(key => Convert.ToUInt32(key, 16))
            .Where(key => key is > 2u and < uint.MaxValue).ToHashSet();
        var linkedActorTracks = document.Worlds.Where(candidate => candidate.Key == world.Key)
            .SelectMany(candidate => candidate.Actors)
            .SelectMany(actor => actor.Clips.Where(clip => linkedEventKeys.Contains(clip.Key))
                .Select(clip => new
                {
                    Actor = actor.Root.Name,
                    ActorKey = actor.Root.GaoKey.ToString("X8"),
                    Clip = clip.Key.ToString("X8"),
                    clip.Name,
                    clip.Duration,
                    DecodedCommands = CinematicLiteralCall.ReadTimeline(clip).Select(item => new
                    {
                        item.Time,
                        Target = item.Call.Target.ToString("X8"),
                        TargetName = ResolveName(item.Call.Target, world.Key),
                        Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                        Arguments = item.Call.Arguments.Select(argument => $"0x{argument:X8}").ToArray(),
                        Description = CinematicEngineFunctionCatalog.Describe(item.Call)
                    })
                })).ToArray();
        var sceneEventRecords = document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks)
            .Where(track => track.WorldKey == world.Key)
            .Select(track => new
            {
                Owner = track.OwnerKey.ToString("X8"),
                OwnerName = document.CinematicScriptOwners.GetValueOrDefault(track.OwnerId)?.Name,
                Link = track.LinkName,
                Clip = track.EventKey.ToString("X8"),
                track.Name,
                track.Duration,
                EmbeddedTracks = document.CinematicEmbeddedEventTracks
                    .GetValueOrDefault((track.WorldKey, track.OwnerKey), [])
                    .Where(list => list.EventKey == track.EventKey)
                    .SelectMany(list => list.Tracks.Select(embedded => new
                    {
                        embedded.Index,
                        Target = embedded.TargetKey.ToString("X8"),
                        embedded.Bone,
                        embedded.ChannelFlags,
                        embedded.KeyCount,
                        embedded.Duration,
                        ActionLinks = GetEmbeddedActionLinks(embedded, track.WorldKey, document),
                        Calls = embedded.Calls.Select(item => new
                        {
                            item.Time,
                            Target = item.Call.Target.ToString("X8"),
                            TargetName = ResolveName(item.Call.Target, track.WorldKey),
                            Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                            Arguments = item.Call.Arguments.Select(argument => $"0x{argument:X8}").ToArray(),
                            Description = CinematicEngineFunctionCatalog.Describe(item.Call),
                            LinkedTrack = CinematicEmbeddedEventTrackResolver.Describe(
                                item.Call, document, track.WorldKey)
                        }),
                        Messages = embedded.Messages.Select(item => new
                        {
                            item.Time,
                            ExecutionObject = item.Message.ExecutionObjectKey.ToString("X8"),
                            Gao = item.Message.Gao.ToString("X8"),
                            Gao2 = item.Message.Gao2.ToString("X8"),
                            ExecutionObjectName = ResolveName(item.Message.ExecutionObjectKey, track.WorldKey),
                            GaoName = ResolveName(item.Message.Gao, track.WorldKey),
                            Gao2Name = ResolveName(item.Message.Gao2, track.WorldKey),
                            item.Message.Type,
                            item.Message.Int1,
                            item.Message.Int2,
                            Behavior = CinematicScriptMessageAnalyzer.Describe(
                                item.Message, allLoadedScriptOwners, track.WorldKey),
                            item.Message.Float1,
                            item.Message.Float2,
                            item.Message.Vector1,
                            item.Message.Vector2
                        })
                    })),
                Calls = track.Calls.Select(item => new
                {
                    item.Time,
                    Target = item.Call.Target.ToString("X8"),
                    Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                    Arguments = item.Call.Arguments.Select(argument => $"0x{argument:X8}").ToArray(),
                    Description = CinematicEngineFunctionCatalog.Describe(item.Call)
                }),
                Messages = track.Messages.Select(item => new
                {
                    item.Time,
                    ExecutionObject = item.Message.ExecutionObjectKey.ToString("X8"),
                    Gao = item.Message.Gao.ToString("X8"),
                    Gao2 = item.Message.Gao2.ToString("X8"),
                    ExecutionObjectName = ResolveName(item.Message.ExecutionObjectKey, world.Key),
                    GaoName = ResolveName(item.Message.Gao, world.Key),
                    Gao2Name = ResolveName(item.Message.Gao2, world.Key),
                    item.Message.Type,
                    item.Message.Int1,
                    item.Message.Int2,
                    Behavior = CinematicScriptMessageAnalyzer.Describe(
                        item.Message, allLoadedScriptOwners, world.Key),
                    item.Message.Float1,
                    item.Message.Float2,
                    item.Message.Vector1,
                    item.Message.Vector2
                })
            }).ToArray();

        string path = Path.Combine(output, $"{containerId}-{world.Key:X8}-script-world.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Container = containerId,
            World = world.Key.ToString("X8"),
            world.Name,
            ArchiveWorlds = document.Worlds.Select(sourceWorld => new
            {
                Key = sourceWorld.Key.ToString("X8"),
                sourceWorld.Name,
                ObjectCount = sourceWorld.Objects.Count,
                LighthouseObjects = sourceWorld.Objects.Where(sceneObject =>
                        sceneObject.Name.Contains("phare", StringComparison.OrdinalIgnoreCase)
                        || sceneObject.Name.Contains("lighthouse", StringComparison.OrdinalIgnoreCase))
                    .Select(sceneObject => new
                    {
                        Key = sceneObject.GaoKey.ToString("X8"),
                        sceneObject.Name,
                        Geometry = sceneObject.GeometryKey.ToString("X8"),
                        sceneObject.DrawFlags,
                        sceneObject.Flags,
                        sceneObject.Transform.Translation
                    })
            }),
            SceneObjects = world.Objects.Select(sceneObject => new
            {
                Key = sceneObject.GaoKey.ToString("X8"),
                sceneObject.Name,
                Geometry = sceneObject.GeometryKey.ToString("X8"),
                Material = sceneObject.MaterialKey.ToString("X8"),
                sceneObject.DrawFlags,
                sceneObject.Flags,
                sceneObject.Transform.Translation
            }),
            AssembledObjectCount = assembledWorld.Objects.Count,
            AssembledLighthouseObjects = assembledWorld.Objects.Where(sceneObject =>
                    sceneObject.Name.Contains("phare", StringComparison.OrdinalIgnoreCase)
                    || sceneObject.Name.Contains("lighthouse", StringComparison.OrdinalIgnoreCase))
                .Select(sceneObject => new
                {
                    Key = sceneObject.GaoKey.ToString("X8"),
                    sceneObject.Name,
                    Geometry = sceneObject.GeometryKey.ToString("X8")
                }),
            ScriptOwners = owners,
            LoadDiagnostics = document.Diagnostics.ToArray(),
            LinkedActorTracks = linkedActorTracks,
            LinkedSceneEventRecords = sceneEventRecords,
            SavedFlagLinks = worldScriptOwners.SelectMany(owner =>
                    CinematicScriptFlagFlowAnalyzer.FindRelatedOwners(owner, allLoadedScriptOwners, scriptEventTracks)
                        .Select(link => new
                        {
                            Owner = owner.OwnerKey.ToString("X8"),
                            OwnerName = owner.Name,
                            RelatedOwner = link.RelatedOwner.OwnerKey.ToString("X8"),
                            RelatedOwnerName = link.RelatedOwner.Name,
                            RelatedWorld = link.RelatedOwner.WorldKey.ToString("X8"),
                            link.Flag,
                            link.Direction
                        }))
                .ToArray(),
            CinematicClips = document.CinematicClips.Values
                .Where(clip => clip.CinematicWorldKey == world.Key)
                .Select(clip => new { Key = clip.Key.ToString("X8"), clip.Duration }),
            DecodedCinematicMessages = CinematicScriptMessageTrack
                .FromCinematicClips(document.CinematicClips.Values.Where(clip => clip.CinematicWorldKey == world.Key))
                .Where(track => track.Messages.Length > 0)
                .Select(track => new
                {
                    Clip = track.EventKey.ToString("X8"),
                    track.Name,
                    Messages = track.Messages.Select(item => new
                    {
                        item.Time,
                        item.Message.ExecutionObjectKey,
                        item.Message.Type,
                        item.Message.Int1,
                        item.Message.Gao,
                        item.Message.Gao2
                    })
                }),
            Limits = "Static owner/model/OVA relationships and bounded literal event calls/messages. SceneEvents "
                + "keys and callbacks are not executed; world transitions and live gameplay state are not inferred."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"WORLD SCRIPT GRAPH: {world.Name} · {owners.Length} owners; {path}");
    }

    private static object[] GetUnresolvedSceneEventCalls(CinematicScriptOwner owner, SceneDocument document) =>
        CinematicScriptEventTrackCallResolver.Analyze(owner,
                CinematicScriptReader.ReachableEngineCalls(owner), document.CinematicEmbeddedEventTracks)
            .Unresolved.Select(unresolved => new
            {
                unresolved.SourceCall.Api,
                Arguments = unresolved.SourceCall.Arguments,
                unresolved.SourceCall.FunctionName,
                unresolved.SourceCall.CallbackPath,
                Source = unresolved.SourceCall.Source,
                unresolved.SourceCall.Line,
                BranchConditions = unresolved.SourceCall.BranchConditions,
                unresolved.Reason
            })
            .Cast<object>()
            .ToArray();

    private static object GetEmbeddedActionLinks(CinematicEmbeddedEventTrack track,
        uint preferredWorldKey, SceneDocument document)
    {
        var analysis = CinematicEventActionLinkResolver.AnalyzeCalls(track.Calls,
            preferredWorldKey, document.CinematicActionKitsByOwner);
        return new
        {
            Resolved = analysis.Resolved.Select(link => new
            {
                link.Time,
                link.Function,
                link.ActionIndex,
                link.ControlValue,
                Target = link.Call.Target.ToString("X8"),
                ActionTableWorld = link.ActionTableWorldKey.ToString("X8"),
                Clips = link.Action.Items.Select(item => new
                {
                    Clip = item.Clip.Key.ToString("X8"),
                    Shape = item.ShapeKey.ToString("X8")
                })
            }),
            Unresolved = analysis.Unresolved.Select(link => new
            {
                link.Time,
                link.Function,
                Target = link.Call.Target.ToString("X8"),
                Arguments = link.Call.Arguments.Select(argument => argument.ToString("X8")),
                link.Reason
            })
        };
    }
}
