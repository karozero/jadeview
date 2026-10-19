using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Forms = System.Windows.Forms;

namespace JadeView;
public sealed partial class EditorShell
{
    private void OnCommandExecuted(IEditCommand command)
    {
        if (command is TransformCommand transformCommand)
        {
            RecordObjectChangeInProject(transformCommand.Target);
        }
        else if (command is SceneObjectPresenceCommand presence)
        {
            SyncSceneObjectPresence(presence);
        }
    }

    private void OnCommandUndone(IEditCommand command) => SyncHistoryCommand(command);
    private void OnCommandRedone(IEditCommand command) => SyncHistoryCommand(command);
    private void SyncHistoryCommand(IEditCommand command)
    {
        if (CurrentProject == null || Document == null)
            return;
        switch (command)
        {
            case SceneObjectPresenceCommand presence:
                SyncSceneObjectPresence(presence);
                break;
            case TransformCommand transformCommand:
                SyncHistoryTransform(transformCommand.Target);
                break;
        }
    }

    private void SyncHistoryTransform(SceneObject sceneObject)
    {
        if (sceneObject.IsUserCreated)
        {
            CurrentProject!.UpdateAddedObject(sceneObject, Document!.ContainerName, ActiveWorld?.Key ?? 0);
            return;
        }

        var change = CurrentProject!.GetChange(sceneObject.Id, Document!.ContainerName);
        if (change == null)
            return;
        change.NewTransform = ModTransform.FromTransformData(sceneObject.Transform);
        change.ModifiedAt = DateTimeOffset.UtcNow;
        CurrentProject.ModifiedAt = DateTimeOffset.UtcNow;
    }

    private void SyncSceneObjectPresence(SceneObjectPresenceCommand command)
    {
        if (CurrentProject != null && Document != null)
            CurrentProject.RecordSceneObjectState(command.Target, Document.ContainerName, command.World.Key, command.World.Objects.Contains(command.Target));
        if (command.World.Objects.Contains(command.Target))
        {
            ActiveWorld = command.World;
            Renderer.ActiveWorld = command.World;
            SelectedObject = command.Target;
            Renderer.FrameSelected();
            _statusMessage = $"Added {command.Target.Name} to the scene.";
        }
        else
        {
            if (SelectedObject == command.Target)
                SelectedObject = command.World.Objects.FirstOrDefault();
            _statusMessage = $"Removed {command.Target.Name} from the scene.";
        }
    }

    private void RecordObjectChangeInProject(SceneObject obj)
    {
        if (CurrentProject == null || Document == null)
            return;
        if (obj.IsUserCreated)
        {
            CurrentProject.UpdateAddedObject(obj, Document.ContainerName, ActiveWorld?.Key ?? 0);
            return;
        }

        var existing = CurrentProject.GetChange(obj.Id, Document.ContainerName);
        TransformData oldTransform = OriginalTransformForChange(obj, existing);
        CurrentProject.RecordChange(obj, Document.ContainerName, ActiveWorld?.Key ?? 0, oldTransform, obj.Transform, translationOnly: obj.Transform.Rotation == oldTransform.Rotation && obj.Transform.Scale == oldTransform.Scale);
    }

    private TransformData OriginalTransformForChange(SceneObject sceneObject, ModObjectChange? existing)
    {
        if (existing != null)
            return new TransformData
            {
                Translation = existing.OldTransform.ToTranslation(),
                Rotation = existing.OldTransform.ToRotation(),
                Scale = existing.OldTransform.ToScale(),
                MatrixByteRange = sceneObject.Transform.MatrixByteRange
            };
        bool hasTransformCommand = History.CanUndo && History.LastExecutedCommand is TransformCommand;
        if (hasTransformCommand && History.LastExecutedCommand is TransformCommand command && command.Target == sceneObject)
            return new TransformData
            {
                Translation = command.OldTranslation,
                Rotation = command.OldRotation,
                Scale = command.OldScale,
                MatrixByteRange = sceneObject.Transform.MatrixByteRange
            };
        return sceneObject.InitialTransform ?? sceneObject.Transform;
    }
}
