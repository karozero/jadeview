using System.Numerics;
using System.Text;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawSceneObjectsTable()
    {
        if (ActiveWorld == null)
        {
            ImGui.TextDisabled("No scene loaded.");
            return;
        }

        if (ImGui.BeginTable("ObjectsTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Object name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("GAO Key", ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
            ImGui.TableSetupColumn("GRO Key", ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100 * _scale);
            ImGui.TableSetupColumn("Position X,Y,Z", ImGuiTableColumnFlags.WidthFixed, 180 * _scale);
            ImGui.TableHeadersRow();

            // Context-menu actions can add or remove objects during this frame.
            foreach (var sceneObject in ActiveWorld.Objects.ToArray())
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                bool isSelected = SelectedObject?.Id == sceneObject.Id;
                if (ImGui.Selectable($"{sceneObject.Name}##{sceneObject.Id}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    SelectedObject = sceneObject;
                }

                DrawObjectContextMenu(sceneObject);
                if(_revealContent==sceneObject){ImGui.SetScrollHereY(.5f);_revealContent=null;}
                ImGui.TableNextColumn();
                ImGui.TextDisabled(sceneObject.GaoKey.ToString("X8"));

                ImGui.TableNextColumn();
                ImGui.TextDisabled(sceneObject.GeometryKey != 0 ? sceneObject.GeometryKey.ToString("X8") : "—");

                ImGui.TableNextColumn();
                if (sceneObject.Capabilities.CanTranslate)
                    ImGui.TextColored(Green, "Static");
                else
                    ImGui.TextColored(Yellow, "Restricted");

                ImGui.TableNextColumn();
                ImGui.TextDisabled($"{sceneObject.Transform.Translation.X:F1}, {sceneObject.Transform.Translation.Y:F1}, {sceneObject.Transform.Translation.Z:F1}");
            }
            ImGui.EndTable();
        }
    }
}
