using Raylib_cs;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private void UpdateCameraMouseCapture(bool isHovered)
    {
        if (!IsFlying && isHovered && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            IsFlying = true;
            SynchronizeManualCameraOrientation();
            Raylib.DisableCursor();
        }

        bool released = !Raylib.IsMouseButtonDown(MouseButton.Right);
        if (IsFlying && (released || !Raylib.IsWindowFocused()))
            ReleaseCameraMouse();
    }

    private void ReleaseCameraMouse()
    {
        if (!IsFlying) return;
        IsFlying = false;
        Raylib.EnableCursor();
    }
}
