namespace JadeView;

internal static class CinematicAudioTimeline
{
    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document)
        => Build(clip, world, document, null, out _);

    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document,
        out string[] diagnostics) => Build(clip, world, document, null, out diagnostics);

    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document,
        IReadOnlyList<Entry>? archiveEntries, out string[] diagnostics)
    {
        var modifiers = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey).ToDictionary(group => group.Key,
            group => group.SelectMany(sceneObject => sceneObject.Modifiers)
                .Select(modifier => CinematicSoundModifier.Read(modifier, document))
                .OfType<CinematicSoundModifier>().Distinct().ToArray());
        var commands = CinematicLiteralCall.ReadTimeline(clip)
            .Where(item => item.Call.Selector is 12058 or 12060 && item.Call.Arguments.Length == 1);
        var cues = new List<CinematicAudioCue>();
        foreach (var (time, call) in commands)
        {
            int id = unchecked((int)call.Arguments[0]);
            for (int index = 0; index < cues.Count; index++)
            {
                var cue = cues[index];
                if (cue.OwnerKey == call.Target && (id == -1 || cue.ModifierId == id) && cue.End > time)
                    cues[index] = cue with { End = time };
            }
            if (call.Selector != 12058 || !modifiers.TryGetValue(call.Target, out var candidates)) continue;
            foreach (var modifier in candidates.Where(modifier => id == -1 || modifier.Id == id))
                cues.Add(new(time + modifier.Delay, clip.Duration, call.Target,
                    modifier.Id, modifier.SoundKey, modifier.Volume, modifier.Loop));
        }

        var dialogue = BuildDialogueCues(clip.Duration, CinematicTimelineMessage.ReadTimeline(clip), world, document);
        cues.AddRange(dialogue.Cues);
        diagnostics = dialogue.Diagnostics;
        var sourceCues = cues.Where(cue => cue.End > cue.Start).OrderBy(cue => cue.Start).ToArray();
        return archiveEntries == null ? sourceCues : CinematicAudioLocalization.Expand(sourceCues, archiveEntries);
    }

    internal static CinematicAudioTimelineResult BuildDialogueCues(float duration,
        IEnumerable<(float Time, CinematicTimelineMessage Message)> messages,
        WorldData world, SceneDocument document)
    {
        var cues = new List<CinematicAudioCue>();
        var diagnostics = new List<string>();
        foreach (var (time, message) in messages.Where(item => item.Message.Type == 30).OrderBy(item => item.Time))
        {
            var resolution = CinematicDialogueMessageResolver.Resolve(message,
                document.CinematicScriptOwners.Values, world.Key);
            if (resolution.Line is not { SoundId: >= 0 } line) continue;

            // Iris passes this ID to SND_l_Request(NULL, id): it indexes the runtime-global sound table,
            // not a bank attached to the location/speaker GAO. The serialized archive does not preserve
            // that runtime table order, so use the explicit cinematic WAD cues instead of guessing.
            if (line.SoundId == 0) continue;

            diagnostics.Add($"Dialogue at {time:F2}s (slot {resolution.Slot}) uses global sound ID {line.SoundId}; "
                + "the runtime sound-table order is unavailable in this archive preview.");
        }

        return new(cues.ToArray(), diagnostics.Distinct().ToArray());
    }
}
