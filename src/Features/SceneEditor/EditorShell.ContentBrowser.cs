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
    #region 5. Content Browser (Обозреватель ресурсов снизу)

    private static unsafe bool BeginTabItemWithFlags(string label, ImGuiTabItemFlags flags)
    {
        int byteCount = Encoding.UTF8.GetByteCount(label);
        byte* utf8 = stackalloc byte[byteCount + 1];
        fixed (char* chars = label)
        {
            Encoding.UTF8.GetBytes(chars, label.Length, utf8, byteCount);
        }
        utf8[byteCount] = 0;
        return ImGuiNative.igBeginTabItem(utf8, null, flags) != 0;
    }

    private void DrawContentBrowser()
    {
        ImGui.TextColored(Green, "CONTENT BROWSER");
        ImGui.SameLine();

        if (ImGui.BeginTabBar("ContentBrowserTabs"))
        {
            var tab0Flags = _requestedContentBrowserTab == 0 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Scene objects", tab0Flags))
            {
                _contentBrowserTab = 0;
                DrawSceneObjectsTable();
                ImGui.EndTabItem();
            }

            var tab1Flags = _requestedContentBrowserTab == 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Textures", tab1Flags))
            {
                _contentBrowserTab = 1;
                DrawTexturesTab();
                ImGui.EndTabItem();
            }

            var tab2Flags = _requestedContentBrowserTab == 2 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Audio", tab2Flags))
            {
                _contentBrowserTab = 2;
                DrawAudioTab();
                ImGui.EndTabItem();
            }

            var tab3Flags = _requestedContentBrowserTab == 3 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Text", tab3Flags))
            {
                _contentBrowserTab = 3;
                DrawTextTab();
                ImGui.EndTabItem();
            }

            var tab4Flags = _requestedContentBrowserTab == 4 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("BF archive", tab4Flags))
            {
                _contentBrowserTab = 4;
                DrawCatalogTab();
                ImGui.EndTabItem();
            }

            var tab5Flags = _requestedContentBrowserTab == 5 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Diagnostics", tab5Flags))
            {
                _contentBrowserTab = 5;
                DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            _requestedContentBrowserTab = null;
            ImGui.EndTabBar();
        }
    }

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

            foreach (var o in ActiveWorld.Objects)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                bool isSelected = SelectedObject?.Id == o.Id;
                if (ImGui.Selectable($"{o.Name}##{o.Id}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    SelectedObject = o;
                }

                DrawObjectContextMenu(o);
                if(_revealContent==o){ImGui.SetScrollHereY(.5f);_revealContent=null;}
                ImGui.TableNextColumn();
                ImGui.TextDisabled(o.GaoKey.ToString("X8"));

                ImGui.TableNextColumn();
                ImGui.TextDisabled(o.GeometryKey != 0 ? o.GeometryKey.ToString("X8") : "—");

                ImGui.TableNextColumn();
                if (o.Capabilities.CanTranslate)
                    ImGui.TextColored(Green, "Static");
                else
                    ImGui.TextColored(Yellow, "Restricted");

                ImGui.TableNextColumn();
                ImGui.TextDisabled($"{o.Transform.Translation.X:F1}, {o.Transform.Translation.Y:F1}, {o.Transform.Translation.Z:F1}");
            }
            ImGui.EndTable();
        }
    }

    private void DrawTexturesTab()
    {
        var textures = Resolver.TextureVariants.ToList();
        if (textures.Count == 0)
        {
            ImGui.TextDisabled("No resolved textures in the current scene or FIX.");
            return;
        }

        ImGui.TextDisabled($"Textures: {textures.Count} (including scene containers and global FIX)");
        ImGui.Separator();

        float listWidth = 280.0f * _scale;
        ImGui.BeginChild("TextureListArea", new Vector2(listWidth, 0), ImGuiChildFlags.Borders);
        {
            foreach (var tex in textures)
            {
                bool isSelected = _selectedTexture?.RawKey == tex.RawKey;
                string label = $"{tex.Id} ({tex.Width}x{tex.Height})";
                if (tex.IsFallback) label += " [FALLBACK]";

                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedTexture = tex;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("TexturePreviewArea", new Vector2(0, 0), ImGuiChildFlags.Borders);
        {
            if (_selectedTexture != null)
            {
                var tex = _selectedTexture;
                ImGui.TextColored(Green, $"Texture: {tex.Id}");
                ImGui.TextDisabled($"Size: {tex.Width} × {tex.Height} · Format: 0x{tex.FormatType:X4} · {(tex.IsFallback ? "Generated fallback" : "BGE original")}");

                if (ImGui.Button("Export PNG"))
                {
                    using var d = new Forms.SaveFileDialog
                    {
                        Filter = "PNG image (*.png)|*.png",
                        FileName = SceneExporter.SafeFileName(tex.Id.ToString()) + ".png"
                    };
                    if (d.ShowDialog() == Forms.DialogResult.OK)
                    {
                        try
                        {
                            byte[] rgba = Preview.DecodeTga(tex.ToTextureAsset());
                            unsafe
                            {
                                fixed (byte* p = rgba)
                                {
                                    Image img = new() { Data = p, Width = tex.Width, Height = tex.Height, Format = PixelFormat.UncompressedR8G8B8A8, Mipmaps = 1 };
                                    File.WriteAllBytes(d.FileName, Png(img));
                                    _statusMessage = $"Texture exported: {d.FileName}";
                                }
                            }
                        }
                        catch (Exception ex) { SetError(ex.Message); }
                    }
                }

                var gpuTex = Renderer.GetOrUploadTexture(tex.ToTextureAsset());
                if (gpuTex.Id != 0)
                {
                    var avail = ImGui.GetContentRegionAvail();
                    float fit = Math.Min(avail.X / gpuTex.Width, avail.Y / gpuTex.Height);
                    fit = Math.Clamp(fit, 0.1f, 4.0f);
                    Vector2 displaySize = new(gpuTex.Width * fit, gpuTex.Height * fit);
                    ImGui.Image((nint)gpuTex.Id, displaySize);
                }
            }
            else
            {
                ImGui.TextDisabled("Select a texture on the left to preview or export.");
            }
        }
        ImGui.EndChild();
    }

    private void DrawAudioTab()
    {
        if (_catalogPreview?.Audio is not { } audio)
        {
            ImGui.TextDisabled("Select an audio file (.wad, .waa, .wam, .wac) in the BF archive.");
            if (Archive != null && ImGui.Button("Find first audio file"))
            {
                var soundEntry = Archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".waa", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase));
                if (soundEntry != null) SelectCatalogEntry(soundEntry);
            }
            return;
        }

        ImGui.TextColored(Green, $"Audio: {_selectedCatalogEntry?.Name}");
        ImGui.TextDisabled($"Duration: {audio.Seconds:F1} s · Sample rate: {audio.Rate} Hz · Channels: {audio.Channels} · Format: PCM 16-bit");

        if (_waveform.Length > 0)
        {
            ImGui.PlotHistogram("##Waveform", ref _waveform[0], _waveform.Length, 0, "", 0, 1, new Vector2(-1, 80 * _scale));
        }

        ImGui.BeginDisabled(!_soundLoaded);
        if (ImGui.Button(_soundPlaying ? "Pause" : "Play"))
        {
            if (_soundPlaying)
            {
                Raylib.PauseSound(_sound);
                _soundPlaying = false;
            }
            else
            {
                Raylib.PlaySound(_sound);
                _soundPlaying = true;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop"))
        {
            Raylib.StopSound(_sound);
            _soundPlaying = false;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * _scale);
        if (ImGui.SliderFloat("Volume", ref _volume, 0, 1) && _soundLoaded)
        {
            Raylib.SetSoundVolume(_sound, _volume);
        }

        ImGui.SameLine();
        if (ImGui.Button("Export WAV"))
        {
            using var d = new Forms.SaveFileDialog
            {
                Filter = "WAVE Audio (*.wav)|*.wav",
                FileName = Path.GetFileNameWithoutExtension(_selectedCatalogEntry?.Name ?? "sound") + ".wav"
            };
            if (d.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllBytes(d.FileName, audio.Wav);
                _statusMessage = $"Audio exported: {d.FileName}";
            }
        }
    }

    private void DrawTextTab()
    {
        if (_catalogPreview?.Text is not { } txtDoc)
        {
            ImGui.TextDisabled("Select a text file (fd-prefixed .bin) in the BF archive.");
            if (Archive != null && ImGui.Button("Find first text file"))
            {
                var textEntry = Archive.Entries.FirstOrDefault(e => e.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase));
                if (textEntry != null) SelectCatalogEntry(textEntry);
            }
            return;
        }

        ImGui.TextColored(Green, $"Text: {_selectedCatalogEntry?.Name}");
        ImGui.SameLine();

        if (ImGui.Button("Export JSON"))
        {
            using var d = new Forms.SaveFileDialog
            {
                Filter = "Jade text JSON (*.json)|*.json",
                FileName = (_selectedCatalogEntry?.Name ?? "text") + ".json"
            };
            if (d.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllText(d.FileName, txtDoc.Json());
                _statusMessage = $"Text exported: {d.FileName}";
            }
        }

        ImGui.Separator();
        ImGui.BeginChild("TextLinesArea", new Vector2(0, 0), ImGuiChildFlags.None);
        ImGui.TextUnformatted(_catalogPreview?.Readable ?? "");
        ImGui.EndChild();
    }

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
                        for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            var e = _visibleCatalogEntries[i];
                            ImGui.PushID(e.Index);
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();

                            bool isSelected = _selectedCatalogEntry?.Index == e.Index;
                            if (ImGui.Selectable(e.Name, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                            {
                                SelectCatalogEntry(e);
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && (e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase) || e.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
                                {
                                    OpenSceneContainer(e);
                                }
                            }

                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(e.Id);
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(FormatSize(e.StoredSize));
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(e.Folder);

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
            foreach (var c in node.Children.Values) DrawFolderNode(c);
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

    #endregion
}
