using static JadeView.EditorUi;
using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawContentBrowser()
    {
        DrawPanelTitle("CONTENT BROWSER", "Browse and export textures, audio, text and BF archive files; scene containers can also be opened here. Placing assets into the scene from this browser is not supported yet. Use Outliner > Add object for loaded scene/FIX geometry.");
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Muted);
        ImGui.TextWrapped("Adding assets to the scene from here is not supported yet. Use Outliner > Add object for loaded scene/FIX geometry.");
        ImGui.PopStyleColor();
        DrawCategoryTitle("Current scene");
        DrawCurrentSceneName();
        DrawCategoryTitle("Resource categories");
        if (!ImGui.BeginTabBar("ContentBrowserTabs")) return;
        DrawContentCategory(ContentBrowserCategory.AllAssets, "All Assets", DrawCatalogTab);
        DrawContentCategory(ContentBrowserCategory.Textures, "Textures", DrawTexturesTab);
        DrawContentCategory(ContentBrowserCategory.Audio, "Audio", DrawAudioTab);
        DrawContentCategory(ContentBrowserCategory.Text, "Text", DrawTextTab);
        DrawContentCategory(ContentBrowserCategory.Diagnostics, "Diagnostics", DrawDiagnosticsTab);
        _requestedContentBrowserTab = null;
        ImGui.EndTabBar();
    }

    private void DrawContentCategory(ContentBrowserCategory category, string label, Action drawContent)
    {
        var flags = _requestedContentBrowserTab == category ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!BeginTabItemWithFlags(label, flags)) return;
        _contentBrowserTab = category;
        drawContent();
        ImGui.EndTabItem();
    }
}
