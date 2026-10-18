using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Сериализуемое представление матрицы/трансформации объекта для проекта модификации.
/// Хранит перенос, поворот и масштаб в человеко-читаемом и устойчивом виде.
/// </summary>
public sealed class ModTransform : IEquatable<ModTransform>
{
    [JsonPropertyName("tx")]
    public float Tx { get; set; }

    [JsonPropertyName("ty")]
    public float Ty { get; set; }

    [JsonPropertyName("tz")]
    public float Tz { get; set; }

    [JsonPropertyName("qx")]
    public float Qx { get; set; }

    [JsonPropertyName("qy")]
    public float Qy { get; set; }

    [JsonPropertyName("qz")]
    public float Qz { get; set; }

    [JsonPropertyName("qw")]
    public float Qw { get; set; } = 1.0f;

    [JsonPropertyName("sx")]
    public float Sx { get; set; } = 1.0f;

    [JsonPropertyName("sy")]
    public float Sy { get; set; } = 1.0f;

    [JsonPropertyName("sz")]
    public float Sz { get; set; } = 1.0f;

    public ModTransform() { }

    public ModTransform(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        Tx = translation.X;
        Ty = translation.Y;
        Tz = translation.Z;

        Qx = rotation.X;
        Qy = rotation.Y;
        Qz = rotation.Z;
        Qw = rotation.W;

        Sx = scale.X;
        Sy = scale.Y;
        Sz = scale.Z;
    }

    public ModTransform(Vector3 translation) : this(translation, Quaternion.Identity, Vector3.One) { }

    public static ModTransform FromTransformData(TransformData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ModTransform(data.Translation, data.Rotation, data.Scale);
    }

    public Vector3 ToTranslation() => new(Tx, Ty, Tz);
    public Quaternion ToRotation() => new(Qx, Qy, Qz, Qw);
    public Vector3 ToScale() => new(Sx, Sy, Sz);

    public void ApplyTo(TransformData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Translation = ToTranslation();
        data.Rotation = ToRotation();
        data.Scale = ToScale();
    }

    public ModTransform Clone() => new()
    {
        Tx = Tx, Ty = Ty, Tz = Tz,
        Qx = Qx, Qy = Qy, Qz = Qz, Qw = Qw,
        Sx = Sx, Sy = Sy, Sz = Sz
    };

    public bool Equals(ModTransform? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Math.Abs(Tx - other.Tx) < 1e-4f &&
               Math.Abs(Ty - other.Ty) < 1e-4f &&
               Math.Abs(Tz - other.Tz) < 1e-4f &&
               Math.Abs(Qx - other.Qx) < 1e-4f &&
               Math.Abs(Qy - other.Qy) < 1e-4f &&
               Math.Abs(Qz - other.Qz) < 1e-4f &&
               Math.Abs(Qw - other.Qw) < 1e-4f &&
               Math.Abs(Sx - other.Sx) < 1e-4f &&
               Math.Abs(Sy - other.Sy) < 1e-4f &&
               Math.Abs(Sz - other.Sz) < 1e-4f;
    }

    public override bool Equals(object? obj) => Equals(obj as ModTransform);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Tx); h.Add(Ty); h.Add(Tz);
        h.Add(Qx); h.Add(Qy); h.Add(Qz); h.Add(Qw);
        h.Add(Sx); h.Add(Sy); h.Add(Sz);
        return h.ToHashCode();
    }

    public override string ToString() =>
        $"T:({Tx:F2}, {Ty:F2}, {Tz:F2}) R:({Qx:F2}, {Qy:F2}, {Qz:F2}, {Qw:F2}) S:({Sx:F2}, {Sy:F2}, {Sz:F2})";
}

