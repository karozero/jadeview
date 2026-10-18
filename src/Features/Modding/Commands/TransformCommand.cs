using System.Numerics;

namespace JadeView;

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
