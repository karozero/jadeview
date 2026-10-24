using System.Runtime.CompilerServices;

namespace JadeView;

// A GRO can be drawn by several GAOs with different transforms and paint.
// CPU samples belong to instances, while the shared mesh holds only the last uploaded sample.
internal sealed class GpuDiffuseLightingSamples
{
    private readonly ConditionalWeakTable<SceneObject, Dictionary<int, GpuDiffuseLightingCache>> samples = new();
    private readonly Dictionary<int, (GpuDiffuseLightingCache Sample, int Revision)> uploaded = [];

    internal GpuDiffuseLightingCache Get(SceneObject owner, int part)
    {
        var parts = samples.GetOrCreateValue(owner);
        if (!parts.TryGetValue(part, out var sample)) parts[part] = sample = new();
        return sample;
    }

    internal bool NeedsUpload(int part, GpuDiffuseLightingCache sample) =>
        !uploaded.TryGetValue(part, out var current)
        || !ReferenceEquals(current.Sample, sample) || current.Revision != sample.Revision;

    internal void MarkUploaded(int part, GpuDiffuseLightingCache sample) =>
        uploaded[part] = (sample, sample.Revision);

    internal void Clear()
    {
        samples.Clear();
        uploaded.Clear();
    }
}
