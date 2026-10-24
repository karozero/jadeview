namespace JadeView;

/// <summary>Opt-in per-instance timing breakdown for render diagnostics.</summary>
internal sealed class SceneRendererOwnerWork
{
    private sealed class Sample
    {
        internal double GeometryMilliseconds;
        internal double SkinMilliseconds;
        internal double LightingMilliseconds;
    }

    private readonly Dictionary<SceneObject, Sample> samples = new(ReferenceEqualityComparer.Instance);

    internal void Clear() => samples.Clear();

    internal void AddGeometry(SceneObject owner, double milliseconds) =>
        GetOrCreate(owner).GeometryMilliseconds += milliseconds;

    internal void AddSkin(SceneObject owner, double milliseconds) =>
        GetOrCreate(owner).SkinMilliseconds += milliseconds;

    internal void AddLighting(SceneObject owner, double milliseconds) =>
        GetOrCreate(owner).LightingMilliseconds += milliseconds;

    internal string FormatHotspots(int count = 6)
    {
        if (count <= 0 || samples.Count == 0) return "none";

        return string.Join("; ", samples
            .Where(item => item.Value.GeometryMilliseconds > 0 || item.Value.SkinMilliseconds > 0
                || item.Value.LightingMilliseconds > 0)
            .OrderByDescending(item => item.Value.GeometryMilliseconds + item.Value.SkinMilliseconds
                + item.Value.LightingMilliseconds)
            .Take(count)
            .Select(item => Format(item.Key, item.Value)));
    }

    private Sample GetOrCreate(SceneObject owner)
    {
        if (!samples.TryGetValue(owner, out Sample? sample))
            samples.Add(owner, sample = new());
        return sample;
    }

    private static string Format(SceneObject owner, Sample sample)
    {
        string name = owner.Name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return FormattableString.Invariant(
            $"{owner.GaoKey:X8}/{name}[geo={sample.GeometryMilliseconds:F2},skin={sample.SkinMilliseconds:F2},light={sample.LightingMilliseconds:F2}]ms");
    }
}
