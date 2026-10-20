namespace JadeView;

internal static class CinematicAudioTimeline
{
    internal static CinematicAudioCue[] Build(AnimationClip clip, WorldData world, SceneDocument document)
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
        return cues.Where(cue => cue.End > cue.Start).ToArray();
    }
}
