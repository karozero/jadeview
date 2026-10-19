namespace JadeView;

public sealed class ActorModuleMap
{
    public const int Capacity = 64;
    private const int HeaderSize = 1;
    private const int EntrySize = 3;
    private const byte InheritedSelector = byte.MaxValue;
    private readonly byte[] selectors;

    private ActorModuleMap(byte[] selectors) => this.selectors = selectors;

    // PC B5BA30 expands (component, selector, appearance) triples into a table.
    // B5AB00 preserves the previous selector wherever the new table contains FF.
    public static ActorModuleMap Read(ReadOnlySpan<byte> shape, ActorModuleMap? previous = null)
    {
        if (shape.IsEmpty)
            throw new InvalidDataException("Invalid PC actor module shape.");
        bool supportedCount = shape[0] <= Capacity;
        bool completeEntries = shape.Length >= HeaderSize + shape[0] * EntrySize;
        if (!supportedCount || !completeEntries)
            throw new InvalidDataException("Invalid PC actor module shape.");
        byte[] selectors = Enumerable.Repeat(InheritedSelector, Capacity).ToArray();
        for (int entry = 0; entry < shape[0]; entry++)
        {
            int offset = HeaderSize + entry * EntrySize;
            int component = (sbyte)shape[offset];
            if (component < 0 || component >= Capacity)
                throw new InvalidDataException("PC module component is outside the supported table.");
            selectors[component] = shape[offset + 1];
        }
        InheritSelectors(selectors, previous);
        return new(selectors);
    }

    private static void InheritSelectors(byte[] selectors, ActorModuleMap? previous)
    {
        if (previous == null) return;
        for (int component = 0; component < Capacity; component++)
        {
            if (selectors[component] != InheritedSelector) continue;
            selectors[component] = previous.selectors[component];
        }
    }

    public int FindComponent(byte selector, IReadOnlyList<SceneObject?> components)
    {
        for (int component = 0; component < Math.Min(components.Count, Capacity); component++)
            if (selectors[component] == selector && components[component] != null) return component;
        return -1;
    }
}
