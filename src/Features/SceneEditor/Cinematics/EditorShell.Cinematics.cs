namespace JadeView;

public sealed partial class EditorShell
{
    private bool _cutsceneMode;
    private string? _loadedCinematicEntry;
    private WorldData? _assetWorldBeforeCinematic;
    private WorldData? _cinematicWorld;
    private CinematicPlayback[] _cinematicPlayers = [];
    private AnimationClip[] _cinematicClipSources = [];
    private CinematicPlayback? _cinematicPlayback;
    private readonly HashSet<CinematicScriptOwnerId> _disabledCinematicCallbackOwners = [];
    private readonly HashSet<CinematicScriptOwnerId> _previewedCinematicSceneEventOwners = [];
    private readonly Dictionary<CinematicScriptOwnerId, float> _previewedCinematicSceneEventStartTimes = [];
    private bool _cinematicVideoPostRollActive;
    private bool _cinematicVideoPostRollCompleted;
    private float _cinematicVideoPostRollSeconds;
    private CinematicScriptOwnerId? _selectedCinematicScriptOwnerId;
    private CinematicScriptOwnerCategory _cinematicScriptOwnerCategory = CinematicScriptOwnerCategory.ClipLinked;
    private string _cinematicEveSearch = string.Empty;
    private SceneDocument? _cinematicFlagFlowDocument;
    private int _cinematicFlagFlowOwnerCount = -1;
    private int _cinematicFlagFlowTrackCount = -1;
    private Dictionary<CinematicScriptOwnerId, CinematicScriptFlagLink[]> _cinematicFlagFlowLinks = [];
    private readonly Dictionary<int, bool?> _cinematicSavedFlagAssumptions = [];
    private PcAiSavedFlagSnapshot? _cinematicSavedFlagSnapshot;

    private void ExitCinematicMode()
    {
        ClearCinematicPlayback();
        ActiveWorld = _assetWorldBeforeCinematic != null && Document?.Worlds.Contains(_assetWorldBeforeCinematic) == true
            ? _assetWorldBeforeCinematic
            : Document?.ActiveWorld;
        Renderer.ActiveWorld = ActiveWorld;
        SelectedObject = ActiveWorld?.Objects.FirstOrDefault();
        if (SelectedObject != null) Renderer.FrameAll();
    }

    private void ClearCinematicPlayback()
    {
        _cinematicCameraWasFollowing = false;
        _cinematicViewportCamera = null;
        Renderer.CinematicShutter = null;
        Renderer.CinematicFlashes = [];
        Renderer.CinematicLightnings = [];
        Renderer.CinematicScreenFade = null;
        Renderer.CinematicHorizontalFov = null;
        Renderer.CinematicMaterials = null;
        Renderer.CinematicLights = null;
        Renderer.CinematicVideoTime = null;
        Renderer.CinematicVideoPlayback = null;
        _cinematicPlayback?.Deactivate();
        ResetCinematicAudio();
        _cinematicPlayback = null;
        _cinematicVideoPostRollActive = false;
        _cinematicVideoPostRollCompleted = false;
        _cinematicVideoPostRollSeconds = 0;
        _cinematicPlayers = [];
        _cinematicClipSources = [];
        _selectedCinematicScriptOwnerId = null;
        _cinematicFlagFlowDocument = null;
        _cinematicFlagFlowOwnerCount = -1;
        _cinematicFlagFlowTrackCount = -1;
        _cinematicFlagFlowLinks.Clear();
        _cinematicAiTrackMutationCache.Clear();
        _cinematicOwnerActionContributionCache.Clear();
        _cinematicWorld = null;
        _loadedCinematicEntry = null;
        _cutsceneMode = false;
    }
}
