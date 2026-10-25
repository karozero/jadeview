using System.Text.Json;
using JadeView;

internal static class CinematicScriptGraphAudit
{
    internal static void Write(string archivePath, string containerId, uint clipKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate => candidate.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        if (!document.CinematicClips.TryGetValue(clipKey, out var clip))
            throw new InvalidDataException($"Cinematic {clipKey:X8} was not decoded from {containerId}.");
        var namedReferences = CinematicScriptNamedReferenceCatalog.ForDocument(document);
        var cinematicSourceWorld = document.Worlds.FirstOrDefault(world => world.Key == clip.CinematicWorldKey)
            ?? throw new InvalidDataException($"Cinematic world {clip.CinematicWorldKey:X8} is missing from {containerId}.");
        var assembledWorld = CinematicWorldAssembler.Assemble(document, cinematicSourceWorld);
        var participantKeys = assembledWorld.Actors.Select(actor => actor.Root.GaoKey).ToHashSet();
        var worldOwners = document.CinematicScriptOwners.Values
            .Where(owner => owner.WorldKey == clip.CinematicWorldKey).ToArray();
        participantKeys.UnionWith(worldOwners.SelectMany(owner => owner.ObjectReferences)
            .Select(reference => reference.TargetKey));
        var calls = CinematicLiteralCall.ReadTimeline(clip);
        var trackedObjectKeys = clip.Tracks.Select(track => track.Target)
            .Where(key => key is > 0 and < uint.MaxValue).ToArray();
        var serializedActionLinks = CinematicEventActionLinkResolver.AnalyzeCalls(calls,
            clip.CinematicWorldKey, document.CinematicActionKitsByOwner);
        participantKeys.UnionWith(calls.Select(item => item.Call.Target));
        participantKeys.UnionWith(trackedObjectKeys);
        var typedMessages = CinematicTimelineMessage.ReadTimeline(clip);
        var messageTracks = document.CinematicMessageTracks
            .Concat(CinematicScriptMessageTrack.FromScriptEventTracks(
                document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks)))
            .Concat(CinematicScriptMessageTrack.FromCinematicClips(document.CinematicClips.Values))
            .Concat(CinematicScriptMessageTrack.FromEmbeddedEventTracks(
                document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists)))
            .Concat(CinematicScriptMessageTrack.FromActorClips(document.Worlds))
            .ToArray();
        var reachableScriptOwnerObjects = CinematicScriptScope.ForClip(document.CinematicScriptOwners.Values,
                clip.CinematicWorldKey,
                document.Worlds.Where(world => world.Key == clip.CinematicWorldKey)
                .SelectMany(world => world.Key == clip.CinematicWorldKey
                    ? assembledWorld.Actors
                    : world.Actors).Select(actor => actor.Root.GaoKey),
                calls.Select(item => item.Call.Target).Concat(trackedObjectKeys),
                typedMessages.Select(item => item.Message),
                ownerEventTracks: document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks))
            .OrderBy(owner => owner.WorldKey)
            .ThenBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reachableScriptOwners = reachableScriptOwnerObjects
            .Select(owner => new
            {
                Owner = owner.OwnerKey.ToString("X8"),
                owner.Name,
                SourceWorld = document.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name,
                Model = owner.ScriptModelKey.ToString("X8"),
                Events = owner.EventsKey.ToString("X8"),
                SceneEvents = owner.SceneEventsKey.ToString("X8"),
                SerializedGaoFlags = owner.SerializedGaoFlags.ToString("X8"),
                SerializedStateFlags = owner.SerializedStateFlags.ToString("X8"),
                owner.CallbackNames,
                owner.ModelFunctionNames,
                SerializedObjectReferences = owner.ObjectReferences,
                SerializedOvaValues = owner.Variables,
                CallbackDrivenSceneEvents = GetCallbackDrivenSceneEventCalls(owner, document),
                CallbackRegistrationAnalysis = GetCallbackRegistrationAnalysis(owner),
                RliDisplayCallbackAnalysis = CinematicScriptRliDisplayCallbackAnalyzer.Analyze(owner)
                    .Select(callback => new
                    {
                        callback.ContextKey,
                        TargetKey = callback.TargetKey?.ToString("X8"),
                        callback.CallbackType,
                        callback.FunctionName,
                        callback.FactorExpression,
                        callback.DestinationColorExpression,
                        callback.FactorIsUsed,
                        callback.HasPairedCallback,
                        callback.Source,
                        callback.Line,
                        callback.BranchConditions,
                        callback.CallbackPath
                    }).ToArray(),
                RliDisplayOvaFieldAccesses = GetRliDisplayOvaFieldAccesses(owner),
                AiTrackMutationAnalysis = GetAiTrackMutationAnalysis(owner),
                CallbackStateEffectCandidates = CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner)
                    .Select(effect => new
                    {
                        effect.Api,
                        effect.Variant,
                        effect.StateEffect,
                        effect.Arguments,
                        effect.BranchConditions,
                        effect.FunctionName,
                        effect.Source,
                        effect.Line,
                        effect.CallbackPath,
                        effect.ExecutionStatus,
                        ReceiverKey = effect.ReceiverKey?.ToString("X8"),
                        effect.ReceiverEvidence,
                        effect.ReceiverGuardAliases
                    }).ToArray(),
                UnresolvedSceneEventCalls = GetUnresolvedSceneEventCalls(owner, document),
                CallbackActionRequests = GetCallbackActionRequests(owner, document),
                CallbackActionKitMerges = GetCallbackActionKitMerges(owner, document),
                SourceReferencedObjectCalls = CinematicScriptReferencedObjectCallResolver.Resolve(owner),
                SourceChannelTransfers = CinematicScriptChannelTransferAnalyzer.Analyze(owner)
                    .Select(transfer => new
                    {
                        transfer.OwnerKey,
                        TargetKey = transfer.TargetKey.ToString("X8"),
                        transfer.TargetName,
                        transfer.OvaField,
                        transfer.SourceChannel,
                        transfer.TargetChannel,
                        transfer.FunctionName,
                        transfer.Source,
                        transfer.GetterLine,
                        transfer.SetterLine,
                        transfer.CallbackPath,
                        transfer.SourcePathResolved,
                        transfer.BranchConditions,
                        transfer.ClearsInvisibleFlag,
                        transfer.InvisibleFlagLine,
                        transfer.ReceiverGuardAliases,
                        CallbackPathSteps = transfer.CallbackPathSteps.Select(step => new
                        {
                            step.FromFunction,
                            step.ToFunction,
                            step.Dispatch,
                            step.TrackIndex,
                            step.ReferenceVariable,
                            step.BranchConditions,
                            step.DispatchConditionsResolved
                        }).ToArray()
                    }).ToArray(),
                SourceOvaArgumentBindings = GetSourceOvaArgumentBindings(owner),
                SourceSavedFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(owner),
                CallbackSlots = owner.CallbackSlots?.Select(slot => new
                {
                    slot.Index,
                    Key = slot.FunctionKey.ToString("X8"),
                    slot.FunctionName
                })
            }).ToArray();
        var availableObjectReferences = namedReferences.Where(reference => reference.IsObject).ToArray();
        var assembledObjectKeys = assembledWorld.Objects.Select(sceneObject => sceneObject.GaoKey).ToHashSet();
        var sourceObjectDependencies = reachableScriptOwnerObjects.SelectMany(owner =>
                CinematicScriptSourceObjectDependencyResolver.ResolveUniqueObjects(
                        CinematicScriptReader.ReachableSourceReferences(owner), availableObjectReferences)
                    .Select(reference => new
                    {
                        FromOwner = owner.Name,
                        FromOwnerKey = owner.OwnerKey.ToString("X8"),
                        SourceWorld = reference.WorldKey.ToString("X8"),
                        Key = reference.Key.ToString("X8"),
                        reference.Name,
                        IsIncludedInAssembly = assembledObjectKeys.Contains(reference.Key)
                    }))
            .DistinctBy(reference => (reference.FromOwnerKey, reference.SourceWorld, reference.Key))
            .OrderBy(reference => reference.FromOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Key)
            .ToArray();
        var allScriptOwners = document.CinematicScriptOwners.Values.ToArray();
        var allScriptEventTracks = document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks).ToArray();
        var savedFlagLinkGraph = CinematicScriptFlagFlowAnalyzer.BuildLinkGraph(allScriptOwners,
            allScriptEventTracks);
        var reachableSavedFlagLinks = reachableScriptOwnerObjects.SelectMany(owner =>
                savedFlagLinkGraph.GetValueOrDefault(owner.Id, []).Select(link => new
                {
                    Owner = owner.OwnerKey.ToString("X8"),
                    owner.Name,
                    SourceWorld = owner.WorldKey.ToString("X8"),
                    RelatedOwner = link.RelatedOwner.OwnerKey.ToString("X8"),
                    RelatedName = link.RelatedOwner.Name,
                    RelatedWorld = link.RelatedOwner.WorldKey.ToString("X8"),
                    link.Flag,
                    link.Direction
                }))
            .ToArray();
        var participants = assembledWorld.Actors
            .Select(actor => new { Key = actor.Root.GaoKey.ToString("X8"), actor.Root.Name })
            .ToArray();
        var participantScripts = document.CinematicScriptOwners.Values
            .Where(owner => participantKeys.Contains(owner.OwnerKey)
                && owner.WorldKey != clip.CinematicWorldKey)
            .Select(owner => new
            {
                Owner = owner.OwnerKey.ToString("X8"),
                owner.Name,
                SourceWorld = document.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name,
                Brain = owner.BrainKey.ToString("X8"),
                Model = owner.ScriptModelKey.ToString("X8"),
                Events = owner.EventsKey.ToString("X8"),
                SceneEvents = owner.SceneEventsKey.ToString("X8"),
                SerializedGaoFlags = owner.SerializedGaoFlags.ToString("X8"),
                SerializedStateFlags = owner.SerializedStateFlags.ToString("X8"),
                WorldAiSchedulerFindings = CinematicScriptSchedulerAnalyzer.Analyze(owner),
                WorldEventDispatchFindings = CinematicEventSchedulerAnalyzer.Analyze(owner),
                owner.CallbackNames,
                owner.ModelFunctionNames,
                CallbackActionRequests = GetCallbackActionRequests(owner, document),
                CallbackActionKitMerges = GetCallbackActionKitMerges(owner, document),
                SourceSavedFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(owner),
                SourceCallbackPaths = CinematicScriptReader.NestedCallbackPaths(owner),
                EngineCallbackDispatchSites = CinematicScriptReader.DispatchSitesFor(
                    CinematicScriptReader.NestedCallbackPaths(owner)),
                SourceWorldSchedulerOperations = CinematicScriptReader.ReachableWorldSchedulerOperations(owner),
                SourceEngineCallSummary = CinematicScriptReader.SummarizeReachableEngineCalls(owner),
                SourceMessageReaders = CinematicScriptReader.ReachableMessageReaders(owner),
                SourceMessageRoutes = CinematicScriptMessageFlowResolver.Resolve(owner,
                    document.CinematicScriptOwners.Values),
                SourceReferencedObjectCalls = CinematicScriptReferencedObjectCallResolver.Resolve(owner),
                SourceObjectArgumentBindings = GetSourceObjectArgumentBindings(owner, namedReferences),
                SourceOvaArgumentBindings = GetSourceOvaArgumentBindings(owner),
                SourceResolvedReferences = CinematicScriptReader.ReachableSourceReferences(owner).Select(reference => new
                {
                    reference.FunctionName,
                    reference.CallbackPath,
                    reference.Kind,
                    reference.Symbol,
                    Key = reference.Key.ToString("X8"),
                    TargetName = reference.Kind == "Function"
                        ? CinematicScriptReader.FunctionNameFor(reference.Key)
                        : ResolveMessageReference(reference.Key, owner.WorldKey),
                    reference.Source,
                    reference.Line
                }),
                CallbackSlots = owner.CallbackSlots?.Select(slot => new
                {
                    slot.Index,
                    Key = slot.FunctionKey.ToString("X8"),
                    slot.FunctionName
                }),
                CallbackScanSpan = CinematicScriptCallbackCoverage.ResolveConfiguredRange(
                    owner.CallbackSlots ?? []),
                EventTracks = document.CinematicScriptEventTracks.GetValueOrDefault(owner.Id, [])
                    .Select(track => new
                    {
                        track.LinkName,
                        Event = track.EventKey.ToString("X8"),
                        track.Name,
                        track.Duration,
                        Commands = track.Calls.Length,
                        ActionLinks = CinematicEventActionLinkResolver.Analyze(track,
                                document.CinematicActionKitsByOwner).Resolved.Select(link => new
                            {
                                link.Time,
                                link.Function,
                                Target = link.Call.Target.ToString("X8"),
                                link.ActionIndex,
                                link.ControlValue,
                                ActionTableWorld = link.ActionTableWorldKey.ToString("X8"),
                                Clips = link.Action.Items.Select(item => new
                                {
                                    Key = item.Clip.Key.ToString("X8"),
                                    item.ShapeKey
                                }).ToArray()
                            }),
                        UnresolvedActionLinks = CinematicEventActionLinkResolver.Analyze(track,
                                document.CinematicActionKitsByOwner).Unresolved.Select(link => new
                            {
                                link.Time,
                                link.Function,
                                Target = link.Call.Target.ToString("X8"),
                                Arguments = link.Call.Arguments,
                                link.Reason
                            }),
                        SoundCommands = track.Calls.Select(item => new
                            {
                                item.Time,
                                Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                                Binding = CinematicSoundCommandResolver.Resolve(item.Call, track.WorldKey,
                                    document, archiveEntries: archive.Entries)
                            }).Where(item => item.Binding != null),
                        Messages = track.Messages.Length
                    })
            }).ToArray();
        var referencedActorLibraries = document.Worlds
            .SelectMany(world => world.Actors.Select(actor => new { World = world, Actor = actor }))
            .Where(item => participantKeys.Contains(item.Actor.Root.GaoKey))
            .Select(item => new
            {
                Actor = item.Actor.Root.GaoKey.ToString("X8"),
                item.Actor.Root.Name,
                SourceWorld = item.World.Name,
                item.Actor.Diagnostic,
                ActionClipCount = item.Actor.Clips.Count,
                ActionClips = item.Actor.Clips.Take(12).Select(actorClip => new
                {
                    Key = actorClip.Key.ToString("X8"),
                    actorClip.Name,
                    actorClip.Duration,
                    actorClip.Cinematic
                })
            }).ToArray();

        var owners = document.CinematicScriptOwners.Values.Where(owner => owner.WorldKey == clip.CinematicWorldKey)
            .Select(owner => new
            {
                Owner = owner.OwnerKey.ToString("X8"),
                owner.Name,
                Brain = owner.BrainKey.ToString("X8"),
                Model = owner.ScriptModelKey.ToString("X8"),
                Events = owner.EventsKey.ToString("X8"),
                SceneEvents = owner.SceneEventsKey.ToString("X8"),
                SerializedGaoFlags = owner.SerializedGaoFlags.ToString("X8"),
                SerializedStateFlags = owner.SerializedStateFlags.ToString("X8"),
                WorldAiSchedulerFindings = CinematicScriptSchedulerAnalyzer.Analyze(owner),
                WorldEventDispatchFindings = CinematicEventSchedulerAnalyzer.Analyze(owner),
                owner.CallbackNames,
                owner.ModelFunctionNames,
                CallbackDrivenSceneEvents = GetCallbackDrivenSceneEventCalls(owner, document),
                UnresolvedSceneEventCalls = GetUnresolvedSceneEventCalls(owner, document),
                SourceCallbackPaths = CinematicScriptReader.NestedCallbackPaths(owner),
                EngineCallbackDispatchSites = CinematicScriptReader.DispatchSitesFor(
                    CinematicScriptReader.NestedCallbackPaths(owner)),
                SourceWorldSchedulerOperations = CinematicScriptReader.ReachableWorldSchedulerOperations(owner),
                SourceEngineCallSummary = CinematicScriptReader.SummarizeReachableEngineCalls(owner),
                SourceMessageReaders = CinematicScriptReader.ReachableMessageReaders(owner),
                SourceMessageRoutes = CinematicScriptMessageFlowResolver.Resolve(owner,
                    document.CinematicScriptOwners.Values),
                SourceObjectArgumentBindings = GetSourceObjectArgumentBindings(owner, namedReferences),
                SourceOvaArgumentBindings = GetSourceOvaArgumentBindings(owner),
                SourceResolvedReferences = CinematicScriptReader.ReachableSourceReferences(owner).Select(reference => new
                {
                    reference.FunctionName,
                    reference.CallbackPath,
                    reference.Kind,
                    reference.Symbol,
                    Key = reference.Key.ToString("X8"),
                    TargetName = reference.Kind == "Function"
                        ? CinematicScriptReader.FunctionNameFor(reference.Key)
                        : ResolveMessageReference(reference.Key, owner.WorldKey),
                    reference.Source,
                    reference.Line
                }),
                CallbackSlots = owner.CallbackSlots?.Select(slot => new
                {
                    slot.Index,
                    Key = slot.FunctionKey.ToString("X8"),
                    slot.FunctionName
                }),
                CallbackScanSpan = CinematicScriptCallbackCoverage.ResolveConfiguredRange(
                    owner.CallbackSlots ?? []),
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
                AudioBindings = CinematicScriptAudioBindingResolver.Resolve(owner, document).Select(binding => new
                {
                    binding.VariableName,
                    binding.ModifierId,
                    SoundKeys = binding.Modifiers.Select(modifier => modifier.SoundKey.ToString("X8")),
                    binding.Status
                }),
                SelectedClipIncomingLinks = CinematicClipIncomingLinkResolver.Find(owner.OwnerKey, calls,
                    typedMessages).Select(link => new
                    {
                        link.Time,
                        Target = link.TargetKey.ToString("X8"),
                        link.Description
                    }),
                TriggerAnalysis = CinematicScriptBehaviorAnalyzer.Analyze(owner,
                    document.CinematicScriptOwners.Values),
                MessageGateCandidates = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(owner,
                    messageTracks,
                    namedReferences)?.Select(candidate => new
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
                    .FindMessageGateContextMismatches(owner, messageTracks,
                        namedReferences)?.Select(candidate => new
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
                            ContextName = candidate.ExecutionContext.Name,
                            QueueObject = candidate.QueueObject.Key.ToString("X8"),
                            QueueName = candidate.QueueObject.Name
                        }).ToArray(),
                ExtendedMessageGateCandidates = CinematicScriptBehaviorAnalyzer
                    .FindExtendedMessageGateCandidates(owner, messageTracks, namedReferences)?.Select(candidate => new
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
                    .FindExtendedMessageGateTargetMismatches(owner, messageTracks, namedReferences)?.Select(candidate => new
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
                    .FindExtendedMessageGateInt1MaskMismatches(owner, messageTracks, namedReferences)?.Select(candidate => new
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
                OwnerQueueMessageCandidates = CinematicScriptBehaviorAnalyzer
                    .FindOwnerQueueMessageCandidates(owner, messageTracks, namedReferences)?.Select(candidate =>
                    {
                        _ = CinematicEngineFunctionCatalog.TryReadMessageGateFields(candidate.Call,
                            out int messageId, out int messageInt1, out _);
                        return new
                        {
                            Source = candidate.Track.Name,
                            candidate.Track.LinkName,
                            Event = candidate.Track.EventKey.ToString("X8"),
                            candidate.Track.ChannelIndex,
                            candidate.Time,
                            Sender = candidate.Call.Target.ToString("X8"),
                            MessageId = messageId,
                            Int1 = messageInt1,
                            GateQueue = candidate.GateQueueObject.Key.ToString("X8"),
                            GateQueueName = candidate.GateQueueObject.Name,
                            PriorSameChannelRegistrations = candidate.PriorSameChannelRegistrations.Select(registration => new
                            {
                                Source = registration.Track.Name,
                                registration.Track.LinkName,
                                Event = registration.Track.EventKey.ToString("X8"),
                                registration.Track.ChannelIndex,
                                registration.Time,
                                Target = registration.Call.Target.ToString("X8"),
                                TargetName = registration.RegisteredOwnerCandidate?.Name
                            }).ToArray(),
                            SourceLinks = candidate.Sources.Select(source => new
                            {
                                source.Name,
                                source.LinkName,
                                Channel = source.ChannelIndex
                            })
                        };
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
                }),
                EventTracks = document.CinematicScriptEventTracks.GetValueOrDefault(owner.Id, [])
                    .Select(track => new
                    {
                        track.LinkName,
                        Event = track.EventKey.ToString("X8"),
                        track.Name,
                        track.Duration,
                        ActionLinks = CinematicEventActionLinkResolver.Analyze(track,
                                document.CinematicActionKitsByOwner).Resolved.Select(link => new
                            {
                                link.Time,
                                link.Function,
                                Target = link.Call.Target.ToString("X8"),
                                link.ActionIndex,
                                link.ControlValue,
                                ActionTableWorld = link.ActionTableWorldKey.ToString("X8"),
                                Clips = link.Action.Items.Select(item => new
                                {
                                    Key = item.Clip.Key.ToString("X8"),
                                    item.ShapeKey
                                }).ToArray()
                            }),
                        EmbeddedTracks = document.CinematicEmbeddedEventTracks
                            .GetValueOrDefault((owner.WorldKey, owner.OwnerKey), [])
                            .Where(list => list.EventKey == track.EventKey)
                            .SelectMany(list => list.Tracks.Select(embedded => new
                            {
                                embedded.Index,
                                Target = embedded.TargetKey.ToString("X8"),
                                embedded.Bone,
                                embedded.ChannelFlags,
                                embedded.KeyCount,
                                embedded.Duration
                            })),
                        Commands = track.Calls.Select(item => new
                        {
                            item.Time,
                            Target = item.Call.Target.ToString("X8"),
                            Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                            item.Call.Arguments,
                            Description = CinematicEngineFunctionCatalog.Describe(item.Call),
                            SoundBinding = CinematicSoundCommandResolver.Resolve(item.Call, owner.WorldKey,
                                document, archiveEntries: archive.Entries),
                            EmbeddedTrack = CinematicEmbeddedEventTrackResolver.Describe(
                                item.Call, document, owner.WorldKey)
                        }),
                        Messages = track.Messages.Length
                    })
            }).ToArray();
        var ownerReferences = owners.Select(owner => new
        {
            owner.Owner,
            owner.Name,
            ReferencedBy = owners.SelectMany(source => source.References
                .Where(reference => reference.Target == owner.Owner)
                .Select(reference => new
                {
                    SourceOwner = source.Owner,
                    SourceName = source.Name,
                    reference.VariableName,
                    reference.Source
                }))
        }).ToArray();
        var serializedCalls = calls.Select(item => new
        {
            item.Time,
            Target = item.Call.Target.ToString("X8"),
            TargetName = ResolveMessageReference(item.Call.Target, clip.CinematicWorldKey),
            Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
            item.Call.Arguments,
            Description = CinematicEngineFunctionCatalog.Describe(item.Call),
            ActionLink = serializedActionLinks.Resolved.FirstOrDefault(link => link.Time == item.Time
                    && ReferenceEquals(link.Call, item.Call)) is { } actionLink
                ? new
                {
                    actionLink.Function,
                    actionLink.ActionIndex,
                    actionLink.ControlValue,
                    ActionTableWorld = actionLink.ActionTableWorldKey.ToString("X8"),
                    Clips = actionLink.Action.Items.Select(animationItem => new
                    {
                        Key = animationItem.Clip.Key.ToString("X8"),
                        animationItem.ShapeKey
                    }).ToArray()
                } : null,
            UnresolvedActionLink = serializedActionLinks.Unresolved.FirstOrDefault(link => link.Time == item.Time
                    && ReferenceEquals(link.Call, item.Call)) is { } unresolvedActionLink
                ? new
                {
                    unresolvedActionLink.Function,
                    unresolvedActionLink.Reason
                } : null,
            SoundBinding = CinematicSoundCommandResolver.Resolve(item.Call, clip.CinematicWorldKey,
                document, archiveEntries: archive.Entries),
            EmbeddedTrack = CinematicEmbeddedEventTrackResolver.Describe(
                item.Call, document, clip.CinematicWorldKey)
        }).ToArray();
        var messages = typedMessages.Select(item => new
        {
            item.Time,
            ExecutionObject = item.Message.ExecutionObjectKey.ToString("X8"),
            ExecutionObjectName = ResolveMessageReference(item.Message.ExecutionObjectKey, clip.CinematicWorldKey),
            Gao = item.Message.Gao.ToString("X8"),
            GaoName = ResolveMessageReference(item.Message.Gao, clip.CinematicWorldKey),
            Gao2 = item.Message.Gao2.ToString("X8"),
            Gao2Name = ResolveMessageReference(item.Message.Gao2, clip.CinematicWorldKey),
            item.Message.Type,
            item.Message.Int1,
            item.Message.Int2,
            Dialogue = item.Message.Type == 30
                ? DescribeDialogueMessage(item.Message, document.CinematicScriptOwners.Values,
                    clip.CinematicWorldKey)
                : null,
            Behavior = CinematicScriptMessageAnalyzer.Describe(item.Message,
                document.CinematicScriptOwners.Values, clip.CinematicWorldKey),
            item.Message.Float1,
            item.Message.Float2,
            item.Message.Vector1,
            item.Message.Vector2
        }).ToArray();
        var embeddedTrackLists = document.CinematicEmbeddedEventTracks.Values.SelectMany(lists => lists)
            .Where(list => list.WorldKey == clip.CinematicWorldKey)
            .ToArray();
        var referencedExternalEmbeddedLists = document.CinematicEmbeddedEventTracks
            .Where(entry => entry.Key.WorldKey != clip.CinematicWorldKey
                && participantKeys.Contains(entry.Key.OwnerKey))
            .SelectMany(entry => entry.Value)
            .ToArray();
        var embeddedIndexProbes = embeddedTrackLists.Where(list => list.Tracks.Length > 0)
            .Select(list => new
            {
                Owner = list.OwnerKey.ToString("X8"),
                list.OwnerName,
                Link = list.LinkName,
                Event = list.EventKey.ToString("X8"),
                Resolved = CinematicEmbeddedEventTrackResolver.Describe(
                    new(list.OwnerKey, 0x3B64, [0]), document, list.WorldKey)
                    ?.Contains("EVE track 0", StringComparison.Ordinal) == true
            }).ToArray();

        string? ResolveMessageReference(uint key, uint preferredWorldKey)
        {
            return CinematicScriptReferenceNameResolver.Resolve(key, preferredWorldKey, namedReferences)?.Name;
        }

        static object DescribeDialogueMessage(CinematicTimelineMessage message,
            IEnumerable<CinematicScriptOwner> owners, uint preferredWorldKey)
        {
            var resolution = CinematicDialogueMessageResolver.Resolve(message, owners, preferredWorldKey);
            var receiver = resolution.ReceiverId is { } receiverId
                ? owners.FirstOrDefault(owner => owner.Id == receiverId)
                : null;
            return new
            {
                Kind = resolution.Kind.ToString(),
                resolution.Slot,
                TextBank = resolution.TextBankKey?.ToString("X8"),
                resolution.TextId,
                Receiver = receiver?.Name,
                resolution.EffectivePriority,
                Line = resolution.Line is { } line ? new
                {
                    line.SoundId,
                    line.SoundVariantMax,
                    line.Volume,
                    line.Near,
                    line.Far,
                    line.Duration
                } : null,
                Details = CinematicDialogueMessageResolver.DescribeSelectedLine(message, resolution)
            };
        }

        string path = Path.Combine(output, $"{containerId}-{clipKey:X8}-script-graph.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Container = containerId,
            WorldKey = clip.CinematicWorldKey.ToString("X8"),
            WorldName = document.Worlds.FirstOrDefault(world => world.Key == clip.CinematicWorldKey)?.Name,
            Clip = clip.Key.ToString("X8"),
            clip.Duration,
            LoadedWorlds = document.Worlds.Select(world => new { Key = world.Key.ToString("X8"), world.Name }),
            CinematicParticipants = participants,
            BoundedCallbackPlaybackProbe = ProbeBoundedCallbackPlayback(archive, entry, document,
                assembledWorld, clip),
            ScriptOwners = owners,
            ParticipantScriptOwners = participantScripts,
            ReferencedActorLibraries = referencedActorLibraries,
            SourceObjectDependencies = sourceObjectDependencies,
            ReachableScriptOwners = reachableScriptOwners,
            SavedFlagLinks = reachableSavedFlagLinks,
            ScriptOwnerReferenceGraph = ownerReferences,
            LoadDiagnostics = document.Diagnostics.ToArray(),
            DecodedTrackCommands = serializedCalls,
            DecodedTypedMessages = messages,
            EmbeddedEventTrackLists = embeddedTrackLists.Select(list => new
                {
                    Owner = list.OwnerKey.ToString("X8"),
                    list.OwnerName,
                    Link = list.LinkName,
                    Event = list.EventKey.ToString("X8"),
                    Tracks = list.Tracks.Select(track => new
                    {
                        track.Index,
                        Target = track.TargetKey.ToString("X8"),
                        TargetName = ResolveMessageReference(track.TargetKey, list.WorldKey),
                        track.Bone,
                        track.ChannelFlags,
                        track.KeyCount,
                        track.Duration,
                        Calls = track.Calls.Select(item => new
                        {
                            item.Time,
                            Target = item.Call.Target.ToString("X8"),
                            Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                            item.Call.Arguments,
                            Description = CinematicEngineFunctionCatalog.Describe(item.Call),
                            SoundBinding = CinematicSoundCommandResolver.Resolve(item.Call, list.WorldKey,
                                document, archiveEntries: archive.Entries),
                            EmbeddedTrack = CinematicEmbeddedEventTrackResolver.Describe(
                                item.Call, document, list.WorldKey)
                        }),
                        NestedSceneEventRoutes = CinematicNestedEventTrackResolver.HasNestedCalls(track)
                            ? CinematicNestedEventTrackResolver.Analyze(list, track,
                                document.CinematicEmbeddedEventTracks.Values.SelectMany(eventLists => eventLists))
                                .Routes.Select(route => new
                                {
                                    route.Depth,
                                    route.Path,
                                    TargetOwner = route.TargetOwnerKey.ToString("X8"),
                                    TargetEvent = route.TargetList?.EventKey.ToString("X8"),
                                    route.TargetTrackIndex,
                                    route.IsResolved,
                                    route.IsCycle,
                                    route.Limitation
                                }).ToArray()
                            : [],
                        Messages = track.Messages.Select(item => new
                        {
                            item.Time,
                            item.Message.Type,
                            ExecutionObject = item.Message.ExecutionObjectKey.ToString("X8"),
                            Gao = item.Message.Gao.ToString("X8"),
                            Gao2 = item.Message.Gao2.ToString("X8"),
                            item.Message.Int1,
                            item.Message.Int2,
                            Dialogue = item.Message.Type == 30
                                ? DescribeDialogueMessage(item.Message, document.CinematicScriptOwners.Values,
                                    list.WorldKey)
                                : null,
                            Behavior = CinematicScriptMessageAnalyzer.Describe(item.Message,
                                document.CinematicScriptOwners.Values, list.WorldKey)
                        })
                    })
                }),
            ReferencedExternalEveLists = referencedExternalEmbeddedLists.Select(list => new
            {
                Owner = list.OwnerKey.ToString("X8"),
                list.OwnerName,
                SourceWorld = list.WorldKey.ToString("X8"),
                Link = list.LinkName,
                Event = list.EventKey.ToString("X8"),
                Channels = list.Tracks.Length,
                LiteralCalls = list.Tracks.Sum(track => track.Calls.Length),
                TypedMessages = list.Tracks.Sum(track => track.Messages.Length),
                DecodedChannels = list.Tracks
                    .Where(track => track.Calls.Length > 0 || track.Messages.Length > 0)
                    .Select(track => new
                    {
                        track.Index,
                        Target = track.TargetKey.ToString("X8"),
                        track.Bone,
                        track.ChannelFlags,
                        track.KeyCount,
                        track.Duration,
                        Calls = track.Calls.Select(item => new
                        {
                            item.Time,
                            Target = item.Call.Target.ToString("X8"),
                            Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
                            item.Call.Arguments,
                            Description = CinematicEngineFunctionCatalog.Describe(item.Call),
                            EmbeddedTrack = CinematicEmbeddedEventTrackResolver.Describe(
                                item.Call, document, list.WorldKey)
                        }),
                        Messages = track.Messages.Select(item => new
                        {
                            item.Time,
                            item.Message.Type,
                            ExecutionObject = item.Message.ExecutionObjectKey.ToString("X8"),
                            Gao = item.Message.Gao.ToString("X8"),
                            Gao2 = item.Message.Gao2.ToString("X8"),
                            item.Message.Int1,
                            item.Message.Int2,
                            Dialogue = item.Message.Type == 30
                                ? DescribeDialogueMessage(item.Message, document.CinematicScriptOwners.Values,
                                    list.WorldKey)
                                : null,
                            Behavior = CinematicScriptMessageAnalyzer.Describe(item.Message,
                                document.CinematicScriptOwners.Values, list.WorldKey)
                        })
                    })
            }),
            SyntheticEmbeddedTrackIndexProbe = new
            {
                Lists = embeddedIndexProbes.Length,
                Resolved = embeddedIndexProbes.Count(probe => probe.Resolved),
                Unresolved = embeddedIndexProbes.Count(probe => !probe.Resolved),
                Note = "Each probe applies synthetic TRAPlay(0) to an actual serialized owner/list to verify the index join; it does not claim that this call exists or runs in the archive."
            },
            Limits = "The graph is static source/archive evidence. BoundedCallbackPlaybackProbe samples source-linked callback state effects at relevant event times with unknown save flags left unresolved; it does not run arbitrary AI, EVE scheduling, message queues, timers, collision tests, or the full VM."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CINEMATIC SCRIPT GRAPH: {owners.Length} script owners, {calls.Length} decoded calls");
    }

    private static object ProbeBoundedCallbackPlayback(Archive archive, Entry entry,
        SceneDocument document, WorldData world, AnimationClip clip)
    {
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        CinematicPlayback[] playbacks = CinematicPlayback.Discover(document, world, resolver,
                archive.Entries)
            .Where(candidate => candidate.Key == clip.Key).ToArray();
        if (playbacks.Length == 0)
            return new { Status = "no cinematic playback", Candidates = Array.Empty<object>() };
        if (playbacks.Length > 1)
            return new
            {
                Status = "ambiguous cinematic playback",
                CandidateCount = playbacks.Length,
                Candidates = Array.Empty<object>()
            };

        CinematicPlayback playback = playbacks[0];
        SceneObject[] sceneObjects = world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members))
            .Distinct().ToArray();
        var objectGroups = sceneObjects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        CinematicScriptOwner[] scopedOwners = CinematicScriptScope.ForClip(
            document.CinematicScriptOwners.Values, clip.CinematicWorldKey,
            world.Actors.Select(actor => actor.Root.GaoKey),
            clip.Tracks.Select(track => track.Target),
            CinematicTimelineMessage.ReadTimeline(clip).Select(item => item.Message),
            ownerEventTracks: document.CinematicScriptEventTracks.Values.SelectMany(tracks => tracks));
        var candidates = scopedOwners
            .Where(owner => objectGroups.TryGetValue(owner.OwnerKey, out SceneObject[]? ownerMatches)
                && ownerMatches.Length == 1)
            .SelectMany(owner => CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner)
                .Where(effect => effect.ReceiverKey is { } receiverKey
                    && objectGroups.TryGetValue(receiverKey, out SceneObject[]? targetMatches)
                    && targetMatches.Length == 1)
                .Select(effect => new
                {
                    Owner = owner,
                    OwnerObject = objectGroups[owner.OwnerKey][0],
                    Target = objectGroups[effect.ReceiverKey!.Value][0],
                    Effect = effect
                }))
            .ToArray();
        if (candidates.Length == 0)
            return new
            {
                Status = "no uniquely bound callback state-effect candidates",
                CandidateCount = 0,
                Samples = Array.Empty<object>()
            };

        var relevantOwnerKeys = candidates.Select(candidate => candidate.Owner.OwnerKey).ToHashSet();
        float[] eventTimes = playback.ScriptStateCalls
            .Where(item => relevantOwnerKeys.Contains(item.Call.Target))
            .Select(item => item.Time)
            .Append(0)
            .Append(playback.Duration)
            .Where(time => float.IsFinite(time) && time >= 0 && time <= playback.Duration)
            .Distinct().Order().ToArray();
        const int maximumSamples = 16;
        float[] sampleTimes = eventTimes.Length <= maximumSamples
            ? eventTimes
            : Enumerable.Range(0, maximumSamples)
                .Select(index => eventTimes[(int)Math.Round(index * (eventTimes.Length - 1d)
                    / (maximumSamples - 1))])
                .Distinct().ToArray();
        object[] candidateSummary = candidates.Select(candidate => new
        {
            Owner = candidate.Owner.Name,
            OwnerKey = candidate.Owner.OwnerKey.ToString("X8"),
            Target = candidate.Target.Name,
            TargetKey = candidate.Target.GaoKey.ToString("X8"),
            candidate.Effect.Api,
            candidate.Effect.Variant,
            candidate.Effect.FunctionName,
            candidate.Effect.Line,
            candidate.Effect.ExecutionStatus,
            SupportedByBoundedPreview = candidate.Effect.Api is "OBJ_FlagInvisibleSet"
                or "OBJ_FlagInactiveSet" or "OBJDestroy" or "OBJ_CapaSet"
                or "OBJDrawMaskSet" or "COLSetActivationSet",
            candidate.Effect.BranchConditions
        }).Cast<object>().ToArray();
        SceneObject[] observedTargets = candidates.Select(candidate => candidate.Target)
            .Distinct().ToArray();
        playback.BindSavedFlagAssumptions(new Dictionary<int, bool?>());
        playback.Activate();
        try
        {
            object[] samples = sampleTimes.Select(time =>
            {
                playback.Seek(time);
                return (object)new
                {
                    Time = time,
                    Targets = observedTargets.Select(target => new
                    {
                        Key = target.GaoKey.ToString("X8"),
                        target.Name,
                        ControlFlags = (target.PreviewControlFlags ?? target.ControlFlags).ToString("X8"),
                        Capabilities = target.PreviewCapabilities ?? target.InitialCapabilities,
                        DrawMask = target.PreviewDrawFlags ?? target.DrawFlags,
                        CollisionActivationMask = target.PreviewCollisionActivationMask
                    }).ToArray()
                };
            }).ToArray();
            return new
            {
                Status = "bounded source-linked callback preview",
                Note = "Candidate calls are source evidence. Samples show only effects applied by JadeView's bounded callback preview with unknown saved flags left unresolved; this is not a retail runtime trace.",
                CandidateCount = candidates.Length,
                Candidates = candidateSummary,
                Samples = samples
            };
        }
        finally
        {
            playback.Deactivate();
        }
    }

    private static object[] GetSourceObjectArgumentBindings(CinematicScriptOwner owner,
        CinematicScriptNamedReference[] namedReferences)
    {
        var sourceReferences = CinematicScriptReader.ReachableSourceReferences(owner);
        return CinematicScriptReader.ReachableEngineCalls(owner)
            .SelectMany(call => CinematicScriptArgumentBindingResolver.ResolveSourceObjectReferences(call,
                    sourceReferences)
                .Select(reference => (object)new
                {
                    call.FunctionName,
                    call.CallbackPath,
                    call.Api,
                    call.Variant,
                    call.Arguments,
                    call.ArgumentFields,
                    call.MessageFields,
                    CallSource = call.Source,
                    CallLine = call.Line,
                    ObjectSymbol = reference.Symbol,
                    ObjectKey = reference.Key.ToString("X8"),
                    TargetName = CinematicScriptReferenceNameResolver.Resolve(reference.Key,
                        owner.WorldKey, namedReferences)?.Name,
                    ReferenceSource = reference.Source,
                    ReferenceLine = reference.Line
                }))
            .ToArray();
    }

    private static object GetCallbackRegistrationAnalysis(CinematicScriptOwner owner)
    {
        CinematicScriptReachableEngineCall[] reachableCalls =
            CinematicScriptReader.ReachableEngineCalls(owner);
        CinematicScriptReachableSourceReference[] sourceReferences =
            CinematicScriptReader.ReachableSourceReferences(owner);
        var registrations = CinematicScriptCallbackRegistrationAnalyzer.Analyze(owner,
            reachableCalls, sourceReferences).Select(candidate => new
            {
                candidate.Operation,
                candidate.CallbackSourceContextOperand,
                candidate.DestinationInstanceOperand,
                ContextKey = candidate.ResolvedContextKey?.ToString("X8"),
                DestinationKey = candidate.ResolvedDestinationKey?.ToString("X8"),
                candidate.CallbackTypeOperand,
                candidate.CallbackType,
                candidate.FunctionOperand,
                FunctionKey = candidate.FunctionKey?.ToString("X8"),
                candidate.MatchingRemovalSites,
                candidate.BranchConditions,
                candidate.Source,
                candidate.Line,
                candidate.CallbackPath
            }).ToArray();
        var resets = CinematicScriptCallbackRegistrationAnalyzer.AnalyzeResetSites(owner,
            reachableCalls).Select(candidate => new
            {
                candidate.Source,
                candidate.Line,
                candidate.CallbackPath,
                candidate.BranchConditions
            }).ToArray();
        return new { Registrations = registrations, PotentialResetSites = resets };
    }

    private static object[] GetRliDisplayOvaFieldAccesses(CinematicScriptOwner owner)
    {
        CinematicScriptRliDisplayCallbackCandidate[] callbacks =
            CinematicScriptRliDisplayCallbackAnalyzer.Analyze(owner);
        string[] referencedFields = callbacks.SelectMany(callback => owner.Variables
                .Select(variable => variable.Name)
                .Where(fieldName => callback.FactorExpression.Contains(fieldName,
                    StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return CinematicScriptReader.ReachableOvaFieldAccesses(owner)
            .Where(access => referencedFields.Contains(access.VariableName,
                StringComparer.OrdinalIgnoreCase))
            .Select(access => (object)new
            {
                access.VariableName,
                access.Access,
                access.Source,
                access.Receiver,
                access.Expression,
                access.BranchConditions,
                access.SourceFile,
                access.Line,
                access.FunctionName,
                access.CallbackPath
            }).ToArray();
    }

    private static object[] GetAiTrackMutationAnalysis(CinematicScriptOwner owner)
    {
        CinematicScriptReachableEngineCall[] reachableCalls =
            CinematicScriptReader.ReachableEngineCalls(owner);
        return CinematicScriptAiTrackMutationAnalyzer.Analyze(owner, reachableCalls,
                CinematicScriptReader.ReachableSourceReferences(owner))
            .Select(candidate => (object)new
            {
                candidate.Api,
                candidate.Effect,
                candidate.TrackIndex,
                candidate.TrackDescription,
                candidate.FunctionOperand,
                FunctionKey = candidate.FunctionKey?.ToString("X8"),
                candidate.FunctionDescription,
                candidate.CurrentTrackSlotCandidates,
                candidate.HasUnknownCurrentTrackPath,
                candidate.BranchConditions,
                candidate.SerializedBranchFindings,
                candidate.Source,
                candidate.Line,
                candidate.CallbackPath
            }).ToArray();
    }

    private static object[] GetCallbackDrivenSceneEventCalls(CinematicScriptOwner owner, SceneDocument document) =>
        CinematicScriptEventTrackCallResolver.Resolve(owner,
                CinematicScriptReader.ReachableEngineCalls(owner), document.CinematicEmbeddedEventTracks)
            .Select(invocation => (object)new
            {
                invocation.Operation,
                invocation.TrackIndex,
                invocation.IndexBinding,
                invocation.TargetBinding,
                Event = invocation.EventList.EventKey.ToString("X8"),
                EventName = invocation.EventList.OwnerName,
                TrackCount = invocation.Tracks.Length,
                DistinctTargetCount = invocation.Tracks.Select(track => track.TargetKey).Distinct().Count(),
                TotalKeyCount = invocation.Tracks.Sum(track => track.KeyCount),
                Tracks = invocation.TrackIndex == null
                    ? []
                    : invocation.Tracks.Select(track => new
                {
                    track.Index,
                    Target = track.TargetKey.ToString("X8"),
                    track.Bone,
                    track.KeyCount,
                    track.Duration
                }).ToArray(),
                Source = invocation.SourceCall.Source,
                invocation.SourceCall.Line,
                invocation.SourceCall.FunctionName,
                invocation.SourceCall.CallbackPath,
                Arguments = invocation.SourceCall.Arguments,
                BranchConditions = invocation.SourceCall.BranchConditions,
                BranchAnalysis = CinematicScriptBranchAnalyzer.Describe(owner,
                    invocation.SourceCall.BranchConditions)
            })
            .ToArray();

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
                BranchAnalysis = CinematicScriptBranchAnalyzer.Describe(owner,
                    unresolved.SourceCall.BranchConditions),
                unresolved.Reason
            })
            .Cast<object>()
            .ToArray();

    private static object GetCallbackActionKitMerges(CinematicScriptOwner owner, SceneDocument document)
    {
        var analysis = CinematicScriptActionKitMergeResolver.Analyze(owner,
            CinematicScriptReader.ReachableEngineCalls(owner), document.CinematicActionKitsByOwner);
        return new
        {
            Resolved = analysis.Resolved.Select(merge => new
            {
                merge.Mode,
                Destination = merge.DestinationKey.ToString("X8"),
                DestinationWorld = merge.DestinationWorldKey.ToString("X8"),
                Source = merge.SourceKey.ToString("X8"),
                SourceWorld = merge.SourceWorldKey.ToString("X8"),
                merge.DestinationBinding,
                merge.SourceBinding,
                merge.FirstAction,
                merge.LastAction,
                merge.RangeBinding,
                merge.SourceSlotCount,
                merge.DestinationConflictCount,
                merge.SourceCall.Api,
                merge.SourceCall.Variant,
                Arguments = merge.SourceCall.Arguments,
                merge.SourceCall.CallbackPath,
                CallSource = merge.SourceCall.Source,
                CallLine = merge.SourceCall.Line,
                BranchConditions = merge.SourceCall.BranchConditions
            }).ToArray(),
            Unresolved = analysis.Unresolved.Select(merge => new
            {
                merge.SourceCall.Api,
                merge.SourceCall.Variant,
                Arguments = merge.SourceCall.Arguments,
                merge.SourceCall.CallbackPath,
                CallSource = merge.SourceCall.Source,
                CallLine = merge.SourceCall.Line,
                BranchConditions = merge.SourceCall.BranchConditions,
                merge.Reason
            }).ToArray()
        };
    }

    private static object GetCallbackActionRequests(CinematicScriptOwner owner, SceneDocument document)
    {
        IReadOnlyDictionary<int, CinematicAction> actionTable =
            CinematicActionKitScopeResolver.Resolve(owner.OwnerKey, owner.WorldKey,
                document.CinematicActionKitsByOwner).Actions
            ?? new Dictionary<int, CinematicAction>();
        var analysis = CinematicScriptActionRequestResolver.Analyze(owner,
            CinematicScriptReader.ReachableEngineCalls(owner), actionTable);
        return new
        {
            Resolved = analysis.Resolved.Select(request => new
            {
                request.ActionIndex,
                request.IndexBinding,
                request.ControlValue,
                request.ControlBinding,
                Clips = request.Action.Items.Select(item => new
                {
                    Key = item.Clip.Key.ToString("X8"),
                    item.ShapeKey
                }).ToArray(),
                request.SourceCall.Api,
                request.SourceCall.Variant,
                Arguments = request.SourceCall.Arguments,
                request.SourceCall.CallbackPath,
                Source = request.SourceCall.Source,
                request.SourceCall.Line,
                BranchConditions = request.SourceCall.BranchConditions
            }).ToArray(),
            Unresolved = analysis.Unresolved.Select(unresolved => new
            {
                unresolved.SourceCall.Api,
                Arguments = unresolved.SourceCall.Arguments,
                unresolved.SourceCall.CallbackPath,
                Source = unresolved.SourceCall.Source,
                unresolved.SourceCall.Line,
                unresolved.Reason
            }).ToArray()
        };
    }

    private static object[] GetSourceOvaArgumentBindings(CinematicScriptOwner owner) =>
        CinematicScriptReader.ReachableEngineCalls(owner)
            .SelectMany(call => CinematicScriptArgumentBindingResolver.ResolveOwnerFields(call, owner)
                .Select(binding => (object)new
                {
                    call.FunctionName,
                    call.CallbackPath,
                    call.Api,
                    call.Variant,
                    call.Arguments,
                    call.ArgumentFields,
                    call.MessageFields,
                    CallSource = call.Source,
                    CallLine = call.Line,
                    binding.VariableName,
                    binding.Source,
                    binding.Value,
                    ObjectKey = binding.ObjectKey?.ToString("X8"),
                    binding.TargetName
                }))
            .ToArray();
}
