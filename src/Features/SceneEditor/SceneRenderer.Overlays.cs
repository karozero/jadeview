using System.Buffers.Binary;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private void DrawOrientedBoundingBox(BoundingBox local, Matrix4x4 matrix, Color color)
    {
        var c = new Vector3[8]
        {
            Vector3.Transform(new(local.Min.X, local.Min.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Min.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Max.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Min.X, local.Max.Y, local.Min.Z), matrix),
            Vector3.Transform(new(local.Min.X, local.Min.Y, local.Max.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Min.Y, local.Max.Z), matrix),
            Vector3.Transform(new(local.Max.X, local.Max.Y, local.Max.Z), matrix),
            Vector3.Transform(new(local.Min.X, local.Max.Y, local.Max.Z), matrix)
        };

        // Нижняя грань
        Raylib.DrawLine3D(c[0], c[1], color);
        Raylib.DrawLine3D(c[1], c[2], color);
        Raylib.DrawLine3D(c[2], c[3], color);
        Raylib.DrawLine3D(c[3], c[0], color);

        // Верхняя грань
        Raylib.DrawLine3D(c[4], c[5], color);
        Raylib.DrawLine3D(c[5], c[6], color);
        Raylib.DrawLine3D(c[6], c[7], color);
        Raylib.DrawLine3D(c[7], c[4], color);

        // Боковые стойки
        Raylib.DrawLine3D(c[0], c[4], color);
        Raylib.DrawLine3D(c[1], c[5], color);
        Raylib.DrawLine3D(c[2], c[6], color);
        Raylib.DrawLine3D(c[3], c[7], color);
    }

    private void DrawTransformGizmo(SceneObject obj)
    {
        Vector3 origin = TransformData.ToRender(obj.Transform.Translation);
        float distToCam = Vector3.Distance(Camera.Position, origin);
        float len = Math.Clamp(distToCam * 0.16f, 0.4f, 50.0f);
        float stemLen = len * 0.8f;
        float stemRadius = len * 0.025f;
        float coneLen = len * 0.2f;
        float coneRadius = len * 0.065f;

        var (axisX, axisY, axisZ) = GetGizmoAxisDirections(obj);

        Color colX = (ActiveAxis == GizmoAxis.X || HoveredAxis == GizmoAxis.X) ? Color.Yellow : new Color(240, 50, 50, 255);
        Color colY = (ActiveAxis == GizmoAxis.Y || HoveredAxis == GizmoAxis.Y) ? Color.Yellow : new Color(50, 230, 50, 255);
        Color colZ = (ActiveAxis == GizmoAxis.Z || HoveredAxis == GizmoAxis.Z) ? Color.Yellow : new Color(50, 110, 255, 255);

        // Отключаем проверку глубины, чтобы Gizmo всегда рисовался поверх objects
        Rlgl.DisableDepthTest();

        // Ось X (Красная)
        Vector3 xStemEnd = origin + axisX * stemLen;
        Vector3 xTip = origin + axisX * len;
        Raylib.DrawCylinderEx(origin, xStemEnd, stemRadius, stemRadius, 8, colX);
        Raylib.DrawCylinderEx(xStemEnd, xTip, coneRadius, 0.0f, 12, colX);

        // Ось Y (Зеленая)
        Vector3 yStemEnd = origin + axisY * stemLen;
        Vector3 yTip = origin + axisY * len;
        Raylib.DrawCylinderEx(origin, yStemEnd, stemRadius, stemRadius, 8, colY);
        Raylib.DrawCylinderEx(yStemEnd, yTip, coneRadius, 0.0f, 12, colY);

        // Ось Z (Синяя)
        Vector3 zStemEnd = origin + axisZ * stemLen;
        Vector3 zTip = origin + axisZ * len;
        Raylib.DrawCylinderEx(origin, zStemEnd, stemRadius, stemRadius, 8, colZ);
        Raylib.DrawCylinderEx(zStemEnd, zTip, coneRadius, 0.0f, 12, colZ);

        // Центральная сфера пивота
        Raylib.DrawSphere(origin, stemRadius * 1.6f, Color.White);

        Rlgl.EnableDepthTest();
    }

    private bool IsSphereInFrustum(Vector3 center, float radius)
    {
        if (!frustumBasisValid || !float.IsFinite(radius)) return true;
        Vector3 toCenter = center - Camera.Position;
        float forwardDistance = Vector3.Dot(toCenter, frustumForward);
        if (forwardDistance < -radius || forwardDistance > _farClip + radius) return false;

        float visibleDepth = Math.Max(0, forwardDistance);
        float halfHeight = visibleDepth * frustumTanHalfVerticalFov + radius;
        float halfWidth = halfHeight * _viewportAspect;
        return MathF.Abs(Vector3.Dot(toCenter, frustumRight)) <= halfWidth
            && MathF.Abs(Vector3.Dot(toCenter, frustumUp)) <= halfHeight;
    }

    private void UpdateFrustumBasis()
    {
        frustumBasisValid = false;
        Vector3 forward = Camera.Target - Camera.Position;
        if (forward.LengthSquared() < 1e-8f || !float.IsFinite(Camera.FovY)
            || !float.IsFinite(_viewportAspect)) return;

        forward = Vector3.Normalize(forward);
        Vector3 right = Vector3.Cross(forward, Camera.Up);
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(forward, MathF.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY);
        right = Vector3.Normalize(right);
        frustumForward = forward;
        frustumRight = right;
        frustumUp = Vector3.Normalize(Vector3.Cross(right, forward));
        frustumTanHalfVerticalFov = MathF.Tan(Math.Clamp(Camera.FovY, .1f, 179.9f) * MathF.PI / 360);
        frustumBasisValid = true;
    }

    internal static bool IsSphereInViewFrustum(Vector3 center, float radius,
        Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUp,
        float verticalFovDegrees, float viewportAspect, double farClip)
    {
        Vector3 forward = cameraTarget - cameraPosition;
        if (forward.LengthSquared() < 1e-8f || !float.IsFinite(radius)
            || !float.IsFinite(verticalFovDegrees) || !float.IsFinite(viewportAspect)
            || !double.IsFinite(farClip)) return true;

        forward = Vector3.Normalize(forward);
        Vector3 right = Vector3.Cross(forward, cameraUp);
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(forward, MathF.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY);
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        Vector3 toCenter = center - cameraPosition;
        float forwardDistance = Vector3.Dot(toCenter, forward);
        if (forwardDistance < -radius || forwardDistance > farClip + radius) return false;

        float safeFov = Math.Clamp(verticalFovDegrees, .1f, 179.9f);
        float safeAspect = Math.Max(viewportAspect, .001f);
        float tangentHalfVerticalFov = MathF.Tan(safeFov * MathF.PI / 360);
        float visibleDepth = Math.Max(0, forwardDistance);
        float halfHeight = visibleDepth * tangentHalfVerticalFov + radius;
        float halfWidth = visibleDepth * tangentHalfVerticalFov * safeAspect + radius;
        return MathF.Abs(Vector3.Dot(toCenter, right)) <= halfWidth
            && MathF.Abs(Vector3.Dot(toCenter, up)) <= halfHeight;
    }

    private static double ClipDistanceForBounds(Vector3 camera, Vector3 min, Vector3 max)
    {
        double distance = Vector3.Distance(camera, (min + max) * .5f)
            + Vector3.Distance(min, max) * .5f;
        return double.IsFinite(distance) ? Math.Max(2000, distance * 1.05 + 10) : 2000;
    }

    /// <summary>
    /// Интеграционный UI-виджет Viewport в стиле UE5 для ImGui.
    /// </summary>
    public void DrawViewport(string id, Vector2 availableSize)
    {
        float scale = Math.Clamp(Raylib.GetWindowScaleDPI().X, 1.0f, 2.0f);
        Vector2 size = availableSize;
        size.X = Math.Max(120.0f, size.X);
        size.Y = Math.Max(120.0f, size.Y);

        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool isHovered = ImGui.IsItemHovered();
        bool isFocused = ImGui.IsItemFocused() || isHovered;

        Vector2 mouseInViewport = ImGui.GetMousePos() - origin;

        // Обновление логики
        Update(Raylib.GetFrameTime(), mouseInViewport, size, isHovered, isFocused);

        // Отрисовка кадра
        Render((int)size.X, (int)size.Y);
        long postEffectStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var displayTarget = RenderCinematicAfterEffects();
        StatPostEffectMilliseconds = (float)System.Diagnostics.Stopwatch
            .GetElapsedTime(postEffectStartedAt).TotalMilliseconds;
        StatRenderMilliseconds += StatPostEffectMilliseconds;

        // Вывод текстуры во фрейм ImGui
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImage(
            (nint)displayTarget.Texture.Id,
            origin,
            origin + size,
            new Vector2(0, 1),
            new Vector2(1, 0)
        );

        if (GameView) return;

        // Верхний HUD Overlay в стиле UE5
        ImGui.SetCursorScreenPos(origin + new Vector2(10 * scale, 10 * scale));
        ImGui.BeginGroup();

        // Индикатор выделенного объекта
        if (GizmoMode != TransformGizmoMode.Translate)
            ImGui.TextDisabled("Rotation and scale: Details fields. Move gizmo: W.");
        if (SelectedObject != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.35f, 0.9f, 0.54f, 1.0f), $"Selected: {SelectedObject.Name}");
            ImGui.SameLine();
            ImGui.TextDisabled($"({SelectedObject.GaoKey:X8})");

            if (!SelectedObject.Capabilities.CanTranslate)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.65f, 0.2f, 1.0f), $"[Restricted: {SelectedObject.Capabilities.RestrictionReason}]");
            }
        }

        ImGui.EndGroup();

        // Нижний правый угол: статистика и подсказки
        string skinStatus = CinematicMaterials == null ? $"Skin: {StatSkinUpdates}"
            : $"Skin: {StatSkinUpdates}/{StatSkinningMilliseconds:F1} ms";
        string statText = $"Frame: {Raylib.GetFrameTime() * 1000:F1} ms / {Raylib.GetFPS()} FPS · Objects: {StatVisibleObjects}/{StatTotalObjects} · Tris: {StatDrawnTriangles:N0} · {skinStatus} · Upload: {StatGeometryUploadsThisFrame} · World AI: {StatWorldActorUpdates}/{StatWorldActorUpdateMilliseconds:F1} ms · Cine: {StatCinematicUpdateMilliseconds:F1} ms · Audio: {StatCinematicAudioMilliseconds:F1} ms · Render: {StatRenderMilliseconds:F1} ms (prep {StatGeometryPreparationMilliseconds:F1}, draw {StatDrawPassMilliseconds:F1}, FX {StatPostEffectMilliseconds:F1}) · GPU cache: {StatCachedGeometries} GRO, {StatCachedTextures} TEX";
        Vector2 statSize = ImGui.CalcTextSize(statText);
        Vector2 statPos = origin + size - statSize - new Vector2(12 * scale, 10 * scale);
        drawList.AddRectFilled(statPos - new Vector2(4, 2), statPos + statSize + new Vector2(4, 2), 0xAA101612, 4.0f);
        drawList.AddText(statPos, 0xFF9AE5B4, statText);
    }

}
