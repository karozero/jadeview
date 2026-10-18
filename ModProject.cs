using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Сериализуемое представление матрицы/трансформации объекта для проекта модификации.
/// Хранит перенос, поворот и масштаб в человеко-читаемом и устойчивом виде.
/// </summary>
public sealed class ModTransform : IEquatable<ModTransform>
{
    [JsonPropertyName("tx")]
    public float Tx { get; set; }

    [JsonPropertyName("ty")]
    public float Ty { get; set; }

    [JsonPropertyName("tz")]
    public float Tz { get; set; }

    [JsonPropertyName("qx")]
    public float Qx { get; set; }

    [JsonPropertyName("qy")]
    public float Qy { get; set; }

    [JsonPropertyName("qz")]
    public float Qz { get; set; }

    [JsonPropertyName("qw")]
    public float Qw { get; set; } = 1.0f;

    [JsonPropertyName("sx")]
    public float Sx { get; set; } = 1.0f;

    [JsonPropertyName("sy")]
    public float Sy { get; set; } = 1.0f;

    [JsonPropertyName("sz")]
    public float Sz { get; set; } = 1.0f;

    public ModTransform() { }

    public ModTransform(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        Tx = translation.X;
        Ty = translation.Y;
        Tz = translation.Z;

        Qx = rotation.X;
        Qy = rotation.Y;
        Qz = rotation.Z;
        Qw = rotation.W;

        Sx = scale.X;
        Sy = scale.Y;
        Sz = scale.Z;
    }

    public ModTransform(Vector3 translation) : this(translation, Quaternion.Identity, Vector3.One) { }

    public static ModTransform FromTransformData(TransformData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ModTransform(data.Translation, data.Rotation, data.Scale);
    }

    public Vector3 ToTranslation() => new(Tx, Ty, Tz);
    public Quaternion ToRotation() => new(Qx, Qy, Qz, Qw);
    public Vector3 ToScale() => new(Sx, Sy, Sz);

    public void ApplyTo(TransformData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Translation = ToTranslation();
        data.Rotation = ToRotation();
        data.Scale = ToScale();
    }

    public ModTransform Clone() => new()
    {
        Tx = Tx, Ty = Ty, Tz = Tz,
        Qx = Qx, Qy = Qy, Qz = Qz, Qw = Qw,
        Sx = Sx, Sy = Sy, Sz = Sz
    };

    public bool Equals(ModTransform? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Math.Abs(Tx - other.Tx) < 1e-4f &&
               Math.Abs(Ty - other.Ty) < 1e-4f &&
               Math.Abs(Tz - other.Tz) < 1e-4f &&
               Math.Abs(Qx - other.Qx) < 1e-4f &&
               Math.Abs(Qy - other.Qy) < 1e-4f &&
               Math.Abs(Qz - other.Qz) < 1e-4f &&
               Math.Abs(Qw - other.Qw) < 1e-4f &&
               Math.Abs(Sx - other.Sx) < 1e-4f &&
               Math.Abs(Sy - other.Sy) < 1e-4f &&
               Math.Abs(Sz - other.Sz) < 1e-4f;
    }

    public override bool Equals(object? obj) => Equals(obj as ModTransform);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Tx); h.Add(Ty); h.Add(Tz);
        h.Add(Qx); h.Add(Qy); h.Add(Qz); h.Add(Qw);
        h.Add(Sx); h.Add(Sy); h.Add(Sz);
        return h.ToHashCode();
    }

    public override string ToString() =>
        $"T:({Tx:F2}, {Ty:F2}, {Tz:F2}) R:({Qx:F2}, {Qy:F2}, {Qz:F2}, {Qw:F2}) S:({Sx:F2}, {Sy:F2}, {Sz:F2})";
}

/// <summary>
/// Запись изменения отдельного игрового объекта GAO в проекте мода.
/// </summary>
public sealed class ModObjectChange
{
    [JsonPropertyName("objectId")]
    public string ObjectId { get; set; } = "";

    [JsonPropertyName("gaoKey")]
    public uint GaoKey { get; set; }

    [JsonPropertyName("objectName")]
    public string ObjectName { get; set; } = "";

    [JsonPropertyName("containerName")]
    public string ContainerName { get; set; } = "";

    [JsonPropertyName("worldKey")]
    public uint WorldKey { get; set; }

    [JsonPropertyName("isTranslationOnly")]
    public bool IsTranslationOnly { get; set; }

    [JsonPropertyName("oldTransform")]
    public ModTransform OldTransform { get; set; } = new();

    [JsonPropertyName("newTransform")]
    public ModTransform NewTransform { get; set; } = new();

    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("extraProperties")]
    public Dictionary<string, string> ExtraProperties { get; set; } = [];

    [JsonIgnore]
    public bool HasChanges => !OldTransform.Equals(NewTransform);

    public ModObjectChange Clone() => new()
    {
        ObjectId = ObjectId,
        GaoKey = GaoKey,
        ObjectName = ObjectName,
        ContainerName = ContainerName,
        WorldKey = WorldKey,
        IsTranslationOnly = IsTranslationOnly,
        OldTransform = OldTransform.Clone(),
        NewTransform = NewTransform.Clone(),
        ModifiedAt = ModifiedAt,
        ExtraProperties = new Dictionary<string, string>(ExtraProperties)
    };
}

/// <summary>
/// Результат применения проекта модификаций к SceneDocument.
/// </summary>
public sealed class ModApplyResult
{
    public bool IsSuccess => Errors.Count == 0;
    public int ObjectsUpdated { get; set; }
    public int ObjectsNotFound { get; set; }
    public List<string> UpdatedObjectNames { get; } = [];
    public List<string> MissingObjectIds { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public override string ToString() =>
        IsSuccess
            ? $"Mod applied: updated {ObjectsUpdated} objects, not found: {ObjectsNotFound}, warnings: {Warnings.Count}"
            : $"Mod application failed ({Errors.Count} errors): {string.Join("; ", Errors)}";
}

/// <summary>
/// Сводный манифест проекта модификации для предпросмотра, журналирования и экспорта.
/// </summary>
public sealed class ModManifest
{
    public string ModName { get; init; } = "";
    public int FormatVersion { get; init; }
    public string BaseBfSha256 { get; init; } = "";
    public int TotalModifiedContainers { get; init; }
    public int TotalModifiedObjects { get; init; }
    public List<string> AffectedContainers { get; init; } = [];
    public List<string> ObjectSummaries { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Mod manifest: {ModName}");
        sb.AppendLine($"- **Format version:** {FormatVersion}");
        sb.AppendLine($"- **Base BF SHA-256:** `{BaseBfSha256}`");
        sb.AppendLine($"- **Generated:** {GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Affected containers:** {TotalModifiedContainers}");
        sb.AppendLine($"- **Modified objects:** {TotalModifiedObjects}");
        sb.AppendLine();
        sb.AppendLine("## Affected containers:");
        foreach (var c in AffectedContainers)
        {
            sb.AppendLine($"- `{c}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Modified objects:");
        foreach (var s in ObjectSummaries)
        {
            sb.AppendLine($"- {s}");
        }
        return sb.ToString();
    }
}

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

/// <summary>
/// Менеджер стека команд Undo / Redo с отслеживанием признака изменений (IsDirty)
/// и событиями уведомления пользовательского интерфейса (Агент A6 -> A5).
/// </summary>
public sealed class CommandHistory
{
    private readonly List<IEditCommand> _history = [];
    private int _currentIndex = 0; // Индекс следующей команды для выполнения (или сколько команд сейчас выполнено)
    private int _savedIndex = 0;   // Индекс, на котором проект был сохранен (чистое состояние)
    private readonly int _maxHistory;

    public int MaxHistory => _maxHistory;
    public int UndoCount => _currentIndex;
    public int RedoCount => _history.Count - _currentIndex;
    public bool CanUndo => _currentIndex > 0;
    public bool CanRedo => _currentIndex < _history.Count;

    public string? CurrentUndoDescription => CanUndo ? _history[_currentIndex - 1].Description : null;
    public string? CurrentRedoDescription => CanRedo ? _history[_currentIndex].Description : null;
    public string? LastUndoDescription => CurrentUndoDescription;
    public string? LastRedoDescription => CurrentRedoDescription;
    public IEditCommand? LastExecutedCommand => CanUndo ? _history[_currentIndex - 1] : null;

    /// <summary>
    /// Признак наличия несохраненных изменений в документе/проекте.
    /// Возвращает false, если текущее состояние стека совпадает с состоянием последнего сохранения.
    /// </summary>
    public bool IsDirty
    {
        get => _savedIndex < 0 || !_history.Take(_currentIndex).Where(c => c.AffectsDocument)
            .SequenceEqual(_history.Take(_savedIndex).Where(c => c.AffectsDocument));
        set
        {
            if (!value)
            {
                _savedIndex = _currentIndex;
            }
            else if (_savedIndex == _currentIndex)
            {
                _savedIndex = -1;
            }
        }
    }

    public void Reset() => Clear();

    public event Action? OnHistoryChanged;
    public event Action<bool>? OnDirtyChanged;
    public event Action<IEditCommand>? OnCommandExecuted;
    public event Action<IEditCommand>? OnCommandUndone;
    public event Action<IEditCommand>? OnCommandRedone;

    public CommandHistory(int maxHistory = 200)
    {
        _maxHistory = Math.Max(10, maxHistory);
    }

    /// <summary>
    /// Выполняет новую команду и добавляет её в стек истории, очищая ветку Redo.
    /// </summary>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        bool wasDirty = IsDirty;

        command.Execute();

        // Если находились в середине истории, отсекаем ветку Redo
        if (_currentIndex < _history.Count)
        {
            if (_savedIndex > _currentIndex)
                _savedIndex = _history.Skip(_currentIndex).Take(_savedIndex - _currentIndex).Any(c => c.AffectsDocument)
                    ? -1 : _currentIndex;
            _history.RemoveRange(_currentIndex, _history.Count - _currentIndex);
        }

        _history.Add(command);
        _currentIndex++;

        // Restrictedие глубины стека
        if (_history.Count > _maxHistory)
        {
            int removeCount = _history.Count - _maxHistory;
            _history.RemoveRange(0, removeCount);
            _currentIndex -= removeCount;
            if (_savedIndex >= 0)
            {
                _savedIndex -= removeCount;
                if (_savedIndex < 0) _savedIndex = -1;
            }
        }

        OnCommandExecuted?.Invoke(command);
        OnHistoryChanged?.Invoke();

        if (IsDirty != wasDirty)
        {
            OnDirtyChanged?.Invoke(IsDirty);
        }
    }

    /// <summary>
    /// Отменяет последнюю выполненную команду (Undo).
    /// </summary>
    public bool Undo()
    {
        if (!CanUndo) return false;

        bool wasDirty = IsDirty;
        _currentIndex--;
        var command = _history[_currentIndex];

        command.Undo();

        OnCommandUndone?.Invoke(command);
        OnHistoryChanged?.Invoke();

        if (IsDirty != wasDirty)
        {
            OnDirtyChanged?.Invoke(IsDirty);
        }

        return true;
    }

    /// <summary>
    /// Повторяет отмененную команду (Redo).
    /// </summary>
    public bool Redo()
    {
        if (!CanRedo) return false;

        bool wasDirty = IsDirty;
        var command = _history[_currentIndex];
        _currentIndex++;

        command.Execute();

        OnCommandRedone?.Invoke(command);
        OnHistoryChanged?.Invoke();

        if (IsDirty != wasDirty)
        {
            OnDirtyChanged?.Invoke(IsDirty);
        }

        return true;
    }

    /// <summary>
    /// Помечает текущую позицию в истории как сохраненное состояние (IsDirty = false).
    /// Вызывается после сохранения проекта мода.
    /// </summary>
    public void MarkSaved()
    {
        bool wasDirty = IsDirty;
        _savedIndex = _currentIndex;

        if (wasDirty)
        {
            OnDirtyChanged?.Invoke(false);
            OnHistoryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Очищает всю историю команд.
    /// </summary>
    public void Clear()
    {
        bool wasDirty = IsDirty;
        _history.Clear();
        _currentIndex = 0;
        _savedIndex = 0;

        OnHistoryChanged?.Invoke();
        if (wasDirty)
        {
            OnDirtyChanged?.Invoke(false);
        }
    }

    public IReadOnlyList<IEditCommand> GetUndoCommands() =>
        _history.Take(_currentIndex).ToList().AsReadOnly();

    public IReadOnlyList<IEditCommand> GetRedoCommands() =>
        _history.Skip(_currentIndex).ToList().AsReadOnly();
}

/// <summary>
/// Редакторская команда изменения трансформации SceneObject, синхронизированная с ModProject.
/// Одно перетаскивание gizmo или одно изменение в Details = одна команда в стеке.
/// </summary>
public sealed class ModTransformCommand : IEditCommand
{
    private readonly SceneObject _target;
    private readonly ModProject? _project;
    private readonly string _containerName;
    private readonly uint _worldKey;
    private readonly bool _translationOnly;

    private readonly Vector3 _oldTranslation;
    private readonly Quaternion _oldRotation;
    private readonly Vector3 _oldScale;

    private readonly Vector3 _newTranslation;
    private readonly Quaternion _newRotation;
    private readonly Vector3 _newScale;

    public string Description => $"Transform {_target.Name}";

    public ModTransformCommand(
        SceneObject target,
        Vector3 newPos,
        Quaternion newRot,
        Vector3 newScale,
        ModProject? project = null,
        string containerName = "",
        uint worldKey = 0,
        bool translationOnly = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _project = project;
        _containerName = containerName;
        _worldKey = worldKey;
        _translationOnly = translationOnly;

        _oldTranslation = target.Transform.Translation;
        _oldRotation = target.Transform.Rotation;
        _oldScale = target.Transform.Scale;

        _newTranslation = newPos;
        _newRotation = newRot;
        _newScale = newScale;
    }

    public void Execute()
    {
        if (_target.Capabilities.CanTranslate) _target.Transform.Translation = _newTranslation;
        if (_target.Capabilities.CanRotate) _target.Transform.Rotation = _newRotation;
        if (_target.Capabilities.CanScale) _target.Transform.Scale = _newScale;

        if (_project != null)
        {
            var oldData = new TransformData { Translation = _oldTranslation, Rotation = _oldRotation, Scale = _oldScale };
            var newData = new TransformData { Translation = _newTranslation, Rotation = _newRotation, Scale = _newScale };
            _project.RecordChange(_target, _containerName, _worldKey, oldData, newData, _translationOnly);
        }
    }

    public void Undo()
    {
        if (_target.Capabilities.CanTranslate) _target.Transform.Translation = _oldTranslation;
        if (_target.Capabilities.CanRotate) _target.Transform.Rotation = _oldRotation;
        if (_target.Capabilities.CanScale) _target.Transform.Scale = _oldScale;

        if (_project != null)
        {
            var oldData = new TransformData { Translation = _oldTranslation, Rotation = _oldRotation, Scale = _oldScale };

            var change = _project.GetChange(_target.Id, _containerName);
            if (change != null)
            {
                change.NewTransform = ModTransform.FromTransformData(oldData);
                change.ModifiedAt = DateTimeOffset.UtcNow;
                _project.ModifiedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}

/// <summary>
/// Составная транзакционная команда для пакетного изменения нескольких objects за одно действие.
/// </summary>
public sealed class CompositeEditCommand : IEditCommand
{
    private readonly List<IEditCommand> _commands = [];
    public string Description { get; }

    public CompositeEditCommand(string description, IEnumerable<IEditCommand> commands)
    {
        Description = description;
        _commands.AddRange(commands);
    }

    public void Execute()
    {
        foreach (var cmd in _commands)
        {
            cmd.Execute();
        }
    }

    public void Undo()
    {
        // Cancel выполняется в обратном порядке
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
