namespace JadeView;

/// <summary>Serialized roots from one explicitly identified PC WOL container.</summary>
public sealed class GameWorldListDescriptor
{
    private readonly IReadOnlyList<uint> orderedWorldRootKeys;

    public GameWorldListDescriptor(uint? outerListKey, IEnumerable<uint> orderedWorldRootKeys)
    {
        ArgumentNullException.ThrowIfNull(orderedWorldRootKeys);
        OuterListKey = outerListKey;
        this.orderedWorldRootKeys = Array.AsReadOnly(orderedWorldRootKeys.ToArray());
    }

    /// <summary>Archive key supplied by the caller; null means the container identity is unknown.</summary>
    public uint? OuterListKey { get; }

    /// <summary>Raw WOL roots in serialized order, including zero and 0xFFFFFFFF placeholders.</summary>
    public IReadOnlyList<uint> OrderedWorldRootKeys => orderedWorldRootKeys;
}
