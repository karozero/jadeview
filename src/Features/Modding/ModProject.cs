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
public sealed class ModProject
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
    public string ModName
    {
        get => Name;
        set => Name = value;
    }

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

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Changes.Count == 0;

    [JsonIgnore]
    public bool HasChanges => Changes.Any(c => c.HasChanges);

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

    public WritePlan BuildWritePlan(byte[] originalContainerBytes, SceneDocument document) =>
        BuildWritePlan(document, originalContainerBytes);

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
            error = $"Base BF version mismatch. Expected SHA-256: {BaseBfSha256.ToLowerInvariant()}, actual: {actualSha256.ToLowerInvariant()}. " +
                    "Mod application rejected to prevent data corruption.";
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
    public ModObjectChange RecordChange(
        SceneObject target,
        string containerName,
        uint worldKey,
        TransformData oldTransform,
        TransformData newTransform,
        bool translationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(oldTransform);
        ArgumentNullException.ThrowIfNull(newTransform);

        var existing = Changes.FirstOrDefault(c =>
            string.Equals(c.ObjectId, target.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));

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
        int count = Changes.RemoveAll(c =>
            string.Equals(c.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));

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
    public bool HasChange(string objectId, string containerName) =>
        Changes.Any(c =>
            string.Equals(c.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.ContainerName, containerName, StringComparison.OrdinalIgnoreCase) &&
            c.HasChanges);

    /// <summary>
    /// Возвращает зарегистрированное изменение объекта, если оно есть.
    /// </summary>
    public ModObjectChange? GetChange(string objectId, string containerName) =>
        Changes.FirstOrDefault(c =>
            string.Equals(c.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Возвращает все изменения для указанного контейнера.
    /// </summary>
    public List<ModObjectChange> GetChangesForContainer(string containerName) =>
        Changes.Where(c => string.Equals(c.ContainerName, containerName, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Очищает все зарегистрированные изменения в проекте.
    /// </summary>
    public void ClearChanges()
    {
        Changes.Clear();
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Приняет изменения из проекта мода к переданному SceneDocument.
    /// </summary>
    public ModApplyResult ApplyToDocument(SceneDocument document, bool ignoreHashMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new ModApplyResult();

        bool hasFingerprint = !string.IsNullOrWhiteSpace(BaseBfSha256) || !string.IsNullOrWhiteSpace(document.BaseBfSha256);
        if (!ignoreHashMismatch && hasFingerprint && !VerifyBaseBf(document, out var hashError))
        {
            result.Errors.Add(hashError!);
            return result;
        }

        string currentContainer = document.ContainerName;
        var relevantChanges = string.IsNullOrEmpty(currentContainer)
            ? Changes
            : Changes.Where(c => string.Equals(c.ContainerName, currentContainer, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var change in relevantChanges)
        {
            var target = document.FindObjectById(change.ObjectId);

            // Если не нашли по прямому ID, пробуем найти по GaoKey в активном мире
            if (target == null && change.GaoKey != 0)
            {
                foreach (var w in document.Worlds)
                {
                    target = w.Objects.FirstOrDefault(o => o.GaoKey == change.GaoKey);
                    if (target != null) break;
                }
            }

            if (target == null)
            {
                result.ObjectsNotFound++;
                result.MissingObjectIds.Add(change.ObjectId);
                result.Warnings.Add($"Object '{change.ObjectName}' (ID: {change.ObjectId}, GAO: 0x{change.GaoKey:X8}) not found in scene.");
                continue;
            }

            // Применяем трансформацию
            change.NewTransform.ApplyTo(target.Transform);
            result.ObjectsUpdated++;
            result.UpdatedObjectNames.Add(target.Name);
        }

        return result;
    }

    /// <summary>
    /// Создает WritePlan для SceneWriter на основе зарегистрированных изменений для указанного контейнера.
    /// </summary>
    public WritePlan BuildWritePlan(SceneDocument document, byte[] containerBytes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(containerBytes);

        string containerName = document.ContainerName;
        var plan = new WritePlan
        {
            ContainerName = containerName,
            ExpectedContainerSize = containerBytes.Length,
            ExpectedContainerSha256 = ""
        };

        var containerChanges = GetChangesForContainer(containerName);
        foreach (var change in containerChanges)
        {
            if (!change.HasChanges) continue;

            var target = document.FindObjectById(change.ObjectId);
            if (target == null && change.GaoKey != 0)
            {
                foreach (var w in document.Worlds)
                {
                    target = w.Objects.FirstOrDefault(o => o.GaoKey == change.GaoKey);
                    if (target != null) break;
                }
            }

            if (target == null)
            {
                plan.Diagnostics.Add($"Warning: object '{change.ObjectName}' (0x{change.GaoKey:X8}) skipped: not found in SceneDocument.");
                continue;
            }

            if (!target.Transform.MatrixByteRange.IsValid || target.Transform.MatrixByteRange.Size != JadeMatrix.SizeInBytes)
            {
                plan.Diagnostics.Add($"Warning: object '{change.ObjectName}' skipped: invalid MatrixByteRange.");
                continue;
            }

            if (change.IsTranslationOnly)
            {
                plan.AddTranslationPatch(target, change.NewTransform.ToTranslation(), containerBytes);
            }
            else
            {
                plan.AddTransformPatch(
                    target,
                    change.NewTransform.ToTranslation(),
                    change.NewTransform.ToRotation(),
                    change.NewTransform.ToScale(),
                    containerBytes);
            }
        }

        return plan;
    }

    /// <summary>
    /// Генерирует сводный манифест проекта модификации.
    /// </summary>
    public ModManifest GenerateManifest()
    {
        var containers = Changes.Select(c => c.ContainerName).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        var summaries = Changes.Select(c =>
            $"[{c.ContainerName}] {c.ObjectName} (GAO: 0x{c.GaoKey:X8}) -> T:({c.NewTransform.Tx:F1}, {c.NewTransform.Ty:F1}, {c.NewTransform.Tz:F1})"
        ).ToList();

        return new ModManifest
        {
            ModName = Name,
            FormatVersion = FormatVersion,
            BaseBfSha256 = BaseBfSha256,
            TotalModifiedContainers = containers.Count,
            TotalModifiedObjects = Changes.Count,
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
        var options = indented ? JsonOptions : new JsonSerializerOptions(JsonOptions) { WriteIndented = false };
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

        return project;
    }

    /// <summary>
    /// Сохраняет проект модификации в файл (.bgemod / .json).
    /// </summary>
    public void Save(string filePath)
    {
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
                try { File.Delete(temp); } catch { /* ignore */ }
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

