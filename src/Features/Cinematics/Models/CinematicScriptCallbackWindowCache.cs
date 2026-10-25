namespace JadeView;

/// <summary>Callback windows depend on serialized timelines and saved flag assumptions, not the playhead.</summary>
internal sealed class CinematicScriptCallbackWindowCache
{
    private readonly Dictionary<object, CinematicScriptCallbackWindow?> windows =
        new(ReferenceEqualityComparer.Instance);
    private Dictionary<int, bool?>? savedFlags;

    internal CinematicScriptCallbackWindow? GetOrResolve(object identity,
        IReadOnlyDictionary<int, bool?>? assumptions, Func<CinematicScriptCallbackWindow?> resolve)
    {
        if (!SameFlags(assumptions))
        {
            savedFlags = assumptions?.ToDictionary(pair => pair.Key, pair => pair.Value);
            windows.Clear();
        }
        if (windows.TryGetValue(identity, out var window)) return window;
        window = resolve();
        windows.Add(identity, window);
        return window;
    }

    private bool SameFlags(IReadOnlyDictionary<int, bool?>? assumptions) =>
        (savedFlags?.Count ?? 0) == (assumptions?.Count ?? 0)
        && (savedFlags == null || savedFlags.All(pair =>
            assumptions?.TryGetValue(pair.Key, out bool? value) == true && value == pair.Value));
}
