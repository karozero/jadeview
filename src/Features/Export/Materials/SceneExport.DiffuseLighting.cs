using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private readonly Dictionary<(SceneObject Owner, bool Sprite), uint[]> diffuseSnapshots = [];

    private void CaptureDiffuseLighting(WorldData sourceWorld, IReadOnlyList<SceneObject> objects)
    {
        if (!options.DynamicLighting) return;
        // Actor export visits rest/bind poses later. Capture source-time lighting before it
        // changes the actor, keeping both matrices and skinned positions at the chosen pose.
        var contextObjects = sourceWorld.Objects.Concat(objects)
            .Concat(sourceWorld.Actors.SelectMany(actor => actor.Members)).Distinct().ToArray();
        if (!sourceWorld.Objects.Any(owner => (owner.Flags & 0x10) != 0 && owner.Light != null)) return;
        var matrices = contextObjects.ToDictionary(owner => owner, owner =>
        {
            var matrix = owner.PreviewMatrix ?? AnimationMath.Trs(owner.Transform);
            return owner.CinematicZoom is float zoom ? Matrix4x4.CreateScale(zoom) * matrix : matrix;
        });
        foreach (var owner in objects.Distinct())
        {
            if ((owner.DrawFlags & 0x08000000) == 0 || !Included(owner)
                || !resolver.Geometries.TryGetValue(owner.RenderGeometryKey, out var geometry)) continue;
            CaptureOwnerDiffuse(sourceWorld, owner, geometry, matrices, contextObjects);
        }
        if (diffuseSnapshots.Count > 0)
            warnings.Add("Supported dynamic diffuse lighting is baked at the current source pose; animated light changes and specular are not exported.");
    }

    private void CaptureOwnerDiffuse(WorldData world, SceneObject owner, Geometry geometry,
        IReadOnlyDictionary<SceneObject, Matrix4x4> matrices, IReadOnlyList<SceneObject> contextObjects)
    {
        var positions = owner.Actor?.Skin(owner, geometry) ?? geometry.Positions;
        if (positions.Length == 0) return;
        var parts = geometry.Parts.Where((part, index) => geometry.Particles == null || index < geometry.ParticlePart).ToList();
        var normals = AnimationMath.Normals(positions, parts);
        var paint = Enumerable.Range(0, positions.Length)
            .Select(vertex => SourceDiffusePaint(owner, geometry, vertex)).ToArray();
        var minimum = positions.Aggregate(Vector3.Min);
        var maximum = positions.Aggregate(Vector3.Max);
        var spriteModes = parts.Where(part => part.VertexIndices.Length > 0)
            .Where(part => ReadExportLayers(owner, part, ResolvePartMaterial(owner, part))
                .Any(layer => !layer.Disabled && layer.ColorMode is 0 or 6))
            .Select(part => geometry.IsBillboardVertex(part.VertexIndices[0])).Distinct();
        foreach (bool sprite in spriteModes)
        {
            uint flags = owner.DrawFlags | (sprite ? 0x40u : 0u);
            var plan = SceneObjectDiffuseLightingEvaluator.Prepare(new(world, owner, matrices,
                flags, 0, 0, (minimum + maximum) * .5f, options.CinematicLights,
                ReceiverContextObjects: contextObjects));
            foreach (var diagnostic in plan.Diagnostics.Where(diagnostic => diagnostic.IsUnsupported))
                warnings.Add("Diffuse lighting is incomplete: " + diagnostic.Detail);
            if (!plan.HasModeledContribution) continue;
            var colors = new uint[positions.Length];
            var sample = options.CinematicMaterialLayers;
            Vector4 ambient = ExportAmbient(flags, out _);
            SceneObjectDiffuseLightingEvaluator.Evaluate(plan, positions, normals, paint,
                sample?.RliColor ?? Vector4.Zero, sample?.RliFactor ?? 0, ambient, colors);
            diffuseSnapshots[(owner, sprite)] = colors;
        }
    }

    private static uint SourceDiffusePaint(SceneObject owner, Geometry geometry, int vertex)
    {
        if (geometry.Particles == null) return GeometryVertexColor.PackedColor(owner, geometry, vertex);
        return vertex < geometry.VertexColors.Length ? geometry.VertexColors[vertex] : 0;
    }
}
