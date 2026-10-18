using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Редакторская команда изменения трансформации SceneObject, синхронизированная с ModProject.
/// Одно перетаскивание gizmo или одно изменение в Details = одна команда в стеке.
/// </summary>
public sealed class ModTransformCommand : IEditCommand
{
    private readonly SceneObject _target;
    private readonly ModProject? _project;
    private readonly string _containerName;
    private readonly uint _worldKey;
    private readonly bool _translationOnly;

    private readonly Vector3 _oldTranslation;
    private readonly Quaternion _oldRotation;
    private readonly Vector3 _oldScale;

    private readonly Vector3 _newTranslation;
    private readonly Quaternion _newRotation;
    private readonly Vector3 _newScale;

    public string Description => $"Transform {_target.Name}";

    public ModTransformCommand(
        SceneObject target,
        Vector3 newPos,
        Quaternion newRot,
        Vector3 newScale,
        ModProject? project = null,
        string containerName = "",
        uint worldKey = 0,
        bool translationOnly = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _project = project;
        _containerName = containerName;
        _worldKey = worldKey;
        _translationOnly = translationOnly;

        _oldTranslation = target.Transform.Translation;
        _oldRotation = target.Transform.Rotation;
        _oldScale = target.Transform.Scale;

        _newTranslation = newPos;
        _newRotation = newRot;
        _newScale = newScale;
    }

    public void Execute()
    {
        if (_target.Capabilities.CanTranslate) _target.Transform.Translation = _newTranslation;
        if (_target.Capabilities.CanRotate) _target.Transform.Rotation = _newRotation;
        if (_target.Capabilities.CanScale) _target.Transform.Scale = _newScale;

        if (_project != null)
        {
            var oldData = new TransformData { Translation = _oldTranslation, Rotation = _oldRotation, Scale = _oldScale };
            var newData = new TransformData { Translation = _newTranslation, Rotation = _newRotation, Scale = _newScale };
            _project.RecordChange(_target, _containerName, _worldKey, oldData, newData, _translationOnly);
        }
    }

    public void Undo()
    {
        if (_target.Capabilities.CanTranslate) _target.Transform.Translation = _oldTranslation;
        if (_target.Capabilities.CanRotate) _target.Transform.Rotation = _oldRotation;
        if (_target.Capabilities.CanScale) _target.Transform.Scale = _oldScale;

        if (_project != null)
        {
            var oldData = new TransformData { Translation = _oldTranslation, Rotation = _oldRotation, Scale = _oldScale };

            var change = _project.GetChange(_target.Id, _containerName);
            if (change != null)
            {
                change.NewTransform = ModTransform.FromTransformData(oldData);
                change.ModifiedAt = DateTimeOffset.UtcNow;
                _project.ModifiedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}

