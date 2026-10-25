using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private unsafe void UpdateInstanceAlpha(GpuGeometry geo, SceneObject obj)
    {
        if (geo.Source?.Particles != null) return;
        if (geo.Source == null || geo.AlphaOwner == obj && geo.AlphaRevision == obj.AppearanceRevision || (geo.AlphaOwner == null && obj.RliColors.Length == 0)) return;
        for (int part = 0; part < geo.PartMeshes.Count; part++)
        {
            var mesh = geo.PartMeshes[part];
            var indices = geo.Source.Parts[part].VertexIndices;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int index = indices[i];
                uint color = GeometryVertexColor.PackedColor(obj, geo.Source, index);
                mesh.TexCoords2[i * 2] = GeometryVertexColor.InstanceAlpha(obj, geo.Source, index, color);
                mesh.Colors[i * 4] = (byte)color;
                mesh.Colors[i * 4 + 1] = (byte)(color >> 8);
                mesh.Colors[i * 4 + 2] = (byte)(color >> 16);
                mesh.Colors[i * 4 + 3] = (byte)(color >> 24);
            }
            Raylib.UpdateMeshBuffer(mesh, 5, mesh.TexCoords2, mesh.VertexCount * 8, 0);
            Raylib.UpdateMeshBuffer(mesh, 3, mesh.Colors, mesh.VertexCount * 4, 0);
        }
        geo.AlphaOwner = obj.RliColors.Length == 0 ? null : obj;
        geo.AlphaRevision = obj.AppearanceRevision;
    }

    private unsafe void UpdateSkin(GpuGeometry geo, SceneObject obj)
    {
        if (!CapturePerformanceDiagnostics && CinematicMaterials == null)
        {
            UpdateSkinCore(geo, obj);
            return;
        }
        long skinStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        long skinAllocatedBefore = CapturePerformanceDiagnostics
            ? GC.GetAllocatedBytesForCurrentThread() : 0;
        try
        {
            UpdateSkinCore(geo, obj);
        }
        finally
        {
            double skinMilliseconds = System.Diagnostics.Stopwatch
                .GetElapsedTime(skinStartedAt).TotalMilliseconds;
            StatSkinningMilliseconds += (float)skinMilliseconds;
            if (CapturePerformanceDiagnostics)
            {
                PerformanceOwnerWork.AddSkin(obj, skinMilliseconds);
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - skinAllocatedBefore;
                StatSkinningAllocatedBytes += allocatedBytes;
                if (CaptureSkinAllocationDiagnostics && allocatedBytes > 0)
                {
                    string allocationKey = $"{obj.GaoKey:X8} {obj.Name}";
                    skinAllocationTotals[allocationKey] = skinAllocationTotals.GetValueOrDefault(allocationKey) + allocatedBytes;
                }
            }
        }
    }

    private unsafe void UpdateSkinCore(GpuGeometry geo, SceneObject obj)
    {
        if (geo.Source == null) return;
        bool procedural = geo.Source.Billboards.Count > 0 || geo.Source.Particles != null;
        if (procedural && geo.DeformationOwner == obj && geo.ProceduralFrame == _proceduralFrame && geo.ProceduralMatrix == obj.RenderMatrix && geo.ProceduralCameraPosition == Camera.Position && geo.ProceduralCameraTarget == Camera.Target) return;
        uint[]? particleColors = null;
        FaceMorphPreview? morphPreview = obj.Actor == null && obj.Modifiers.Any(modifier => modifier.Type == 4)
            ? obj.MorphPreview ?? FaceMorphPreview.Get(obj, geo.Source)
            : null;
        int deformationRevision = obj.Actor?.PoseRevision ?? morphPreview?.DeformationRevision ?? 0;
        if (!procedural && geo.DeformationOwner == obj && geo.DeformationRevision == deformationRevision) return;
        var posed = obj.Actor?.Skin(obj, geo.Source);
        if (posed == null && morphPreview != null
            && morphPreview.DeformationRevision != 0)
        {
            var morphPositions = morphPreview.Positions();
            if (!ReferenceEquals(morphPositions, geo.Source.Positions)) posed = morphPositions;
        }
        if (posed == null && geo.DeformationOwner == null && !procedural) return;
        _skinUpdatesThisFrame++;
        var positions = posed ?? geo.Source.Positions;
        if (geo.Source.Billboards.Count > 0)
        {
            var forward = Vector3.Normalize(Camera.Target - Camera.Position);
            var right = Vector3.Normalize(Vector3.Cross(forward, Camera.Up)); var up = Vector3.Normalize(Vector3.Cross(right, forward));
            geo.EnsureDeformedPositionBuffer(positions.Length);
            SpriteGenerator.FillFaceCamera(geo.Source, posed, obj.RenderMatrix, right, up,
                (float)Raylib.GetTime(), geo.DeformedPositionBuffer);
            positions = geo.DeformedPositionBuffer;
        }
        if (geo.Source.Particles is { } definition)
        {
            definition = obj.CinematicParticles ?? definition;
            if (!_particles.TryGetValue(obj, out var state) || !ReferenceEquals(state.Definition, definition))
                _particles[obj] = state = new(definition, obj.GaoKey);
            double particleTime = CinematicMaterials?.Time ?? Raylib.GetTime();
            Geometry? emissionSourceGeometry = null;
            Vector3[]? emissionSourcePositions = null;
            Matrix4x4? emissionSourceMatrix = null;
            if (definition.Shape == 6 && !obj.IsCinematicParticleMatrixReferenceUnresolved
                && obj.CinematicParticleSourceObject is { } sourceObject
                && Resolver?.Geometries.TryGetValue(sourceObject.RenderGeometryKey, out emissionSourceGeometry) == true)
            {
                emissionSourcePositions = sourceObject.Actor?.Skin(sourceObject, emissionSourceGeometry)
                    ?? emissionSourceGeometry.Positions;
                var matrixObject = obj.HasCinematicParticleMatrixOverride
                    ? obj.CinematicParticleMatrixReference : sourceObject;
                emissionSourceMatrix = (matrixObject ?? sourceObject).RenderMatrix;
            }
            var forward = Vector3.Normalize(Camera.Target - Camera.Position); var right = Vector3.Normalize(Vector3.Cross(forward, Camera.Up)); var up = Vector3.Normalize(Vector3.Cross(right, forward));
            (positions, particleColors) = state.Update(geo.Source, positions, obj.PreviewMatrix ?? AnimationMath.Trs(obj.Transform),
                Camera.Position, forward, right, up, particleTime, emissionSourceGeometry,
                emissionSourcePositions, emissionSourceMatrix, obj.CinematicParticleShifts);
            obj.PreviewParticlePositions = positions;
            obj.PreviewParticleColors = particleColors;
            obj.PreviewParticleTime = (float)particleTime;
        }
        bool hasParticleGeometry = particleColors != null && geo.Source.Particles != null;
        int particleStart = hasParticleGeometry
            ? Math.Clamp(geo.Source.ParticleStart, 0, positions.Length)
            : positions.Length;
        int carrierPartCount = hasParticleGeometry
            ? Math.Clamp(geo.Source.ParticlePart, 0, geo.Source.Parts.Count)
            : geo.Source.Parts.Count;
        geo.EnsureNormalBuffers(positions.Length);
        int normalVertexCount = hasParticleGeometry ? particleStart : positions.Length;
        int normalPartCount = hasParticleGeometry ? carrierPartCount : geo.Source.Parts.Count;
        AnimationMath.FillNormals(positions, geo.Source.Parts, normalVertexCount,
            normalPartCount, geo.NormalBuffer, geo.NormalCountsBuffer);
        var normals = geo.NormalBuffer;
        Vector3 particleNormal = Vector3.Zero;
        if (hasParticleGeometry)
        {
            Vector3 cameraForward = Vector3.Normalize(Camera.Target - Camera.Position);
            Vector3 localNormal = Vector3.TransformNormal(cameraForward,
                Matrix4x4.Transpose(obj.RenderMatrix));
            particleNormal = TransformData.FromRender(localNormal);
        }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int p = 0; p < geo.PartMeshes.Count; p++)
        {
            var mesh = geo.PartMeshes[p]; var ids = geo.Source.Parts[p].VertexIndices;
            Vector3 partMin = new(float.MaxValue), partMax = new(float.MinValue);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = Geometry.ToRender(positions[ids[i]]);
                Vector3 sourceNormal = ids[i] < normalVertexCount ? normals[ids[i]] : particleNormal;
                var n = Geometry.ToRender(sourceNormal);
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                partMin = Vector3.Min(partMin, v); partMax = Vector3.Max(partMax, v);
                mesh.Vertices[i * 3] = v.X; mesh.Vertices[i * 3 + 1] = v.Y; mesh.Vertices[i * 3 + 2] = v.Z;
                mesh.Normals[i * 3] = n.X; mesh.Normals[i * 3 + 1] = n.Y; mesh.Normals[i * 3 + 2] = n.Z;
                if (particleColors != null) { uint c = particleColors[ids[i]]; mesh.Colors[i * 4] = (byte)c; mesh.Colors[i * 4 + 1] = (byte)(c >> 8); mesh.Colors[i * 4 + 2] = (byte)(c >> 16); mesh.Colors[i * 4 + 3] = (byte)(c >> 24); }
            }
            Raylib.UpdateMeshBuffer(mesh, 0, mesh.Vertices, mesh.VertexCount * 12, 0);
            Raylib.UpdateMeshBuffer(mesh, 2, mesh.Normals, mesh.VertexCount * 12, 0);
            if (particleColors != null) Raylib.UpdateMeshBuffer(mesh, 3, mesh.Colors, mesh.VertexCount * 4, 0);
            geo.PartLocalBounds[p] = new(partMin, partMax);
        }
        geo.LocalBounds = new(min, max); geo.LocalCenter = (min + max) * .5f; geo.BoundingRadius = (max - min).Length() * .5f;
        geo.DeformationOwner = posed == null && !procedural ? null : obj;
        geo.ProceduralFrame = _proceduralFrame; geo.ProceduralMatrix = obj.RenderMatrix; geo.ProceduralCameraPosition = Camera.Position; geo.ProceduralCameraTarget = Camera.Target;
        geo.DeformationRevision = deformationRevision;
    }

    private void SetUvSpace(SceneObject obj, JadeMaterialLayer layer)
    {
        var jadeWorld = obj.PreviewMatrix ?? AnimationMath.Trs(obj.Transform);
        var basis = new Matrix4x4(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1);
        var view = Matrix4x4.Transpose(Raylib.GetCameraMatrix(Camera));
        var modelView = jadeWorld * basis * view;
        Matrix4x4 planar = Matrix4x4.Identity;
        int space = (layer.Custom >> 6) & 3;
        if (space == 1) planar = jadeWorld;
        if (space == 2) planar = modelView;
        if (space == 3 && obj.ActorNode is { } node)
        {
            int index = (layer.Custom >> 8) & 7;
            if ((obj.Flags & 0x1000000) != 0 && index < node.Links.Length)
            {
                var link = node.Links[index];
                var bone = link.Index == -1 ? obj.Actor?.Root : link.Index >= 0 && obj.Actor != null && link.Index < obj.Actor.Bones.Count ? obj.Actor.Bones[link.Index] : obj.Actor?.Members.FirstOrDefault(o => o.GaoKey == link.Key);
                if (bone != null && Matrix4x4.Invert(jadeWorld, out var invMesh))
                    Matrix4x4.Invert((bone.PreviewMatrix ?? AnimationMath.Trs(bone.Transform)) * invMesh, out planar);
            }
            else if (index < node.Matrices.Length) Matrix4x4.Invert(node.Matrices[index], out planar);
        }
        JadeMatrixUniform("jadeModelView", Matrix4x4.Transpose(modelView));
        JadeMatrixUniform("jadePlanar", planar);
        JadeUniform("planarDistance", 1 / MathF.Tan(Camera.FovY * MathF.PI / 360));
    }

}
