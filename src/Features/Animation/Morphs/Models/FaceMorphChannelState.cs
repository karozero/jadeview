using System.Text.Json.Serialization;

namespace JadeView;

// User-selected channel controls. References remain bound to the original modifier.
public sealed record FaceMorphChannelState(
    [property: JsonPropertyName("weight")] float Weight,
    [property: JsonPropertyName("progression")] float Progression)
{
    [JsonIgnore]
    public bool IsValid => float.IsFinite(Weight) && float.IsFinite(Progression)
        && Progression >= 0 && Progression < 2147483648f;
}
