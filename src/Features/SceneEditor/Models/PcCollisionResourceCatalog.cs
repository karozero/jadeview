namespace JadeView;

internal sealed class PcCollisionResourceCatalog(IReadOnlyDictionary<SceneObject, PcCollisionObjectResources> objects)
{
    private readonly IReadOnlyDictionary<SceneObject, PcCollisionObjectResources> objects = objects;

    internal bool TryGet(SceneObject sceneObject, out PcCollisionObjectResources resources) =>
        objects.TryGetValue(sceneObject, out resources!);
}

internal sealed record PcCollisionObjectResources(uint SourceWorldKey, uint SourceGaoKey,
    uint InstanceKey, uint MapKey, PcCollisionResourceInfo? Instance,
    PcCollisionResourceInfo? Set, PcCollisionResourceInfo? Map,
    string SourceDiagnostic);

internal sealed record PcCollisionResourceInfo(uint Key, uint SourceWorldKey, char Kind,
    PcCollisionResourceStatus Status, string Diagnostic, string[] Warnings,
    PcCollisionInstance? Instance = null, PcCollisionSet? Set = null,
    PcCollisionMap? Map = null);

internal enum PcCollisionResourceStatus
{
    Resolved,
    Missing,
    Ambiguous,
    Malformed,
    Unsupported
}
