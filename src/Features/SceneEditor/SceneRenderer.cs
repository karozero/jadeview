using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

/// <summary>
/// Высокопроизводительный сценовый рендерер Jade (Beyond Good & Evil PC).
/// Реализует:
///  - Рендеринг всех миров и objects с учетом матриц Transform.ToRenderMatrix()
///  - Повторное использование GPU-моделей и текстур (кэширование в VRAM)
///  - Свободная камера в стиле UE5 (ПКМ + WASD/QE, Shift, колесо, F - фокус)
///  - Выбор объекта лучом мыши (Ray casting / Picking) по Bounding Box
///  - Подсветка (Selection wireframe / Oriented Bounding Box)
///  - 3D Transform Gizmo (оси X/Y/Z) с интерактивным перетаскиванием (Translate)
///  - Frustum culling для протяженных сцен
///  - Корректная очистка GPU-ресурсов при смене сцены без утечек VRAM.
/// </summary>
public sealed partial class SceneRenderer : IDisposable
{
    private readonly record struct DrawPacket(SceneObject Obj, GpuGeometry Geo,
        GpuMaterialSet? Materials, int Part, bool Transparent, float Depth, int Order);

    private static readonly Comparison<DrawPacket> DrawPacketComparison = CompareDrawPackets;
    private readonly List<DrawPacket> _drawPackets = new(2048);

    private static int CompareDrawPackets(DrawPacket left, DrawPacket right)
    {
        int transparencyOrder = left.Transparent.CompareTo(right.Transparent);
        if (transparencyOrder != 0) return transparencyOrder;
        if (left.Transparent)
        {
            int depthOrder = right.Depth.CompareTo(left.Depth);
            if (depthOrder != 0) return depthOrder;
        }
        return left.Order.CompareTo(right.Order);
    }

    // Данные документа сцены и графа ресурсов
    public SceneDocument? Document { get; set; }
    public ResourceResolver? Resolver { get; set; }
    public WorldData? ActiveWorld { get; set; }
    public SceneObject? SelectedObject { get; set; }

    // Камера и управление
    public Camera3D Camera;
    public float Yaw = -0.785f;
    public float Pitch = -0.4f;
    public float MoveSpeed = 15.0f;
    public bool IsFlying { get; private set; }

    // Настройки отображения
    private readonly HashSet<SceneObject> _hiddenObjects = [];
    public bool IsHidden(SceneObject obj) => _hiddenObjects.Contains(obj) || obj.Actor is { } a && obj != a.Root && _hiddenObjects.Contains(a.Root);
    public void HideSelected() { if (SelectedObject != null) _hiddenObjects.Add(SelectedObject); }
    public void IsolateSelected()
    {
        if (SelectedObject == null) return;
        var world = ActiveWorld ?? Document?.ActiveWorld;
        if (world == null) return;
        foreach (var obj in world.Objects)
            if (obj != SelectedObject && (SelectedObject.Actor == null || obj.Actor != SelectedObject.Actor)) _hiddenObjects.Add(obj);
        _hiddenObjects.Remove(SelectedObject);
        if (SelectedObject.Actor is { } actor) foreach (var part in actor.Members) _hiddenObjects.Remove(part);
    }
    public void UnhideAll() => _hiddenObjects.Clear();
    public static bool IsCollision(SceneObject obj)
    {
        // COL is a name token, not the beginning of Colonne/Colline/Color.
        var name = obj.Name;
        for (int start = 0; start < name.Length;)
        {
            int at = name.IndexOf("COL", start, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            int end = at + 3;
            if ((at == 0 || !char.IsLetterOrDigit(name[at - 1])) &&
               (end == name.Length || !char.IsLetter(name[end]))) return true;
            start = end;
        }
        return false;
    }
    public IEditCommand? VisibilityChange(string mode)
    {
        var before = _hiddenObjects.ToArray();
        if (mode == "isolate") IsolateSelected();
        else if (mode == "show") UnhideAll();
        else HideSelected();
        var after = _hiddenObjects.ToArray();
        _hiddenObjects.Clear(); _hiddenObjects.UnionWith(before);
        return before.ToHashSet().SetEquals(after) ? null : new VisibilityCommand(this, before, after, mode);
    }
    private sealed class VisibilityCommand(SceneRenderer renderer, SceneObject[] before, SceneObject[] after, string mode) : IEditCommand
    {
        public bool AffectsDocument => false;
        public string Description => $"Visibility: {mode}";
        private void Apply(SceneObject[] state) { renderer._hiddenObjects.Clear(); renderer._hiddenObjects.UnionWith(state); }
        public void Execute() => Apply(after);
        public void Undo() => Apply(before);
    }
    public bool Wireframe = false;
    public bool ShowSelectionWireframe = true;
    public bool ShowColliders = true;
    private bool IsCollisionHidden(SceneObject sceneObject) => (GameView || !ShowColliders) && IsCollision(sceneObject);
    public bool ShowGrid = true;
    public bool ShowGameHidden = false;
    public bool IsGameHidden(SceneObject obj) => !ShowGameHidden && (obj.PreviewHidden || obj.IsControlHidden
        || obj.Actor?.Root.IsControlHidden == true || (obj.DrawFlags & 8) == 0 && !IsCollision(obj));
    [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glDepthFunc")]
    private static extern void SetDepthFunction(uint function);
    public bool ShowGizmo = true;
    public bool GameView;
    public void ToggleGameView() => GameView = !GameView;
    public bool ShowSkeletonBadges = false;
    public bool FrustumCullingEnabled = true;
    private double _farClip = 2000;
    private float _viewportAspect = 16f / 9f;
    private Vector3 frustumForward;
    private Vector3 frustumRight;
    private Vector3 frustumUp;
    private float frustumTanHalfVerticalFov;
    private bool frustumBasisValid;
    public TransformSpace GizmoSpace { get; set; } = TransformSpace.World;
    public TransformGizmoMode GizmoMode { get; set; } = TransformGizmoMode.Translate;
    public bool SnappingEnabled { get; set; } = false;
    public float SnappingStep { get; set; } = 0.5f;

    // Статистика рендера
    public int StatTotalObjects { get; private set; }
    public int StatVisibleObjects { get; private set; }
    public int StatDrawnTriangles { get; private set; }
    public int StatSkinUpdates { get; private set; }
    public float StatRenderMilliseconds { get; private set; }
    public float StatPostEffectMilliseconds { get; private set; }
    public float StatCinematicUpdateMilliseconds { get; set; }
    public float StatCinematicAudioMilliseconds { get; set; }
    public int StatWorldActorUpdates { get; private set; }
    public float StatWorldActorUpdateMilliseconds { get; private set; }
    public long StatWorldActorUpdateAllocatedBytes { get; private set; }
    public float StatSkinningMilliseconds { get; private set; }
    public long StatSkinningAllocatedBytes { get; private set; }
    public long StatGeometryPreparationAllocatedBytes { get; private set; }
    public long StatDrawPassAllocatedBytes { get; private set; }
    internal bool CapturePerformanceDiagnostics { get; set; }
    internal bool CaptureSkinAllocationDiagnostics { get; set; }
    private readonly Dictionary<string, long> skinAllocationTotals = new(StringComparer.Ordinal);
    internal void ResetSkinAllocationDiagnostics() => skinAllocationTotals.Clear();
    internal string SkinAllocationDiagnostics => string.Join(", ", skinAllocationTotals
        .OrderByDescending(item => item.Value).Take(6)
        .Select(item => $"{item.Key}={item.Value / 120.0:F0}B/frame"));
    public int StatGeometryUploadsThisFrame { get; private set; }
    internal List<uint> GeometryUploadKeysThisFrame { get; } = [];
    private int _skinUpdatesThisFrame;
    public int StatCachedGeometries => _geometryCache.Count;
    public int StatCachedTextures => _textureCache.Count + _proceduralVideoTextures.Count;

    // События
    public event Action<SceneObject?>? OnSelectionChanged;
    public event Action<SceneObject, IEditCommand>? OnObjectTransformed;

    // Кэш GPU-ресурсов
    private readonly Dictionary<uint, GpuGeometry> _geometryCache = new();
    private readonly Dictionary<uint, BoundingBox> _geometryBoundsCache = new();
    private readonly Dictionary<SceneObject, ParticlePreview> _particles = new();
    private int _proceduralFrame;
    private readonly Dictionary<(uint GeoKey, uint MatKey), GpuMaterialSet> _materialCache = new();
    private readonly Dictionary<string, Texture2D> _textureCache = new(StringComparer.OrdinalIgnoreCase);

    // Служебные GPU-ресурсы
    private Texture2D _fallbackCheckerTexture;
    private Material _selectionWireMaterial;
    private Material _collisionWireMaterial;
    private Material _selectionFillMaterial;
    private RenderTexture2D _sceneTarget;
    private bool _resourcesInitialized;
    private Shader _jadeShader;
    private Shader _wireShader;
    private readonly Dictionary<string, int> _jadeUniforms = [];
    private const string JadeVertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec2 vertexTexCoord2;
        in vec4 vertexColor;
        in vec3 vertexNormal;
        uniform mat4 mvp;
        uniform mat4 matModel;
        uniform int uvMode;
        uniform int uvCustom;
        uniform vec4 uvMatrix;
        uniform vec2 uvOffset;
        uniform mat4 jadeModelView;
        uniform mat4 jadePlanar;
        uniform float planarDistance;
        uniform int objectDrawFlags;
        out vec2 uv;
        out vec4 paint;
        out float instanceAlpha;
        out float eyeDepth;
        vec3 jade(vec3 p) { return vec3(p.x, -p.z, p.y); }
        void main() {
            gl_Position = mvp * vec4(vertexPosition, 1.0);
            eyeDepth = abs((jadeModelView * vec4(jade(vertexPosition), 1.0)).z);
            vec2 source = vec2(vertexTexCoord.x, 1.0 - vertexTexCoord.y);
            if (uvMode == 6) {
                vec3 p = jade(vertexPosition);
                int space = (uvCustom >> 6) & 3;
                if (space != 0) p = (transpose(jadePlanar) * vec4(p,1.0)).xyz;
                if (space == 2) p *= planarDistance;
                int axis = (uvCustom >> 4) & 3;
                if (axis == 0) source = p.yz;
                else if (axis == 1) source = p.xz;
                else if (axis == 2) source = p.xy;
                else source = vec2(dot(p, vec3(.70711,-.70711,0)), dot(p,vec3(-.57735,-.57735,.57735)));
            }
            if (uvMode == 2) {
                vec3 n = jade(vertexNormal);
                mat4 reflection = jadeModelView * .5;
                if ((objectDrawFlags & 0x8000) != 0) { reflection[0][0] = -reflection[0][0]; reflection[1][0] = -reflection[1][0]; }
                vec2 reflected = (reflection * vec4(n,1.0)).xy;
                vec2 transformed = vec2(dot(uvMatrix.xy,reflected),dot(uvMatrix.zw,reflected)) + uvOffset;
                uv = .5 - transformed;
            } else {
                if (uvMode == 3) {
                    vec4 pos = normalize(jadeModelView * vec4(jade(vertexPosition),1.0));
                    vec3 n = normalize(jadeModelView * vec4(jade(vertexNormal),1.0)).xyz;
                    // Neutral preview has no realtime diffuse light contribution.
                    source = vec2(0.0,clamp(1.0-dot(n,normalize(-pos).xyz),.1,.9));
                }
                vec2 transformed = vec2(dot(uvMatrix.xy, source), dot(uvMatrix.zw, source)) + uvOffset;
                uv = vec2(transformed.x, 1.0 - transformed.y);
            }
            paint = vertexColor;
            instanceAlpha = vertexTexCoord2.x;
        }
        """;
    private const string JadeFragmentShader = """
        #version 330
        in vec2 uv;
        in vec4 paint;
        in float instanceAlpha;
        in float eyeDepth;
        uniform int useRliAlpha;
        uniform int effectColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform int colorMode;
        uniform vec4 constantColor;
        uniform int alphaTest;
        uniform float alphaRef;
        uniform int alphaOverride;
        uniform float overrideValue;
        uniform float materialOpacity;
        uniform int additiveLocalAlpha;
        uniform vec4 rliDestColor;
        uniform float rliFactor;
        uniform int fogEnabled;
        uniform vec4 fogColor;
        uniform float fogNear;
        uniform float fogFar;
        out vec4 finalColor;
        void main() {
            // Neutral editor preview. Realtime lighting is not emulated, but
            // authored per-point colors remain useful as a diffuse fallback.
            vec4 tint = vec4(1.0);
            vec4 lightPaint = paint;
            if (rliFactor > 0.02) lightPaint.rgb = max(paint.rgb - rliDestColor.rgb * rliFactor, vec3(0.0));
            if (colorMode == 3 || colorMode == 5) tint = lightPaint;
            if (colorMode == 5) tint.rgb = 1.0 - tint.rgb;
            if (colorMode == 2 || colorMode == 4) tint = constantColor;
            if (effectColor != 0 && (colorMode == 0 || colorMode == 6)) tint = lightPaint;
            if (colorMode == 6) tint.rgb = min(tint.rgb * 2.0, vec3(1.0));
            if (useRliAlpha != 0 && (colorMode == 0 || colorMode == 6)) tint.a = instanceAlpha;
            if (alphaOverride != 0) tint.a = overrideValue;
            // Additive Jade passes use ONE/ONE blending, so framebuffer alpha
            // cannot fade their RGB contribution by itself.
            if (additiveLocalAlpha != 0) tint.rgb *= overrideValue;
            finalColor = texture(texture0, uv) * tint * colDiffuse;
            finalColor.a *= materialOpacity;
            if (fogEnabled != 0 && fogFar > fogNear) {
                float fogAmount = clamp((eyeDepth - fogNear) / (fogFar - fogNear), 0.0, 1.0);
                finalColor.rgb = mix(finalColor.rgb, fogColor.rgb, fogAmount);
            }
            if (alphaTest == 2 && finalColor.a >= alphaRef) discard;
            if (alphaTest == 5 && finalColor.a <= alphaRef) discard;
        }
        """;
    private unsafe void JadeUniform(string name, int value) => Raylib.SetShaderValue(_jadeShader, _jadeUniforms[name], &value, ShaderUniformDataType.Int);
    private unsafe void JadeUniform(string name, float value) => Raylib.SetShaderValue(_jadeShader, _jadeUniforms[name], &value, ShaderUniformDataType.Float);
    private unsafe void JadeUniform(string name, Vector2 value) => Raylib.SetShaderValue(_jadeShader, _jadeUniforms[name], &value, ShaderUniformDataType.Vec2);
    private unsafe void JadeUniform(string name, Vector4 value) => Raylib.SetShaderValue(_jadeShader, _jadeUniforms[name], &value, ShaderUniformDataType.Vec4);

    // Состояние интерактивного 3D Gizmo
    public GizmoAxis HoveredAxis { get; private set; } = GizmoAxis.None;
    public GizmoAxis ActiveAxis { get; private set; } = GizmoAxis.None;
    public bool IsDraggingGizmo { get; private set; }
    private Vector3 _initialObjectTranslation;
    private Vector3 _initialRenderPos;
    private Vector3 _initialDragHitPoint;
    private Vector3 _dragPlaneNormal;

    public RenderTexture2D Target => _sceneTarget;

    public SceneRenderer()
    {
        InitCamera();
    }

    private void InitCamera()
    {
        Camera = new Camera3D
        {
            Position = new Vector3(25.0f, 15.0f, 25.0f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            FovY = 50.0f,
            Projection = CameraProjection.Perspective
        };
        Vector3 dir = Vector3.Normalize(Camera.Target - Camera.Position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.99f, 0.99f));
    }

    private unsafe void EnsureGpuInitialized()
    {
        if (_resourcesInitialized) return;

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
        foreach (string name in new[] { "uvMode", "uvCustom", "uvMatrix", "uvOffset", "colorMode", "constantColor", "alphaTest", "alphaRef", "alphaOverride", "overrideValue", "materialOpacity", "additiveLocalAlpha", "useRliAlpha", "effectColor", "jadeModelView", "jadePlanar", "planarDistance", "objectDrawFlags", "rliDestColor", "rliFactor", "fogEnabled", "fogColor", "fogNear", "fogFar" })
            _jadeUniforms[name] = Raylib.GetShaderLocation(_jadeShader, name);
        _resourcesInitialized = true;
    }

    /// <summary>
    /// Полный сброс и освобождение GPU-ресурсов (меши, текстуры, материалы) при смене сцены.
    /// </summary>
    public void ClearGpuCache()
    {
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

        byte[] rgba = Preview.DecodeTga(asset);
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
            Texture2D gpuTex = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(gpuTex, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(gpuTex, TextureWrap.Repeat);
            _textureCache[asset.Name] = gpuTex;
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
        if (CapturePerformanceDiagnostics) GeometryUploadKeysThisFrame.Add(geoKey);
        return gpuGeo;
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

        Geometry? geom = null;
        if (Resolver != null && Resolver.Geometries.TryGetValue(geoKey, out geom)) { }
        else if (Document != null && Document.Resources.TryGetValue(geoKey, out var resItem) && resItem.Payload is Geometry g) geom = g;

        if (geom == null) return null;

        var matSet = new GpuMaterialSet { Key = cacheKey };
        var textures = Resolver?.ResolveTexturesForGeometry(geom, matKey, useFallback: true) ?? [];
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
            Vector4 constant = Vector4.One;
            if (effective?.SourceBytes is { Length: >= 16 } raw)
            {
                uint color = Preview.U32(raw, 12); // RGBA, not the header's ambient word.
                constant = new((color & 255) / 255f, (color >> 8 & 255) / 255f, (color >> 16 & 255) / 255f, 1);
            }
            var passes = new List<GpuMaterialPass>();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                var material = Raylib.LoadMaterialDefault();
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
        return matSet;
    }

    /// <summary>
    /// Преобразование экранных координат мыши во вьюпорте в 3D Ray.
    /// </summary>
    public static Ray GetViewportRay(Vector2 mouseInViewport, Vector2 viewportSize, Camera3D camera)
    {
        float fov = camera.FovY * MathF.PI / 180.0f;
        float aspect = viewportSize.X / Math.Max(1.0f, viewportSize.Y);
        float halfHeight = MathF.Tan(fov * 0.5f);
        float halfWidth = halfHeight * aspect;

        float ndcX = (2.0f * mouseInViewport.X / Math.Max(1.0f, viewportSize.X)) - 1.0f;
        float ndcY = 1.0f - (2.0f * mouseInViewport.Y / Math.Max(1.0f, viewportSize.Y));

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        Vector3 up = Vector3.Cross(right, forward);

        Vector3 dir = Vector3.Normalize(forward + right * (ndcX * halfWidth) + up * (ndcY * halfHeight));
        return new Ray(camera.Position, dir);
    }

    /// <summary>
    /// Точное вычисление расстояния между 3D лучом и 3D отрезком для надежного выбора осей Gizmo.
    /// </summary>
    public static float DistanceRayToSegment(Ray ray, Vector3 p0, Vector3 p1)
    {
        Vector3 u = ray.Direction;
        Vector3 v = p1 - p0;
        Vector3 w = ray.Position - p0;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);

        float D = a * c - b * b;
        float sN, sD = D;
        float tN, tD = D;

        if (D < 1e-6f)
        {
            sN = 0.0f;
            sD = 1.0f;
            tN = e;
            tD = c;
        }
        else
        {
            sN = (b * e - c * d);
            tN = (a * e - b * d);
            if (sN < 0.0f)
            {
                sN = 0.0f;
                tN = e;
                tD = c;
            }
        }

        if (tN < 0.0f)
        {
            tN = 0.0f;
            if (-d < 0.0f) sN = 0.0f;
            else if (-d > a) sN = sD;
            else sN = -d;
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-d + b) < 0.0f) sN = 0.0f;
            else if ((-d + b) > a) sN = sD;
            else sN = (-d + b);
        }

        float sc = (MathF.Abs(sN) < 1e-6f ? 0.0f : sN / sD);
        float tc = (MathF.Abs(tN) < 1e-6f ? 0.0f : tN / tD);

        Vector3 dP = w + (sc * u) - (tc * v);
        return dP.Length();
    }

    /// <summary>
    /// Преобразование локального BoundingBox в мировой AABB через произвольную матрицу.
    /// </summary>
    public static (Vector3 Min, Vector3 Max) TransformBoundingBox(BoundingBox local, Matrix4x4 matrix)
    {
        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(local.Min.X, local.Min.Y, local.Min.Z),
            new(local.Max.X, local.Min.Y, local.Min.Z),
            new(local.Min.X, local.Max.Y, local.Min.Z),
            new(local.Max.X, local.Max.Y, local.Min.Z),
            new(local.Min.X, local.Min.Y, local.Max.Z),
            new(local.Max.X, local.Min.Y, local.Max.Z),
            new(local.Min.X, local.Max.Y, local.Max.Z),
            new(local.Max.X, local.Max.Y, local.Max.Z)
        };

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (var corner in corners)
        {
            var tc = Vector3.Transform(corner, matrix);
            min = Vector3.Min(min, tc);
            max = Vector3.Max(max, tc);
        }

        return (min, max);
    }

    /// <summary>
    /// Кадрирование камеры на конкретном объекте сцены (интеграция с Outliner/UI).
    /// </summary>
    public void FrameObject(SceneObject obj, ResourceResolver? resolver = null)
    {
        if (resolver != null) Resolver = resolver;
        SelectedObject = obj;
        FrameSelected();
    }

    /// <summary>
    /// Кадрирование (фокус по клавише F) на выбранном объекте.
    /// </summary>
    public void FrameSelected()
    {
        if (SelectedObject == null)
        {
            FrameAll();
            return;
        }

        Vector3 center;
        float radius = 3.0f;

        if (SelectedObject.Actor is { } actor && SelectedObject == actor.Root)
        {
            actor.Evaluate(0); Vector3 low = new(float.MaxValue), high = new(float.MinValue); int count = 0;
            foreach (var part in actor.Members)
            {
                var shape = GetOrUploadGeometry(part.RenderGeometryKey); if (shape == null || IsGameHidden(part)) continue;
                UpdateSkin(shape, part); var bounds = TransformBoundingBox(shape.LocalBounds, part.RenderMatrix);
                low = Vector3.Min(low, bounds.Item1); high = Vector3.Max(high, bounds.Item2); count++;
            }
            center = count > 0 ? (low + high) * .5f : TransformData.ToRender(SelectedObject.Transform.Translation);
            radius = count > 0 ? Math.Max((high - low).Length() * .5f, .5f) : 3;
        }
        else if (GetOrUploadGeometry(SelectedObject.RenderGeometryKey) is { } geo)
        {
            var m = SelectedObject.RenderMatrix;
            var (wMin, wMax) = TransformBoundingBox(geo.LocalBounds, m);
            center = (wMin + wMax) * 0.5f;
            radius = Math.Max(1.0f, Vector3.Distance(wMin, wMax) * 0.5f);
        }
        else
        {
            center = TransformData.ToRender(SelectedObject.Transform.Translation);
        }

        Camera.Target = center;
        Vector3 offset = new(radius * 1.6f, radius * 1.1f, radius * 2.2f);
        Camera.Position = center + offset;
        Camera.Up = Vector3.UnitY;

        Vector3 dir = Vector3.Normalize(Camera.Target - Camera.Position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.99f, 0.99f));
        MoveSpeed = Math.Clamp(radius * 2.5f, 0.5f, 150.0f);
    }

    /// <summary>
    /// Кадрирование на всей сцене (всех объектах активного мира).
    /// </summary>
    public void FrameAll()
    {
        var world = ActiveWorld ?? Document?.ActiveWorld;
        if (world == null || world.Objects.Count == 0)
        {
            InitCamera();
            return;
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        int validCount = 0;

        foreach (var obj in world.Objects)
        {
            if (IsHidden(obj) || IsGameHidden(obj)
                || !TryGetGeometryBounds(obj.RenderGeometryKey, out var localBounds)) continue;
            var (worldMinimum, worldMaximum) = TransformBoundingBox(localBounds, obj.RenderMatrix);
            min = Vector3.Min(min, worldMinimum);
            max = Vector3.Max(max, worldMaximum);
            validCount++;
        }

        if (validCount == 0)
        {
            InitCamera();
            return;
        }

        Vector3 center = (min + max) * 0.5f;
        float radius = Math.Max(2.0f, Vector3.Distance(min, max) * 0.5f);

        Camera.Target = center;
        Camera.Position = center + new Vector3(radius * 1.5f, radius * 1.2f, radius * 2.0f);
        Camera.Up = Vector3.UnitY;

        Vector3 dir = Vector3.Normalize(Camera.Target - Camera.Position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.99f, 0.99f));
        MoveSpeed = Math.Clamp(radius * 1.5f, 1.0f, 250.0f);
    }

    /// <summary>
    /// Обновление камеры свободного полета (UE5: ПКМ + WASD/QE + Shift, колесо для зума).
    /// </summary>
    public void UpdateCamera(bool isHovered)
    {
        float dt = Raylib.GetFrameTime();
        UpdateCameraMouseCapture(isHovered);

        if (IsFlying)
        {
            Vector2 mouseDelta = Raylib.GetMouseDelta();
            Yaw -= mouseDelta.X * 0.003f;
            Pitch = Math.Clamp(Pitch - mouseDelta.Y * 0.003f, -1.52f, 1.52f);

            MoveSpeed = Math.Clamp(MoveSpeed * MathF.Pow(1.25f, Raylib.GetMouseWheelMove()), 0.05f, 2000.0f);

            Vector3 forward = new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch));
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            Vector3 up = Vector3.UnitY;

            float speed = MoveSpeed * dt * (Raylib.IsKeyDown(KeyboardKey.LeftShift) ? 3.5f : 1.0f);

            if (Raylib.IsKeyDown(KeyboardKey.W)) Camera.Position += forward * speed;
            if (Raylib.IsKeyDown(KeyboardKey.S)) Camera.Position -= forward * speed;
            if (Raylib.IsKeyDown(KeyboardKey.D)) Camera.Position += right * speed;
            if (Raylib.IsKeyDown(KeyboardKey.A)) Camera.Position -= right * speed;
            if (Raylib.IsKeyDown(KeyboardKey.E)) Camera.Position += up * speed;
            if (Raylib.IsKeyDown(KeyboardKey.Q)) Camera.Position -= up * speed;

            Camera.Target = Camera.Position + forward;
        }
        else if (isHovered)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (MathF.Abs(wheel) > 0.01f)
            {
                Vector3 forward = Vector3.Normalize(Camera.Target - Camera.Position);
                Camera.Position += forward * (wheel * MoveSpeed * 0.35f);
                Camera.Target = Camera.Position + forward;
            }
        }
    }

    /// <summary>
    /// Выбор объекта лучом мыши (Ray casting / Picking) по Bounding Box (интеграция с UI/A5).
    /// </summary>
    public SceneObject? PickObject(Vector2 mouseScreen, Vector2 viewportOrigin, Vector2 viewportSize, WorldData? world, ResourceResolver? resolver = null)
    {
        if (resolver != null) Resolver = resolver;
        Vector2 mouseInViewport = mouseScreen - viewportOrigin;
        Ray ray = GetViewportRay(mouseInViewport, viewportSize, Camera);

        var selected = TraceGeometry(ray, world ?? ActiveWorld ?? Document?.ActiveWorld);
        SelectedObject = selected;
        OnSelectionChanged?.Invoke(selected);
        return selected;
    }

    // Wire collision surfaces do not block selection of visible geometry inside them.
    public SceneObject? TraceGeometry(Ray ray, WorldData? world)
    {
        EnsureGpuInitialized();
        SceneObject? visual = null, collision = null;
        float closest = float.PositiveInfinity, collisionDistance = float.PositiveInfinity;
        if (world == null) return null;
        foreach (var obj in world.Objects)
        {
            if (obj.GeometryKey == 0 || IsHidden(obj) || IsGameHidden(obj) || IsCollisionHidden(obj)) continue;
            var geo = GetOrUploadGeometry(obj.RenderGeometryKey);
            if (geo == null) continue;
            UpdateSkin(geo, obj);
            var matrix = obj.RenderMatrix;
            var (min, max) = TransformBoundingBox(geo.LocalBounds, matrix);
            if (!Raylib.GetRayCollisionBox(ray, new BoundingBox(min, max)).Hit) continue;
            bool isCollision = IsCollision(obj);
            foreach (var mesh in geo.PartMeshes)
            {
                var hit = Raylib.GetRayCollisionMesh(ray, mesh, Matrix4x4.Transpose(matrix));
                if (!hit.Hit || hit.Distance < 0) continue;
                if (isCollision) { if (hit.Distance < collisionDistance) { collisionDistance = hit.Distance; collision = obj; } }
                else if (hit.Distance < closest) { closest = hit.Distance; visual = obj; }
            }
        }
        var selected = visual ?? collision;
        return selected?.Actor?.Root ?? selected;
    }

    /// <summary>
    /// Обновление камеры, выбор объекта лучом и интерактивное перетаскивание Gizmo.
    /// </summary>
    public void Update(float dt, Vector2 mouseInViewport, Vector2 viewportSize, bool isHovered, bool isFocused)
    {
        EnsureGpuInitialized();

        Ray ray = GetViewportRay(mouseInViewport, viewportSize, Camera);

        // 1. Интерактивное перемещение через 3D Gizmo
        if (IsDraggingGizmo && SelectedObject != null)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                // Пересечение текущего луча с плоскостью перетаскивания
                float denom = Vector3.Dot(ray.Direction, _dragPlaneNormal);
                if (MathF.Abs(denom) > 1e-5f)
                {
                    float t = Vector3.Dot(_initialDragHitPoint - ray.Position, _dragPlaneNormal) / denom;
                    Vector3 currentHitPoint = ray.Position + ray.Direction * t;
                    Vector3 deltaWorld = currentHitPoint - _initialDragHitPoint;

                    var (axisX, axisY, axisZ) = GetGizmoAxisDirections(SelectedObject);
                    Vector3 activeAxisDir = ActiveAxis switch
                    {
                        GizmoAxis.X => axisX,
                        GizmoAxis.Y => axisY,
                        GizmoAxis.Z => axisZ,
                        _ => Vector3.Zero
                    };

                    float shiftAlongAxis = Vector3.Dot(deltaWorld, activeAxisDir);
                    if (SnappingEnabled && SnappingStep > 1e-4f)
                        shiftAlongAxis = MathF.Round(shiftAlongAxis / SnappingStep) * SnappingStep;
                    Vector3 newRenderPos = _initialRenderPos + activeAxisDir * shiftAlongAxis;
                    Vector3 newJadePos = TransformData.FromRender(newRenderPos);

                    SelectedObject.Transform.Translation = newJadePos;

                    // Синхронизируем строку переноса в RawMatrix
                    var raw = SelectedObject.Transform.RawMatrix;
                    raw.M41 = newJadePos.X;
                    raw.M42 = newJadePos.Y;
                    raw.M43 = newJadePos.Z;
                    SelectedObject.Transform.RawMatrix = raw;
                }
            }
            else
            {
                // Завершение перетаскивания: создаем команду в стек Undo/Redo
                // со старыми координатами из _initialObjectTranslation (до начала drag) для гарантированного возврата при Undo (R3)
                IsDraggingGizmo = false;
                ActiveAxis = GizmoAxis.None;

                var cmd = new TransformCommand(
                    SelectedObject,
                    _initialObjectTranslation,
                    SelectedObject.Transform.Translation,
                    SelectedObject.Transform.Rotation,
                    SelectedObject.Transform.Scale
                );
                OnObjectTransformed?.Invoke(SelectedObject, cmd);
            }
            return;
        }

        // 2. Определение наведения луча на оси Gizmo
        HoveredAxis = GizmoAxis.None;
        if (SelectedObject != null && !IsHidden(SelectedObject) && !IsCollisionHidden(SelectedObject) && ShowGizmo && !GameView && GizmoMode == TransformGizmoMode.Translate)
        {
            Vector3 gizmoPos = TransformData.ToRender(SelectedObject.Transform.Translation);
            float distToCam = Vector3.Distance(Camera.Position, gizmoPos);
            float gizmoLen = Math.Clamp(distToCam * 0.16f, 0.4f, 50.0f);
            float hitThreshold = gizmoLen * 0.14f;

            var (axisX, axisY, axisZ) = GetGizmoAxisDirections(SelectedObject);

            float dX = DistanceRayToSegment(ray, gizmoPos, gizmoPos + axisX * gizmoLen);
            float dY = DistanceRayToSegment(ray, gizmoPos, gizmoPos + axisY * gizmoLen);
            float dZ = DistanceRayToSegment(ray, gizmoPos, gizmoPos + axisZ * gizmoLen);

            float minD = hitThreshold;
            if (dX < minD) { minD = dX; HoveredAxis = GizmoAxis.X; }
            if (dY < minD) { minD = dY; HoveredAxis = GizmoAxis.Y; }
            if (dZ < minD) { minD = dZ; HoveredAxis = GizmoAxis.Z; }
        }

        // 3. Старт перетаскивания Gizmo при нажатии ЛКМ
        if (isHovered && HoveredAxis != GizmoAxis.None && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (!GameView && SelectedObject != null && SelectedObject.Capabilities.CanTranslate)
            {
                IsDraggingGizmo = true;
                ActiveAxis = HoveredAxis;
                _initialObjectTranslation = SelectedObject.Transform.Translation;
                _initialRenderPos = TransformData.ToRender(_initialObjectTranslation);

                var (axisX, axisY, axisZ) = GetGizmoAxisDirections(SelectedObject);
                Vector3 activeAxisDir = ActiveAxis switch
                {
                    GizmoAxis.X => axisX,
                    GizmoAxis.Y => axisY,
                    GizmoAxis.Z => axisZ,
                    _ => Vector3.UnitY
                };

                Vector3 toCam = Vector3.Normalize(Camera.Position - _initialRenderPos);
                Vector3 planeN = Vector3.Normalize(Vector3.Cross(Vector3.Cross(activeAxisDir, toCam), activeAxisDir));
                if (planeN.LengthSquared() < 0.01f) planeN = toCam;
                _dragPlaneNormal = planeN;

                float denom = Vector3.Dot(ray.Direction, _dragPlaneNormal);
                if (MathF.Abs(denom) > 1e-5f)
                {
                    float t = Vector3.Dot(_initialRenderPos - ray.Position, _dragPlaneNormal) / denom;
                    _initialDragHitPoint = ray.Position + ray.Direction * t;
                }
                else
                {
                    _initialDragHitPoint = _initialRenderPos;
                }
                return;
            }
        }

        // 4. Свободная камера в стиле UE5 (зажатая ПКМ + WASD/QE + Shift)
        UpdateCameraMouseCapture(isHovered);

        if (IsFlying)
        {
            Vector2 mouseDelta = Raylib.GetMouseDelta();
            Yaw -= mouseDelta.X * 0.003f;
            Pitch = Math.Clamp(Pitch - mouseDelta.Y * 0.003f, -1.52f, 1.52f);

            MoveSpeed = Math.Clamp(MoveSpeed * MathF.Pow(1.25f, Raylib.GetMouseWheelMove()), 0.05f, 2000.0f);

            Vector3 forward = new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch));
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            Vector3 up = Vector3.UnitY;

            float speed = MoveSpeed * dt * (Raylib.IsKeyDown(KeyboardKey.LeftShift) ? 3.5f : 1.0f);

            if (Raylib.IsKeyDown(KeyboardKey.W)) Camera.Position += forward * speed;
            if (Raylib.IsKeyDown(KeyboardKey.S)) Camera.Position -= forward * speed;
            if (Raylib.IsKeyDown(KeyboardKey.D)) Camera.Position += right * speed;
            if (Raylib.IsKeyDown(KeyboardKey.A)) Camera.Position -= right * speed;
            if (Raylib.IsKeyDown(KeyboardKey.E)) Camera.Position += up * speed;
            if (Raylib.IsKeyDown(KeyboardKey.Q)) Camera.Position -= up * speed;

            Camera.Target = Camera.Position + forward;
        }
        else
        {
            // Приближение колесом без полета
            if (isHovered)
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (MathF.Abs(wheel) > 0.01f)
                {
                    Vector3 forward = Vector3.Normalize(Camera.Target - Camera.Position);
                    Camera.Position += forward * (wheel * MoveSpeed * 0.35f);
                    Camera.Target = Camera.Position + forward;
                }
            }
        }

        if (isFocused && !ImGui.GetIO().WantTextInput && Raylib.IsKeyPressed(KeyboardKey.G)) ToggleGameView();

        // 5. Кадрирование (F)
        if (isHovered && Raylib.IsKeyPressed(KeyboardKey.F))
        {
            FrameSelected();
        }

        // 6. Выбор объекта лучом мыши (Ray casting / Picking) по ЛКМ
        if (isHovered && !IsFlying && Raylib.IsMouseButtonPressed(MouseButton.Left) && HoveredAxis == GizmoAxis.None)
        {
            SelectedObject = TraceGeometry(ray, ActiveWorld ?? Document?.ActiveWorld);
            OnSelectionChanged?.Invoke(SelectedObject);
        }
    }

    private (Vector3 X, Vector3 Y, Vector3 Z) GetGizmoAxisDirections(SceneObject obj)
    {
        if (GizmoSpace == TransformSpace.Local)
        {
            var m = obj.RenderMatrix;
            var x = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m));
            var y = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m));
            var z = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m));
            return (x, y, z);
        }

        return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
    }

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
                uint color = index < obj.RliColors.Length ? obj.RliColors[index]
                    : index < geo.Source.VertexColors.Length ? geo.Source.VertexColors[index] : 0u;
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
            StatSkinningMilliseconds += (float)System.Diagnostics.Stopwatch
                .GetElapsedTime(skinStartedAt).TotalMilliseconds;
            if (CapturePerformanceDiagnostics)
            {
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
        if (!procedural && geo.DeformationOwner == obj && obj.Actor != null && geo.DeformationRevision == obj.Actor.PoseRevision) return;
        var posed = obj.Actor?.Skin(obj, geo.Source);
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
        geo.DeformationRevision = obj.Actor?.PoseRevision ?? 0;
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
        Raylib.SetShaderValueMatrix(_jadeShader, _jadeUniforms["jadeModelView"], Matrix4x4.Transpose(modelView));
        Raylib.SetShaderValueMatrix(_jadeShader, _jadeUniforms["jadePlanar"], planar);
        JadeUniform("planarDistance", 1 / MathF.Tan(Camera.FovY * MathF.PI / 360));
        JadeUniform("objectDrawFlags", unchecked((int)obj.DrawFlags));
    }

    public unsafe void Render(int width, int height)
    {
        long renderStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _skinUpdatesThisFrame = 0;
        StatGeometryUploadsThisFrame = 0;
        GeometryUploadKeysThisFrame.Clear();
        _proceduralFrame++;
        EnsureGpuInitialized();

        width = Math.Max(16, width);
        height = Math.Max(16, height);
        _viewportAspect = width / (float)height;
        ApplyCinematicProjection(width, height);
        UpdateFrustumBasis();
        UpdateProceduralVideoTextures(CinematicVideoTime ?? Raylib.GetTime());

        if (_sceneTarget.Id == 0 || _sceneTarget.Texture.Width != width || _sceneTarget.Texture.Height != height)
        {
            if (_sceneTarget.Id != 0) Raylib.UnloadRenderTexture(_sceneTarget);
            _sceneTarget = Raylib.LoadRenderTexture(width, height);
        }

        Raylib.BeginTextureMode(_sceneTarget);
        Raylib.ClearBackground(new Color(18, 23, 20, 255));

        // Flying/scrolling makes Target one unit from Position. That is a look direction,
        // not the distance to the scene: deriving farClip from it made large maps disappear.
        var world = ActiveWorld ?? Document?.ActiveWorld;
        StatWorldActorUpdates = 0;
        StatWorldActorUpdateMilliseconds = 0;
        StatWorldActorUpdateAllocatedBytes = 0;
        StatGeometryPreparationAllocatedBytes = 0;
        StatDrawPassAllocatedBytes = 0;
        StatSkinningMilliseconds = 0;
        StatSkinningAllocatedBytes = 0;
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
                var geo = GetOrUploadGeometry(obj.RenderGeometryKey); if (geo == null) continue;
                bool geometryDeformsThisFrame = obj.Actor != null
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
            StatGeometryPreparationAllocatedBytes = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() - geometryPreparationAllocatedBefore : 0;
            long drawPassAllocatedBefore = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() : 0;
            packets.Sort(DrawPacketComparison);
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
                    bool hasAuthoredPointColors = geo.Source?.VertexColors.Length > 0;
                    bool usesPaint = spritePart || hasAuthoredPointColors || geo.Source?.Particles != null && i >= geo.Source.ParticlePart
                        || obj.RliColors.Length > 0 && layer.Blend is not (0 or 7);
                    JadeUniform("effectColor", usesPaint ? 1 : 0);
                    JadeUniform("useRliAlpha", layer.Blend != 0 && (obj.RliColors.Length > 0 || spritePart) ? 1 : 0);
                    JadeUniform("rliDestColor", CinematicRliColor);
                    JadeUniform("rliFactor", CinematicRliFactor);
                    bool foggedObject = (obj.DrawFlags & 0x4000) != 0;
                    JadeUniform("fogEnabled", foggedObject && CinematicFogFar > CinematicFogNear ? 1 : 0);
                    JadeUniform("fogColor", FogColorForBlend(layer.Blend));
                    JadeUniform("fogNear", CinematicFogNear);
                    JadeUniform("fogFar", CinematicFogFar);
                    var constant = CinematicMaterials?.ApplyConstant(obj.GaoKey, pass.SubMaterialIndex, pass.Constant)
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
                    Rlgl.TextureParameters(texture.Id, 10242, (layer.Flags & 1) == 0 ? 33071 : 10497);
                    Rlgl.TextureParameters(texture.Id, 10243, (layer.Flags & 2) == 0 ? 33071 : 10497);
                    Rlgl.TextureParameters(texture.Id, 10240, (layer.Flags & 4) == 0 ? 9728 : 9729);
                    Rlgl.TextureParameters(texture.Id, 10241, (layer.Flags & 4) == 0 ? 9728 : 9729);
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
            StatDrawPassAllocatedBytes = CapturePerformanceDiagnostics
                ? GC.GetAllocatedBytesForCurrentThread() - drawPassAllocatedBefore : 0;
        }

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

    private void DrawOrientedBoundingBox(BoundingBox local, Matrix4x4 matrix, Color color)
    {
        var c = new Vector3[8]
        {
            Vector3.Transform(new(local.Min.X, local.Min.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Min.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Max.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Min.X, local.Max.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Min.X, local.Min.Y, local.Max.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Min.Y, local.Max.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Max.Y, local.Max.Z), matrix),
            Vector3.Transform(new(local.Min.X, local.Max.Y, local.Max.Z), matrix)
        };

        // Нижняя грань
        Raylib.DrawLine3D(c[0], c[1], color);
        Raylib.DrawLine3D(c[1], c[2], color);
        Raylib.DrawLine3D(c[2], c[3], color);
        Raylib.DrawLine3D(c[3], c[0], color);

        // Верхняя грань
        Raylib.DrawLine3D(c[4], c[5], color);
        Raylib.DrawLine3D(c[5], c[6], color);
        Raylib.DrawLine3D(c[6], c[7], color);
        Raylib.DrawLine3D(c[7], c[4], color);

        // Боковые стойки
        Raylib.DrawLine3D(c[0], c[4], color);
        Raylib.DrawLine3D(c[1], c[5], color);
        Raylib.DrawLine3D(c[2], c[6], color);
        Raylib.DrawLine3D(c[3], c[7], color);
    }

    private void DrawTransformGizmo(SceneObject obj)
    {
        Vector3 origin = TransformData.ToRender(obj.Transform.Translation);
        float distToCam = Vector3.Distance(Camera.Position, origin);
        float len = Math.Clamp(distToCam * 0.16f, 0.4f, 50.0f);
        float stemLen = len * 0.8f;
        float stemRadius = len * 0.025f;
        float coneLen = len * 0.2f;
        float coneRadius = len * 0.065f;

        var (axisX, axisY, axisZ) = GetGizmoAxisDirections(obj);

        Color colX = (ActiveAxis == GizmoAxis.X || HoveredAxis == GizmoAxis.X) ? Color.Yellow : new Color(240, 50, 50, 255);
        Color colY = (ActiveAxis == GizmoAxis.Y || HoveredAxis == GizmoAxis.Y) ? Color.Yellow : new Color(50, 230, 50, 255);
        Color colZ = (ActiveAxis == GizmoAxis.Z || HoveredAxis == GizmoAxis.Z) ? Color.Yellow : new Color(50, 110, 255, 255);

        // Отключаем проверку глубины, чтобы Gizmo всегда рисовался поверх objects
        Rlgl.DisableDepthTest();

        // Ось X (Красная)
        Vector3 xStemEnd = origin + axisX * stemLen;
        Vector3 xTip = origin + axisX * len;
        Raylib.DrawCylinderEx(origin, xStemEnd, stemRadius, stemRadius, 8, colX);
        Raylib.DrawCylinderEx(xStemEnd, xTip, coneRadius, 0.0f, 12, colX);

        // Ось Y (Зеленая)
        Vector3 yStemEnd = origin + axisY * stemLen;
        Vector3 yTip = origin + axisY * len;
        Raylib.DrawCylinderEx(origin, yStemEnd, stemRadius, stemRadius, 8, colY);
        Raylib.DrawCylinderEx(yStemEnd, yTip, coneRadius, 0.0f, 12, colY);

        // Ось Z (Синяя)
        Vector3 zStemEnd = origin + axisZ * stemLen;
        Vector3 zTip = origin + axisZ * len;
        Raylib.DrawCylinderEx(origin, zStemEnd, stemRadius, stemRadius, 8, colZ);
        Raylib.DrawCylinderEx(zStemEnd, zTip, coneRadius, 0.0f, 12, colZ);

        // Центральная сфера пивота
        Raylib.DrawSphere(origin, stemRadius * 1.6f, Color.White);

        Rlgl.EnableDepthTest();
    }

    private bool IsSphereInFrustum(Vector3 center, float radius)
    {
        if (!frustumBasisValid || !float.IsFinite(radius)) return true;
        Vector3 toCenter = center - Camera.Position;
        float forwardDistance = Vector3.Dot(toCenter, frustumForward);
        if (forwardDistance < -radius || forwardDistance > _farClip + radius) return false;

        float visibleDepth = Math.Max(0, forwardDistance);
        float halfHeight = visibleDepth * frustumTanHalfVerticalFov + radius;
        float halfWidth = halfHeight * _viewportAspect;
        return MathF.Abs(Vector3.Dot(toCenter, frustumRight)) <= halfWidth
            && MathF.Abs(Vector3.Dot(toCenter, frustumUp)) <= halfHeight;
    }

    private void UpdateFrustumBasis()
    {
        frustumBasisValid = false;
        Vector3 forward = Camera.Target - Camera.Position;
        if (forward.LengthSquared() < 1e-8f || !float.IsFinite(Camera.FovY)
            || !float.IsFinite(_viewportAspect)) return;

        forward = Vector3.Normalize(forward);
        Vector3 right = Vector3.Cross(forward, Camera.Up);
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(forward, MathF.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY);
        right = Vector3.Normalize(right);
        frustumForward = forward;
        frustumRight = right;
        frustumUp = Vector3.Normalize(Vector3.Cross(right, forward));
        frustumTanHalfVerticalFov = MathF.Tan(Math.Clamp(Camera.FovY, .1f, 179.9f) * MathF.PI / 360);
        frustumBasisValid = true;
    }

    internal static bool IsSphereInViewFrustum(Vector3 center, float radius,
        Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUp,
        float verticalFovDegrees, float viewportAspect, double farClip)
    {
        Vector3 forward = cameraTarget - cameraPosition;
        if (forward.LengthSquared() < 1e-8f || !float.IsFinite(radius)
            || !float.IsFinite(verticalFovDegrees) || !float.IsFinite(viewportAspect)
            || !double.IsFinite(farClip)) return true;

        forward = Vector3.Normalize(forward);
        Vector3 right = Vector3.Cross(forward, cameraUp);
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(forward, MathF.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY);
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        Vector3 toCenter = center - cameraPosition;
        float forwardDistance = Vector3.Dot(toCenter, forward);
        if (forwardDistance < -radius || forwardDistance > farClip + radius) return false;

        float safeFov = Math.Clamp(verticalFovDegrees, .1f, 179.9f);
        float safeAspect = Math.Max(viewportAspect, .001f);
        float tangentHalfVerticalFov = MathF.Tan(safeFov * MathF.PI / 360);
        float visibleDepth = Math.Max(0, forwardDistance);
        float halfHeight = visibleDepth * tangentHalfVerticalFov + radius;
        float halfWidth = visibleDepth * tangentHalfVerticalFov * safeAspect + radius;
        return MathF.Abs(Vector3.Dot(toCenter, right)) <= halfWidth
            && MathF.Abs(Vector3.Dot(toCenter, up)) <= halfHeight;
    }

    private static double ClipDistanceForBounds(Vector3 camera, Vector3 min, Vector3 max)
    {
        double distance = Vector3.Distance(camera, (min + max) * .5f)
            + Vector3.Distance(min, max) * .5f;
        return double.IsFinite(distance) ? Math.Max(2000, distance * 1.05 + 10) : 2000;
    }

    /// <summary>
    /// Интеграционный UI-виджет Viewport в стиле UE5 для ImGui.
    /// </summary>
    public void DrawViewport(string id, Vector2 availableSize)
    {
        float scale = Math.Clamp(Raylib.GetWindowScaleDPI().X, 1.0f, 2.0f);
        Vector2 size = availableSize;
        size.X = Math.Max(120.0f, size.X);
        size.Y = Math.Max(120.0f, size.Y);

        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool isHovered = ImGui.IsItemHovered();
        bool isFocused = ImGui.IsItemFocused() || isHovered;

        Vector2 mouseInViewport = ImGui.GetMousePos() - origin;

        // Обновление логики
        Update(Raylib.GetFrameTime(), mouseInViewport, size, isHovered, isFocused);

        // Отрисовка кадра
        Render((int)size.X, (int)size.Y);
        long postEffectStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var displayTarget = RenderCinematicAfterEffects();
        StatPostEffectMilliseconds = (float)System.Diagnostics.Stopwatch
            .GetElapsedTime(postEffectStartedAt).TotalMilliseconds;
        StatRenderMilliseconds += StatPostEffectMilliseconds;

        // Вывод текстуры во фрейм ImGui
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImage(
            (nint)displayTarget.Texture.Id,
            origin,
            origin + size,
            new Vector2(0, 1),
            new Vector2(1, 0)
        );

        if (GameView) return;

        // Верхний HUD Overlay в стиле UE5
        ImGui.SetCursorScreenPos(origin + new Vector2(10 * scale, 10 * scale));
        ImGui.BeginGroup();

        // Индикатор выделенного объекта
        if (GizmoMode != TransformGizmoMode.Translate)
            ImGui.TextDisabled("Rotation and scale: Details fields. Move gizmo: W.");
        if (SelectedObject != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.35f, 0.9f, 0.54f, 1.0f), $"Selected: {SelectedObject.Name}");
            ImGui.SameLine();
            ImGui.TextDisabled($"({SelectedObject.GaoKey:X8})");

            if (!SelectedObject.Capabilities.CanTranslate)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.65f, 0.2f, 1.0f), $"[Restricted: {SelectedObject.Capabilities.RestrictionReason}]");
            }
        }

        ImGui.EndGroup();

        // Нижний правый угол: статистика и подсказки
        string skinStatus = CinematicMaterials == null ? $"Skin: {StatSkinUpdates}"
            : $"Skin: {StatSkinUpdates}/{StatSkinningMilliseconds:F1} ms";
        string statText = $"Frame: {Raylib.GetFrameTime() * 1000:F1} ms / {Raylib.GetFPS()} FPS · Objects: {StatVisibleObjects}/{StatTotalObjects} · Tris: {StatDrawnTriangles:N0} · {skinStatus} · Upload: {StatGeometryUploadsThisFrame} · World AI: {StatWorldActorUpdates}/{StatWorldActorUpdateMilliseconds:F1} ms · Cine: {StatCinematicUpdateMilliseconds:F1} ms · Audio: {StatCinematicAudioMilliseconds:F1} ms · Render: {StatRenderMilliseconds:F1} ms (FX {StatPostEffectMilliseconds:F1}) · GPU cache: {StatCachedGeometries} GRO, {StatCachedTextures} TEX";
        Vector2 statSize = ImGui.CalcTextSize(statText);
        Vector2 statPos = origin + size - statSize - new Vector2(12 * scale, 10 * scale);
        drawList.AddRectFilled(statPos - new Vector2(4, 2), statPos + statSize + new Vector2(4, 2), 0xAA101612, 4.0f);
        drawList.AddText(statPos, 0xFF9AE5B4, statText);
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
        DisposeCinematicAfterEffects();
        if (_fallbackCheckerTexture.Id != 0)
        {
            Raylib.UnloadTexture(_fallbackCheckerTexture);
        }
    }
}
