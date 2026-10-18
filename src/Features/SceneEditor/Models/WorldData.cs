using System.Numerics;

namespace JadeView;

public sealed class WorldData
{
    public uint Key { get; init; }
    public string Name { get; set; } = "";
    public SourceRange RecordRange { get; init; }
    public List<SceneObject> Objects { get; } = [];
    public List<SceneActor> Actors { get; } = [];

    public override string ToString() => $"{Name} ({Key:X8}) - {Objects.Count} objects";
}

