using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Image = Raylib_cs.Image;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
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
            DrawCategoryTitle("Textures");
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
            DrawCategoryTitle("Preview and export");
            if (_selectedTexture != null)
            {
                var tex = _selectedTexture;
                ImGui.TextUnformatted($"Texture: {tex.Id}");
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
}
