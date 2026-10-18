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
    #region 6. Statusная строка

    private void DrawStatusBar()
    {
        if (_activeJob != null)
        {
            ImGui.ProgressBar(_jobProgress, new Vector2(200 * _scale, 14 * _scale), "");
            ImGui.SameLine();
            ImGui.TextDisabled(_jobName);
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel")) _jobCancellation?.Cancel();
        }
        else
        {
            ImGui.TextColored(Green, "●");
            ImGui.SameLine();
            ImGui.TextDisabled(_statusMessage);
        }

        if (History.IsDirty)
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - 140 * _scale);
            ImGui.TextColored(Yellow, "[Unsaved changes *]");
        }
    }

    #endregion
}
