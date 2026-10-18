using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

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

