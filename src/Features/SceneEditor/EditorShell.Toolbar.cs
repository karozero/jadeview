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
    #region 1. Панель инструментов (Toolbar)

    private void DrawToolbar()
    {
        ImGui.BeginDisabled(_activeJob != null);

        // Кнопки работы с файлами
        if (ImGui.Button("Open BF"))
        {
            using var d = new Forms.OpenFileDialog
            {
                Title = "Open original Beyond Good & Evil archive",
                Filter = "BigFile archive (*.bf)|*.bf|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (d.ShowDialog() == Forms.DialogResult.OK) LoadBfArchive(d.FileName);
        }
        ImGui.SameLine();

        if (ImGui.Button("Open project"))
        {
            using var d = new Forms.OpenFileDialog
            {
                Title = "Open BGE mod project",
                Filter = "Jade mod project (*.bgemod)|*.bgemod|JSON (*.json)|*.json",
                CheckFileExists = true
            };
            if (d.ShowDialog() == Forms.DialogResult.OK) LoadModProject(d.FileName);
        }
        ImGui.SameLine();

        bool isDirty = History.IsDirty;
        if (isDirty) ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.DirtyButton);
        string saveLabel = isDirty ? "Save project *" : "Save project";
        if (ImGui.Button(saveLabel)) SaveCurrentProject();
        if (isDirty) ImGui.PopStyleColor();
        ImGui.SameLine();

        if (ImGui.Button("Build BF")) BuildModdedBf();
        ImGui.SameLine();

        ImGui.BeginDisabled(ActiveWorld == null);
        if(ImGui.Button("Export")){_exportTarget=null;_openExport=true;}
        ImGui.EndDisabled();
        ImGui.SameLine();

        // Правая часть Toolbar: Кнопка «Play» и «Restore original BF»
        float rightControlsWidth = 430.0f * _scale;
        float curX = ImGui.GetCursorPosX();
        float targetX = ImGui.GetWindowWidth() - rightControlsWidth;
        if (targetX > curX) ImGui.SameLine(targetX);

        ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.PrimaryButton);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, EditorTheme.PrimaryButtonHovered);
        if (ImGui.Button("PLAY (BGE)", new Vector2(120.0f * _scale, 0)))
        {
            _showPlayModal = true;
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine();

        if (ImGui.Button("Restore original BF"))
        {
            var restoreRes = Launcher.RestoreOriginalBf();
            if (restoreRes.IsSuccess)
            {
                _statusMessage = $"Archive restored: {restoreRes.RestoredPath}";
            }
            else
            {
                SetError(restoreRes.Error ?? "Failed to restore archive.");
            }
        }
        ImGui.SameLine();

        if (ImGui.Button("About")) _showAboutModal = true;

        ImGui.EndDisabled();
    }

    #endregion
}
