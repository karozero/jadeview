using System.Numerics;

namespace JadeView;

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

