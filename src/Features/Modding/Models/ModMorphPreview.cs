using System.Text.Json.Serialization;

namespace JadeView;

// Authoring state only: never consumed by the BF writer.
public sealed class ModMorphPreview
{
    [JsonPropertyName("container")] public string Container { get; set; } = "";
    [JsonPropertyName("worldKey")] public uint WorldKey { get; set; }
    [JsonPropertyName("worldIndex")] public int WorldIndex { get; set; }
    [JsonPropertyName("actorId")] public string ActorId { get; set; } = "";
    [JsonPropertyName("objectId")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("geometryKey")] public uint GeometryKey { get; set; }
    [JsonPropertyName("geometrySha256")] public string GeometrySha256 { get; set; } = "";
    [JsonPropertyName("modifierSha256")] public string ModifierSha256 { get; set; } = "";
    [JsonPropertyName("targetCount")] public int TargetCount { get; set; }
    [JsonPropertyName("weights")] public float[] Weights { get; set; } = [];
    [JsonPropertyName("time")] public float Time { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
    [JsonPropertyName("keys")] public FaceMorphKeyframe[] Keys { get; set; } = [];
    [JsonPropertyName("channelState"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FaceMorphChannelState[]? ChannelState { get; set; }

    public void Validate()
    {
        bool identity = !string.IsNullOrWhiteSpace(Container) && !string.IsNullOrWhiteSpace(ActorId) &&
            !string.IsNullOrWhiteSpace(ObjectId) && WorldIndex >= 0;
        bool hashes = IsHash(GeometrySha256) && IsHash(ModifierSha256);
        if (!identity || !hashes || TargetCount <= 0 || TargetCount > 4096 || !float.IsFinite(Time) || Time < 0)
            throw new InvalidDataException("Invalid saved morph identity, source signature or time.");
        ValidateWeights(Weights);
        bool invalidChannels = ChannelState != null && (ChannelState.Length > 4096
            || ChannelState.Any(state => state is not { IsValid: true }));
        if (invalidChannels) throw new InvalidDataException("Invalid saved morph channel controls.");
        if (Keys == null || Keys.Length > 100000) throw new InvalidDataException("Invalid saved morph keys.");
        float previous = -1;
        foreach (var key in Keys)
        {
            if (key == null || !float.IsFinite(key.Time) || key.Time < 0 || key.Time <= previous)
                throw new InvalidDataException("Saved morph keys must have increasing finite times.");
            ValidateWeights(key.Weights);
            previous = key.Time;
        }
    }

    private void ValidateWeights(float[] weights)
    {
        if (weights == null || weights.Length != TargetCount || weights.Any(weight => !float.IsFinite(weight)))
            throw new InvalidDataException("Saved morph weights do not match the target count.");
    }
    private static bool IsHash(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
}
