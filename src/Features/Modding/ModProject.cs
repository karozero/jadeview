using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;
/// <summary>
/// Сериализуемый в JSON проект модификаций Beyond Good &amp; Evil (.bgemod / .json).
/// Хранит метаданные, привязку к базовому BF по SHA-256 и список изменений по стабильным ID.
/// </summary>
public sealed partial class ModProject
{
    public const int CurrentFormatVersion = 1;
    public const string DefaultFileExtension = ".bgemod";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "New modification";

    [JsonIgnore]
    public string ModName { get => Name; set => Name = value; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = Environment.UserName;

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("baseBfPath")]
    public string BaseBfPath { get; set; } = "";

    [JsonPropertyName("baseBfSha256")]
    public string BaseBfSha256 { get; set; } = "";

    [JsonPropertyName("baseBfFileName")]
    public string BaseBfFileName { get; set; } = "sally_clean.bf";

    [JsonPropertyName("baseBfSize")]
    public long BaseBfSize { get; set; }

    [JsonPropertyName("containerKey")]
    public uint ContainerKey { get; set; }

    [JsonPropertyName("containerName")]
    public string ContainerName { get; set; } = "";

    [JsonPropertyName("changes")]
    public List<ModObjectChange> Changes { get; set; } = [];

    [JsonPropertyName("sceneStructureChanges")]
    public List<SceneStructureChange> SceneStructureChanges { get; set; } = [];

    // Keep allocations after an object is deleted: existing saves still own its bit.
    [JsonPropertyName("pickupSaveFlags")]
    public Dictionary<string, int> PickupSaveFlags { get; set; } = [];

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = [];
    [JsonPropertyName("morphPreviews")]
    public List<ModMorphPreview> MorphPreviews { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Changes.Count == 0 && SceneStructureChanges.Count == 0 && MorphPreviews.Count == 0;

    [JsonIgnore]
    public bool HasChanges => Changes.Any(candidateChange => candidateChange.HasChanges) || SceneStructureChanges.Count > 0;

    /// <summary>
    /// Создает новый пустой проект мода, привязанный к SceneDocument.
    /// </summary>
    public static ModProject CreateForDocument(SceneDocument document, string name = "New modification")
    {
        ArgumentNullException.ThrowIfNull(document);
        string sha = document.BaseBfSha256;
        if (string.IsNullOrWhiteSpace(sha) && !string.IsNullOrWhiteSpace(document.BaseBfPath) && File.Exists(document.BaseBfPath))
        {
            try
            {
                using var fs = File.OpenRead(document.BaseBfPath);
                sha = Convert.ToHexStringLower(SHA256.HashData(fs));
                document.BaseBfSha256 = sha;
            }
            catch
            {
            // Игнорируем ошибку чтения при создании
            }
        }

        return new ModProject
        {
            Name = name,
            BaseBfPath = document.BaseBfPath,
            BaseBfSha256 = (sha ?? "").ToLowerInvariant(),
            BaseBfFileName = !string.IsNullOrEmpty(document.BaseBfPath) ? Path.GetFileName(document.BaseBfPath) : "sally_clean.bf",
            ContainerName = document.ContainerName,
            ContainerKey = document.ActiveWorld?.Key ?? 0,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    public static ModProject CreateNew(SceneDocument document) => CreateForDocument(document);
    public void RecordModification(SceneObject obj, Vector3 eulerDeg = default)
    {
        ArgumentNullException.ThrowIfNull(obj);
        RecordChange(obj, !string.IsNullOrEmpty(ContainerName) ? ContainerName : "FF007685", 0, obj.Transform, obj.Transform, translationOnly: false);
    }

    public void ApplyTo(SceneDocument document) => ApplyToDocument(document);
    public ModProject CreateSnapshot() => JsonSerializer.Deserialize<ModProject>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions) ?? throw new InvalidDataException("Cannot snapshot the current project.");
    public WritePlan BuildWritePlan(byte[] originalContainerBytes, SceneDocument document) => BuildWritePlan(document, originalContainerBytes);
    /// <summary>
    /// Проверяет совпадение контрольной суммы SHA-256 базового BF-архива.
    /// Защищает от случайного применения мода к другой версии игры или поврежденному архиву.
    /// </summary>
    public bool VerifyBaseBf(string actualSha256, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(BaseBfSha256))
        {
            error = "Project has no base archive checksum (expected SHA-256 is empty).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(actualSha256))
        {
            error = "Archive checksum is empty.";
            return false;
        }

        if (!string.Equals(BaseBfSha256.Trim(), actualSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            error = $"Base BF version mismatch. Expected SHA-256: {BaseBfSha256.ToLowerInvariant()}, actual: {actualSha256.ToLowerInvariant()}. " + "Mod application rejected to prevent data corruption.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Проверяет совпадение контрольной суммы базового архива с загруженным SceneDocument.
    /// </summary>
    public bool VerifyBaseBf(SceneDocument document, out string? error)
    {
        ArgumentNullException.ThrowIfNull(document);
        return VerifyBaseBf(document.BaseBfSha256, out error);
    }

    /// <summary>
    /// Проверяет контрольную сумму файла BF на диске.
    /// </summary>
    public bool VerifyBaseBfFile(string bfPath, out string? error)
    {
        error = null;
        if (!File.Exists(bfPath))
        {
            error = $"Archive file not found: {bfPath}";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(bfPath);
            var hashBytes = SHA256.HashData(stream);
            var actualHash = Convert.ToHexStringLower(hashBytes);
            return VerifyBaseBf(actualHash, out error);
        }
        catch (Exception ex)
        {
            error = $"Failed to compute SHA-256 of {bfPath}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Регистрирует или обновляет изменение трансформации объекта в проекте.
    /// </summary>
    public ModObjectChange RecordChange(SceneObject target, string containerName, uint worldKey, TransformData oldTransform, TransformData newTransform, bool translationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(oldTransform);
        ArgumentNullException.ThrowIfNull(newTransform);
        var existing = Changes.FirstOrDefault(candidateChange => string.Equals(candidateChange.ObjectId, target.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.NewTransform = ModTransform.FromTransformData(newTransform);
            existing.IsTranslationOnly = translationOnly;
            existing.ModifiedAt = DateTimeOffset.UtcNow;
            ModifiedAt = DateTimeOffset.UtcNow;
            return existing;
        }

        var change = new ModObjectChange
        {
            ObjectId = target.Id,
            GaoKey = target.GaoKey,
            ObjectName = target.Name,
            ContainerName = containerName,
            WorldKey = worldKey,
            OldTransform = ModTransform.FromTransformData(oldTransform),
            NewTransform = ModTransform.FromTransformData(newTransform),
            IsTranslationOnly = translationOnly,
            ModifiedAt = DateTimeOffset.UtcNow
        };
        Changes.Add(change);
        ModifiedAt = DateTimeOffset.UtcNow;
        return change;
    }

    /// <summary>
    /// Удаляет изменение объекта из проекта.
    /// </summary>
    public bool RemoveChange(string objectId, string containerName)
    {
        int count = Changes.RemoveAll(candidateChange => string.Equals(candidateChange.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) && string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));
        if (count > 0)
        {
            ModifiedAt = DateTimeOffset.UtcNow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет наличие зарегистрированного изменения для объекта.
    /// </summary>
    public bool HasChange(string objectId, string containerName) => Changes.Any(candidateChange => string.Equals(candidateChange.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) && string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase) && candidateChange.HasChanges);
    /// <summary>
    /// Возвращает зарегистрированное изменение объекта, если оно есть.
    /// </summary>
    public ModObjectChange? GetChange(string objectId, string containerName) => Changes.FirstOrDefault(candidateChange => string.Equals(candidateChange.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) && string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));
    /// <summary>
    /// Возвращает все изменения для указанного контейнера.
    /// </summary>
    public List<ModObjectChange> GetChangesForContainer(string containerName) => Changes.Where(candidateChange => string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase)).ToList();
    /// <summary>
    /// Очищает все зарегистрированные изменения в проекте.
    /// </summary>
    public void ClearChanges()
    {
        Changes.Clear();
        SceneStructureChanges.Clear();
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
