using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private Vector4? ComputedExportColor(SceneObject owner, Geometry geometry, int vertex,
        JadeMaterialLayer layer, bool spritePart)
    {
        uint drawFlags = owner.DrawFlags | (spritePart ? 0x40u : 0u);
        var sourceGeometry = resolver.Geometries.GetValueOrDefault(owner.RenderGeometryKey);
        bool particleVertex = sourceGeometry?.Particles != null && vertex >= sourceGeometry.ParticleStart;
        if (particleVertex) drawFlags &= ~0x08000100u;
        Vector4 ambient = ExportAmbient(drawFlags, out bool hasAmbient);
        uint[]? dynamicColors = null;
        bool hasDynamic = !particleVertex && diffuseSnapshots.TryGetValue((owner, spritePart), out dynamicColors)
            && vertex < dynamicColors.Length;
        bool usesPaint = hasDynamic || spritePart || particleVertex || geometry.VertexColors.Length > 0 || hasAmbient
            || owner.RliColors.Length > 0 && layer.Blend is not (0 or 7);
        if (!usesPaint) return null;
        uint paint = particleVertex && vertex < geometry.VertexColors.Length
            ? geometry.VertexColors[vertex] : GeometryVertexColor.PackedColor(owner, geometry, vertex);
        var sample = options.CinematicMaterialLayers;
        uint packed = hasDynamic ? dynamicColors![vertex]
            : ComputedVertexColorEvaluator.Evaluate(new(paint, drawFlags,
                sample?.RliColor ?? Vector4.Zero, sample?.RliFactor ?? 0, ambient)).ComputedPackedColor;
        var color = new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, packed >> 24) / 255f;
        if (layer.ColorMode == 6)
        {
            color.X = Math.Min(color.X * 2, 1);
            color.Y = Math.Min(color.Y * 2, 1);
            color.Z = Math.Min(color.Z * 2, 1);
        }
        if (layer.Blend != 0 && (owner.RliColors.Length > 0 || spritePart))
            color.W = GeometryVertexColor.InstanceAlpha(owner, geometry, vertex, paint);
        return color;
    }

    private Vector4 ExportAmbient(uint drawFlags, out bool hasAmbient)
    {
        hasAmbient = (drawFlags & 0x100) != 0
            && (options.CinematicMaterialLayers?.HasAmbient == true || worldLighting != null);
        if (!hasAmbient) return Vector4.Zero;
        bool firstChannel = (drawFlags & 0x00400000) != 0;
        if (options.CinematicMaterialLayers is { HasAmbient: true } sample)
            return firstChannel ? sample.AmbientColor : sample.AmbientColor2;
        uint packed = firstChannel ? worldLighting!.AmbientColor : worldLighting!.EffectiveAmbientColor2;
        return new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, packed >> 24) / 255f;
    }
}
