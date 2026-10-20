namespace JadeView;

internal static class CinematicSceneClips
{
    internal static AnimationClip[] ForWorld(SceneDocument document, WorldData world) =>
        document.CinematicClips.Values.Where(clip => clip.CinematicWorldKey == world.Key
            || clip.CinematicWorldKey == 0).ToArray();

    internal static HashSet<uint> References(IEnumerable<AnimationClip> clips)
    {
        var keys = new HashSet<uint>();
        foreach (var clip in clips)
        {
            if (clip.CinematicOwnerKey > 2 && clip.CinematicOwnerKey != uint.MaxValue) keys.Add(clip.CinematicOwnerKey);
            foreach (var track in clip.Tracks)
            {
                if (track.Target != 0 && track.Target != uint.MaxValue) keys.Add(track.Target);
                foreach (var payload in track.Keys.SelectMany(key => key.OpaquePayloads))
                    AddLiteralObjects(payload, keys);
            }
        }
        return keys;
    }

    private static void AddLiteralObjects(AnimationOpaquePayload payload, HashSet<uint> keys)
    {
        if (!CinematicWordPairList.TryRead(payload, out var rows)) return;
        foreach (var row in rows)
            if (CinematicWordPairList.IsLiteral(row, 0x28) && row.Value > 2 && row.Value != uint.MaxValue)
                keys.Add(row.Value);
    }

    internal static void IncludeGroupMembers(HashSet<uint> keys, IEnumerable<SceneObject> objects)
    {
        var groups = objects.Where(item => item.GroupMemberKeys.Length > 0)
            .GroupBy(item => item.GaoKey).ToDictionary(group => group.Key, group => group.First().GroupMemberKeys);
        var pending = new Queue<uint>(keys);
        while (pending.TryDequeue(out uint key))
        {
            if (!groups.TryGetValue(key, out var members)) continue;
            foreach (uint member in members)
                if (member is > 2 and < uint.MaxValue && keys.Add(member)) pending.Enqueue(member);
        }
    }
}
