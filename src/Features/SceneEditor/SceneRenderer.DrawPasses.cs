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
    /// <summary>
    /// Отрисовка сцены с явной передачей документа, активного мира и резолвера (интеграция с EditorShell/UI).
    /// </summary>
    public void Render(int width, int height, SceneDocument? document, WorldData? world, SceneObject? selected, ResourceResolver? resolver)
    {
        Document = document;
        ActiveWorld = world;
        SelectedObject = selected;
        Resolver = resolver;
        Render(width, height);
    }

    /// <summary>
    /// Отрисовка всей 3D-сцены в RenderTexture.
    /// </summary>
    public unsafe void Render(int width, int height)
    {
        long renderStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _skinUpdatesThisFrame = 0;
        ResetTextureParameterStatistics();
        StatShaderUniformRequests = 0;
        StatShaderUniformUploads = 0;
        StatGeometryUploadsThisFrame = 0;
        StatGeometryCacheMissMilliseconds = 0;
        GeometryUploadKeysThisFrame.Clear();
        _proceduralFrame++;
        ResetResourceDiagnostics();
        EnsureGpuInitialized();

        width = Math.Max(16, width);
        height = Math.Max(16, height);
        _viewportAspect = width / (float)height;
        ApplyCinematicProjection(width, height);
        UpdateFrustumBasis();
        long videoStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateProceduralVideoTextures(CinematicVideoTime ?? Raylib.GetTime());
        StatProceduralTextureMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(videoStartedAt).TotalMilliseconds;
        long targetStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_sceneTarget.Id == 0 || _sceneTarget.Texture.Width != width || _sceneTarget.Texture.Height != height)
        {
            if (_sceneTarget.Id != 0) Raylib.UnloadRenderTexture(_sceneTarget);
            _sceneTarget = Raylib.LoadRenderTexture(width, height);
        }

        Raylib.BeginTextureMode(_sceneTarget);
        Raylib.ClearBackground(new Color(18, 23, 20, 255));
        StatRenderTargetMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(targetStartedAt).TotalMilliseconds;

        // Flying/scrolling makes Target one unit from Position. That is a look direction,
        // not the distance to the scene: deriving farClip from it made large maps disappear.
        var world = ActiveWorld ?? Document?.ActiveWorld;
        StatWorldActorUpdates = 0;
        StatWorldActorUpdateMilliseconds = 0;
        StatWorldActorUpdateAllocatedBytes = 0;
        StatDeferredGeometryObjects = 0;
        StatGeometryPreparationAllocatedBytes = 0;
        StatDrawPassAllocatedBytes = 0;
        StatGeometryPreparationMilliseconds = 0;
        StatDrawPassMilliseconds = 0;
        StatDrawPacketSortMilliseconds = 0;
        StatSkinningMilliseconds = 0;
        StatSkinningAllocatedBytes = 0;
        if (CapturePerformanceDiagnostics) PerformanceOwnerWork.Clear();
        if (world != null)
        {
            long actorUpdateStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            long actorUpdateAllocatedBefore = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() : 0;
            float actorFrameDelta = Raylib.GetFrameTime();
            foreach (var actor in world.Actors)
            {
                if (!ShouldEvaluateWorldActor(actor, ShowGameHidden)) continue;
                actor.Evaluate(actorFrameDelta);
                StatWorldActorUpdates++;
            }
            StatWorldActorUpdateMilliseconds = (float)System.Diagnostics.Stopwatch
                .GetElapsedTime(actorUpdateStartedAt).TotalMilliseconds;
            StatWorldActorUpdateAllocatedBytes = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() - actorUpdateAllocatedBefore : 0;
        }
        long lightingStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        BeginLightingFrame(world);
        StatLightingPreparationMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(lightingStartedAt).TotalMilliseconds;
        long clipStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _farClip = 2000;
        if (world != null)
        {
            foreach (var obj in world.Objects)
            {
                if (IsHidden(obj) || IsGameHidden(obj)
                    || !TryGetGeometryBounds(obj.RenderGeometryKey, out var localBounds)) continue;
                var (min, max) = TransformBoundingBox(localBounds, obj.RenderMatrix);
                _farClip = Math.Max(_farClip, ClipDistanceForBounds(Camera.Position, min, max));
            }
        }
        Rlgl.SetClipPlanes(.05, _farClip);
        StatClipPreparationMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(clipStartedAt).TotalMilliseconds;
        Raylib.BeginMode3D(Camera);
        Rlgl.EnableDepthTest();
        Rlgl.EnableDepthMask();
        SetDepthFunction(515);
        Rlgl.DisableBackfaceCulling();

        if (ShowGrid && !GameView)
        {
            Raylib.DrawGrid(50, 2.0f);
            // Оси мира у начала координат
            Raylib.DrawLine3D(Vector3.Zero, Vector3.UnitX * 3.0f, Color.Red);
            Raylib.DrawLine3D(Vector3.Zero, Vector3.UnitY * 3.0f, Color.Green);
            Raylib.DrawLine3D(Vector3.Zero, Vector3.UnitZ * 3.0f, Color.Blue);
        }

        StatTotalObjects = world?.Objects.Count ?? 0;
        StatVisibleObjects = 0;
        StatDrawnTriangles = 0;

        if (world != null)
        {
            // Keep dependent passes adjacent (destination-alpha masks), but sort individual
            // material parts, not entire GAOs containing both walls and transparent effects.
            long geometryPreparationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            long geometryPreparationAllocatedBefore = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() : 0;
            var packets = _drawPackets;
            packets.Clear();
            var forward = Vector3.Normalize(Camera.Target - Camera.Position);
            foreach (var obj in world.Objects)
            {
                if (obj.GeometryKey == 0 || IsHidden(obj) || IsGameHidden(obj) || IsCollisionHidden(obj)) continue;
                Geometry? sourceGeometry = ResolveGeometrySource(obj.RenderGeometryKey);
                bool isStaticGeometry = CanCullBeforeGpuUpload(obj.Actor != null,
                    sourceGeometry is { Billboards.Count: > 0 }, sourceGeometry?.Particles != null,
                    obj.CinematicParticles != null);
                if (isStaticGeometry && FrustumCullingEnabled
                    && TryGetGeometryBounds(obj.RenderGeometryKey, out var sourceBounds))
                {
                    var (sourceMinimum, sourceMaximum) = TransformBoundingBox(sourceBounds, obj.RenderMatrix);
                    float sourceRadius = (sourceMaximum - sourceMinimum).Length() * .5f;
                    if (!IsSphereInFrustum((sourceMinimum + sourceMaximum) * .5f, sourceRadius)) continue;
                }
                bool selectedObjectOrActor = SelectedObject == obj
                    || SelectedObject?.Actor != null && SelectedObject.Actor == obj.Actor;
                double geometryPreparationElapsedMilliseconds = System.Diagnostics.Stopwatch
                    .GetElapsedTime(geometryPreparationStartedAt).TotalMilliseconds;
                bool shouldDeferGeometryUpload = ShouldDeferGeometryUpload(
                    _geometryCache.ContainsKey(obj.RenderGeometryKey), selectedObjectOrActor,
                    StatGeometryUploadsThisFrame, geometryPreparationElapsedMilliseconds,
                    GeometryUploadFrameBudgetMilliseconds);
                if (shouldDeferGeometryUpload)
                {
                    StatDeferredGeometryObjects++;
                    continue;
                }
                var geo = GetOrUploadGeometryForOwner(obj); if (geo == null) continue;
                bool geometryDeformsThisFrame = obj.Actor != null
                    || obj.Modifiers.Any(modifier => modifier.Type == 4)
                    || geo.Source is { Billboards.Count: > 0 }
                    || geo.Source?.Particles != null
                    || obj.CinematicParticles != null;
                bool canCullBeforeSkinning = obj.Actor != null && ReferenceEquals(geo.DeformationOwner, obj);
                if (geometryDeformsThisFrame && !canCullBeforeSkinning) UpdateSkin(geo, obj);
                var (low, high) = TransformBoundingBox(geo.LocalBounds, obj.RenderMatrix);
                float boundsRadius = (high - low).Length() * .5f;
                float cullingRadius = canCullBeforeSkinning ? boundsRadius * 1.5f : boundsRadius;
                bool outsideFrustum = FrustumCullingEnabled
                    && !IsSphereInFrustum((low + high) * .5f, cullingRadius);
                if (outsideFrustum) continue;

                if (canCullBeforeSkinning) UpdateSkin(geo, obj);
                else if (!geometryDeformsThisFrame) UpdateSkin(geo, obj);

                if (canCullBeforeSkinning && FrustumCullingEnabled)
                {
                    (low, high) = TransformBoundingBox(geo.LocalBounds, obj.RenderMatrix);
                    if (!IsSphereInFrustum((low + high) * .5f, (high - low).Length() * .5f)) continue;
                }
                var materials = IsCollision(obj) ? null : GetOrUploadMaterialSet(obj.RenderGeometryKey, obj.MaterialKey);
                StatVisibleObjects++;
                for (int part = 0; part < geo.PartMeshes.Count; part++)
                {
                    if (!IsCollision(obj) && (materials == null || part >= materials.Passes.Count)) continue;
                    var passes = materials?.Passes[part];
                    var particleDefinition = obj.CinematicParticles ?? geo.Source?.Particles;
                    bool particlePass = particleDefinition != null && part >= geo.Source!.ParticlePart;
                    bool particleRequestsTransparentPass = particlePass && particleDefinition!.IsTransparent;
                    bool transparent = !IsCollision(obj) && !(Wireframe && !GameView) &&
                        (particleRequestsTransparentPass || passes != null &&
                            !HasOpaqueMaterialPass(obj, passes));
                    var center = Vector3.Transform(geo.PartLocalBounds[part].Min
                        + (geo.PartLocalBounds[part].Max - geo.PartLocalBounds[part].Min) * .5f, obj.RenderMatrix);
                    packets.Add(new(obj, geo, materials, part, transparent,
                        Vector3.Dot(center - Camera.Position, forward), packets.Count));
                }
            }
            StatGeometryPreparationMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(geometryPreparationStartedAt).TotalMilliseconds;
            long drawPassStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            StatGeometryPreparationAllocatedBytes = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() - geometryPreparationAllocatedBefore : 0;
            long drawPassAllocatedBefore = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long packetSortStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            packets.Sort(DrawPacketComparison);
            StatDrawPacketSortMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(packetSortStartedAt).TotalMilliseconds;
            foreach (var packet in packets)
            {
                var obj = packet.Obj; var geo = packet.Geo; int i = packet.Part;
                UpdateSkin(geo, obj); UpdateInstanceAlpha(geo, obj);
                var mesh = geo.PartMeshes[i]; var worldMatrix = obj.RenderMatrix;
                if (IsCollision(obj) || Wireframe && !GameView)
                {
                    Raylib.BeginBlendMode(BlendMode.Alpha);
                    Rlgl.EnableDepthTest(); Rlgl.EnableDepthMask(); SetDepthFunction(515);
                    Rlgl.EnableWireMode();
                    Raylib.DrawMesh(mesh, IsCollision(obj) ? _collisionWireMaterial : _selectionWireMaterial, Matrix4x4.Transpose(worldMatrix));
                    Rlgl.DisableWireMode(); Raylib.EndBlendMode();
                }
                else foreach (var pass in packet.Materials!.Passes[i])
                {
                    var materialPasses = packet.Materials.Passes[i];
                    CinematicRliDrawState rliState = CinematicRliDrawState.Resolve(
                        CinematicRliColor, CinematicRliFactor, obj.PreviewRliDrawState);
                    var layer = EffectiveLayer(obj, pass);
                    if (layer.Disabled) continue;
                    SetUvSpace(obj, layer);
                    JadeUniform("uvMode", layer.UvMode);
                    JadeUniform("uvCustom", (int)layer.Custom);
                    float materialTime = CinematicMaterials?.Time ?? (float)Raylib.GetTime();
                    JadeUniform("uvMatrix", layer.UvMatrix(materialTime, out var uvOffset));
                    JadeUniform("uvOffset", uvOffset);
                    JadeUniform("colorMode", layer.ColorMode);
                    bool spritePart = geo.Source is { } sourceGeometry && sourceGeometry.Parts[i].VertexIndices.Length > 0
                        && sourceGeometry.IsBillboardVertex(sourceGeometry.Parts[i].VertexIndices[0]);
                    // Jaded forces UseRLI for sprite primitives before selecting color inputs.
                    uint effectiveDrawFlags = obj.EffectiveDrawFlags | (spritePart ? 0x40u : 0u);
                    if (obj.PreviewRliDrawState is not null)
                        effectiveDrawFlags = CinematicRliDrawState.ApplyRliFactorUsage(
                            effectiveDrawFlags, rliState.FactorIsUsed);
                    bool particlePart = (obj.CinematicParticles ?? geo.Source?.Particles) != null
                        && i >= geo.Source!.ParticlePart;
                    if (particlePart) effectiveDrawFlags &= ~0x08000100u; // PAG clears Lighted and UseAmbient.
                    JadeUniform("objectDrawFlags", unchecked((int)effectiveDrawFlags));
                    bool computedLighting = !particlePart && layer.ColorMode is 0 or 6
                        && UpdateDiffuseLighting(world, obj, geo, i, effectiveDrawFlags,
                            rliState.DestinationColor, rliState.Factor);
                    JadeUniform("computedLighting", computedLighting ? 1 : 0);
                    bool hasAmbient = TryGetWorldAmbient(world, effectiveDrawFlags, out Vector4 ambient);
                    JadeUniform("ambientColor", ambient);
                    bool hasAuthoredPointColors = geo.Source?.VertexColors.Length > 0;
                    bool usesPaint = computedLighting || spritePart || hasAuthoredPointColors || particlePart || hasAmbient
                        || obj.RliColors.Length > 0 && layer.Blend is not (0 or 7);
                    JadeUniform("effectColor", usesPaint ? 1 : 0);
                    JadeMaterialLayer? nextLayer = pass.LayerIndex + 1 < materialPasses.Count
                        ? materialPasses[pass.LayerIndex + 1].Layer : null;
                    bool materialUsesRliAlpha = MaterialRliAlphaPolicy.UsesRliAlpha(layer, nextLayer);
                    JadeUniform("useRliAlpha", materialUsesRliAlpha
                        && (obj.RliColors.Length > 0 || spritePart) ? 1 : 0);
                    JadeUniform("rliDestColor", rliState.DestinationColor);
                    JadeUniform("rliFactor", rliState.Factor);
                    bool foggedObject = (obj.EffectiveDrawFlags & 0x4000) != 0;
                    JadeUniform("fogEnabled", foggedObject && CinematicFogFar > CinematicFogNear ? 1 : 0);
                    JadeUniform("fogColor", FogColorForBlend(layer.Blend));
                    JadeUniform("fogNear", CinematicFogNear);
                    JadeUniform("fogFar", CinematicFogFar);
                    var constant = CinematicMaterials?.ApplyConstant(pass.MaterialKey, obj.GaoKey,
                        pass.SubMaterialIndex, pass.Constant)
                        ?? pass.Constant;
                    JadeUniform("constantColor", layer.ColorMode == 4 ? Vector4.One : constant);
                    JadeUniform("alphaTest", (layer.Flags & 0x10) == 0 ? -1 : (layer.Flags & 0x80) == 0 ? 5 : 2);
                    JadeUniform("alphaRef", (layer.Flags >> 22 & 0xfc) / 255f);
                    JadeUniform("alphaOverride", (layer.Flags & 0x400) != 0 ? 1 : 0);
                    JadeUniform("overrideValue", layer.OverrideAlpha);
                    JadeUniform("materialOpacity", layer.Opacity);
                    JadeUniform("additiveLocalAlpha", layer.Blend == 5
                        && (layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0 ? 1 : 0);
                    var texture = pass.Material.Maps[(int)MaterialMapIndex.Albedo].Texture;
                    SetTextureParameter(texture, 10242, (layer.Flags & 1) == 0 ? 33071 : 10497);
                    SetTextureParameter(texture, 10243, (layer.Flags & 2) == 0 ? 33071 : 10497);
                    SetTextureParameter(texture, 10240, (layer.Flags & 4) == 0 ? 9728 : 9729);
                    SetTextureParameter(texture, 10241, (layer.Flags & 4) == 0 ? 9728 : 9729);
                    bool inverse = (layer.Flags & 0x80) != 0;
                    var factors = layer.Blend switch
                    {
                        1 => inverse ? (771, 770) : (770, 771),
                        2 => inverse ? (1, 770) : (1, 771),
                        3 => inverse ? (773, 772) : (772, 773),
                        4 => inverse ? (773, 1) : (772, 1),
                        5 => (1, 1),
                        6 or 8 => (0, 769),
                        _ => (1, 0)
                    };
                    Rlgl.SetBlendFactors(factors.Item1, factors.Item2, 32774);
                    Raylib.BeginBlendMode(BlendMode.Custom);
                    Rlgl.EnableDepthTest();
                    SetDepthFunction(layer.DepthEqual ? 514u : 515u);
                    Rlgl.ColorMask(layer.WriteRgb, layer.WriteRgb, layer.WriteRgb, layer.WriteAlpha);
                    if (layer.DepthWrite) Rlgl.EnableDepthMask(); else Rlgl.DisableDepthMask();
                    Raylib.DrawMesh(mesh, pass.Material, Matrix4x4.Transpose(worldMatrix));
                    Rlgl.EnableDepthMask();
                    Rlgl.ColorMask(true, true, true, true);
                    SetDepthFunction(515);
                    Raylib.EndBlendMode();

                }
                StatDrawnTriangles += mesh.TriangleCount;
            }
            StatDrawPassMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(drawPassStartedAt).TotalMilliseconds;
            StatDrawPassAllocatedBytes = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() - drawPassAllocatedBefore : 0;
        }

        long overlayStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawCinematicFlashes();

        // Подсветка выбранного объекта: Bounding Box и Selection Wireframe
        foreach (var selectedPart in SelectedObject?.Actor is { } selectedActor && SelectedObject == selectedActor.Root
            ? selectedActor.Members : SelectedObject != null ? new List<SceneObject> { SelectedObject } : [])
        {
            if (!GameView && !IsHidden(selectedPart) && !IsGameHidden(selectedPart) && !IsCollisionHidden(selectedPart) && _geometryCache.TryGetValue(selectedPart.GeometryKey, out var selGeo))
            {
                UpdateSkin(selGeo, selectedPart);
                var selMatrix = selectedPart.RenderMatrix;

                // Keep the selection outline independent from the animated pose and bounds.
                if (ShowSelectionWireframe)
                {
                    Rlgl.EnableWireMode();
                    foreach (var mesh in selGeo.PartMeshes)
                    {
                        Raylib.DrawMesh(mesh, IsCollision(selectedPart) ? _collisionWireMaterial : _selectionWireMaterial, Matrix4x4.Transpose(selMatrix));
                    }
                    Rlgl.DisableWireMode();
                }

                // 2. Отрисовка ориентированного Bounding Box
                DrawOrientedBoundingBox(selGeo.LocalBounds, selMatrix, IsCollision(selectedPart) ? new Color(65, 155, 255, 255) : new Color(80, 255, 150, 255));
            }
        }

        // Отрисовка 3D Transform Gizmo поверх геометрии
        if (SelectedObject != null && !IsHidden(SelectedObject) && !IsCollisionHidden(SelectedObject) && ShowGizmo && !GameView)
        {
            if (GizmoMode == TransformGizmoMode.Translate) DrawTransformGizmo(SelectedObject);
        }

        DrawCinematicLightnings();

        Rlgl.EnableBackfaceCulling();
        StatOverlayMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(overlayStartedAt).TotalMilliseconds;
        long finalizeStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        Raylib.EndMode3D();
        DrawCinematicFade(width, height);
        DrawCinematicShutter(width, height);
        // Destination alpha is used between Jade material passes, but the editor
        // presents an opaque viewport. Do not let ImGui composite it a second time.
        Rlgl.DrawRenderBatchActive();
        Rlgl.ColorMask(false, false, false, true);
        Raylib.ClearBackground(new Color(0, 0, 0, 255));
        Rlgl.ColorMask(true, true, true, true);
        Raylib.EndTextureMode();
        StatRenderFinalizeMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(finalizeStartedAt).TotalMilliseconds;
        StatSkinUpdates = _skinUpdatesThisFrame;
        StatRenderMilliseconds = (float)(System.Diagnostics.Stopwatch.GetElapsedTime(renderStartedAt).TotalMilliseconds);
    }

    private bool HasOpaqueMaterialPass(SceneObject sceneObject, IReadOnlyList<GpuMaterialPass> passes)
    {
        foreach (var pass in passes)
        {
            var layer = EffectiveLayer(sceneObject, pass);
            if (!layer.Disabled && layer.WriteRgb && layer.Blend is 0 or 7) return true;
        }
        return false;
    }

    private Vector4 FogColorForBlend(int blend) => blend is 5 or 6 or 8
        ? Vector4.Zero
        : CinematicFogColor;

    internal static bool ShouldEvaluateWorldActor(SceneActor actor, bool showHiddenObjects) =>
        !actor.CinematicClockOwned && (showHiddenObjects || !actor.Root.IsControlHidden);

    internal static bool CanCullBeforeGpuUpload(bool isActor, bool hasBillboards,
        bool hasParticles, bool hasCinematicParticles) =>
        !isActor && !hasBillboards && !hasParticles && !hasCinematicParticles;

    internal static bool ShouldDeferGeometryUpload(bool geometryCached, bool isSelected,
        int uploadsThisFrame, double elapsedMilliseconds, double budgetMilliseconds) =>
        !geometryCached && !isSelected && uploadsThisFrame > 0
        && elapsedMilliseconds >= budgetMilliseconds;

}
