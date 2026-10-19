using System.Numerics;
using ImGuiNET;

namespace JadeView;

public static class EditorTheme
{
    public const float FontSize = 18.5f;
    public const float WindowTitleFontSize = 24;
    public static ImFontPtr WindowTitleFont { get; private set; }
    public static bool HasWindowTitleFont { get; private set; }
    public static readonly Vector4 Green = new(0.35f, 0.90f, 0.54f, 1);
    public static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.25f, 1);
    public static readonly Vector4 Red = new(0.95f, 0.35f, 0.35f, 1);
    public static readonly Vector4 Muted = new(0.56f, 0.62f, 0.60f, 1);
    public static readonly Vector4 DirtyButton = new(0.18f, 0.42f, 0.25f, 1);
    public static readonly Vector4 PrimaryButton = new(0.12f, 0.50f, 0.28f, 1);
    public static readonly Vector4 PrimaryButtonHovered = new(0.18f, 0.65f, 0.36f, 1);
    public static readonly Vector4 StagedButton = new(0.12f, 0.45f, 0.25f, 1);
    public static readonly Vector2 CompactFramePadding = new(5, 2);
    public static readonly Vector2 CompactItemSpacing = new(6, 3);

    public static unsafe void LoadFonts(ImGuiIOPtr io, float scale)
    {
        HasWindowTitleFont = false;
        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string regularPath = Path.Combine(fontsDirectory, "segoeui.ttf");
        if (!File.Exists(regularPath))
        {
            io.Fonts.AddFontDefault();
            return;
        }
        io.Fonts.AddFontFromFileTTF(regularPath, FontSize * scale, null, io.Fonts.GetGlyphRangesCyrillic());
        string titlePath = Path.Combine(fontsDirectory, "segoeuib.ttf");
        WindowTitleFont = io.Fonts.AddFontFromFileTTF(File.Exists(titlePath) ? titlePath : regularPath,
            WindowTitleFontSize * scale, null, io.Fonts.GetGlyphRangesCyrillic());
        HasWindowTitleFont = WindowTitleFont.NativePtr != null;
    }

    public static void Apply(float scale)
    {
        var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(14, 10);
        style.FramePadding = new Vector2(9, 6);
        style.ItemSpacing = new Vector2(8, 7);
        style.WindowRounding = 0;
        style.ChildRounding = 6;
        style.FrameRounding = 4;
        style.GrabRounding = 4;
        style.ScrollbarRounding = 6;
        style.WindowBorderSize = 0;
        style.ChildBorderSize = 1;
        style.FrameBorderSize = 0;

        // Фирменная тёмно-зеленая цветовая схема JadeView
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.055f, 0.070f, 0.065f, 1.0f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.075f, 0.090f, 0.085f, 1.0f);
        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.88f, 0.92f, 0.90f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = Muted;
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.15f, 0.20f, 0.17f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.12f, 0.15f, 0.13f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.17f, 0.23f, 0.19f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.19f, 0.30f, 0.23f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.12f, 0.21f, 0.16f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.18f, 0.35f, 0.24f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.21f, 0.43f, 0.28f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.13f, 0.29f, 0.19f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.17f, 0.32f, 0.22f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.19f, 0.40f, 0.25f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = Green;
        style.Colors[(int)ImGuiCol.SliderGrab] = Green;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = Green;
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.10f, 0.15f, 0.12f, 1.0f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.16f, 0.30f, 0.20f, 1.0f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.19f, 0.36f, 0.24f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.10f, 0.14f, 0.115f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(0.12f, 0.17f, 0.135f, 0.25f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = Green;

        if (scale != 1.0f)
        {
            style.ScaleAllSizes(scale);
        }
    }

}
