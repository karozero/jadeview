using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using JadeView;

internal static class ArchiveCinematicCensus
{
    private sealed record SceneSummary(string Container, int Records, int AnimationRecords,
        int DecodedClips, int DecodeFailures, Dictionary<int, int> SendSelectors,
        Dictionary<string, int> EventControlFlags, List<object> FlaggedCinematicTracks,
        List<object> EventFunctionWaitKeys, List<object> LiteralMessageTemplates, int DecodedLiteralCommands,
        int DescribedLiteralCommands, Dictionary<string, int> UndescribedEngineCommands);

    internal static void Write(string archivePath, string output)
    {
        var archive = new Archive(archivePath);
        var sceneEntries = archive.Entries
            .Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
            .ToArray();
        var summaries = new List<SceneSummary>();
        for (int entryIndex = 0; entryIndex < sceneEntries.Length; entryIndex++)
        {
            var entry = sceneEntries[entryIndex];
            summaries.Add(ReadContainer(archive, entry));
            if ((entryIndex + 1) % 10 == 0 || entryIndex + 1 == sceneEntries.Length)
                Console.WriteLine($"ARCHIVE CINEMATIC CENSUS: {entryIndex + 1}/{sceneEntries.Length} containers");
        }

        var report = new
        {
            Archive = archive.PathName,
            ContainersScanned = sceneEntries.Length,
            Totals = new
            {
                Records = summaries.Sum(summary => summary.Records),
                AnimationRecords = summaries.Sum(summary => summary.AnimationRecords),
                DecodedClips = summaries.Sum(summary => summary.DecodedClips),
                DecodeFailures = summaries.Sum(summary => summary.DecodeFailures),
                DecodedLiteralCommands = summaries.Sum(summary => summary.DecodedLiteralCommands),
                DescribedLiteralCommands = summaries.Sum(summary => summary.DescribedLiteralCommands),
                UndescribedEngineCommands = summaries.SelectMany(summary => summary.UndescribedEngineCommands)
                    .GroupBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal),
                EventControlFlags = summaries.SelectMany(summary => summary.EventControlFlags)
                    .GroupBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal)
            },
            Selector8056Occurrences = summaries.Sum(summary => summary.SendSelectors.GetValueOrDefault(8056)),
            Selector8050Occurrences = summaries.Sum(summary => summary.SendSelectors.GetValueOrDefault(8050)),
            EventFlagSemantics = "Track lifecycle labels are source-backed by Iris and corroborated by Jaded. AI-function hold labels follow Iris events.c: key bit 0x0008 holds while the node returns true, and 0x0010 holds while false. The census counts serialized keys; it does not execute event nodes or claim retail binary equivalence.",
            Limits = "Archive-wide parsed FF containers and source animation event words. Counts preserve container occurrences; literal templates are candidates only. No VM execution, actor delivery, audio timing, shared-resource deduplication or completeness of non-FF resources is inferred.",
            Containers = summaries
        };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "archive-cinematic-census.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SceneSummary ReadContainer(Archive archive, Entry entry)
    {
        byte[] bytes = archive.Read(entry);
        object replay = ReadScene(bytes, archive.PathName);
        var records = (IEnumerable)GetField(replay, "Records")!;
        var sourceRecords = records.Cast<object>().ToArray();
        var worldsByKey = ((IEnumerable)GetField(replay, "Worlds")!).Cast<object>()
            .GroupBy(world => (uint)GetField(world, "Key")!)
            .ToDictionary(group => group.Key, group => (string)GetField(group.First(), "Name")!);
        var recordsByKey = sourceRecords
            .GroupBy(record => (uint)GetField(record, "Key")!)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(record => (int)GetField(record, "BrainFieldOffset")! >= 0)
                .First());
        var recordsByWorldAndKey = sourceRecords
            .GroupBy(record => ((uint)GetField(record, "LoadWorldKey")!, (uint)GetField(record, "Key")!))
            .ToDictionary(group => group.Key, group => group.First());
        var animationRecords = new List<object>();
        foreach (object record in sourceRecords)
            if ((char)GetField(record, "Kind")! == 'i') animationRecords.Add(record);

        var selectorCounts = new Dictionary<int, int>();
        var eventControlFlags = new Dictionary<string, int>(StringComparer.Ordinal);
        var flaggedCinematicTracks = new List<object>();
        var eventFunctionWaitKeys = new List<object>();
        var literalCandidates = new List<object>();
        var undescribedEngineCommands = new Dictionary<string, int>(StringComparer.Ordinal);
        int decodedClips = 0;
        int decodeFailures = 0;
        int decodedLiteralCommands = 0;
        int describedLiteralCommands = 0;
        foreach (object record in animationRecords)
        {
            int offset = (int)GetField(record, "Offset")!;
            int size = (int)GetField(record, "Size")!;
            uint key = (uint)GetField(record, "Key")!;
            uint worldKey = (uint)GetField(record, "LoadWorldKey")!;
            string recordName = (string)GetField(record, "Name")!;
            var payload = bytes.AsSpan(offset, size).ToArray();
            AnimationClip clip;
            try
            {
                clip = AnimationReader.Read(payload, 0, payload.Length);
                if (clip.Error != null) { decodeFailures++; continue; }
                decodedClips++;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException
                or OverflowException or IndexOutOfRangeException)
            {
                decodeFailures++;
                continue;
            }

            foreach (var (_, call) in CinematicLiteralCall.ReadTimeline(clip))
            {
                decodedLiteralCommands++;
                if (CinematicEngineFunctionCatalog.Describe(call).Length > 0)
                {
                    describedLiteralCommands++;
                    continue;
                }

                string name = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                undescribedEngineCommands[name] = undescribedEngineCommands.GetValueOrDefault(name) + 1;
            }

            foreach (var track in clip.Tracks)
            {
                CountFlag(eventControlFlags, "track.running-init", track.ChannelFlags, 0x0001);
                CountFlag(eventControlFlags, "track.auto-loop", track.ChannelFlags, 0x0002);
                CountFlag(eventControlFlags, "track.auto-stop", track.ChannelFlags, 0x0004);
                if (worldsByKey.TryGetValue(worldKey, out string? trackWorldName)
                    && trackWorldName.Contains("cine", StringComparison.OrdinalIgnoreCase)
                    && (track.ChannelFlags & 0x0007) != 0)
                {
                    object? targetRecord = recordsByWorldAndKey.GetValueOrDefault((worldKey, track.Target));
                    flaggedCinematicTracks.Add(new
                    {
                        Clip = key.ToString("X8"),
                        ClipName = recordName,
                        World = trackWorldName,
                        TrackTarget = track.Target.ToString("X8"),
                        TrackTargetName = targetRecord == null ? null : GetField(targetRecord, "Name") as string,
                        track.Bone,
                        Flags = track.ChannelFlags.ToString("X4"),
                        AutoLoop = (track.ChannelFlags & 0x0002) != 0,
                        AutoStop = (track.ChannelFlags & 0x0004) != 0,
                        RunningInit = (track.ChannelFlags & 0x0001) != 0,
                        KeyCount = track.Keys.Count,
                        Duration = track.Keys.Sum(animationKey => animationKey.Duration)
                    });
                }
                foreach (var animationKey in track.Keys)
                {
                    int eventType = animationKey.Flags & 0x07c0;
                    CountFlag(eventControlFlags, "event.do-once", animationKey.Flags, 0x0001);
                    if (eventType == 0x0040)
                    {
                        CinematicEventFunctionWaitMode waitMode =
                            CinematicEventFunctionControlFlowReader.WaitModeFor(animationKey.Flags);
                        if (waitMode == CinematicEventFunctionWaitMode.WaitWhileTrue)
                            Increment(eventControlFlags, "event.hold-while-true");
                        else if (waitMode == CinematicEventFunctionWaitMode.WaitWhileFalse)
                            Increment(eventControlFlags, "event.hold-while-false");
                        if (waitMode != CinematicEventFunctionWaitMode.None)
                        {
                            eventFunctionWaitKeys.Add(new
                            {
                                Clip = key.ToString("X8"),
                                ClipName = recordName,
                                World = worldKey.ToString("X8"),
                                TrackIndex = clip.Tracks.IndexOf(track),
                                Target = track.Target.ToString("X8"),
                                track.Bone,
                                KeyIndex = track.Keys.IndexOf(animationKey),
                                Time = animationKey.Time,
                                Flags = animationKey.Flags.ToString("X4"),
                                WaitMode = waitMode.ToString()
                            });
                        }
                    }

                    string? controlEvent = eventType switch
                    {
                        0x0200 => "event.goto-label",
                        0x0400 => "event.set-time-to-label",
                        _ => null
                    };
                    if (controlEvent != null)
                        eventControlFlags[controlEvent] = eventControlFlags.GetValueOrDefault(controlEvent) + 1;
                }
            }

            foreach (var track in clip.Tracks)
                foreach (var keyframe in track.Keys)
                    foreach (var sourcePayload in keyframe.OpaquePayloads)
                    {
                        if (sourcePayload.Kind != "word-pair-list" || sourcePayload.Data.Length < sizeof(uint)) continue;
                        uint rowCount = BitConverter.ToUInt32(sourcePayload.Data);
                        if (rowCount > (sourcePayload.Data.Length - sizeof(uint)) / 8
                            || sizeof(uint) + rowCount * 8 != sourcePayload.Data.Length) continue;
                        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
                        {
                            int rowOffset = sizeof(uint) + rowIndex * 8;
                            uint tag = BitConverter.ToUInt32(sourcePayload.Data, rowOffset + sizeof(uint));
                            if (tag >> 24 != 3) continue;
                            int selector = (int)(tag & 0xffff);
                            selectorCounts[selector] = selectorCounts.GetValueOrDefault(selector) + 1;
                            if (selector != 8056 || rowCount != 19 || rowIndex != 17) continue;
                            object? targetRecord = recordsByKey.GetValueOrDefault(track.Target);
                            var owners = FindClipOwners(sourceRecords, worldKey, key, bytes);
                            var clipLinks = FindClipReferenceNodes(sourceRecords, worldKey, key, bytes);
                            literalCandidates.Add(new
                            {
                                Clip = key.ToString("X8"),
                                RecordSha256 = Convert.ToHexString(SHA256.HashData(payload)),
                                RecordName = recordName,
                                RecordOffset = offset,
                                WorldKey = worldKey.ToString("X8"),
                                WorldName = worldsByKey.GetValueOrDefault(worldKey),
                                Time = keyframe.Time,
                                Duration = keyframe.Duration,
                                track.Bone,
                                Target = track.Target.ToString("X8"),
                                TargetRecordKind = targetRecord == null ? null : GetField(targetRecord, "Kind")!.ToString(),
                                TargetRecordName = targetRecord == null ? null : GetField(targetRecord, "Name") as string,
                                TargetHasBrain = targetRecord != null && (int)GetField(targetRecord, "BrainFieldOffset")! >= 0,
                                ClipOwners = owners,
                                ClipLinks = clipLinks,
                                track.ChannelFlags,
                                HeaderWord = track.HeaderWord.ToString("X8"),
                                keyframe.Flags,
                                keyframe.Format,
                                Kind = sourcePayload.Kind,
                                SourceOffset = sourcePayload.SourceOffset,
                                Bytes = Convert.ToHexString(sourcePayload.Data)
                            });
                        }
                    }
        }

        return new(entry.Id, sourceRecords.Length, animationRecords.Count, decodedClips,
            decodeFailures, selectorCounts, eventControlFlags, flaggedCinematicTracks, eventFunctionWaitKeys,
            literalCandidates,
            decodedLiteralCommands, describedLiteralCommands, undescribedEngineCommands);
    }

    private static void CountFlag(Dictionary<string, int> counts, string name, int value, int mask)
    {
        if ((value & mask) != 0) counts[name] = counts.GetValueOrDefault(name) + 1;
    }

    private static void Increment(Dictionary<string, int> counts, string name) =>
        counts[name] = counts.GetValueOrDefault(name) + 1;

    private static object ReadScene(byte[] bytes, string archivePath)
    {
        var loader = typeof(SceneActor).Assembly.GetType("JadeView.PcSceneLoader")!;
        var read = loader.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
        object replay = read.Invoke(null, [bytes, archivePath, null])!;
        string? error = (string?)GetField(replay, "Error");
        if (error != null) throw new InvalidDataException(error);
        return replay;
    }

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name)!.GetValue(instance);

    private static object[] FindClipOwners(object[] records, uint worldKey, uint clipKey, byte[] source)
    {
        var worldRecords = records.Where(record => (uint)GetField(record, "LoadWorldKey")! == worldKey).ToArray();
        var recordsByKindAndKey = worldRecords
            .GroupBy(record => ((char)GetField(record, "Kind")!, (uint)GetField(record, "Key")!))
            .ToDictionary(group => group.Key, group => group.First());
        var owners = new List<object>();
        foreach (object actor in worldRecords.Where(record => (char)GetField(record, "Kind")! == 'l'))
        {
            uint objectKey = (uint)GetField(actor, "Key")!;
            uint eventsKey = (uint)GetField(actor, "Events")!;
            uint actionsKey = (uint)GetField(actor, "Actions")!;
            int brainOffset = (int)GetField(actor, "BrainFieldOffset")!;
            bool hasTypedAnimationReference = ((IEnumerable)GetField(actor, "References")!).Cast<object>()
                .Any(reference => (uint)reference.GetType().GetProperty("Key")!.GetValue(reference)! == clipKey
                    && (char)reference.GetType().GetProperty("Kind")!.GetValue(reference)! == 'i');
            string? relation = eventsKey == clipKey ? "events-field"
                : hasTypedAnimationReference ? "gao-animation-reference" : null;
            var actionPath = new List<string>();

            if (actionsKey is not (0 or uint.MaxValue)
                && recordsByKindAndKey.TryGetValue(('k', actionsKey), out object? actionGroup))
            {
                foreach (object actionReference in (IEnumerable)GetField(actionGroup!, "References")!)
                {
                    uint actionKey = (uint)actionReference.GetType().GetProperty("Key")!.GetValue(actionReference)!;
                    if (!recordsByKindAndKey.TryGetValue(('a', actionKey), out object? actionRecord)) continue;
                    bool referencesClip = ((IEnumerable)GetField(actionRecord, "References")!).Cast<object>()
                        .Any(reference => (uint)reference.GetType().GetProperty("Key")!.GetValue(reference)! == clipKey);
                    if (referencesClip)
                        actionPath.Add(actionKey.ToString("X8"));
                }
                if (actionPath.Count > 0)
                    relation = "actions-k-a";
            }

            if (relation == null) continue;
            owners.Add(new
            {
                Key = objectKey.ToString("X8"),
                Name = (string)GetField(actor, "Name")!,
                Brain = brainOffset < 0 ? null : BitConverter.ToUInt32(source, brainOffset).ToString("X8"),
                BrainField = brainOffset >= 0,
                EventsKey = eventsKey.ToString("X8"),
                ActionsKey = actionsKey.ToString("X8"),
                Relation = relation,
                ActionKeys = actionPath
            });
        }
        return owners.ToArray();
    }

    private static object[] FindClipReferenceNodes(object[] records, uint worldKey, uint clipKey, byte[] source)
    {
        return records.Where(record => (uint)GetField(record, "LoadWorldKey")! == worldKey)
            .Where(record => ((IEnumerable)GetField(record, "References")!).Cast<object>()
                .Any(reference => (uint)reference.GetType().GetProperty("Key")!.GetValue(reference)! == clipKey)
                || (char)GetField(record, "Kind")! == 'l' &&
                    new[] { "Events", "Actions", "Skeleton" }.Any(field => (uint)GetField(record, field)! == clipKey))
            .Select(record => new
            {
                Key = ((uint)GetField(record, "Key")!).ToString("X8"),
                Kind = GetField(record, "Kind")!.ToString(),
                Name = GetField(record, "Name") as string,
                Brain = (int)GetField(record, "BrainFieldOffset")! < 0 ? null
                    : BitConverter.ToUInt32(source, (int)GetField(record, "BrainFieldOffset")!).ToString("X8"),
                Events = ((uint)GetField(record, "Events")!).ToString("X8"),
                Actions = ((uint)GetField(record, "Actions")!).ToString("X8"),
                References = ((IEnumerable)GetField(record, "References")!).Cast<object>()
                    .Select(reference => new
                    {
                        Key = ((uint)reference.GetType().GetProperty("Key")!.GetValue(reference)!).ToString("X8"),
                        Kind = reference.GetType().GetProperty("Kind")!.GetValue(reference)!.ToString()
                    }).ToArray()
            }).ToArray();
    }
}
