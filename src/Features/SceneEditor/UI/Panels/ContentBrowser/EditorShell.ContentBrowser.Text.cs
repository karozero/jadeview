using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawTextTab()
    {
        if (_catalogPreview?.Text is not { } txtDoc)
        {
            ImGui.TextDisabled("Select a text file (fd-prefixed .bin) in the BF archive.");
            if (Archive != null && ImGui.Button("Find first text file"))
            {
                var textEntry = Archive.Entries.FirstOrDefault(catalogEntry => catalogEntry.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase));
                if (textEntry != null) SelectCatalogEntry(textEntry);
            }
            return;
        }

        DrawCategoryTitle("Text resource");
        ImGui.TextUnformatted(_selectedCatalogEntry?.Name ?? "Text");
        ImGui.SameLine();

        if (ImGui.Button("Export JSON"))
        {
            using var dialog = new Forms.SaveFileDialog
            {
                Filter = "Jade text JSON (*.json)|*.json",
                FileName = (_selectedCatalogEntry?.Name ?? "text") + ".json"
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllText(dialog.FileName, txtDoc.Json());
                _statusMessage = $"Text exported: {dialog.FileName}";
            }
        }

        DrawCategoryTitle("Content");
        ImGui.BeginChild("TextLinesArea", new Vector2(0, 0), ImGuiChildFlags.None);
        ImGui.TextUnformatted(_catalogPreview?.Readable ?? "");
        ImGui.EndChild();
    }
}
