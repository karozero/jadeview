using System.Numerics;

namespace JadeView;

public sealed class ResourceItem
{
    public ResourceId Id { get; init; }
    public ResourceType Type { get; init; }
    public ResolutionState State { get; set; } = ResolutionState.Unresolved;
    public object? Payload { get; set; }
    public List<uint> Dependencies { get; } = [];
}

