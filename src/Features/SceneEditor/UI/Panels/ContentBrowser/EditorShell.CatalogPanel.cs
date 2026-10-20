using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

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
            DrawCategoryTitle("Folders");
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
            DrawCategoryTitle("Search and filters");
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
                ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.StagedButton);
                if (ImGui.Button($"Open {_selectedCatalogEntry.Id} as scene"))
                {
                    OpenSceneContainer(_selectedCatalogEntry);
                }
                ImGui.PopStyleColor();
            }

            DrawCategoryTitle("Archive assets");
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

                            DrawCopyNameContextMenu("CatalogEntryName", catalogEntry.Name);
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

}
