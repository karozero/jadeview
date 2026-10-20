using System.Security.Cryptography;
using System.Text.Json;
using JadeView;

internal static class CinematicTimingAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, string editorPath, string output)
    {
        using var editor = JsonDocument.Parse(File.ReadAllText(editorPath));
        var reference = editor.RootElement.EnumerateArray().Single(clip => clip.GetProperty("key").GetString() == $"{clipKey:X8}");
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var clip = document.CinematicClips[clipKey];
        int curves = 0, samples = 0;
        float maximumError = 0;
        foreach (var expected in reference.GetProperty("timingCurves").EnumerateArray())
        {
            int trackIndex = expected.GetProperty("track").GetInt32();
            int eventIndex = expected.GetProperty("event").GetInt32();
            var track = clip.Tracks[trackIndex];
            var key = track.Keys[eventIndex];
            if ($"{track.Target:X8}" != expected.GetProperty("target").GetString() || key.TimeCurve == null)
                throw new InvalidDataException("Editor time curve does not map to its retail track/key.");
            var raw = key.OpaquePayloads.Single(payload => payload.Kind == "extra-transform-controls");
            string hash = Convert.ToHexStringLower(SHA256.HashData(raw.Data));
            if (hash != expected.GetProperty("sha256").GetString())
                throw new InvalidDataException("Editor/retail time-curve bytes differ.");
            foreach (var sample in expected.GetProperty("samples").EnumerateArray())
            {
                float elapsed = sample.GetProperty("elapsed").GetSingle();
                float warped = sample.GetProperty("warped").GetSingle();
                float error = MathF.Abs(key.TimeCurve.Evaluate(elapsed) - warped);
                maximumError = Math.Max(maximumError, error);
                if (error > .00005f * Math.Max(1, MathF.Abs(warped)))
                    throw new InvalidDataException($"Time-curve reference mismatch at {trackIndex}/{eventIndex}/{elapsed}.");
                samples++;
            }
            curves++;
        }
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(world => world.Key == clip.CinematicWorldKey));
        var playback = CinematicPlayback.Discover(document, world, new()).Single(player => player.Key == clipKey);
        var report = new
        {
            Container = container,
            Clip = $"{clipKey:X8}",
            Curves = curves,
            Samples = samples,
            MaximumError = maximumError,
            Cameras = playback.Cameras.Select(binding => new
            {
                Source = binding.Source.GaoKey.ToString("X8"),
                binding.Source.Name,
                Target = binding.Target?.GaoKey.ToString("X8"),
                binding.FromSourceEvent
            }),
            CameraEvents = CinematicCameraEvent.Read(clip)
        };
        File.WriteAllText(Path.Combine(output, $"{container}-{clipKey:X8}-timing.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"TIMING AUDIT: {clipKey:X8}: {curves} identical curves, {samples} reference samples, max error {maximumError:G4}; {playback.Cameras.Count(camera => camera.FromSourceEvent)} explicit camera pairs");
    }
}
