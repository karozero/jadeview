using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    #region 4. Details (Панель свойств справа снизу)

    private void DrawDetails()
    {
        DrawCategoryTitle("Identity", "Object identifiers and source record information.");

        if (SelectedObject == null)
        {
            ImGui.TextDisabled("Select an object in the Outliner or viewport.");
            return;
        }

        var obj = SelectedObject;

        // Идентификация
        ImGui.TextUnformatted(obj.Name);
        ImGui.TextDisabled($"GaoKey: 0x{obj.GaoKey:X8} | ID: {obj.Id}");
        ImGui.TextDisabled($"Record: offset 0x{obj.RecordRange.Offset:X8}, Size {obj.RecordRange.Size} B");

        DrawCategoryTitle("Editing capabilities");

        // Status возможностей редактирования (Capabilities)
        if (obj.Capabilities.CanTranslate)
        {
            ImGui.TextColored(Green, obj.IsUserCreated ? "Editor object: transform will be written to a new GAO" : "Position editing available (In-place patch)");
        }
        else
        {
            ImGui.TextColored(Yellow, $"Restriction: {obj.Capabilities.RestrictionReason}");
        }

        DrawCategoryTitle("Transform", "Editable controls depend on the object's capabilities; restricted controls are disabled.");

        // Редактирование координат Translation
        Vector3 curPos = obj.Transform.Translation;

        ImGui.BeginDisabled(!obj.Capabilities.CanTranslate);
        if (ImGui.DragFloat3("Position X/Y/Z", ref curPos, SnappingEnabled ? SnappingStep : 0.1f))
        {
            if (SnappingEnabled && SnappingStep > 1e-4f)
            {
                curPos = new Vector3(
                    MathF.Round(curPos.X / SnappingStep) * SnappingStep,
                    MathF.Round(curPos.Y / SnappingStep) * SnappingStep,
                    MathF.Round(curPos.Z / SnappingStep) * SnappingStep
                );
            }
            var cmd = new TransformCommand(obj, curPos, obj.Transform.Rotation, obj.Transform.Scale);
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        }
        ImGui.EndDisabled();

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(obj.Transform.Rotation);
        Vector3 angles = new(
            MathF.Asin(Math.Clamp(-rotationMatrix.M32, -1, 1)),
            MathF.Atan2(rotationMatrix.M31, rotationMatrix.M33),
            MathF.Atan2(rotationMatrix.M12, rotationMatrix.M22));
        angles *= 180 / MathF.PI;
        ImGui.BeginDisabled(!obj.Capabilities.CanRotate);
        if (ImGui.DragFloat3("Rotation X/Y/Z (degrees)", ref angles, 0.5f))
        {
            angles *= MathF.PI / 180;
            var rotation = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z));
            History.Execute(new TransformCommand(obj, obj.Transform.Translation, rotation, obj.Transform.Scale));
        }
        ImGui.EndDisabled();

        // Масштабирование Scale
        Vector3 curScale = obj.Transform.Scale;
        ImGui.BeginDisabled(!obj.Capabilities.CanScale);
        if (ImGui.DragFloat3("Scale X/Y/Z", ref curScale, 0.05f, 0.01f, 100.0f))
        {
            var cmd = new TransformCommand(obj, obj.Transform.Translation, obj.Transform.Rotation, curScale);
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        }
        ImGui.EndDisabled();
        bool canReset = obj.Capabilities.CanTranslate || obj.Capabilities.CanRotate || obj.Capabilities.CanScale;
        ImGui.BeginDisabled(!canReset);
        if (ImGui.SmallButton("Reset to zero"))
        {
            Vector3 targetPos = obj.Capabilities.CanTranslate ? Vector3.Zero : obj.Transform.Translation;
            Quaternion targetRot = obj.Capabilities.CanRotate ? Quaternion.Identity : obj.Transform.Rotation;
            Vector3 targetScale = obj.Capabilities.CanScale ? Vector3.One : obj.Transform.Scale;

            var cmd = new TransformCommand(obj, targetPos, targetRot, targetScale);
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        }
        ImGui.EndDisabled();

        DrawCategoryTitle("Geometry");

        // Геометрия
        ImGui.TextUnformatted($"Geometry GRO: 0x{obj.GeometryKey:X8}");
        if (Resolver.Geometries.TryGetValue(obj.GeometryKey, out var geo))
        {
            ImGui.TextDisabled($"Vertices: {geo.Positions.Length:N0} · Triangles: {geo.Triangles:N0} · Parts: {geo.Parts.Count}");
        }
        else
        {
            ImGui.TextDisabled("No geometry, or this is a helper object.");
        }

        DrawCategoryTitle("Material");
        // Материал
        ImGui.TextUnformatted($"Material GRM: 0x{obj.MaterialKey:X8}");
        if (Resolver.Materials.TryGetValue(obj.MaterialKey, out var mat))
        {
            ImGui.TextDisabled($"Type: {(mat.Type == 4 ? "MultiMaterial" : "MultiTexture")} · Submaterials: {mat.SubMaterialKeys.Count} · Textures: {mat.TextureKeys.Count}");
        }
        else
        {
            ImGui.TextDisabled("No material (default/fallback used).");
        }
    }

    #endregion
}
