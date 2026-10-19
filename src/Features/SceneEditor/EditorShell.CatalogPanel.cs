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
    private void DrawCatalogTab()
    {
        if (Archive == null)
        {
            ImGui.TextDisabled("No BigFile archive loaded.");
            return;
        }

        // Дерево папок и таблица files из Archive
        float treeWidth = 240.0f * _scale;
        ImGui.BeginChild("CatalogFolders", new Vector2(treeWidth, 0), ImGuiChildFlags.Borders);
        {
            if (ImGui.Selectable($"All folders ({_rootFolder.Count})", _catalogFolder.Length == 0))
            {
                _catalogFolder = "";
                FilterCatalog();
            }
            foreach (var child in _rootFolder.Children.Values)
            {
                DrawFolderNode(child);
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("CatalogFiles", new Vector2(0, 0), ImGuiChildFlags.Borders);
        {
            ImGui.SetNextItemWidth(260 * _scale);
            if (ImGui.InputTextWithHint("##CatSearch", "Search archive...", ref _catalogSearch, 128)) FilterCatalog();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150 * _scale);
            if (ImGui.Combo("##CatFilter", ref _catalogFilter, _catalogFilterNames, _catalogFilterNames.Length)) FilterCatalog();
            ImGui.SameLine();
            ImGui.TextDisabled($"{_visibleCatalogEntries.Count:N0} files");

            if (_selectedCatalogEntry != null && (_selectedCatalogEntry.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase) || _selectedCatalogEntry.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.45f, 0.25f, 1.0f));
                if (ImGui.Button($"Open {_selectedCatalogEntry.Id} as scene"))
                {
                    OpenSceneContainer(_selectedCatalogEntry);
                }
                ImGui.PopStyleColor();
            }

            if (ImGui.BeginTable("CatalogTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Filename", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("FAT key", ImGuiTableColumnFlags.WidthFixed, 85 * _scale);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 85 * _scale);
                ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                unsafe
                {
                    var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                    clipper.Begin(_visibleCatalogEntries.Count);
                    while (clipper.Step())
                    {
                        for (int rowIndex = clipper.DisplayStart; rowIndex < clipper.DisplayEnd; rowIndex++)
                        {
                            var catalogEntry = _visibleCatalogEntries[rowIndex];
                            ImGui.PushID(catalogEntry.Index);
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();

                            bool isSelected = _selectedCatalogEntry?.Index == catalogEntry.Index;
                            if (ImGui.Selectable(catalogEntry.Name, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                            {
                                SelectCatalogEntry(catalogEntry);
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && (catalogEntry.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase) || catalogEntry.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
                                {
                                    OpenSceneContainer(catalogEntry);
                                }
                            }

                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(catalogEntry.Id);
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(FormatSize(catalogEntry.StoredSize));
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(catalogEntry.Folder);

                            ImGui.PopID();
                        }
                    }
                    clipper.End();
                    clipper.Destroy();
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawFolderNode(FolderNode node)
    {
        ImGui.PushID(node.Path);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (node.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (_catalogFolder == node.Path) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx($"{node.Name} ({node.Count})", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            _catalogFolder = node.Path;
            FilterCatalog();
        }
        if (open && node.Children.Count > 0)
        {
            foreach (var childNode in node.Children.Values) DrawFolderNode(childNode);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private void DrawDiagnosticsTab()
    {
        ImGui.TextColored(Green, "SCENE AND ASSET DIAGNOSTICS");
        ImGui.Separator();

        if (Document != null)
        {
            ImGui.TextUnformatted($"Base BF: {Document.BaseBfPath}");
            ImGui.TextUnformatted($"SHA-256 BF: {Document.BaseBfSha256}");
            ImGui.TextUnformatted($"Container: {Document.ContainerName}");
            ImGui.Spacing();

            string text = $"Base BF: {Document.BaseBfPath}\nSHA-256: {Document.BaseBfSha256}\nContainer: {Document.ContainerName}\n\n" +
                string.Join("\n", Resolver.TextureLoaderStatus.Select(status => $"{status.Key}: {status.Value}")) + "\n" +
                string.Join("\n", Document.Diagnostics);
            if (ImGui.Button("Copy all")) ImGui.SetClipboardText(text);
            ImGui.InputTextMultiline("##DiagLog", ref text, (uint)Math.Max(4096, System.Text.Encoding.UTF8.GetByteCount(text) + 1),
                new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);

        }
        else
        {
            ImGui.TextDisabled("No scene document loaded.");
        }
    }
}
