using System.Numerics;

namespace JadeView;

public readonly record struct ResourceId(uint Key, string Scope = "")
{
    public override string ToString() => string.IsNullOrEmpty(Scope) ? $"{Key:X8}" : $"{Scope}::{Key:X8}";
}

public readonly record struct SourceRange(int Offset, int Size)
{
    public bool IsValid => Offset >= 0 && Size > 0;
}

public enum ResourceType
{
    Unknown = 0,
    Geometry,
    Material,
    Texture,
    Sound,
    ScriptOFC
}

public enum ResolutionState
{
    Unresolved,
    Resolved,
    Missing,
    Fallback
}

public sealed class TransformData
{
    public Matrix4x4 RawMatrix { get; set; } = Matrix4x4.Identity;
    public Vector3 Translation { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;

    // Смещение матричного блока в сырых bytesах GAO для точечного патча (A3)
    public SourceRange MatrixByteRange { get; set; }

    public static Vector3 ToRender(Vector3 v) => new(v.X, v.Z, -v.Y);
    public static Vector3 FromRender(Vector3 v) => new(v.X, -v.Z, v.Y);

    // Матрицы смены базиса между Jade (Z-up) и Raylib (Y-up):
    // v_render = v_jade * B^T, где B = [1 0 0; 0 0 1; 0 -1 0]
    // M_render = B * M_jade * B^T
    private static readonly Matrix4x4 BasisChangeB = new(
        1f,  0f,  0f, 0f,
        0f,  0f,  1f, 0f,
        0f, -1f,  0f, 0f,
        0f,  0f,  0f, 1f
    );

    private static readonly Matrix4x4 BasisChangeBt = new(
        1f, 0f,  0f, 0f,
        0f, 0f, -1f, 0f,
        0f, 1f,  0f, 0f,
        0f, 0f,  0f, 1f
    );

    public Matrix4x4 ToRenderMatrix()
    {
        // Преобразование системы координат Jade (Z-up) в систему координат рендера Raylib (Y-up)
        // Математически корректная смена базиса: M_render = B * M_jade * B^T
        var mJade = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
        return BasisChangeB * mJade * BasisChangeBt;
    }

    public TransformData Clone() => new()
    {
        RawMatrix = RawMatrix,
        Translation = Translation,
        Rotation = Rotation,
        Scale = Scale,
        MatrixByteRange = MatrixByteRange
    };
}

[Flags]
public enum EditCapabilities
{
    None = 0,
    Translate = 1 << 0,
    Rotate = 1 << 1,
    Scale = 1 << 2,
    AllTransform = Translate | Rotate | Scale
}

public sealed class ObjectEditCapabilities
{
    public EditCapabilities Flags { get; set; } = EditCapabilities.AllTransform;
    public string RestrictionReason { get; set; } = "";
    public bool CanTranslate => (Flags & EditCapabilities.Translate) != 0;
    public bool CanRotate => (Flags & EditCapabilities.Rotate) != 0;
    public bool CanScale => (Flags & EditCapabilities.Scale) != 0;
}

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
    public uint RenderGeometryKey => PreviewGeometryKey != 0 ? PreviewGeometryKey : GeometryKey;
    public uint GeometryKey { get; set; }
    public uint MaterialKey { get; set; }
    public uint Flags { get; set; }
    public List<SceneModifier> Modifiers { get; set; } = [];
    public SourceRange RecordRange { get; init; }
    public ObjectEditCapabilities Capabilities { get; set; } = new();

    public override string ToString() => $"{Name} ({GaoKey:X8})";
}

public sealed record SceneModifier(uint Type, byte[] Data);

public sealed class WorldData
{
    public uint Key { get; init; }
    public string Name { get; set; } = "";
    public SourceRange RecordRange { get; init; }
    public List<SceneObject> Objects { get; } = [];
    public List<SceneActor> Actors { get; } = [];

    public override string ToString() => $"{Name} ({Key:X8}) - {Objects.Count} objects";
}

public sealed class ResourceItem
{
    public ResourceId Id { get; init; }
    public ResourceType Type { get; init; }
    public ResolutionState State { get; set; } = ResolutionState.Unresolved;
    public object? Payload { get; set; }
    public List<uint> Dependencies { get; } = [];
}

public sealed class SceneDocument
{
    public string BaseBfPath { get; set; } = "";
    public string BaseBfSha256 { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public List<WorldData> Worlds { get; } = [];
    public Dictionary<uint, ResourceItem> Resources { get; } = [];
    public List<string> Diagnostics { get; } = [];

    public WorldData? ActiveWorld => Worlds.Count > 0 ? Worlds[0] : null;

    public SceneObject? FindObjectById(string id)
    {
        foreach (var w in Worlds)
        {
            var obj = w.Objects.FirstOrDefault(o => o.Id == id);
            if (obj != null) return obj;
        }
        return null;
    }
}

public interface IEditCommand
{
    bool AffectsDocument => true;
    string Description { get; }
    void Execute();
    void Undo();
}

public sealed class TransformCommand : IEditCommand
{
    private readonly SceneObject _target;
    private readonly Vector3 _oldTranslation;
    private readonly Vector3 _newTranslation;
    private readonly Quaternion _oldRotation;
    private readonly Quaternion _newRotation;
    private readonly Vector3 _oldScale;
    private readonly Vector3 _newScale;

    public string Description => $"Transform {_target.Name}";

    public SceneObject Target => _target;
    public Vector3 OldTranslation => _oldTranslation;
    public Vector3 NewTranslation => _newTranslation;
    public Quaternion OldRotation => _oldRotation;
    public Quaternion NewRotation => _newRotation;
    public Vector3 OldScale => _oldScale;
    public Vector3 NewScale => _newScale;

    public TransformCommand(SceneObject target, Vector3 oldPos, Quaternion oldRot, Vector3 oldScale, Vector3 newPos, Quaternion newRot, Vector3 newScale)
    {
        _target = target;
        _oldTranslation = oldPos;
        _oldRotation = oldRot;
        _oldScale = oldScale;
        _newTranslation = newPos;
        _newRotation = newRot;
        _newScale = newScale;
    }

    public TransformCommand(SceneObject target, Vector3 oldPos, Vector3 newPos, Quaternion newRot, Vector3 newScale)
        : this(target, oldPos, target.Transform.Rotation, target.Transform.Scale, newPos, newRot, newScale)
    {
    }

    public TransformCommand(SceneObject target, Vector3 newPos, Quaternion newRot, Vector3 newScale)
        : this(target, target.Transform.Translation, target.Transform.Rotation, target.Transform.Scale, newPos, newRot, newScale)
    {
    }

    public void Execute()
    {
        if (_target.Capabilities.CanTranslate)
        {
            _target.Transform.Translation = _newTranslation;
            var raw = _target.Transform.RawMatrix;
            raw.M41 = _newTranslation.X;
            raw.M42 = _newTranslation.Y;
            raw.M43 = _newTranslation.Z;
            _target.Transform.RawMatrix = raw;
        }
        if (_target.Capabilities.CanRotate)
        {
            _target.Transform.Rotation = _newRotation;
        }
        if (_target.Capabilities.CanScale)
        {
            _target.Transform.Scale = _newScale;
        }
    }

    public void Undo()
    {
        if (_target.Capabilities.CanTranslate)
        {
            _target.Transform.Translation = _oldTranslation;
            var raw = _target.Transform.RawMatrix;
            raw.M41 = _oldTranslation.X;
            raw.M42 = _oldTranslation.Y;
            raw.M43 = _oldTranslation.Z;
            _target.Transform.RawMatrix = raw;
        }
        if (_target.Capabilities.CanRotate)
        {
            _target.Transform.Rotation = _oldRotation;
        }
        if (_target.Capabilities.CanScale)
        {
            _target.Transform.Scale = _oldScale;
        }
    }
}
