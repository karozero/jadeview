namespace JadeView;

public sealed partial class EditorShell
{
    private bool _cutsceneMode;
    private string? _loadedCinematicEntry;
    private WorldData? _assetWorldBeforeCinematic;
    private WorldData? _cinematicWorld;
    private CinematicPlayback[] _cinematicPlayers = [];
    private CinematicPlayback? _cinematicPlayback;

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
        Renderer.CinematicShutter = null;
        Renderer.CinematicFlashes = [];
        Renderer.CinematicLightnings = [];
        Renderer.CinematicScreenFade = null;
        Renderer.CinematicHorizontalFov = null;
        Renderer.CinematicMaterials = null;
        _cinematicPlayback?.Deactivate();
        ResetCinematicAudio();
        _cinematicPlayback = null;
        _cinematicPlayers = [];
        _cinematicWorld = null;
        _loadedCinematicEntry = null;
        _cutsceneMode = false;
    }
}
