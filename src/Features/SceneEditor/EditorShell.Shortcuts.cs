using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace JadeView;

public sealed partial class EditorShell
{
    private SceneObjectClipboard? _objectClipboard;

    private void HandleKeyboardShortcuts()
    {
        bool editingField = ImGui.GetIO().WantTextInput || ImGui.IsAnyItemActive();
        bool navigating = Renderer.IsFlying || Raylib.IsMouseButtonDown(MouseButton.Right);
        bool popupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
        if (editingField || navigating || popupOpen || _activeJob != null)
            return;

        bool controlDown = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (!Renderer.IsDraggingGizmo)
            HandleObjectShortcuts(controlDown);
        if (controlDown && Raylib.IsKeyPressed(KeyboardKey.Z))
            History.Undo();
        if (controlDown && Raylib.IsKeyPressed(KeyboardKey.Y))
            History.Redo();
        if (controlDown && Raylib.IsKeyPressed(KeyboardKey.S))
            SaveCurrentProject();

        if (Raylib.IsKeyPressed(KeyboardKey.W))
            Renderer.GizmoMode = TransformGizmoMode.Translate;
        if (Raylib.IsKeyPressed(KeyboardKey.F) && SelectedObject != null)
            Renderer.FrameSelected();
        if (Raylib.IsKeyPressed(KeyboardKey.H) && !Renderer.IsDraggingGizmo)
        {
            bool altDown = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
            bool shiftDown = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            var command = Renderer.VisibilityChange(altDown ? "show" : shiftDown ? "isolate" : "hide");
            if (command != null)
                History.Execute(command);
        }
    }

    private void HandleObjectShortcuts(bool controlDown)
    {
        if (controlDown && Raylib.IsKeyPressed(KeyboardKey.C))
            CopySelectedSceneObject();
        if (controlDown && Raylib.IsKeyPressed(KeyboardKey.V))
            PasteSceneObject();
        if (!controlDown && Raylib.IsKeyPressed(KeyboardKey.Delete) && SelectedObject != null)
            DeleteSceneObject(SelectedObject);
    }

    private void CopySelectedSceneObject()
    {
        if (Document == null || ActiveWorld == null || SelectedObject == null)
            return;
        if (!ActiveWorld.Objects.Contains(SelectedObject) || !CanDuplicateSceneObject(SelectedObject))
        {
            _statusMessage = "Only independent static scene objects can be copied.";
            return;
        }

        var snapshot = SceneStructureChange.Added(SelectedObject, Document.ContainerName, ActiveWorld.Key);
        snapshot.SourceObjectId = SelectedObject.IsUserCreated ? SelectedObject.SourceObjectId : SelectedObject.Id;
        _objectClipboard = new(Document, ActiveWorld, snapshot);
        _statusMessage = $"Copied {SelectedObject.Name}. Press Ctrl+V to paste.";
    }

    private void PasteSceneObject()
    {
        if (_objectClipboard == null)
            return;
        bool sameScene = ReferenceEquals(Document, _objectClipboard.Document);
        bool sameWorld = ReferenceEquals(ActiveWorld, _objectClipboard.World);
        if (!sameScene || !sameWorld)
        {
            _statusMessage = "Paste into the source world; copying between worlds is not supported.";
            return;
        }

        var source = _objectClipboard.Snapshot.CreateObject();
        _objectClipboard.Snapshot.Transform.ApplyTo(source.Transform);
        source.Transform.Translation += Vector3.UnitX * _objectClipboard.PasteCount;
        DuplicateSceneObject(source);
        _objectClipboard.PasteCount++;
    }
}
