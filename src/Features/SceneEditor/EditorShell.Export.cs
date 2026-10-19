using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Forms = System.Windows.Forms;

namespace JadeView;
public sealed partial class EditorShell
{
    private void DrawExportDialog()
    {
        if (_openExport)
        {
            ImGui.OpenPopup("Export GLB");
            _openExport = false;
        }

        ImGui.SetNextWindowSize(new Vector2(460 * _scale, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Export GLB", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        DrawPanelTitle("EXPORT GLB", "Export the current world or selected asset as a Blender-compatible GLB file.");
        DrawCategoryTitle("Target");
        ImGui.TextWrapped(_exportTarget == null ? $"Level: {ActiveWorld?.Name}" : $"Object: {_exportTarget.Name}");
        if (_exportTarget?.Actor is { } actor && _exportTarget == actor.Root)
            ImGui.TextDisabled($"Actor with {actor.Members.Count - 1} components");
        DrawCategoryTitle("Assets");
        ImGui.Checkbox("Embed textures", ref _exportOptions.Textures);
        if (_exportTarget == null)
            ImGui.Checkbox("Include actors", ref _exportOptions.Actors);
        ImGui.Checkbox("Include collision meshes", ref _exportOptions.Collisions);
        ImGui.Checkbox("Include unknown GAOs", ref _exportOptions.UnknownObjects);
        if (_exportOptions.UnknownObjects)
            ImGui.TextWrapped("GAOs without decoded geometry become named empty objects with their source keys.");
        ImGui.Checkbox("Include game-hidden objects", ref _exportOptions.GameHidden);
        DrawCategoryTitle("Animation");
        bool hasAnimations = _exportTarget?.Actor != null || _exportTarget == null && ActiveWorld?.Actors.Count > 0;
        ImGui.BeginDisabled(!hasAnimations);
        ImGui.Checkbox("Animations and skinning", ref _exportOptions.Animations);
        if (!_exportOptions.Animations) _exportOptions.ExperimentalMouthCycle = false;
        if (_exportOptions.Animations)
        {
            ImGui.Checkbox("All clips (otherwise current / first)", ref _exportOptions.AllClips);
            ImGui.Checkbox("Manual morph animations", ref _exportOptions.MorphAnimations);
            ImGui.Checkbox("Active experimental mouth cycle", ref _exportOptions.ExperimentalMouthCycle);
            if (_exportOptions.ExperimentalMouthCycle)
            {
                ImGui.SliderFloat("Mouth clip duration (s)", ref _exportOptions.MouthCycleDuration, .25f, 10, "%.2f");
                ImGui.TextWrapped("Start a supported mouth preview first. Exports a separate simulated clip from its current state, without dialogue synchronization.");
            }
        }
        ImGui.EndDisabled();
        DrawCategoryTitle("Placement");
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##ExportPlacement", ref _exportOptions.Placement, new[] { "Current placement", "Original game placement", "At origin" }, 3);
        ImGui.TextWrapped("One .glb file for Blender. Jade multi-pass effects are simplified; animations use the current mesh appearance.");
        DrawCategoryTitle("Output");
        if (ImGui.Button("Export...", new Vector2(120 * _scale, 0)) && ActiveWorld != null)
        {
            using var dialog = new Forms.SaveFileDialog
            {
                Title = "Export Blender-compatible GLB",
                Filter = "Binary glTF (*.glb)|*.glb",
                DefaultExt = "glb",
                AddExtension = true,
                FileName = SceneExporter.SafeFileName(_exportTarget?.Name ?? ActiveWorld.Name) + ".glb"
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                try
                {
                    Forms.Cursor.Current = Forms.Cursors.WaitCursor;
                    var result = SceneExporter.Export(dialog.FileName, ActiveWorld, Resolver, _exportOptions, _exportTarget);
                    _statusMessage = $"Exported {result.Meshes} meshes, {result.Textures} textures, {result.Animations} animations, {result.EmptyObjects} empty GAOs: {dialog.FileName}";
                    if (result.Warnings.Length > 0)
                        _statusMessage += $" ({result.Warnings.Length} format limitations; see GLB extras)";
                    ImGui.CloseCurrentPopup();
                }
                catch (Exception ex)
                {
                    SetError("Export failed: " + ex.Message);
                }
                finally
                {
                    Forms.Cursor.Current = Forms.Cursors.Default;
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
