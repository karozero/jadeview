using System.Text.Json;
using JadeView;

internal static class ScriptModelVariableAudit
{
    internal static void Write(string archivePath, string container, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(candidate => candidate.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        byte[] bytes = archive.Read(entry);
        var replay = PcSceneLoader.Read(bytes, archivePath, entry.Key);
        if (!replay.Complete) throw new InvalidDataException(replay.Error);
        var document = SceneLoader.Load(bytes, entry.Name, archivePath, worldListKey: entry.Key);

        var models = replay.Records.Where(record => record.Kind == 's')
            .Select(model => new
            {
                Key = model.Key.ToString("X8"),
                WorldKey = model.LoadWorldKey.ToString("X8"), model.Name,
                OrderedVariableKeys = model.References.Where(reference => reference.Kind == 't')
                    .Select(reference => reference.Key.ToString("X8")).ToArray()
            }).ToArray();
        var summary = new
        {
            Container = entry.Id,
            ModelCount = models.Length,
            ModelsWithMultipleVariableReferences = models.Count(model => model.OrderedVariableKeys.Length > 1),
            ModelsWithRepeatedVariableKeys = models.Count(model => model.OrderedVariableKeys.Distinct().Count()
                != model.OrderedVariableKeys.Length),
            Owners = document.CinematicScriptOwners.Values.OrderBy(owner => owner.WorldKey)
                .ThenBy(owner => owner.OwnerKey).Select(owner => new
                {
                    Key = owner.OwnerKey.ToString("X8"), WorldKey = owner.WorldKey.ToString("X8"),
                    ModelKey = owner.ScriptModelKey.ToString("X8"), owner.Name,
                    CallbackKeys = owner.CallbackSlots?.Select(slot => slot.FunctionKey.ToString("X8")).ToArray(),
                    ModelVariableCount = owner.Variables.Count(variable => variable.Source == "model"),
                    InstanceVariableCount = owner.Variables.Count(variable => variable.Source == "instance"),
                    ReferenceCount = owner.ObjectReferences.Length,
                    DialogueLineCount = owner.DialogueLines?.Length ?? 0
                }).ToArray(),
            Models = models,
            Limits = "Ordered model OVA references in this decoded container only. Shared/fix resources may be omitted; these are not runtime pointer assignments or callback execution evidence."
        };
        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, $"{entry.Id}-model-variable-references.json");
        File.WriteAllText(output, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MODEL OVA AUDIT: {entry.Id}: {summary.ModelCount} models, "
            + $"{summary.ModelsWithMultipleVariableReferences} multi-OVA, "
            + $"{summary.ModelsWithRepeatedVariableKeys} repeated-key -> {output}");
    }
}
