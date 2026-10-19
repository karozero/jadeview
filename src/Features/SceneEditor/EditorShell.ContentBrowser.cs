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
                    using var dialog = new Forms.SaveFileDialog
                    {
                        Filter = "PNG image (*.png)|*.png",
                        FileName = SceneExporter.SafeFileName(tex.Id.ToString()) + ".png"
                    };
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        try
                        {
                            byte[] rgba = Preview.DecodeTga(tex.ToTextureAsset());
                            unsafe
                            {
                                fixed (byte* point = rgba)
                                {
                                    Image img = new() { Data = point, Width = tex.Width, Height = tex.Height, Format = PixelFormat.UncompressedR8G8B8A8, Mipmaps = 1 };
                                    File.WriteAllBytes(dialog.FileName, Png(img));
                                    _statusMessage = $"Texture exported: {dialog.FileName}";
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
                var soundEntry = Archive.Entries.FirstOrDefault(catalogEntry => catalogEntry.Name.EndsWith(".waa", StringComparison.OrdinalIgnoreCase) || catalogEntry.Name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase));
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
            using var dialog = new Forms.SaveFileDialog
            {
                Filter = "WAVE Audio (*.wav)|*.wav",
                FileName = Path.GetFileNameWithoutExtension(_selectedCatalogEntry?.Name ?? "sound") + ".wav"
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllBytes(dialog.FileName, audio.Wav);
                _statusMessage = $"Audio exported: {dialog.FileName}";
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
                var textEntry = Archive.Entries.FirstOrDefault(catalogEntry => catalogEntry.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase));
                if (textEntry != null) SelectCatalogEntry(textEntry);
            }
            return;
        }

        ImGui.TextColored(Green, $"Text: {_selectedCatalogEntry?.Name}");
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

        ImGui.Separator();
        ImGui.BeginChild("TextLinesArea", new Vector2(0, 0), ImGuiChildFlags.None);
        ImGui.TextUnformatted(_catalogPreview?.Readable ?? "");
        ImGui.EndChild();
    }







    #endregion
}
