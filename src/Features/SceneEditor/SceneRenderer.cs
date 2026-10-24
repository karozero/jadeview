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
    internal const double GeometryUploadFrameBudgetMilliseconds = 2;
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
        || obj.Actor?.Root.IsControlHidden == true
        || (obj.EffectiveDrawFlags & SceneObject.DrawVisibilityMask) == 0 && !IsCollision(obj));
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
    public float StatGeometryCacheMissMilliseconds { get; private set; }
    public float StatOverlayMilliseconds { get; private set; }
    public float StatRenderFinalizeMilliseconds { get; private set; }
    public float StatProceduralTextureMilliseconds { get; private set; }
    public float StatRenderTargetMilliseconds { get; private set; }
    public float StatLightingPreparationMilliseconds { get; private set; }
    public float StatClipPreparationMilliseconds { get; private set; }
    public float StatGeometryPreparationMilliseconds { get; private set; }
    public float StatDrawPassMilliseconds { get; private set; }
    public float StatDrawPacketSortMilliseconds { get; private set; }
    public float StatSkinningMilliseconds { get; private set; }
    public long StatSkinningAllocatedBytes { get; private set; }
    public long StatGeometryPreparationAllocatedBytes { get; private set; }
    public long StatDrawPassAllocatedBytes { get; private set; }
    internal bool CapturePerformanceDiagnostics { get; set; }
    internal bool CaptureSkinAllocationDiagnostics { get; set; }
    internal SceneRendererOwnerWork PerformanceOwnerWork { get; } = new();
    private readonly Dictionary<string, long> skinAllocationTotals = new(StringComparer.Ordinal);
    internal void ResetSkinAllocationDiagnostics() => skinAllocationTotals.Clear();
    internal string SkinAllocationDiagnostics => string.Join(", ", skinAllocationTotals
        .OrderByDescending(item => item.Value).Take(6)
        .Select(item => $"{item.Key}={item.Value / 120.0:F0}B/frame"));
    public int StatGeometryUploadsThisFrame { get; private set; }
    public int StatDeferredGeometryObjects { get; private set; }
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
        in vec4 vertexTangent;
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
        uniform vec4 rliDestColor;
        uniform float rliFactor;
        uniform vec4 ambientColor;
        uniform int computedLighting;
        out vec2 uv;
        out vec4 paint;
        out vec4 lightPaint;
        out float instanceAlpha;
        out float eyeDepth;
        vec3 jade(vec3 p) { return vec3(p.x, -p.z, p.y); }
        uvec3 ScaleRliDestination(vec3 color, float factor) {
            uvec3 packedColor = uvec3(round(clamp(color, 0.0, 1.0) * 255.0));
            if (factor >= 0.98) return packedColor;
            if (factor <= 0.02) return uvec3(0u);
            uint interpoler = floatBitsToUint(factor + 49152.0);
            uvec3 scaled = uvec3(0u);
            if ((interpoler & 128u) != 0u) scaled += (packedColor & uvec3(0xfefefefeu)) >> 1u;
            if ((interpoler & 64u) != 0u) scaled += (packedColor & uvec3(0xfcfcfcfcu)) >> 2u;
            if ((interpoler & 32u) != 0u) scaled += (packedColor & uvec3(0xf8f8f8f8u)) >> 3u;
            if ((interpoler & 16u) != 0u) scaled += (packedColor & uvec3(0xf0f0f0f0u)) >> 4u;
            if ((interpoler & 8u) != 0u) scaled += (packedColor & uvec3(0xe0e0e0e0u)) >> 5u;
            if ((interpoler & 4u) != 0u) scaled += (packedColor & uvec3(0xc0c0c0c0u)) >> 6u;
            if ((interpoler & 2u) != 0u) scaled += (packedColor & uvec3(0x80808080u)) >> 7u;
            return scaled;
        }
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
            // GEO_Render_InitRLI keeps raw RLI separate from computed diffuse.
            bool useRli = (objectDrawFlags & 0x40) != 0;
            bool dontScaleRli = (objectDrawFlags & 0x02000000) != 0;
            lightPaint = useRli ? paint : vec4(0.0);
            if (useRli && !dontScaleRli && rliFactor > 0.02) {
                uvec4 destination = uvec4(ScaleRliDestination(rliDestColor.rgb, rliFactor),
                    ScaleRliDestination(rliDestColor.aaa, rliFactor).x);
                ivec4 difference = ivec4(round(clamp(paint, 0.0, 1.0) * 255.0)) - ivec4(destination);
                lightPaint = vec4(max(difference, ivec4(0))) / 255.0;
            }
            if (computedLighting != 0) lightPaint = vertexTangent;
            // LIGHT_ComputeVertexColor adds ambient RGB after RLI initialization.
            // Alpha remains the computed/source channel, not the world's ambient alpha.
            lightPaint.rgb = min(lightPaint.rgb + ambientColor.rgb, vec3(1.0));
            instanceAlpha = vertexTexCoord2.x;
        }
        """;
    private const string JadeFragmentShader = """
        #version 330
        in vec2 uv;
        in vec4 paint;
        in vec4 lightPaint;
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
        uniform int fogEnabled;
        uniform vec4 fogColor;
        uniform float fogNear;
        uniform float fogFar;
        out vec4 finalColor;
        void main() {
            // Raw RLI and computed diffuse are distinct material inputs.
            // Unmodeled sources (including specular) retain the neutral fallback.
            vec4 tint = vec4(1.0);
            if (colorMode == 3 || colorMode == 5) tint = paint;
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

    // Состояние интерактивного 3D Gizmo
    public GizmoAxis HoveredAxis { get; private set; } = GizmoAxis.None;
    public GizmoAxis ActiveAxis { get; private set; } = GizmoAxis.None;
    public bool IsDraggingGizmo { get; private set; }
    private Vector3 _initialObjectTranslation;
    private Vector3 _initialRenderPos;
    private Vector3 _initialDragHitPoint;
    private Vector3 _dragPlaneNormal;

    public RenderTexture2D Target => _sceneTarget;

}
