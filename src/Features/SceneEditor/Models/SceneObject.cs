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
    public int AppearanceRevision { get; set; }
    public bool PreviewHidden { get; set; }
    public SceneActor? Actor { get; set; }
    public ActorNode? ActorNode { get; set; }
    public Matrix4x4? PreviewMatrix { get; set; }
    public Matrix4x4 RenderMatrix => PreviewMatrix is Matrix4x4 m ? AnimationMath.ToRender(m) : Transform.ToRenderMatrix();
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

