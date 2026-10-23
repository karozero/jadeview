using JadeView;

internal static class CinematicSoundFadeAudit
{
    internal static void Write(string archivePath, string container)
    {
        var archive = new Archive(archivePath);
        Entry sceneEntry = archive.Entries.Single(entry => entry.Id == container);
        SceneDocument document = SceneLoader.Load(archive.Read(sceneEntry), sceneEntry.Name, archivePath);
        var entriesByKey = archive.Entries.GroupBy(entry => entry.Key)
            .ToDictionary(group => group.Key, group => group.First());
        Entry[] modifierEntries = archive.Entries
            .Where(entry => entry.Name.EndsWith(".smd", StringComparison.OrdinalIgnoreCase)).ToArray();
        var parsed = new List<(Entry Entry, CinematicSoundModifierFile Modifier)>();
        foreach (Entry entry in modifierEntries)
        {
            CinematicSoundModifierFile? modifier = CinematicSoundModifierFile.Read(archive.Read(entry));
            if (modifier != null) parsed.Add((entry, modifier));
        }

        var withFade = parsed.Where(item => item.Modifier.FadeOutKey != uint.MaxValue).ToArray();
        Console.WriteLine($"SOUND MODIFIER AUDIT: {container}; worlds={document.Worlds.Count}; "
            + $"archive .smd={modifierEntries.Length}; parsed={parsed.Count}; "
            + $"withFadeOut={withFade.Length}; archive .fad resources="
            + archive.Entries.Count(entry => entry.Name.EndsWith(".fad", StringComparison.OrdinalIgnoreCase)));

        foreach (var item in withFade.Take(80))
        {
            string soundName = entriesByKey.TryGetValue(item.Modifier.SoundKey, out Entry? sound)
                ? $"{sound.Folder}/{sound.Name}"
                : "missing sound key";
            string fadeName = entriesByKey.TryGetValue(item.Modifier.FadeOutKey, out Entry? fadeEntry)
                ? $"{fadeEntry.Folder}/{fadeEntry.Name}"
                : "missing fade key";
            CinematicSoundFadeEnvelope? fade = fadeEntry == null
                ? null
                : CinematicSoundFadeEnvelope.Read(archive.Read(fadeEntry));
            Console.WriteLine($"  smd={item.Entry.Key:X8} {item.Entry.Folder}/{item.Entry.Name}; "
                + $"wave={item.Modifier.SoundKey:X8} {soundName}; fade="
                + $"{item.Modifier.FadeOutKey:X8} {fadeName}; "
                + (fade == null ? "curve=unparsed" : $"duration={fade.Duration:F3}s, "
                    + $"start={fade.Evaluate(0):F3}, middle={fade.Evaluate(fade.Duration / 2):F3}, "
                    + $"end={fade.Evaluate(fade.Duration):F3}"));
        }
    }
}
