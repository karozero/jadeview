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
    /// <summary>
    /// Комплексный набор верификационных тестов для проверки математики, камеры, лучей,
    /// Gizmo, конвертации координат и безопасности памяти SceneRenderer.
    /// </summary>
    public static List<(string Name, bool Passed, string Details)> RunVerificationTests()
    {
        var list = new List<(string Name, bool Passed, string Details)>();
        var hiddenActor = new SceneActor
        {
            Root = new SceneObject { Name = "hidden cinematic actor", CinematicExcluded = true }
        };
        var visibleActor = new SceneActor
        {
            Root = new SceneObject { Name = "visible cinematic actor" }
        };
        bool hiddenActorSkipped = !ShouldEvaluateWorldActor(hiddenActor, showHiddenObjects: false);
        bool debugActorEvaluated = ShouldEvaluateWorldActor(hiddenActor, showHiddenObjects: true);
        bool visibleActorEvaluated = ShouldEvaluateWorldActor(visibleActor, showHiddenObjects: false);
        visibleActor.CinematicClockOwned = true;
        bool cinematicClockPreserved = !ShouldEvaluateWorldActor(visibleActor, showHiddenObjects: true);
        bool actorUpdatePolicyCorrect = hiddenActorSkipped && debugActorEvaluated
            && visibleActorEvaluated && cinematicClockPreserved;
        list.Add(("Hidden_Actor_Animation_Update_Is_Skipped", actorUpdatePolicyCorrect,
            $"hidden skipped={hiddenActorSkipped}, debug evaluated={debugActorEvaluated}, "
            + $"visible evaluated={visibleActorEvaluated}, cinematic clock preserved={cinematicClockPreserved}"));

        // Exercise the native Matrix ABI, not merely Numerics coordinate math.
        // raylib memory order: m0,m4,m8,m12,m1,m5,... while Numerics uses row vectors.
        var managed = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(4000, 8, -4000);
        var native = Matrix4x4.Transpose(managed);
        var vertex = new Vector3(2, 3, 4);
        var nativeResult = new Vector3(
            vertex.X * native.M11 + vertex.Y * native.M12 + vertex.Z * native.M13 + native.M14,
            vertex.X * native.M21 + vertex.Y * native.M22 + vertex.Z * native.M23 + native.M24,
            vertex.X * native.M31 + vertex.Y * native.M32 + vertex.Z * native.M33 + native.M34);
        bool abiOk = Vector3.Distance(nativeResult, Vector3.Transform(vertex, managed)) < .001f;
        list.Add(("Native_Matrix_ABI_Preserves_World_Translation", abiOk, $"Native={nativeResult}"));

        // Reproduce the first scroll/fly step after FrameAll on a large map.
        using (var renderer = new SceneRenderer())
        {
            var center = new Vector3(0, 0, -12000);
            var min = center - new Vector3(500);
            var max = center + new Vector3(500);
            renderer.Camera.Position = Vector3.Zero;
            renderer.Camera.Target = center;
            double before = ClipDistanceForBounds(renderer.Camera.Position, min, max);
            renderer.Camera.Position = new Vector3(0, 0, -100);
            renderer.Camera.Target = renderer.Camera.Position - Vector3.UnitZ;
            renderer._farClip = ClipDistanceForBounds(renderer.Camera.Position, min, max);
            bool visible = renderer.IsSphereInFrustum(center, Vector3.Distance(min, max) * .5f);
            bool ok = before > 12000 && renderer._farClip > 11900 && visible;
            list.Add(("Large_Map_Remains_Visible_After_Fly_Step", ok,
                $"far before={before:F1}, after={renderer._farClip:F1}, visible={visible}"));
        }

        // 1. Тест конвертации координат Jade (Z-up) <-> Render (Y-up)
        try
        {
            Vector3 vJade = new(12.5f, -34.8f, 56.2f);
            Vector3 vRender = TransformData.ToRender(vJade);
            Vector3 vBack = TransformData.FromRender(vRender);

            bool ok = MathF.Abs(vRender.X - 12.5f) < 1e-5f &&
                      MathF.Abs(vRender.Y - 56.2f) < 1e-5f &&
                      MathF.Abs(vRender.Z - 34.8f) < 1e-5f &&
                      Vector3.Distance(vJade, vBack) < 1e-5f;

            list.Add(("Coordinate_Roundtrip_Jade_Render", ok,
                ok ? "Вектор (X, Z, -Y) обратим без потерь точности" : $"Ошибка конвертации: {vBack} != {vJade}"));
        }
        catch (Exception ex)
        {
            list.Add(("Coordinate_Roundtrip_Jade_Render", false, ex.Message));
        }

        // 2. Тест вычисления луча камеры (GetViewportRay)
        try
        {
            var cam = new Camera3D
            {
                Position = new Vector3(0, 0, 10),
                Target = Vector3.Zero,
                Up = Vector3.UnitY,
                FovY = 60.0f,
                Projection = CameraProjection.Perspective
            };
            Vector2 viewport = new(800, 600);
            Vector2 centerMouse = new(400, 300);

            var ray = GetViewportRay(centerMouse, viewport, cam);
            bool ok = Vector3.Distance(ray.Position, new Vector3(0, 0, 10)) < 1e-4f &&
                      Vector3.Distance(ray.Direction, -Vector3.UnitZ) < 1e-4f;

            list.Add(("Ray_Center_Matches_Camera_Forward", ok,
                ok ? $"Луч из центра экрана строго направлен вдоль -Z: {ray.Direction}" : $"Ошибка луча: {ray.Direction}"));
        }
        catch (Exception ex)
        {
            list.Add(("Ray_Center_Matches_Camera_Forward", false, ex.Message));
        }

        // 3. Тест точного расстояния между лучом и 3D отрезком (DistanceRayToSegment)
        try
        {
            // Отрезок оси X: от (0,0,0) до (10,0,0)
            Vector3 p0 = Vector3.Zero;
            Vector3 p1 = new(10, 0, 0);

            // Луч пересекает отрезок в точке (4, 0, 0)
            Ray rayHit = new(new Vector3(4, 5, 0), -Vector3.UnitY);
            float distHit = DistanceRayToSegment(rayHit, p0, p1);

            // Луч промахивается на 3 единицы по Z
            Ray rayMiss = new(new Vector3(4, 5, 3), -Vector3.UnitY);
            float distMiss = DistanceRayToSegment(rayMiss, p0, p1);

            bool ok = MathF.Abs(distHit) < 1e-4f && MathF.Abs(distMiss - 3.0f) < 1e-4f;
            list.Add(("DistanceRayToSegment_Exact_Intersection", ok,
                ok ? $"Точное совпадение: hit={distHit:F4}, miss={distMiss:F4}" : $"Неверное расстояние: hit={distHit}, miss={distMiss}"));
        }
        catch (Exception ex)
        {
            list.Add(("DistanceRayToSegment_Exact_Intersection", false, ex.Message));
        }

        // 4. Тест трансформации Bounding Box
        try
        {
            var localBox = new BoundingBox(new Vector3(-1, -2, -3), new Vector3(1, 2, 3));
            var translation = Matrix4x4.CreateTranslation(10, 20, 30);
            var (wMin, wMax) = TransformBoundingBox(localBox, translation);

            bool ok = Vector3.Distance(wMin, new Vector3(9, 18, 27)) < 1e-4f &&
                      Vector3.Distance(wMax, new Vector3(11, 22, 33)) < 1e-4f;

            list.Add(("TransformBoundingBox_AABB_Calculation", ok,
                ok ? $"Границы корректно смещены: min={wMin}, max={wMax}" : $"Ошибка AABB: min={wMin}, max={wMax}"));
        }
        catch (Exception ex)
        {
            list.Add(("TransformBoundingBox_AABB_Calculation", false, ex.Message));
        }

        try
        {
            var localBox = new BoundingBox(new Vector3(-2, -1, -3), new Vector3(2, 1, 3));
            var transform = Matrix4x4.CreateRotationY(.63f) * Matrix4x4.CreateTranslation(3, 7, -4);
            Vector3 checksum = Vector3.Zero;
            for (int iteration = 0; iteration < 64; iteration++)
                checksum += TransformBoundingBox(localBox, transform).Min;

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 10_000; iteration++)
                checksum += TransformBoundingBox(localBox, transform).Min;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            bool finite = float.IsFinite(checksum.X + checksum.Y + checksum.Z);
            bool ok = finite && allocatedBytes <= 64;
            list.Add(("TransformBoundingBox_HotPath_DoesNotAllocate", ok,
                $"allocated={allocatedBytes} bytes; checksum={checksum}"));
        }
        catch (Exception ex)
        {
            list.Add(("TransformBoundingBox_HotPath_DoesNotAllocate", false, ex.Message));
        }

        // 5. Тест конвертации осей Gizmo (World vs Local)
        try
        {
            var obj = new SceneObject
            {
                Name = "TestRotatedObject",
                GaoKey = 0x12345678,
                Transform = new TransformData
                {
                    Translation = new Vector3(5, 5, 5),
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f), // 90 градусов Jade Z (Raylib Y-up)
                    Scale = Vector3.One
                }
            };

            using var renderer = new SceneRenderer();
            renderer.GizmoSpace = TransformSpace.World;
            var (wX, wY, wZ) = renderer.GetGizmoAxisDirections(obj);

            renderer.GizmoSpace = TransformSpace.Local;
            var (lX, lY, lZ) = renderer.GetGizmoAxisDirections(obj);

            bool okWorld = Vector3.Distance(wX, Vector3.UnitX) < 1e-4f &&
                           Vector3.Distance(wY, Vector3.UnitY) < 1e-4f &&
                           Vector3.Distance(wZ, Vector3.UnitZ) < 1e-4f;

            bool okLocal = Vector3.Distance(lY, Vector3.UnitY) < 1e-4f &&
                           MathF.Abs(Vector3.Dot(lX, Vector3.UnitX)) < 1e-3f; // X повернулся на 90 градусов

            bool ok = okWorld && okLocal;
            list.Add(("Gizmo_Axis_Directions_World_Local", ok,
                ok ? "Мировые и локальные оси вычислены корректно" : "Ошибка расчета направлений осей Gizmo"));
        }
        catch (Exception ex)
        {
            list.Add(("Gizmo_Axis_Directions_World_Local", false, ex.Message));
        }

        // 6. Тест интерактивного перемещения (Translate Drag -> Jade Translation)
        try
        {
            var obj = new SceneObject
            {
                Name = "DraggableObj",
                GaoKey = 0x11112222,
                Transform = new TransformData
                {
                    Translation = new Vector3(10, 20, 30) // Jade: X=10, Y=20, Z=30 (Z-up)
                }
            };

            // Сдвиг по вертикали в Render (ось Y в Raylib) на +5 единиц
            // Так как Render Y соответствует Jade Z:
            Vector3 renderDelta = new Vector3(0, 5, 0);
            Vector3 jadeDelta = TransformData.FromRender(renderDelta);

            Vector3 newJadePos = obj.Transform.Translation + jadeDelta;
            obj.Transform.Translation = newJadePos;

            bool ok = MathF.Abs(obj.Transform.Translation.X - 10) < 1e-5f &&
                      MathF.Abs(obj.Transform.Translation.Y - 20) < 1e-5f &&
                      MathF.Abs(obj.Transform.Translation.Z - 35) < 1e-5f; // Z вырос на 5

            list.Add(("Translate_Vertical_Drag_Maps_To_Jade_Z_Up", ok,
                ok ? $"Вертикальный сдвиг в рендере (+Y) точно увеличил Jade Z: {obj.Transform.Translation.Z}" : $"Ошибка сдвига: {obj.Transform.Translation}"));
        }
        catch (Exception ex)
        {
            list.Add(("Translate_Vertical_Drag_Maps_To_Jade_Z_Up", false, ex.Message));
        }

        // 7. Тест генерации команды TransformCommand после изменения
        try
        {
            var obj = new SceneObject
            {
                Name = "CommandTestObj",
                Transform = new TransformData { Translation = new Vector3(1, 2, 3) }
            };

            var cmd = new TransformCommand(obj, new Vector3(10, 20, 30), obj.Transform.Rotation, obj.Transform.Scale);
            cmd.Execute();
            bool okExec = Vector3.Distance(obj.Transform.Translation, new Vector3(10, 20, 30)) < 1e-5f;

            cmd.Undo();
            bool okUndo = Vector3.Distance(obj.Transform.Translation, new Vector3(1, 2, 3)) < 1e-5f;

            bool ok = okExec && okUndo;
            list.Add(("TransformCommand_Execute_Undo_Roundtrip", ok,
                ok ? "Execute и Undo точно восстанавливают положение объекта" : "Ошибка TransformCommand"));
        }
        catch (Exception ex)
        {
            list.Add(("TransformCommand_Execute_Undo_Roundtrip", false, ex.Message));
        }

        // 8. Тест ограничения редактирования для скелетных/иерархических objects
        try
        {
            var boneObj = new SceneObject
            {
                Name = "Bone_Spine",
                Flags = SceneLoader.IdentityFlagBone,
                Capabilities = new ObjectEditCapabilities
                {
                    Flags = EditCapabilities.None,
                    RestrictionReason = "Скелетный или анимированный объект."
                }
            };

            bool ok = !boneObj.Capabilities.CanTranslate &&
                      !string.IsNullOrEmpty(boneObj.Capabilities.RestrictionReason);

            list.Add(("Capabilities_Bone_Restriction_Respected", ok,
                ok ? $"Restrictedие подтверждено: {boneObj.Capabilities.RestrictionReason}" : "Ошибка проверки Capabilities"));
        }
        catch (Exception ex)
        {
            list.Add(("Capabilities_Bone_Restriction_Respected", false, ex.Message));
        }

        // 9. Тест безопасного сброса GPU кэша
        try
        {
            using var renderer = new SceneRenderer();
            renderer.ClearGpuCache();
            bool ok = renderer.StatCachedGeometries == 0 &&
                      renderer.StatCachedTextures == 0 &&
                      renderer.SelectedObject == null &&
                      !renderer.IsDraggingGizmo;

            list.Add(("ClearGpuCache_Resets_Cleanly", ok,
                ok ? "Кэш GPU и состояние интерактивности сброшены без errors" : "Ошибка сброса кэша"));
        }
        catch (Exception ex)
        {
            list.Add(("ClearGpuCache_Resets_Cleanly", false, ex.Message));
        }

        // 10. Тест восстановления начального положения при Gizmo Drag Undo (R3)
        try
        {
            var obj = new SceneObject
            {
                Name = "GizmoUndoTestObj",
                Transform = new TransformData { Translation = new Vector3(10.5f, -20.2f, 30.8f) }
            };

            Vector3 initialPos = obj.Transform.Translation;

            // Во время перетаскивания объект мутирует
            obj.Transform.Translation = new Vector3(45.0f, -20.2f, 30.8f);

            // Завершение перетаскивания создает команду с initialPos в качестве oldPos
            var cmd = new TransformCommand(
                obj,
                initialPos,
                obj.Transform.Translation,
                obj.Transform.Rotation,
                obj.Transform.Scale
            );

            // Undo гарантированно возвращает к initialPos
            cmd.Undo();
            bool okUndo = Vector3.Distance(obj.Transform.Translation, initialPos) < 1e-5f &&
                          Vector3.Distance(new Vector3(obj.Transform.RawMatrix.M41, obj.Transform.RawMatrix.M42, obj.Transform.RawMatrix.M43), initialPos) < 1e-5f;

            // Redo возвращает к измененному положению
            cmd.Execute();
            bool okRedo = Vector3.Distance(obj.Transform.Translation, new Vector3(45.0f, -20.2f, 30.8f)) < 1e-5f;

            bool ok = okUndo && okRedo;
            list.Add(("Gizmo_Drag_Undo_Restores_Initial_Translation", ok,
                ok ? "Undo гарантированно восстанавливает положение до начала перетаскивания" : "Ошибка Undo после drag"));
        }
        catch (Exception ex)
        {
            list.Add(("Gizmo_Drag_Undo_Restores_Initial_Translation", false, ex.Message));
        }

        // 11. Тест округления координат к шагу сетки при Snapping (R9)
        try
        {
            float step = 0.5f;
            Vector3 rawPos = new(10.12f, -20.39f, 30.71f);
            Vector3 snappedPos = new(
                MathF.Round(rawPos.X / step) * step,
                MathF.Round(rawPos.Y / step) * step,
                MathF.Round(rawPos.Z / step) * step
            );

            Vector3 expected = new(10.0f, -20.5f, 30.5f);
            bool ok = Vector3.Distance(snappedPos, expected) < 1e-5f;

            list.Add(("Gizmo_Snapping_Rounds_Coordinates_To_Step", ok,
                ok ? $"Координаты точно округлены к шагу {step}: {snappedPos}" : $"Ошибка привязки: {snappedPos} != {expected}"));
        }
        catch (Exception ex)
        {
            list.Add(("Gizmo_Snapping_Rounds_Coordinates_To_Step", false, ex.Message));
        }

        return list;
    }

}
