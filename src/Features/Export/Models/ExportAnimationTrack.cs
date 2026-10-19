using System.Numerics;

namespace JadeView;

internal sealed class ExportAnimationTrack
{
    internal List<float> Translation { get; } = [];
    internal List<float> Rotation { get; } = [];
    internal List<float> Scale { get; } = [];
    private Quaternion previousRotation;
    private bool hasPreviousRotation;

    internal void Append(Vector3 scale, Quaternion rotation, Vector3 translation)
    {
        // q and -q represent the same rotation; keep interpolation on one hemisphere.
        bool reverseQuaternion = hasPreviousRotation && Quaternion.Dot(previousRotation, rotation) < 0;
        if (reverseQuaternion) rotation = new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
        previousRotation = rotation;
        hasPreviousRotation = true;
        Translation.AddRange([translation.X, translation.Y, translation.Z]);
        Rotation.AddRange([rotation.X, rotation.Y, rotation.Z, rotation.W]);
        Scale.AddRange([scale.X, scale.Y, scale.Z]);
    }
}
