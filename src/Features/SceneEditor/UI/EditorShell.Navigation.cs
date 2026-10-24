namespace JadeView;

public sealed partial class EditorShell
{
    // Состояние UI
    private SceneObject? _revealOutliner;
    private string _searchOutliner = "";
    private int _outlinerFilterType = 0; // 0: All, 1: Movable, 2: Restricted
    private OutlinerContentKind _outlinerContentKind = OutlinerContentKind.All;
    private string _searchCinematicScenes = "";
    private ContentBrowserCategory _contentBrowserTab = ContentBrowserCategory.AllAssets;
    private ContentBrowserCategory? _requestedContentBrowserTab;
    public int ContentBrowserTab
    {
        get => (int)_contentBrowserTab;
        set
        {
            _contentBrowserTab = (ContentBrowserCategory)value;
            _requestedContentBrowserTab = (ContentBrowserCategory)value;
        }
    }

}
