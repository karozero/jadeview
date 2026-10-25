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
    private unsafe void EnsureGpuInitialized()
    {
        if (_resourcesInitialized) return;
        long shaderInitializationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // 1. Создаем детерминированную шахматную текстуру для неразрешенных связей
        byte[] checkerTga = FallbackGenerator.CreateCheckerboardTga(64, 64, 0xFFFF00FF, 0xFF151515);
        var asset = new TextureAsset("FALLBACK_CHECKER", 64, 64, checkerTga, 0, 0x1001, null);
        byte[] rgba = Preview.DecodeTga(asset);
        fixed (byte* p = rgba)
        {
            Image img = new()
            {
                Data = p,
                Width = 64,
                Height = 64,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
            };
            _fallbackCheckerTexture = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(_fallbackCheckerTexture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(_fallbackCheckerTexture, TextureWrap.Repeat);
        }

        _wireShader = Raylib.LoadShaderFromMemory(null, """
            #version 330
            uniform vec4 colDiffuse;
            out vec4 finalColor;
            void main() { finalColor = vec4(colDiffuse.rgb, 1.0); }
            """);
        // 2. Материал подсветки выделения
        _selectionWireMaterial = Raylib.LoadMaterialDefault();
        _selectionWireMaterial.Shader = _wireShader;
        _selectionWireMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = new Color(75, 255, 140, 255);

        _collisionWireMaterial = Raylib.LoadMaterialDefault();
        _collisionWireMaterial.Shader = _wireShader;
        _collisionWireMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = new Color(65, 155, 255, 255);
        _selectionFillMaterial = Raylib.LoadMaterialDefault();
        _selectionFillMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = new Color(50, 220, 110, 60);

        _jadeShader = Raylib.LoadShaderFromMemory(JadeVertexShader, JadeFragmentShader);
        ResetJadeUniformCache();
        foreach (string name in new[] { "uvMode", "uvCustom", "uvMatrix", "uvOffset", "colorMode", "constantColor", "alphaTest", "alphaRef", "alphaOverride", "overrideValue", "materialOpacity", "additiveLocalAlpha", "useRliAlpha", "effectColor", "jadeModelView", "jadePlanar", "planarDistance", "objectDrawFlags", "rliDestColor", "rliFactor", "ambientColor", "computedLighting", "fogEnabled", "fogColor", "fogNear", "fogFar" })
            _jadeUniforms[name] = Raylib.GetShaderLocation(_jadeShader, name);
        _resourcesInitialized = true;
        TotalShaderInitializationMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(shaderInitializationStartedAt).TotalMilliseconds;
    }

    /// <summary>
    /// Полный сброс и освобождение GPU-ресурсов (меши, текстуры, материалы) при смене сцены.
    /// </summary>
    public void ClearGpuCache()
    {
        textureParameterCache.Clear();
        ClearProceduralVideoTextures();
        _hiddenObjects.Clear();
        foreach (var geo in _geometryCache.Values)
        {
            geo.Dispose();
        }
        _geometryCache.Clear();
        _geometryBoundsCache.Clear();
        _particles.Clear();

        foreach (var matSet in _materialCache.Values)
        {
            matSet.Dispose();
        }
        _materialCache.Clear();

        foreach (var tex in _textureCache.Values)
        {
            Raylib.UnloadTexture(tex);
        }
        _textureCache.Clear();

        if (_sceneTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(_sceneTarget);
            _sceneTarget = default;
        }
        if (cinematicAfterEffectsTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(cinematicAfterEffectsTarget);
            cinematicAfterEffectsTarget = default;
        }

        SelectedObject = null;
        HoveredAxis = GizmoAxis.None;
        ActiveAxis = GizmoAxis.None;
        IsDraggingGizmo = false;
        ReleaseCameraMouse();
    }

    /// <summary>
    /// Получение или загрузка в GPU текстуры по ее ресурсному представлению.
    /// </summary>
    public unsafe Texture2D GetOrUploadTexture(TextureAsset? asset)
    {
        EnsureGpuInitialized();
        if (asset == null) return _fallbackCheckerTexture;

        if (_textureCache.TryGetValue(asset.Name, out var existing))
        {
            return existing;
        }

        long decodeStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        byte[] rgba = Preview.DecodeTga(asset);
        StatTextureDecodeMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(decodeStartedAt).TotalMilliseconds;
        if (rgba.Length == 0) return _fallbackCheckerTexture;

        fixed (byte* p = rgba)
        {
            Image img = new()
            {
                Data = p,
                Width = asset.Width,
                Height = asset.Height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
            };
            long uploadStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            Texture2D gpuTex = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(gpuTex, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(gpuTex, TextureWrap.Repeat);
            _textureCache[asset.Name] = gpuTex;
            StatTextureUploadMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(uploadStartedAt).TotalMilliseconds;
            StatTextureUploadsThisFrame++;
            return gpuTex;
        }
    }

    /// <summary>
    /// Совместимый метод получения текстуры для ResolvedTexture (интеграция с UI/A5).
    /// </summary>
    public Texture2D GetOrCreateGpuTexture(ResolvedTexture? tex)
    {
        if (tex == null) return _fallbackCheckerTexture;
        return GetOrUploadTexture(tex.ToTextureAsset());
    }

    /// <summary>
    /// Совместимый метод получения текстуры для TextureAsset.
    /// </summary>
    public Texture2D GetOrCreateGpuTexture(TextureAsset? tex)
    {
        return GetOrUploadTexture(tex);
    }

    /// <summary>
    /// Получение или загрузка в VRAM мешей для указанной геометрии GRO.
    /// </summary>
    public unsafe GpuGeometry? GetOrUploadGeometry(uint geoKey)
    {
        if (geoKey == 0 || geoKey == uint.MaxValue) return null;
        if (_geometryCache.TryGetValue(geoKey, out var cached)) return cached;
        long cacheMissStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        Geometry? geom = ResolveGeometrySource(geoKey);

        if (geom == null || geom.Positions.Length == 0 || geom.Parts.Count == 0)
        {
            return null;
        }

        var gpuGeo = new GpuGeometry { Key = geoKey, Source = geom };
        var smoothNormals = AnimationMath.Normals(geom.Positions, geom.Parts);
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        for (int pIdx = 0; pIdx < geom.Parts.Count; pIdx++)
        {
            var part = geom.Parts[pIdx];
            int count = part.VertexIndices.Length;
            if (count < 3) continue;

            Vector3 partMin = new(float.MaxValue);
            Vector3 partMax = new(float.MinValue);

            Mesh mesh = new()
            {
                VertexCount = count,
                TriangleCount = count / 3
            };
            mesh.Vertices = (float*)Raylib.MemAlloc((uint)(count * 12));
            mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(count * 8));
            mesh.TexCoords2 = (float*)Raylib.MemAlloc((uint)(count * 8));
            mesh.Normals = (float*)Raylib.MemAlloc((uint)(count * 12));
            mesh.Tangents = (float*)Raylib.MemAlloc((uint)(count * 16));
            mesh.Colors = (byte*)Raylib.MemAlloc((uint)(count * 4));

            for (int i = 0; i < count; i += 3)
            {
                var v0 = Geometry.ToRender(geom.Positions[part.VertexIndices[i]]);
                var v1 = Geometry.ToRender(geom.Positions[part.VertexIndices[i + 1]]);
                var v2 = Geometry.ToRender(geom.Positions[part.VertexIndices[i + 2]]);

                var cross = Vector3.Cross(v1 - v0, v2 - v0);
                var normal = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.UnitY;

                for (int j = 0; j < 3; j++)
                {
                    int k = i + j;
                    var v = Geometry.ToRender(geom.Positions[part.VertexIndices[k]]);

                    min = Vector3.Min(min, v);
                    max = Vector3.Max(max, v);
                    partMin = Vector3.Min(partMin, v);
                    partMax = Vector3.Max(partMax, v);

                    mesh.Vertices[k * 3 + 0] = v.X;
                    mesh.Vertices[k * 3 + 1] = v.Y;
                    mesh.Vertices[k * 3 + 2] = v.Z;

                    normal = Geometry.ToRender(smoothNormals[part.VertexIndices[k]]);
                    mesh.Normals[k * 3 + 0] = normal.X;
                    mesh.Normals[k * 3 + 1] = normal.Y;
                    mesh.Normals[k * 3 + 2] = normal.Z;
                    for (int channel = 0; channel < 4; channel++) mesh.Tangents[k * 4 + channel] = 0;

                    var uv = (geom.Uvs.Length > 0 && part.UvIndices.Length > k) ? geom.Uvs[part.UvIndices[k]] : Vector2.Zero;
                    int sourceVertex = part.VertexIndices[k];
                    mesh.TexCoords2[k * 2] = geom.IsBillboardVertex(sourceVertex) && sourceVertex < geom.VertexColors.Length
                        ? (geom.VertexColors[sourceVertex] >> 24) / 255f : 1;
                    mesh.TexCoords2[k * 2 + 1] = 0;
                    mesh.TexCoords[k * 2 + 0] = uv.X;
                    // DecodeTga returns top-down rows; Jade's mesh V uses the opposite origin.
                    // Without this conversion atlas regions (e.g. the hangar workbench)
                    // sample unrelated details from the other half of the same texture.
                    mesh.TexCoords[k * 2 + 1] = 1f - uv.Y;

                    int originalIndex = part.VertexIndices[k];
                    uint paint = originalIndex < geom.VertexColors.Length ? geom.VertexColors[originalIndex] : 0u;
                    mesh.Colors[k * 4 + 0] = (byte)paint;
                    mesh.Colors[k * 4 + 1] = (byte)(paint >> 8);
                    mesh.Colors[k * 4 + 2] = (byte)(paint >> 16);
                    mesh.Colors[k * 4 + 3] = (byte)(paint >> 24);
                }
            }

            Raylib.UploadMesh(ref mesh, false);
            gpuGeo.PartMeshes.Add(mesh);
            gpuGeo.PartLocalBounds.Add(new(partMin, partMax));
        }

        gpuGeo.LocalBounds = new BoundingBox(min, max);
        gpuGeo.LocalCenter = (min + max) * 0.5f;
        gpuGeo.BoundingRadius = Math.Max(0.1f, Vector3.Distance(min, max) * 0.5f);

        _geometryCache[geoKey] = gpuGeo;
        StatGeometryUploadsThisFrame++;
        StatGeometryCacheMissMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(cacheMissStartedAt).TotalMilliseconds;
        if (CapturePerformanceDiagnostics) GeometryUploadKeysThisFrame.Add(geoKey);
        return gpuGeo;
    }

    private GpuGeometry? GetOrUploadGeometryForOwner(SceneObject owner)
    {
        if (!CapturePerformanceDiagnostics)
            return GetOrUploadGeometry(owner.RenderGeometryKey);

        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return GetOrUploadGeometry(owner.RenderGeometryKey);
        }
        finally
        {
            PerformanceOwnerWork.AddGeometry(owner,
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private Geometry? ResolveGeometrySource(uint geoKey)
    {
        if (Resolver?.Geometries.TryGetValue(geoKey, out var resolved) == true) return resolved;
        return Document?.Resources.TryGetValue(geoKey, out var resource) == true
            ? resource.Payload as Geometry : null;
    }

    private bool TryGetGeometryBounds(uint geoKey, out BoundingBox bounds)
    {
        if (_geometryCache.TryGetValue(geoKey, out var uploaded))
        {
            bounds = uploaded.LocalBounds;
            return uploaded.Source != null;
        }
        if (_geometryBoundsCache.TryGetValue(geoKey, out bounds)) return true;

        Geometry? geometry = ResolveGeometrySource(geoKey);
        if (geometry == null || geometry.Positions.Length == 0)
        {
            bounds = default;
            return false;
        }

        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        foreach (var position in geometry.Positions)
        {
            var renderPosition = Geometry.ToRender(position);
            minimum = Vector3.Min(minimum, renderPosition);
            maximum = Vector3.Max(maximum, renderPosition);
        }

        bounds = new(minimum, maximum);
        _geometryBoundsCache[geoKey] = bounds;
        return true;
    }

    /// <summary>
    /// Получение или сборка материалов для связки (GeometryKey, MaterialKey).
    /// </summary>
    public unsafe GpuMaterialSet? GetOrUploadMaterialSet(uint geoKey, uint matKey)
    {
        EnsureGpuInitialized();
        var cacheKey = (geoKey, matKey);
        if (_materialCache.TryGetValue(cacheKey, out var existing)) return existing;
        long materialStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        Geometry? geom = null;
        if (Resolver != null && Resolver.Geometries.TryGetValue(geoKey, out geom)) { }
        else if (Document != null && Document.Resources.TryGetValue(geoKey, out var resItem) && resItem.Payload is Geometry g) geom = g;

        if (geom == null) return null;

        var matSet = new GpuMaterialSet { Key = cacheKey };
        long sourceResolutionStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var textures = Resolver?.ResolveTexturesForGeometry(geom, matKey, useFallback: true) ?? [];
        StatMaterialSourceResolutionMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(sourceResolutionStartedAt).TotalMilliseconds;
        var root = Resolver?.Materials.GetValueOrDefault(matKey);
        for (int pIdx = 0; pIdx < geom.Parts.Count; pIdx++)
        {
            var effective = root;
            int id = geom.Parts[pIdx].MaterialId;
            if (root?.Type == 4)
                effective = root.SubMaterialKeys.Count > 0 && id >= 0
                    ? Resolver?.Materials.GetValueOrDefault(root.SubMaterialKeys[Math.Min(id, root.SubMaterialKeys.Count - 1)]) : null;
            var layers = JadeMaterialLayer.Read(effective?.SourceBytes);
            if (layers.Count == 0 && effective == null) layers.Add(new(0, 7, 0, 0, 0));
            Vector4 constant = MaterialLayerTint.ReadConstant(effective);
            var passes = new List<GpuMaterialPass>();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                long materialAllocationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                var material = Raylib.LoadMaterialDefault();
                StatMaterialAllocationMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(materialAllocationStartedAt).TotalMilliseconds;
                StatMaterialPassesThisFrame++;
                material.Shader = _jadeShader;
                bool isVideoTexture = Resolver?.ProceduralTextures.TryGetValue(layer.Texture, out var procedural) == true
                    && procedural.Subtype == 3;
                TextureAsset? asset = !isVideoTexture && layer.Texture is not (0 or uint.MaxValue)
                    && Resolver != null && Resolver.TryResolveTexture(layer.Texture, out var resolved)
                    ? resolved.ToTextureAsset() : null;
                if (asset == null && !isVideoTexture && layer.Texture is not (0 or uint.MaxValue))
                    asset = FallbackGenerator.CreateFallbackTexture(layer.Texture).ToTextureAsset();
                if (asset == null && effective?.SourceBytes == null && pIdx < textures.Count) asset = textures[pIdx];
                Texture2D layerTexture = isVideoTexture
                    ? GetOrCreateProceduralVideoTexture(Resolver!.ProceduralTextures[layer.Texture])
                    : asset != null ? GetOrUploadTexture(asset) : _fallbackCheckerTexture;
                Raylib.SetMaterialTexture(ref material, MaterialMapIndex.Albedo, layerTexture);
                material.Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
                int subMaterialIndex = root?.Type == 4 && root.SubMaterialKeys.Count > 0 && id >= 0
                    ? Math.Min(id, root.SubMaterialKeys.Count - 1) : 0;
                passes.Add(new(material, layer, constant, effective?.Id.Key ?? matKey, layerIndex, subMaterialIndex));
            }
            matSet.Passes.Add(passes);
            matSet.PartMaterials.Add(passes.Count > 0 ? passes[0].Material : default);
        }

        _materialCache[cacheKey] = matSet;
        StatMaterialSetsThisFrame++;
        StatMaterialPreparationMilliseconds += (float)System.Diagnostics.Stopwatch.GetElapsedTime(materialStartedAt).TotalMilliseconds;
        return matSet;
    }

    public unsafe void Dispose()
    {
        ClearGpuCache();

        if (_selectionWireMaterial.Maps != null)
        {
            Raylib.MemFree(_selectionWireMaterial.Maps);
        }
        if (_wireShader.Id != 0) Raylib.UnloadShader(_wireShader);
        if (_collisionWireMaterial.Maps != null) Raylib.MemFree(_collisionWireMaterial.Maps);
        if (_selectionFillMaterial.Maps != null)
        {
            Raylib.MemFree(_selectionFillMaterial.Maps);
        }
        if (_jadeShader.Id != 0) Raylib.UnloadShader(_jadeShader);
        _jadeShader = default;
        ResetJadeUniformCache();
        DisposeCinematicAfterEffects();
        if (_fallbackCheckerTexture.Id != 0)
        {
            Raylib.UnloadTexture(_fallbackCheckerTexture);
        }
    }
}
