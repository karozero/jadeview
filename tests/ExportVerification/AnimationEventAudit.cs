using JadeView;

internal static class AnimationEventAudit
{
    internal static void Write(SceneActor actor, string output)
    {
        WriteClips(actor.Root.Name, actor.Clips, output);
    }

    internal static void WriteClips(string owner, IReadOnlyCollection<AnimationClip> clips, string output)
    {
        var events = new List<object>();
        var failures = new List<object>();
        int decoded = 0;
        foreach (var clip in clips)
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
        var calls = clips.SelectMany(clip => CinematicLiteralCall.ReadTimeline(clip).Select(item => new
        {
            Clip = clip.Key.ToString("X8"),
            clip.Name,
            item.Time,
            Target = item.Call.Target.ToString("X8"),
            Function = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector),
            Arguments = item.Call.Arguments.Select(argument => $"0x{argument:X8}").ToArray(),
            Description = CinematicEngineFunctionCatalog.Describe(item.Call)
        })).ToArray();
        var report = new { Actor = owner, TotalClips = clips.Count, Decoded = decoded, Failures = failures, Events = events, DecodedCalls = calls };
        File.WriteAllText(Path.Combine(output, SceneExporter.SafeFileName(owner) + "-animation-events.json"),
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"EVENT AUDIT: {owner}: {decoded}/{clips.Count} clips decoded, {events.Count} retained opaque payloads, {failures.Count} failures");
    }
}
