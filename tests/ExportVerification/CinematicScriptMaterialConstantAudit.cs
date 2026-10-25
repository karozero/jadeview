using System.Globalization;
using System.Numerics;
using JadeView;

internal static class CinematicScriptMaterialConstantAudit
{
    internal static void Write(string archivePath, string containerId, uint clipKey,
        uint materialKey, float start, float end, float step, string outputDirectory,
        IReadOnlyDictionary<int, bool?>? savedFlags = null)
    {
        if (!float.IsFinite(start) || !float.IsFinite(end) || !float.IsFinite(step)
            || start < 0 || end < start || step <= 0 || (end - start) / step > 10000)
            throw new ArgumentOutOfRangeException(nameof(step), "Use a finite positive sample interval and range.");

        var archive = new Archive(archivePath);
        Entry entry = archive.Entries.Single(item => item.Id == containerId);
        SceneDocument document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        if (!document.CinematicClips.TryGetValue(clipKey, out AnimationClip? clip))
            throw new InvalidDataException($"Clip {clipKey:X8} was not found in {containerId}.");
        WorldData sourceWorld = document.Worlds.Single(world => world.Key == clip.CinematicWorldKey);
        WorldData world = CinematicWorldAssembler.Assemble(document, sourceWorld);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        CinematicPlayback playback = CinematicPlayback.Discover(document, world, resolver,
                archive.Entries)
            .Single(candidate => candidate.Key == clipKey);
        CinematicScriptMaterialConstantPlayback? callbackMaterialPlayback =
            CinematicScriptMaterialConstantPlayback.Create(document, world, resolver.Materials,
                playback.ScriptStateCalls);
        var sourceCallbackOwners = document.CinematicScriptOwners.Values
            .Where(owner => owner.WorldKey == sourceWorld.Key && owner.ModelFunctionNames.Length > 0)
            .Select(owner => new
            {
                Owner = owner.Name,
                Key = owner.OwnerKey.ToString("X8", CultureInfo.InvariantCulture),
                owner.CallbackNames,
                owner.ModelFunctionNames,
                MaterialCalls = CinematicScriptReader.ReachableEngineCalls(owner)
                    .Where(call => call.Api == "MATSetConstant")
                    .Select(call => new { call.FunctionName, call.Arguments, call.BranchConditions,
                        call.Line, call.Source }).ToArray()
            })
            .Where(owner => owner.MaterialCalls.Length > 0)
            .ToArray();
        IReadOnlyDictionary<int, bool?> assumptions = savedFlags
            ?? new Dictionary<int, bool?>();
        playback.BindSavedFlagAssumptions(assumptions);

        var samples = new List<(float Time, Vector4? Color)>();
        playback.Activate();
        try
        {
            int sampleCount = (int)MathF.Floor((end - start) / step) + 1;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = Math.Min(end, start + sampleIndex * step);
                playback.Seek(time);
                Vector4? color = playback.MaterialState.CaptureLayers()
                    .TryGetMaterialConstant(materialKey, out Vector4 sampledColor)
                        ? sampledColor : null;
                samples.Add((time, color));
            }
        }
        finally
        {
            playback.Deactivate();
        }

        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory,
            $"cinematic-script-material-{containerId}-{clipKey:X8}-{materialKey:X8}.json");
        File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            Container = containerId,
            Clip = clipKey.ToString("X8", CultureInfo.InvariantCulture),
            Material = materialKey.ToString("X8", CultureInfo.InvariantCulture),
            SavedFlagAssumptions = assumptions,
            ScriptCallbackPlaybackDiagnostics = playback.ScriptCallbackPlaybackDiagnostics,
            MaterialCallbackTrace = samples.Select(sample => new
            {
                sample.Time,
                Writes = callbackMaterialPlayback?.Trace(sample.Time, assumptions, materialKey) ?? []
            }).ToArray(),
            SourceCallbackOwners = sourceCallbackOwners,
            Samples = samples.Select(sample => new
            {
                sample.Time,
                Color = sample.Color is { } color
                    ? new[] { color.X, color.Y, color.Z, color.W }
                    : null
            }).ToArray(),
            Limits = "Samples are reconstructed JadeView callback/material state. They do not prove retail execution or renderer parity."
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"CINEMATIC MATERIAL CALLBACK {containerId}/{clipKey:X8} material={materialKey:X8}; flags={string.Join(",", assumptions.Select(pair => $"{pair.Key}:{pair.Value?.ToString() ?? "unknown"}"))}");
        foreach (string diagnostic in playback.ScriptCallbackPlaybackDiagnostics)
            Console.WriteLine("  callback: " + diagnostic);
        Console.WriteLine($"  reachable callback owners with material writes: {sourceCallbackOwners.Length}");
        foreach (var sample in samples)
        {
            foreach (string write in callbackMaterialPlayback?.Trace(sample.Time, assumptions, materialKey) ?? [])
                Console.WriteLine($"  t={sample.Time:F3}s trace: {write}");
        }
        foreach ((float time, Vector4? color) in samples)
            Console.WriteLine(color is { } value
                ? $"  t={time:F3}s constant=({value.X:F4},{value.Y:F4},{value.Z:F4},{value.W:F4})"
                : $"  t={time:F3}s no resolved constant");
        Console.WriteLine($"WROTE {outputPath}");
    }
}
