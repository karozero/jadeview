using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicBrowser()
    {
        DrawPanelTitle("CUTSCENES", "Select a cinematic FF container. Playback uses keyed animation clips shared across actors; export writes the assembled actors to one GLB.");
        DrawCategoryTitle("Available cinematics");
        if (Archive == null)
        {
            ImGui.TextDisabled("Open a BF archive to browse cinematic scenes.");
            return;
        }

        var scenes = _archiveScenes.Where(IsCinematicScene).ToArray();
        ImGui.TextDisabled($"{scenes.Length} cinematic containers found");
        ImGui.BeginChild("CinematicSceneList", Vector2.Zero, ImGuiChildFlags.Borders);
        foreach (var scene in scenes)
        {
            bool selected = _loadedCinematicEntry == scene.Entry.Id;
            if (ImGui.Selectable($"{scene.Label} · {scene.Entry.Id}", selected)) LoadCinematicScene(scene);
            if (scene.Diagnostic != null && ImGui.IsItemHovered()) ImGui.SetTooltip(scene.Diagnostic);
            DrawCopyNameContextMenu($"CinematicSceneName##{scene.Entry.Id}", scene.Label, "Copy scene name");
        }
        ImGui.EndChild();
    }

    private static bool IsCinematicScene(ArchiveSceneItem scene) =>
        scene.Entry.Name.Contains("cine", StringComparison.OrdinalIgnoreCase)
        || scene.WorldNames.Any(name => name.Contains("cine", StringComparison.OrdinalIgnoreCase));

    private void LoadCinematicScene(ArchiveSceneItem scene)
    {
        _cinematicPlayback?.Deactivate();
        ResetCinematicAudio();
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
        _cinematicPlayers = CinematicPlayback.Discover(Document, _cinematicWorld, Resolver, Archive?.Entries);
        _cinematicPlayback = _cinematicPlayers.FirstOrDefault();
        _cinematicPlayback?.Activate();
        Renderer.ShowGameHidden = false;
        _followCinematicCamera = true;
        if (SelectedObject != null) Renderer.FrameAll();
        _statusMessage = $"Cinematic scene loaded: {cinematicWorld.Name} · shared tracks: {_cinematicPlayers.Length}";
    }
}
