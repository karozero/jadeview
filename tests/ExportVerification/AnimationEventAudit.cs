using JadeView;

internal static class AnimationEventAudit
{
    internal static void Write(SceneActor actor, string output)
    {
        var events = new List<object>();
        var failures = new List<object>();
        int decoded = 0;
        foreach (var clip in actor.Clips)
        {
            clip.EnsureLoaded();
            if (clip.Error != null) { failures.Add(new { clip.Name, clip.Error }); continue; }
            decoded++;
            foreach (var track in clip.Tracks)
            foreach (var key in track.Keys)
            foreach (var payload in key.OpaquePayloads)
                events.Add(new { Clip = clip.Key.ToString("X8"), clip.Name, key.Time, key.Duration,
                    track.Bone, Target = track.Target.ToString("X8"), track.ChannelFlags,
                    HeaderWord = track.HeaderWord.ToString("X8"), key.Flags, key.Format, payload.Kind,
                    payload.SourceOffset, Bytes = Convert.ToHexString(payload.Data),
                    Words = Enumerable.Range(0, payload.Data.Length / 4).Select(index => BitConverter.ToUInt32(payload.Data, index * 4).ToString("X8")).ToArray() });
        }
        var report = new { Actor = actor.Root.Name, TotalClips = actor.Clips.Count, Decoded = decoded, Failures = failures, Events = events };
        File.WriteAllText(Path.Combine(output, SceneExporter.SafeFileName(actor.Root.Name) + "-animation-events.json"),
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"EVENT AUDIT: {actor.Root.Name}: {decoded}/{actor.Clips.Count} clips decoded, {events.Count} retained opaque payloads, {failures.Count} failures");
    }
}
