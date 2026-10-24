namespace JadeView;

internal static class CinematicAudioTimeline
{
    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document)
        => Build(clip, world, document, null, out _);

    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document,
        out string[] diagnostics) => Build(clip, world, document, null, out diagnostics);

    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document,
        IReadOnlyList<Entry>? archiveEntries, out string[] diagnostics,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? nestedEventCommands = null,
        IEnumerable<(float Time, CinematicTimelineMessage Message)>? timelineMessages = null)
    {
        var timelineCalls = CinematicLiteralCall.ReadTimeline(clip)
            .Concat(nestedEventCommands ?? [])
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time)
            .ToArray();
        var modifiers = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey).ToDictionary(group => group.Key,
            group => group.SelectMany(sceneObject => sceneObject.Modifiers)
                .Select(modifier => CinematicSoundModifier.Read(modifier, document))
                .OfType<CinematicSoundModifier>().Distinct().ToArray());
        var commands = timelineCalls
            .Where(item => item.Call.Selector is 12058 or 12060 && item.Call.Arguments.Length == 1);
        var cues = new List<CinematicAudioCue>();
        var audioDiagnostics = new List<string>();
        foreach (var (time, call) in commands)
        {
            int id = unchecked((int)call.Arguments[0]);
            for (int index = 0; index < cues.Count; index++)
            {
                var cue = cues[index];
                bool cueMatchesStop = cue.OwnerKey == call.Target
                    && (id == -1 || cue.ModifierId == id)
                    && cue.Start <= time && cue.StopTime < 0;
                if (!cueMatchesStop) continue;
                cues[index] = cue with { End = time, StopTime = time };
            }
            if (call.Selector != 12058) continue;
            CinematicSoundModifier[] matchingModifiers = modifiers.TryGetValue(call.Target, out var candidates)
                ? candidates.Where(modifier => id == -1 || modifier.Id == id).ToArray()
                : [];
            if (matchingModifiers.Length == 0)
            {
                string status = CinematicSoundCommandResolver.Resolve(call, world.Key, document)?.Status
                    ?? "target GAO is not loaded in this world";
                audioDiagnostics.Add($"SNDPlayModifier at {time:F2}s on {call.Target:X8}: {status}");
                continue;
            }

            foreach (var modifier in matchingModifiers)
                cues.Add(new(time + modifier.Delay, clip.Duration, call.Target,
                    modifier.Id, modifier.SoundKey, modifier.Volume, modifier.Loop));
        }

        foreach (var (time, call) in timelineCalls
            .Where(item => item.Call.Selector is 12022 or 12103))
        {
            CinematicSoundCommandBinding? binding = CinematicSoundCommandResolver.Resolve(call,
                world.Key, document, archiveEntries: archiveEntries);
            if (binding is not { SoundKeys.Length: 1, ModifierId: int soundSlot })
            {
                audioDiagnostics.Add($"SND_RequestPlay at {time:F2}s on {call.Target:X8}: "
                    + (binding?.Status ?? "sound request could not be resolved"));
                continue;
            }

            float requestedVolume = BitConverter.UInt32BitsToSingle(call.Arguments[1]);
            if (!float.IsFinite(requestedVolume))
            {
                audioDiagnostics.Add($"{CinematicEngineFunctionCatalog.NameFor(call.Selector)} at {time:F2}s "
                    + $"on {call.Target:X8}: "
                    + $"non-finite volume bits 0x{call.Arguments[1]:X8}");
                continue;
            }
            float volume = requestedVolume == -1 ? 1 : Math.Clamp(requestedVolume, 0, 1);
            uint soundKey = Convert.ToUInt32(binding.SoundKeys[0], 16);
            bool randomFrequency = call.Selector == 12103;
            string cueLabel = randomFrequency
                ? $"Script request · sound-bank slot {soundSlot} · random pitch not reconstructed"
                : $"Script request · sound-bank slot {soundSlot}";
            cues.Add(new(time, clip.Duration, call.Target, soundSlot, soundKey, volume, false, cueLabel));
        }

        foreach (var (time, call) in timelineCalls
            .Where(item => item.Call.Selector == 12060 && item.Call.Arguments.Length == 1))
        {
            int id = unchecked((int)call.Arguments[0]);
            for (int index = 0; index < cues.Count; index++)
            {
                var cue = cues[index];
                bool cueMatchesStop = cue.OwnerKey == call.Target
                    && (id == -1 || cue.ModifierId == id)
                    && cue.Start <= time && cue.StopTime < 0;
                if (cueMatchesStop)
                    cues[index] = cue with { End = time, StopTime = time };
            }
        }

        var dialogue = BuildDialogueCues(clip.Duration,
            timelineMessages ?? CinematicTimelineMessage.ReadTimeline(clip), world, document);
        cues.AddRange(dialogue.Cues);
        audioDiagnostics.AddRange(dialogue.Diagnostics);
        var sourceCues = cues.Where(cue => cue.End > cue.Start).OrderBy(cue => cue.Start).ToArray();
        diagnostics = audioDiagnostics.Distinct().ToArray();
        return archiveEntries == null ? sourceCues : CinematicAudioLocalization.Expand(sourceCues, archiveEntries);
    }

    internal static CinematicAudioTimelineResult BuildDialogueCues(float duration,
        IEnumerable<(float Time, CinematicTimelineMessage Message)> messages,
        WorldData world, SceneDocument document)
    {
        var cues = new List<CinematicAudioCue>();
        var diagnostics = new List<string>();
        var inspectedTextReferences = new HashSet<(uint BankKey, int TextId)>();
        foreach (var (time, message) in messages.Where(item => item.Message.Type == 30).OrderBy(item => item.Time))
        {
            var resolution = CinematicDialogueMessageResolver.Resolve(message,
                document.CinematicScriptOwners.Values, world.Key);
            if (resolution.Line is not { } line || line.TextBankKey is 0 or uint.MaxValue
                || line.TextId < 0 || !inspectedTextReferences.Add((line.TextBankKey, line.TextId)))
                continue;

            string associatedSound = line.SoundId is { } soundId
                ? $"des_ai_son_associe={soundId}"
                : "des_ai_son_associe is unavailable";
            diagnostics.Add($"Dialogue text {line.TextBankKey:X8}:{line.TextId} has {associatedSound}; "
                + "this OVA field is not a resolved WAVE key. Jaded's SND_RequestPlayDialog resolves a text-group/id "
                + "through TXL.ul_SoundKey, but the current PC TXI/TXS reader does not expose that voice mapping.");
        }

        return new(cues.ToArray(), diagnostics.Distinct().ToArray());
    }
}
