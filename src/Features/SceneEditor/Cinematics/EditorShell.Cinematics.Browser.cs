using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicBrowser()
    {
        DrawPanelTitle("CUTSCENES", "Browse cinematic containers by location, search by name or archive key, then select one to load it.");
        DrawCategoryTitle("Find a cutscene");
        if (Archive == null)
        {
            ImGui.TextDisabled("Open a BF archive to browse cinematic scenes.");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchCinematicScenes", "Search location, scene name, or archive key...",
            ref _searchCinematicScenes, 128);
        var scenes = _archiveScenes.Where(IsCinematicScene).Where(MatchesCinematicSearch).ToArray();
        DrawCategoryTitle($"Locations and scenes ({scenes.Length})");
        if (scenes.Length == 0)
        {
            ImGui.TextDisabled("No cinematic containers match this search.");
            return;
        }

        ImGui.BeginChild("CinematicSceneList", Vector2.Zero, ImGuiChildFlags.Borders);
        foreach (var locationGroup in scenes.GroupBy(GetCinematicLocation).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!ImGui.CollapsingHeader($"{locationGroup.Key} ({locationGroup.Count()})", ImGuiTreeNodeFlags.DefaultOpen))
                continue;
            foreach (var scene in locationGroup.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            {
                bool selected = _loadedCinematicEntry == scene.Entry.Id;
                if (ImGui.Selectable($"{scene.Label} · {scene.Entry.Id}", selected)) LoadCinematicScene(scene);
                if (scene.Diagnostic != null && ImGui.IsItemHovered()) ImGui.SetTooltip(scene.Diagnostic);
                DrawCopyNameContextMenu($"CinematicSceneName##{scene.Entry.Id}", scene.Label, "Copy scene name");
            }
        }
        ImGui.EndChild();
    }

    private bool MatchesCinematicSearch(ArchiveSceneItem scene)
    {
        if (string.IsNullOrWhiteSpace(_searchCinematicScenes)) return true;
        return scene.Label.Contains(_searchCinematicScenes, StringComparison.OrdinalIgnoreCase)
            || scene.Entry.Name.Contains(_searchCinematicScenes, StringComparison.OrdinalIgnoreCase)
            || scene.Entry.Id.Contains(_searchCinematicScenes, StringComparison.OrdinalIgnoreCase)
            || scene.WorldNames.Any(name => name.Contains(_searchCinematicScenes, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCinematicLocation(ArchiveSceneItem scene)
    {
        string? namedWorld = scene.WorldNames.FirstOrDefault(name => name.Length > 0 && char.IsDigit(name[0]));
        if (namedWorld == null) return "Other";
        string[] nameParts = namedWorld.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (nameParts.Length < 2) return namedWorld;
        return string.Join('_', nameParts.Take(2));
    }

    private static bool IsCinematicScene(ArchiveSceneItem scene) =>
        scene.Entry.Name.Contains("cine", StringComparison.OrdinalIgnoreCase)
        || scene.WorldNames.Any(name => name.Contains("cine", StringComparison.OrdinalIgnoreCase));

    private void LoadCinematicScene(ArchiveSceneItem scene)
    {
        _cinematicPlayback?.Deactivate();
        ResetCinematicAudio();
        _previewedCinematicSceneEventOwners.Clear();
        _previewedCinematicSceneEventStartTimes.Clear();
        _assetWorldBeforeCinematic = ActiveWorld;
        _loadedCinematicEntry = scene.Entry.Id;
        OpenSceneContainer(scene.Entry, () =>
        {
            _cutsceneMode = true;
            _loadedCinematicEntry = scene.Entry.Id;
            InitializeCinematicWorld();
        });
    }

    private void InitializeCinematicWorld()
    {
        if (Document == null) return;
        var cinematicWorld = Document.Worlds.FirstOrDefault(world =>
            world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase));
        if (cinematicWorld == null)
        {
            SetError("This container does not expose a cinematic world.");
            return;
        }
        _cinematicWorld = CinematicWorldAssembler.Assemble(Document, cinematicWorld);

        ActiveWorld = _cinematicWorld;
        Renderer.ActiveWorld = _cinematicWorld;
        Renderer.Document = Document;
        SelectedObject = _cinematicWorld.Objects.FirstOrDefault();
        _cinematicPlayers = CinematicPlayback.Discover(Document, _cinematicWorld, Resolver, Archive?.Entries,
            _disabledCinematicCallbackOwners, _previewedCinematicSceneEventOwners,
            _previewedCinematicSceneEventStartTimes);
        _cinematicPlayback = _cinematicPlayers.FirstOrDefault();
        _cinematicPlayback?.Activate();
        Renderer.ShowGameHidden = false;
        _followCinematicCamera = true;
        _cinematicCameraWasFollowing = false;
        Renderer.CinematicHorizontalFov = null;
        if (SelectedObject != null) Renderer.FrameAll();
        _cinematicViewportCamera = Renderer.CaptureViewportCameraState();
        _statusMessage = $"Cinematic scene loaded: {cinematicWorld.Name} · shared tracks: {_cinematicPlayers.Length}";
    }
}
