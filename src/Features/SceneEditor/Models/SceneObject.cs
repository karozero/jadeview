using System.Numerics;

namespace JadeView;

public sealed class SceneObject
{
    public string Id { get; set; } = "";
    public uint GaoKey { get; init; }
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public TransformData Transform { get; set; } = new();
    public TransformData InitialTransform { get; set; } = new();
    public uint[] RliColors { get; set; } = [];
    // GAO visual draw mask; bit 3 controls visibility, independently of identity flags.
    public uint DrawFlags { get; set; } = 0x01000008;
    public uint ControlFlags { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal uint InitialCapabilities { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal uint? PreviewCapabilities { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public uint[] GroupMemberKeys { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public uint? PreviewControlFlags { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CinematicExcluded { get; set; }
    // Apply runtime visibility only while a cinematic overlay is active; asset setups remain inspectable.
    public bool IsControlHidden => CinematicExcluded
        || PreviewControlFlags is uint flags && (flags & CinematicObjectState.Invisible) != 0;
    public int AppearanceRevision { get; set; }
    public bool PreviewHidden { get; set; }
    public SceneActor? Actor { get; set; }
    public ActorNode? ActorNode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public FaceMorphPreview? MorphPreview { get; set; }
    internal Action<FaceMorphPreview>? RestoreMorphPreview { get; set; }
    public Matrix4x4? PreviewMatrix { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal float? CinematicZoom { get; set; }
    public Matrix4x4 RenderMatrix
    {
        get
        {
            var matrix = PreviewMatrix is Matrix4x4 preview ? AnimationMath.ToRender(preview) : Transform.ToRenderMatrix();
            if (CinematicZoom is not float zoom || !Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
                return matrix;
            return Matrix4x4.CreateScale(scale * zoom) * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(translation);
        }
    }
    // Procedural preview keeps the original GRO key and archive patch metadata intact.
    public uint PreviewGeometryKey { get; set; }
    // Objects created in the editor are stored in the .bgemod scene overlay.
    public bool IsUserCreated { get; set; }
    public string? SourceObjectId { get; set; }
    public uint RenderGeometryKey => PreviewGeometryKey != 0 ? PreviewGeometryKey : GeometryKey;
    public uint GeometryKey { get; set; }
    public uint MaterialKey { get; set; }
    public uint Flags { get; set; }
    public List<SceneModifier> Modifiers { get; set; } = [];
    public SourceRange RecordRange { get; init; }
    public ObjectEditCapabilities Capabilities { get; set; } = new();

    public override string ToString() => $"{Name} ({GaoKey:X8})";
}

