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

            ImGui.TextColored(Green, "Launch the original game to test modifications");
            ImGui.TextDisabled($"Executable: {exe}");
            ImGui.Spacing();

            if (running)
            {
                ImGui.TextColored(Yellow, $"BGE.exe is already running (PID {pid})!");
            }

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
            ImGui.TextColored(Green, "JadeView — Beyond Good & Evil scene editor");
            ImGui.TextWrapped("See EDITOR_PLAN.md for the editor design.\nLayout: Toolbar, Viewport, Outliner, Details, Content Browser.\nModules: SceneContract, SceneLoader, SceneWriter, ResourceResolver, ModProject, SceneRenderer, GameLauncher.\nShortcuts: W (move), F (focus), G (game view), H (hide), Shift+H (isolate), Alt+H (show all), Ctrl+Z/Ctrl+Y (undo/redo), Ctrl+S (save).");
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
