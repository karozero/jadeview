using System.Numerics;

namespace JadeView;

public sealed partial class SceneRenderer
{
    internal CinematicLightStateEvaluation? CinematicLights { get; set; }
    public bool DynamicLighting = true;
    public int StatDiffuseLightingUpdates { get; private set; }
    public int StatDiffuseLightingPreparations { get; private set; }
    private readonly Dictionary<SceneObject, Matrix4x4> lightingMatrices = [];
    private readonly List<(SceneObject Owner, Matrix4x4 Matrix, SceneLight? Light)> frameLights = [];
    private readonly Dictionary<SceneObject, SceneLight> evaluatedFrameLights = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SceneObject, Dictionary<uint, LightingPreparation>> lightingPlans = new();
    private WorldData? lightingWorld;
    private CinematicLightStateEvaluation? previousLightState;
    private int lightingRevision;
    private int lightingObjectCount = -1;
    private readonly record struct LightingKey(int Revision, int ObjectCount, uint Identity,
        uint State, string? Parent, Matrix4x4 Matrix, Vector3 BoundsCenter, PcObjectBounds? Bounds);
    private sealed record LightingPreparation(LightingKey Key, PreparedSceneObjectDiffuseLighting Plan);

    private void BeginLightingFrame(WorldData? world)
    {
        StatDiffuseLightingUpdates = 0;
        StatDiffuseLightingPreparations = 0;
        lightingMatrices.Clear();
        if (!DynamicLighting) return;
        if (!ReferenceEquals(world, lightingWorld))
        {
            lightingPlans.Clear();
            foreach (var geometry in _geometryCache.Values) geometry.DiffuseLighting.Clear();
            frameLights.Clear();
            lightingWorld = world;
            lightingRevision++;
        }
        if (world == null) return;
        if (lightingObjectCount != world.Objects.Count)
        {
            lightingPlans.Clear();
            foreach (var geometry in _geometryCache.Values) geometry.DiffuseLighting.Clear();
            lightingObjectCount = world.Objects.Count;
        }
        if (!ReferenceEquals(previousLightState, CinematicLights))
        {
            evaluatedFrameLights.Clear();
            if (CinematicLights != null)
                foreach (var state in CinematicLights.Lights)
                    evaluatedFrameLights[state.Receiver] = state.Light;
            previousLightState = CinematicLights;
        }
        // Equivalent snapshots can carry new diagnostics without changing lighting.
        // Invalidate GPU work only when an effective component or matrix changes.
        bool changed = false;
        int lightIndex = 0;
        foreach (var owner in world.Objects)
        {
            Matrix4x4 matrix = owner.PreviewMatrix ?? AnimationMath.Trs(owner.Transform);
            if (owner.CinematicZoom is float zoom) matrix = Matrix4x4.CreateScale(zoom) * matrix;
            lightingMatrices[owner] = matrix;
            if ((owner.Flags & 0x10) == 0) continue;
            var current = (owner, matrix, evaluatedFrameLights.GetValueOrDefault(owner) ?? owner.Light);
            if (lightIndex == frameLights.Count)
            {
                frameLights.Add(current);
                changed = true;
            }
            else if (frameLights[lightIndex] != current)
            {
                frameLights[lightIndex] = current;
                changed = true;
            }
            lightIndex++;
        }
        if (frameLights.Count > lightIndex)
        {
            frameLights.RemoveRange(lightIndex, frameLights.Count - lightIndex);
            changed = true;
        }
        if (changed) lightingRevision++;
    }

    private unsafe bool UpdateDiffuseLighting(WorldData world, SceneObject owner,
        GpuGeometry geometry, int partIndex, uint drawFlags,
        Vector4 rliDestination, float rliFactor)
    {
        if (!CapturePerformanceDiagnostics)
            return UpdateDiffuseLightingCore(world, owner, geometry, partIndex, drawFlags,
                rliDestination, rliFactor);

        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return UpdateDiffuseLightingCore(world, owner, geometry, partIndex, drawFlags,
                rliDestination, rliFactor);
        }
        finally
        {
            PerformanceOwnerWork.AddLighting(owner,
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private unsafe bool UpdateDiffuseLightingCore(WorldData world, SceneObject owner,
        GpuGeometry geometry, int partIndex, uint drawFlags,
        Vector4 rliDestination, float rliFactor)
    {
        if (!DynamicLighting || (drawFlags & 0x08000000) == 0 || frameLights.Count == 0) return false;
        var key = new LightingKey(lightingRevision, world.Objects.Count, owner.Flags,
            owner.SerializedStateFlags, owner.ParentId, lightingMatrices[owner],
            TransformData.FromRender(geometry.LocalCenter), owner.PcBounds);
        var ownerPlans = lightingPlans.GetOrCreateValue(owner);
        ownerPlans.TryGetValue(drawFlags, out var preparation);
        bool boundsReferenceChanged = preparation?.Plan.BoundsReferenceOwner is { } boundsOwner
            && (!lightingMatrices.TryGetValue(boundsOwner, out var boundsMatrix)
                || boundsMatrix != preparation.Plan.BoundsReferenceMatrix);
        if (preparation == null || preparation.Key != key || boundsReferenceChanged)
        {
            long planStartedAt = CapturePerformanceDiagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var plan = SceneObjectDiffuseLightingEvaluator.Prepare(new(world, owner, lightingMatrices,
                drawFlags, 0, 0, key.BoundsCenter, CinematicLights));
            if (CapturePerformanceDiagnostics)
                StatDiffuseLightingPlanMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(planStartedAt).TotalMilliseconds;
            StatDiffuseLightingPreparations++;
            preparation = new(key, plan);
            ownerPlans[drawFlags] = preparation;
        }
        if (!preparation.Plan.HasModeledContribution) return false;
        var cache = geometry.DiffuseLighting.Get(owner, partIndex);
        var mesh = geometry.PartMeshes[partIndex];
        cache.EnsureCapacity(mesh.VertexCount);
        int poseRevision = geometry.Source?.Billboards.Count > 0
            ? _proceduralFrame : owner.Actor?.PoseRevision ?? 0;
        bool unchanged = cache.HasSample
            && cache.Signature == preparation.Plan.Signature && cache.PoseRevision == poseRevision
            && cache.AppearanceRevision == owner.AppearanceRevision
            && cache.RliDestination == rliDestination && cache.RliFactor == rliFactor;
        if (!unchanged)
        {
            long evaluationStartedAt = CapturePerformanceDiagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
            {
                int offset = vertex * 3;
                cache.Positions[vertex] = TransformData.FromRender(new(mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]));
                cache.Normals[vertex] = TransformData.FromRender(new(mesh.Normals[offset], mesh.Normals[offset + 1], mesh.Normals[offset + 2]));
                int colorOffset = vertex * 4;
                cache.RawPaint[vertex] = mesh.Colors[colorOffset] | (uint)mesh.Colors[colorOffset + 1] << 8
                    | (uint)mesh.Colors[colorOffset + 2] << 16 | (uint)mesh.Colors[colorOffset + 3] << 24;
            }
            SceneObjectDiffuseLightingEvaluator.Evaluate(preparation.Plan, cache.Positions, cache.Normals,
                cache.RawPaint, rliDestination, rliFactor, Vector4.Zero, cache.ComputedColors);
            if (CapturePerformanceDiagnostics)
                StatDiffuseLightingEvaluationMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(evaluationStartedAt).TotalMilliseconds;
            StatDiffuseLightingUpdates++;
            cache.Signature = preparation.Plan.Signature;
            cache.PoseRevision = poseRevision;
            cache.AppearanceRevision = owner.AppearanceRevision;
            cache.RliDestination = rliDestination;
            cache.RliFactor = rliFactor;
            cache.HasSample = true;
            cache.Revision++;
        }
        if (geometry.DiffuseLighting.NeedsUpload(partIndex, cache))
        {
            long uploadStartedAt = CapturePerformanceDiagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
                for (int channel = 0; channel < 4; channel++)
                    mesh.Tangents[vertex * 4 + channel] = (cache.ComputedColors[vertex] >> (channel * 8) & 255) / 255f;
            Raylib_cs.Raylib.UpdateMeshBuffer(mesh, 4, mesh.Tangents, mesh.VertexCount * 16, 0);
            if (CapturePerformanceDiagnostics)
                StatDiffuseLightingUploadMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(uploadStartedAt).TotalMilliseconds;
            geometry.DiffuseLighting.MarkUploaded(partIndex, cache);
        }
        return true;
    }
}
