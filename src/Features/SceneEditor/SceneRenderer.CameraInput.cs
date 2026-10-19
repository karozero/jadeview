using System.Numerics;
using Raylib_cs;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private Vector2 cameraCapturePosition;

    private void UpdateCameraMouseCapture(bool isHovered)
    {
        if (!IsFlying && isHovered && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            cameraCapturePosition = Raylib.GetMousePosition();
            IsFlying = true;
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
        if (!Raylib.IsWindowFocused()) return;
        int positionX = (int)Math.Clamp(cameraCapturePosition.X, 0, Math.Max(0, Raylib.GetScreenWidth() - 1));
        int positionY = (int)Math.Clamp(cameraCapturePosition.Y, 0, Math.Max(0, Raylib.GetScreenHeight() - 1));
        Raylib.SetMousePosition(positionX, positionY);
    }
}
