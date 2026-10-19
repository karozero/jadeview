using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;
public sealed partial class ModProject
{
    /// <summary>
    /// Генерирует сводный манифест проекта модификации.
    /// </summary>
    public ModManifest GenerateManifest()
    {
        var containers = Changes.Select(candidateChange => candidateChange.ContainerName).Concat(SceneStructureChanges.Select(candidateChange => candidateChange.ContainerName)).Where(candidateChange => !string.IsNullOrEmpty(candidateChange)).Distinct().ToList();
        var summaries = Changes.Select(candidateChange => $"[{candidateChange.ContainerName}] {candidateChange.ObjectName} (GAO: 0x{candidateChange.GaoKey:X8}) -> T:({candidateChange.NewTransform.Tx:F1}, {candidateChange.NewTransform.Ty:F1}, {candidateChange.NewTransform.Tz:F1})").Concat(SceneStructureChanges.Select(candidateChange => $"[{candidateChange.ContainerName}] {(candidateChange.Kind == "add" ? "Added" : "Removed")} {candidateChange.Name} (GAO: 0x{candidateChange.GaoKey:X8})")).ToList();
        return new ModManifest
        {
            ModName = Name,
            FormatVersion = FormatVersion,
            BaseBfSha256 = BaseBfSha256,
            TotalModifiedContainers = containers.Count,
            TotalModifiedObjects = Changes.Count + SceneStructureChanges.Count,
            AffectedContainers = containers,
            ObjectSummaries = summaries,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Сериализует проект в JSON-строку.
    /// </summary>
    public string ToJson(bool indented = true)
    {
        var options = indented ? JsonOptions : new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = false
        };
        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Десериализует проект из JSON-строки.
    /// </summary>
    public static ModProject FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var project = JsonSerializer.Deserialize<ModProject>(json, JsonOptions);
        if (project == null)
            throw new InvalidDataException("Failed to parse mod project JSON.");
        project.ValidateMorphPreviews();
        return project;
    }

    /// <summary>
    /// Сохраняет проект модификации в файл (.bgemod / .json).
    /// </summary>
    public void Save(string filePath)
    {
        ValidateMorphPreviews();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Project file path must not be empty.", nameof(filePath));
        ModifiedAt = DateTimeOffset.UtcNow;
        string json = ToJson(indented: true);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json, System.Text.Encoding.UTF8);
            File.Move(temp, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                { /* ignore */
                }
            }
        }
    }

    /// <summary>
    /// Загружает проект модификации из файла (.bgemod / .json).
    /// </summary>
    public static ModProject Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Mod project file not found: {filePath}", filePath);
        string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        return FromJson(json);
    }
}
