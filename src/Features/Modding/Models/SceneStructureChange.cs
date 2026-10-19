using System.Text.Json.Serialization;

namespace JadeView;
public sealed class SceneStructureChange
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "add";

    [JsonPropertyName("containerName")]
    public string ContainerName { get; set; } = "";

    [JsonPropertyName("worldKey")]
    public uint WorldKey { get; set; }

    [JsonPropertyName("objectId")]
    public string ObjectId { get; set; } = "";

    [JsonPropertyName("sourceObjectId")]
    public string? SourceObjectId { get; set; }

    [JsonPropertyName("gaoKey")]
    public uint GaoKey { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("geometryKey")]
    public uint GeometryKey { get; set; }

    [JsonPropertyName("materialKey")]
    public uint MaterialKey { get; set; }

    [JsonPropertyName("flags")]
    public uint Flags { get; set; }

    [JsonPropertyName("drawFlags")]
    public uint DrawFlags { get; set; }

    [JsonPropertyName("rliColors")]
    public uint[] RliColors { get; set; } = [];

    [JsonPropertyName("modifiers")]
    public List<SceneModifier> Modifiers { get; set; } = [];

    [JsonPropertyName("transform")]
    public ModTransform Transform { get; set; } = new();

    public static SceneStructureChange Added(SceneObject obj, string containerName, uint worldKey) => new()
    {
        Kind = "add",
        ContainerName = containerName,
        WorldKey = worldKey,
        ObjectId = obj.Id,
        SourceObjectId = obj.SourceObjectId,
        GaoKey = obj.GaoKey,
        Name = obj.Name,
        GeometryKey = obj.GeometryKey,
        MaterialKey = obj.MaterialKey,
        Flags = obj.Flags,
        DrawFlags = obj.DrawFlags,
        RliColors = obj.RliColors.ToArray(),
        Modifiers = obj.Modifiers.Select(modifier => new SceneModifier(modifier.Type, modifier.Data.ToArray())).ToList(),
        Transform = ModTransform.FromTransformData(obj.Transform)
    };
    public SceneObject CreateObject() => new()
    {
        Id = ObjectId,
        SourceObjectId = SourceObjectId,
        GaoKey = GaoKey,
        Name = Name,
        GeometryKey = GeometryKey,
        MaterialKey = MaterialKey,
        Flags = Flags,
        DrawFlags = DrawFlags,
        RliColors = RliColors.ToArray(),
        Modifiers = Modifiers.Select(modifier => new SceneModifier(modifier.Type, modifier.Data.ToArray())).ToList(),
        Transform = new TransformData(),
        InitialTransform = new TransformData(),
        Capabilities = new ObjectEditCapabilities
        {
            Flags = EditCapabilities.AllTransform
        },
        IsUserCreated = true
    };
}
