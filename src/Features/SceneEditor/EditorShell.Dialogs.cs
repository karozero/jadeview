using static JadeView.EditorUi;
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
    #region Модальные диалоги (Play, About, Выход)

    private void DrawModals()
    {
        // Модальное окно "Play"
        if (_showPlayModal)
        {
            ImGui.OpenPopup("Launch Beyond Good & Evil");
            _showPlayModal = false;
        }

        if (ImGui.BeginPopupModal("Launch Beyond Good & Evil", ImGuiWindowFlags.AlwaysAutoResize))
        {
            bool running = Launcher.IsGameRunning(out int? pid);
            string exe = Launcher.Installation.ExePath;

            DrawPanelTitle("LAUNCH GAME", "Launch the original game with scene changes, or restore its original archive.");
            DrawCategoryTitle("Game installation");
            ImGui.TextDisabled($"Executable: {exe}");
            ImGui.Spacing();

            if (running)
            {
                ImGui.TextColored(Yellow, $"BGE.exe is already running (PID {pid})!");
            }

            DrawCategoryTitle("Actions");
            ImGui.BeginDisabled(running);

            if (ImGui.Button("1. Launch game with current mod"))
            {
                PlayGameWithCurrentMod();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("2. Restore original BF"))
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

            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // Модальное окно "About"
        if (_showAboutModal)
        {
            ImGui.OpenPopup("About JadeView");
            _showAboutModal = false;
        }

        if (ImGui.BeginPopupModal("About JadeView", ImGuiWindowFlags.AlwaysAutoResize))
        {
            DrawPanelTitle("ABOUT JADEVIEW", "A scene and asset editor for Beyond Good & Evil on PC.");
            DrawCategoryTitle("Editor");
            ImGui.TextWrapped("JadeView — Beyond Good & Evil scene editor. See EDITOR_PLAN.md for the editor design.");
            DrawCategoryTitle("Panels");
            ImGui.TextWrapped("Toolbar, Viewport, Asset Inspector, Outliner, Content Browser.");
            DrawCategoryTitle("Shortcuts");
            ImGui.TextWrapped("W (move), F (focus), G (game view), H (hide), Shift+H (isolate), Alt+H (show all), Delete (delete selection), Ctrl+C/Ctrl+V (copy/paste in the same world), Ctrl+Z/Ctrl+Y (undo/redo), Ctrl+S (save).");
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(120 * _scale, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    #endregion
}
