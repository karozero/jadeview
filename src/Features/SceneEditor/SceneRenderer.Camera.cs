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
    public readonly record struct ViewportCameraState(Camera3D Camera, float Yaw, float Pitch,
        float MoveSpeed, float? CinematicHorizontalFov);

    public SceneRenderer()
    {
        InitCamera();
    }

    private void InitCamera()
    {
        Camera = new Camera3D
        {
            Position = new Vector3(25.0f, 15.0f, 25.0f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            FovY = 50.0f,
            Projection = CameraProjection.Perspective
        };
        Vector3 dir = Vector3.Normalize(Camera.Target - Camera.Position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.99f, 0.99f));
    }

    public ViewportCameraState CaptureViewportCameraState() =>
        new(Camera, Yaw, Pitch, MoveSpeed, CinematicHorizontalFov);

    public void RestoreViewportCameraState(ViewportCameraState state)
    {
        Camera = state.Camera;
        Yaw = state.Yaw;
        Pitch = state.Pitch;
        MoveSpeed = state.MoveSpeed;
        CinematicHorizontalFov = state.CinematicHorizontalFov;
    }

    /// <summary>Synchronizes the free-look camera basis with its current view direction.</summary>
    public void SynchronizeManualCameraOrientation()
    {
        Vector3 forward = Camera.Target - Camera.Position;
        if (forward.LengthSquared() < 1e-6f) return;
        forward = Vector3.Normalize(forward);
        Camera.Up = Vector3.UnitY;
        Camera.Target = Camera.Position + forward;
        Yaw = MathF.Atan2(forward.X, forward.Z);
        Pitch = MathF.Asin(Math.Clamp(forward.Y, -.99f, .99f));
    }

    public void ApplyManualCameraLook(Vector2 mouseDelta)
    {
        Yaw -= mouseDelta.X * .003f;
        Pitch = Math.Clamp(Pitch - mouseDelta.Y * .003f, -1.52f, 1.52f);
        Vector3 forward = new(MathF.Sin(Yaw) * MathF.Cos(Pitch),
            MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch));
        Camera.Target = Camera.Position + forward;
        Camera.Up = Vector3.UnitY;
    }

    private void UpdateFlyingCamera(float deltaTime)
    {
        Vector2 mouseDelta = Raylib.GetMouseDelta();
        ApplyManualCameraLook(mouseDelta);
        MoveSpeed = Math.Clamp(MoveSpeed * MathF.Pow(1.25f, Raylib.GetMouseWheelMove()), 0.05f, 2000.0f);
        Vector3 forward = Vector3.Normalize(Camera.Target - Camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        float speed = MoveSpeed * deltaTime * (Raylib.IsKeyDown(KeyboardKey.LeftShift) ? 3.5f : 1.0f);
        if (Raylib.IsKeyDown(KeyboardKey.W)) Camera.Position += forward * speed;
        if (Raylib.IsKeyDown(KeyboardKey.S)) Camera.Position -= forward * speed;
        if (Raylib.IsKeyDown(KeyboardKey.D)) Camera.Position += right * speed;
        if (Raylib.IsKeyDown(KeyboardKey.A)) Camera.Position -= right * speed;
        if (Raylib.IsKeyDown(KeyboardKey.E)) Camera.Position += Vector3.UnitY * speed;
        if (Raylib.IsKeyDown(KeyboardKey.Q)) Camera.Position -= Vector3.UnitY * speed;
        Camera.Target = Camera.Position + forward;
        Camera.Up = Vector3.UnitY;
    }

    /// <summary>Applies a world-space camera pose while preserving the current viewing distance.</summary>
    public bool TrySetCameraPose(Matrix4x4 pose, float? horizontalFov = null)
    {
        float determinant = pose.GetDeterminant();
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-8f
            || !Matrix4x4.Decompose(pose, out _, out var rotation, out var position)
            || rotation.LengthSquared() < 1e-6f
            || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) return false;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);
        if (forward.LengthSquared() < 1e-6f || up.LengthSquared() < 1e-6f) return false;

        float distance = Math.Clamp(Vector3.Distance(Camera.Position, Camera.Target), 1, 1000);
        forward = Vector3.Normalize(forward);
        Camera.Position = position;
        Camera.Target = position + forward * distance;
        Camera.Up = Vector3.Normalize(up);
        CinematicHorizontalFov = horizontalFov;
        Yaw = MathF.Atan2(forward.X, forward.Z);
        Pitch = MathF.Asin(Math.Clamp(forward.Y, -.99f, .99f));
        return true;
    }

    /// Преобразование экранных координат мыши во вьюпорте в 3D Ray.
    /// </summary>
    public static Ray GetViewportRay(Vector2 mouseInViewport, Vector2 viewportSize, Camera3D camera)
    {
        float fov = camera.FovY * MathF.PI / 180.0f;
        float aspect = viewportSize.X / Math.Max(1.0f, viewportSize.Y);
        float halfHeight = MathF.Tan(fov * 0.5f);
        float halfWidth = halfHeight * aspect;

        float ndcX = (2.0f * mouseInViewport.X / Math.Max(1.0f, viewportSize.X)) - 1.0f;
        float ndcY = 1.0f - (2.0f * mouseInViewport.Y / Math.Max(1.0f, viewportSize.Y));

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        Vector3 up = Vector3.Cross(right, forward);

        Vector3 dir = Vector3.Normalize(forward + right * (ndcX * halfWidth) + up * (ndcY * halfHeight));
        return new Ray(camera.Position, dir);
    }

    /// <summary>
    /// Точное вычисление расстояния между 3D лучом и 3D отрезком для надежного выбора осей Gizmo.
    /// </summary>
    public static float DistanceRayToSegment(Ray ray, Vector3 p0, Vector3 p1)
    {
        Vector3 u = ray.Direction;
        Vector3 v = p1 - p0;
        Vector3 w = ray.Position - p0;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);

        float D = a * c - b * b;
        float sN, sD = D;
        float tN, tD = D;

        if (D < 1e-6f)
        {
            sN = 0.0f;
            sD = 1.0f;
            tN = e;
            tD = c;
        }
        else
        {
            sN = (b * e - c * d);
            tN = (a * e - b * d);
            if (sN < 0.0f)
            {
                sN = 0.0f;
                tN = e;
                tD = c;
            }
        }

        if (tN < 0.0f)
        {
            tN = 0.0f;
            if (-d < 0.0f) sN = 0.0f;
            else if (-d > a) sN = sD;
            else sN = -d;
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-d + b) < 0.0f) sN = 0.0f;
            else if ((-d + b) > a) sN = sD;
            else sN = (-d + b);
        }

        float sc = (MathF.Abs(sN) < 1e-6f ? 0.0f : sN / sD);
        float tc = (MathF.Abs(tN) < 1e-6f ? 0.0f : tN / tD);

        Vector3 dP = w + (sc * u) - (tc * v);
        return dP.Length();
    }

    /// <summary>
    /// Преобразование локального BoundingBox в мировой AABB через произвольную матрицу.
    /// </summary>
    public static (Vector3 Min, Vector3 Max) TransformBoundingBox(BoundingBox local, Matrix4x4 matrix)
    {
        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(local.Min.X, local.Min.Y, local.Min.Z),
            new(local.Max.X, local.Min.Y, local.Min.Z),
            new(local.Min.X, local.Max.Y, local.Min.Z),
            new(local.Max.X, local.Max.Y, local.Min.Z),
            new(local.Min.X, local.Min.Y, local.Max.Z),
            new(local.Max.X, local.Min.Y, local.Max.Z),
            new(local.Min.X, local.Max.Y, local.Max.Z),
            new(local.Max.X, local.Max.Y, local.Max.Z)
        };

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (var corner in corners)
        {
            var tc = Vector3.Transform(corner, matrix);
            min = Vector3.Min(min, tc);
            max = Vector3.Max(max, tc);
        }

        return (min, max);
    }

    /// <summary>
    /// Кадрирование камеры на конкретном объекте сцены (интеграция с Outliner/UI).
    /// </summary>
    public void FrameObject(SceneObject obj, ResourceResolver? resolver = null)
    {
        if (resolver != null) Resolver = resolver;
        SelectedObject = obj;
        FrameSelected();
    }

    /// <summary>
    /// Кадрирование (фокус по клавише F) на выбранном объекте.
    /// </summary>
    public void FrameSelected()
    {
        if (SelectedObject == null)
        {
            FrameAll();
            return;
        }

        Vector3 center;
        float radius = 3.0f;

        if (SelectedObject.Actor is { } actor && SelectedObject == actor.Root)
        {
            actor.Evaluate(0); Vector3 low = new(float.MaxValue), high = new(float.MinValue); int count = 0;
            foreach (var part in actor.Members)
            {
                var shape = GetOrUploadGeometry(part.RenderGeometryKey); if (shape == null || IsGameHidden(part)) continue;
                UpdateSkin(shape, part); var bounds = TransformBoundingBox(shape.LocalBounds, part.RenderMatrix);
                low = Vector3.Min(low, bounds.Item1); high = Vector3.Max(high, bounds.Item2); count++;
            }
            center = count > 0 ? (low + high) * .5f : TransformData.ToRender(SelectedObject.Transform.Translation);
            radius = count > 0 ? Math.Max((high - low).Length() * .5f, .5f) : 3;
        }
        else if (GetOrUploadGeometry(SelectedObject.RenderGeometryKey) is { } geo)
        {
            var m = SelectedObject.RenderMatrix;
            var (wMin, wMax) = TransformBoundingBox(geo.LocalBounds, m);
            center = (wMin + wMax) * 0.5f;
            radius = Math.Max(1.0f, Vector3.Distance(wMin, wMax) * 0.5f);
        }
        else
        {
            center = TransformData.ToRender(SelectedObject.Transform.Translation);
        }

        Camera.Target = center;
        Vector3 offset = new(radius * 1.6f, radius * 1.1f, radius * 2.2f);
        Camera.Position = center + offset;
        Camera.Up = Vector3.UnitY;

        Vector3 dir = Vector3.Normalize(Camera.Target - Camera.Position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.99f, 0.99f));
        MoveSpeed = Math.Clamp(radius * 2.5f, 0.5f, 150.0f);
    }

    /// <summary>
    /// Кадрирование на всей сцене (всех объектах активного мира).
    /// </summary>
    public void FrameAll()
    {
        var world = ActiveWorld ?? Document?.ActiveWorld;
        if (world == null || world.Objects.Count == 0)
        {
            InitCamera();
            return;
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        int validCount = 0;

        foreach (var obj in world.Objects)
        {
            if (IsHidden(obj) || IsGameHidden(obj)
                || !TryGetGeometryBounds(obj.RenderGeometryKey, out var localBounds)) continue;
            var (worldMinimum, worldMaximum) = TransformBoundingBox(localBounds, obj.RenderMatrix);
            min = Vector3.Min(min, worldMinimum);
            max = Vector3.Max(max, worldMaximum);
            validCount++;
        }

        if (validCount == 0)
        {
            InitCamera();
            return;
        }

        Vector3 center = (min + max) * 0.5f;
        float radius = Math.Max(2.0f, Vector3.Distance(min, max) * 0.5f);

        Camera.Target = center;
        Camera.Position = center + new Vector3(radius * 1.5f, radius * 1.2f, radius * 2.0f);
        Camera.Up = Vector3.UnitY;

        Vector3 dir = Vector3.Normalize(Camera.Target - Camera.Position);
        Yaw = MathF.Atan2(dir.X, dir.Z);
        Pitch = MathF.Asin(Math.Clamp(dir.Y, -0.99f, 0.99f));
        MoveSpeed = Math.Clamp(radius * 1.5f, 1.0f, 250.0f);
    }

    /// <summary>
    /// Обновление камеры свободного полета (UE5: ПКМ + WASD/QE + Shift, колесо для зума).
    /// </summary>
    public void UpdateCamera(bool isHovered)
    {
        float dt = Raylib.GetFrameTime();
        UpdateCameraMouseCapture(isHovered);

        if (IsFlying)
        {
            UpdateFlyingCamera(dt);
        }
        else if (isHovered)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (MathF.Abs(wheel) > 0.01f)
            {
                Vector3 forward = Vector3.Normalize(Camera.Target - Camera.Position);
                Camera.Position += forward * (wheel * MoveSpeed * 0.35f);
                Camera.Target = Camera.Position + forward;
            }
        }
    }

    /// <summary>
    /// Выбор объекта лучом мыши (Ray casting / Picking) по Bounding Box (интеграция с UI/A5).
    /// </summary>
    public SceneObject? PickObject(Vector2 mouseScreen, Vector2 viewportOrigin, Vector2 viewportSize, WorldData? world, ResourceResolver? resolver = null)
    {
        if (resolver != null) Resolver = resolver;
        Vector2 mouseInViewport = mouseScreen - viewportOrigin;
        Ray ray = GetViewportRay(mouseInViewport, viewportSize, Camera);

        var selected = TraceGeometry(ray, world ?? ActiveWorld ?? Document?.ActiveWorld);
        SelectedObject = selected;
        OnSelectionChanged?.Invoke(selected);
        return selected;
    }

    // Wire collision surfaces do not block selection of visible geometry inside them.
    public SceneObject? TraceGeometry(Ray ray, WorldData? world)
    {
        EnsureGpuInitialized();
        SceneObject? visual = null, collision = null;
        float closest = float.PositiveInfinity, collisionDistance = float.PositiveInfinity;
        if (world == null) return null;
        foreach (var obj in world.Objects)
        {
            if (obj.GeometryKey == 0 || IsHidden(obj) || IsGameHidden(obj) || IsCollisionHidden(obj)) continue;
            var geo = GetOrUploadGeometry(obj.RenderGeometryKey);
            if (geo == null) continue;
            UpdateSkin(geo, obj);
            var matrix = obj.RenderMatrix;
            var (min, max) = TransformBoundingBox(geo.LocalBounds, matrix);
            if (!Raylib.GetRayCollisionBox(ray, new BoundingBox(min, max)).Hit) continue;
            bool isCollision = IsCollision(obj);
            foreach (var mesh in geo.PartMeshes)
            {
                var hit = Raylib.GetRayCollisionMesh(ray, mesh, Matrix4x4.Transpose(matrix));
                if (!hit.Hit || hit.Distance < 0) continue;
                if (isCollision) { if (hit.Distance < collisionDistance) { collisionDistance = hit.Distance; collision = obj; } }
                else if (hit.Distance < closest) { closest = hit.Distance; visual = obj; }
            }
        }
        var selected = visual ?? collision;
        return selected?.Actor?.Root ?? selected;
    }

    /// <summary>
    /// Обновление камеры, выбор объекта лучом и интерактивное перетаскивание Gizmo.
    /// </summary>
    public void Update(float dt, Vector2 mouseInViewport, Vector2 viewportSize, bool isHovered, bool isFocused)
    {
        EnsureGpuInitialized();

        Ray ray = GetViewportRay(mouseInViewport, viewportSize, Camera);

        // 1. Интерактивное перемещение через 3D Gizmo
        if (IsDraggingGizmo && SelectedObject != null)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                // Пересечение текущего луча с плоскостью перетаскивания
                float denom = Vector3.Dot(ray.Direction, _dragPlaneNormal);
                if (MathF.Abs(denom) > 1e-5f)
                {
                    float t = Vector3.Dot(_initialDragHitPoint - ray.Position, _dragPlaneNormal) / denom;
                    Vector3 currentHitPoint = ray.Position + ray.Direction * t;
                    Vector3 deltaWorld = currentHitPoint - _initialDragHitPoint;

                    var (axisX, axisY, axisZ) = GetGizmoAxisDirections(SelectedObject);
                    Vector3 activeAxisDir = ActiveAxis switch
                    {
                        GizmoAxis.X => axisX,
                        GizmoAxis.Y => axisY,
                        GizmoAxis.Z => axisZ,
                        _ => Vector3.Zero
                    };

                    float shiftAlongAxis = Vector3.Dot(deltaWorld, activeAxisDir);
                    if (SnappingEnabled && SnappingStep > 1e-4f)
                        shiftAlongAxis = MathF.Round(shiftAlongAxis / SnappingStep) * SnappingStep;
                    Vector3 newRenderPos = _initialRenderPos + activeAxisDir * shiftAlongAxis;
                    Vector3 newJadePos = TransformData.FromRender(newRenderPos);

                    SelectedObject.Transform.Translation = newJadePos;

                    // Синхронизируем строку переноса в RawMatrix
                    var raw = SelectedObject.Transform.RawMatrix;
                    raw.M41 = newJadePos.X;
                    raw.M42 = newJadePos.Y;
                    raw.M43 = newJadePos.Z;
                    SelectedObject.Transform.RawMatrix = raw;
                }
            }
            else
            {
                // Завершение перетаскивания: создаем команду в стек Undo/Redo
                // со старыми координатами из _initialObjectTranslation (до начала drag) для гарантированного возврата при Undo (R3)
                IsDraggingGizmo = false;
                ActiveAxis = GizmoAxis.None;

                var cmd = new TransformCommand(
                    SelectedObject,
                    _initialObjectTranslation,
                    SelectedObject.Transform.Translation,
                    SelectedObject.Transform.Rotation,
                    SelectedObject.Transform.Scale
                );
                OnObjectTransformed?.Invoke(SelectedObject, cmd);
            }
            return;
        }

        // 2. Определение наведения луча на оси Gizmo
        HoveredAxis = GizmoAxis.None;
        if (SelectedObject != null && !IsHidden(SelectedObject) && !IsCollisionHidden(SelectedObject) && ShowGizmo && !GameView && GizmoMode == TransformGizmoMode.Translate)
        {
            Vector3 gizmoPos = TransformData.ToRender(SelectedObject.Transform.Translation);
            float distToCam = Vector3.Distance(Camera.Position, gizmoPos);
            float gizmoLen = Math.Clamp(distToCam * 0.16f, 0.4f, 50.0f);
            float hitThreshold = gizmoLen * 0.14f;

            var (axisX, axisY, axisZ) = GetGizmoAxisDirections(SelectedObject);

            float dX = DistanceRayToSegment(ray, gizmoPos, gizmoPos + axisX * gizmoLen);
            float dY = DistanceRayToSegment(ray, gizmoPos, gizmoPos + axisY * gizmoLen);
            float dZ = DistanceRayToSegment(ray, gizmoPos, gizmoPos + axisZ * gizmoLen);

            float minD = hitThreshold;
            if (dX < minD) { minD = dX; HoveredAxis = GizmoAxis.X; }
            if (dY < minD) { minD = dY; HoveredAxis = GizmoAxis.Y; }
            if (dZ < minD) { minD = dZ; HoveredAxis = GizmoAxis.Z; }
        }

        // 3. Старт перетаскивания Gizmo при нажатии ЛКМ
        if (isHovered && HoveredAxis != GizmoAxis.None && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (!GameView && SelectedObject != null && SelectedObject.Capabilities.CanTranslate)
            {
                IsDraggingGizmo = true;
                ActiveAxis = HoveredAxis;
                _initialObjectTranslation = SelectedObject.Transform.Translation;
                _initialRenderPos = TransformData.ToRender(_initialObjectTranslation);

                var (axisX, axisY, axisZ) = GetGizmoAxisDirections(SelectedObject);
                Vector3 activeAxisDir = ActiveAxis switch
                {
                    GizmoAxis.X => axisX,
                    GizmoAxis.Y => axisY,
                    GizmoAxis.Z => axisZ,
                    _ => Vector3.UnitY
                };

                Vector3 toCam = Vector3.Normalize(Camera.Position - _initialRenderPos);
                Vector3 planeN = Vector3.Normalize(Vector3.Cross(Vector3.Cross(activeAxisDir, toCam), activeAxisDir));
                if (planeN.LengthSquared() < 0.01f) planeN = toCam;
                _dragPlaneNormal = planeN;

                float denom = Vector3.Dot(ray.Direction, _dragPlaneNormal);
                if (MathF.Abs(denom) > 1e-5f)
                {
                    float t = Vector3.Dot(_initialRenderPos - ray.Position, _dragPlaneNormal) / denom;
                    _initialDragHitPoint = ray.Position + ray.Direction * t;
                }
                else
                {
                    _initialDragHitPoint = _initialRenderPos;
                }
                return;
            }
        }

        // 4. Свободная камера в стиле UE5 (зажатая ПКМ + WASD/QE + Shift)
        UpdateCameraMouseCapture(isHovered);

        if (IsFlying)
        {
            UpdateFlyingCamera(dt);
        }
        else
        {
            // Приближение колесом без полета
            if (isHovered)
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (MathF.Abs(wheel) > 0.01f)
                {
                    Vector3 forward = Vector3.Normalize(Camera.Target - Camera.Position);
                    Camera.Position += forward * (wheel * MoveSpeed * 0.35f);
                    Camera.Target = Camera.Position + forward;
                }
            }
        }

        if (isFocused && !ImGui.GetIO().WantTextInput && Raylib.IsKeyPressed(KeyboardKey.G)) ToggleGameView();

        // 5. Кадрирование (F)
        if (isHovered && Raylib.IsKeyPressed(KeyboardKey.F))
        {
            FrameSelected();
        }

        // 6. Выбор объекта лучом мыши (Ray casting / Picking) по ЛКМ
        if (isHovered && !IsFlying && Raylib.IsMouseButtonPressed(MouseButton.Left) && HoveredAxis == GizmoAxis.None)
        {
            SelectedObject = TraceGeometry(ray, ActiveWorld ?? Document?.ActiveWorld);
            OnSelectionChanged?.Invoke(SelectedObject);
        }
    }

    private (Vector3 X, Vector3 Y, Vector3 Z) GetGizmoAxisDirections(SceneObject obj)
    {
        if (GizmoSpace == TransformSpace.Local)
        {
            var m = obj.RenderMatrix;
            var x = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m));
            var y = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m));
            var z = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m));
            return (x, y, z);
        }

        return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
    }

}
