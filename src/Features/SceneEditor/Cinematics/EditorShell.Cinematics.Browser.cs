using System.Diagnostics;
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
        ImGui.BeginDisabled(_activeJob != null);
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
        ImGui.EndDisabled();
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
        if (_activeJob != null) return;
        _cinematicPlayback?.Pause();
        _previewedCinematicSceneEventOwners.Clear();
        _previewedCinematicSceneEventStartTimes.Clear();
        _assetWorldBeforeCinematic = ActiveWorld;
        _loadedCinematicEntry = scene.Entry.Id;
        var disabledCallbackOwners = new HashSet<CinematicScriptOwnerId>(
            _disabledCinematicCallbackOwners);
        var previewedEventOwners = new HashSet<CinematicScriptOwnerId>(
            _previewedCinematicSceneEventOwners);
        var previewedEventStartTimes = new Dictionary<CinematicScriptOwnerId, float>(
            _previewedCinematicSceneEventStartTimes);
        IReadOnlyDictionary<int, bool?> savedFlags = GetCinematicSavedFlagStates();
        IReadOnlyList<Entry>? archiveEntries = Archive?.Entries;
        OpenSceneContainer(scene.Entry, prepareOnWorker: (document, resolver) =>
            PrepareCinematicWorld(document, resolver, scene.Entry.Id,
                disabledCallbackOwners, previewedEventOwners,
                previewedEventStartTimes, savedFlags, archiveEntries));
    }

    private Action PrepareCinematicWorld(SceneDocument document, ResourceResolver resolver,
        string sceneEntryId, IReadOnlySet<CinematicScriptOwnerId> disabledCallbackOwners,
        IReadOnlySet<CinematicScriptOwnerId> previewedEventOwners,
        IReadOnlyDictionary<CinematicScriptOwnerId, float> previewedEventStartTimes,
        IReadOnlyDictionary<int, bool?> savedFlags, IReadOnlyList<Entry>? archiveEntries)
    {
        var cinematicWorld = document.Worlds.FirstOrDefault(world =>
            world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase));
        if (cinematicWorld == null)
        {
            return () => SetError("This container does not expose a cinematic world.");
        }
        long assembleStartedAt = Stopwatch.GetTimestamp();
        WorldData assembledWorld = CinematicWorldAssembler.Assemble(document, cinematicWorld);
        double assemblyMilliseconds = Stopwatch.GetElapsedTime(assembleStartedAt).TotalMilliseconds;
        AnimationClip[] clipSources = CinematicSceneClips.ForWorld(document, cinematicWorld)
            .OrderBy(clip => clip.Key).ToArray();
        long playbackStartedAt = Stopwatch.GetTimestamp();
        CinematicPlayback? selectedPlayback = null;
        foreach (AnimationClip sourceClip in clipSources)
        {
            selectedPlayback = CinematicPlayback.DiscoverClip(sourceClip, document, assembledWorld,
                resolver, disabledCallbackOwners, previewedEventOwners, previewedEventStartTimes,
                archiveEntries);
            if (selectedPlayback != null) break;
        }
        CinematicPlayback[] players = selectedPlayback == null ? [] : [selectedPlayback];
        selectedPlayback?.BindSavedFlagAssumptions(savedFlags);
        selectedPlayback?.Activate();
        double playbackMilliseconds = Stopwatch.GetElapsedTime(playbackStartedAt).TotalMilliseconds;

        return () => PresentCinematicWorld(document, resolver, cinematicWorld,
            assembledWorld, clipSources, players, selectedPlayback, sceneEntryId,
            assemblyMilliseconds, playbackMilliseconds);
    }

    private void PresentCinematicWorld(SceneDocument document, ResourceResolver resolver,
        WorldData sourceWorld, WorldData assembledWorld, AnimationClip[] clipSources,
        CinematicPlayback[] players,
        CinematicPlayback? selectedPlayback, string sceneEntryId,
        double assemblyMilliseconds, double playbackMilliseconds)
    {
        _cutsceneMode = true;
        _loadedCinematicEntry = sceneEntryId;
        _cinematicWorld = assembledWorld;
        ActiveWorld = assembledWorld;
        Renderer.ActiveWorld = assembledWorld;
        Renderer.Document = document;
        Renderer.Resolver = resolver;
        SelectedObject = assembledWorld.Objects.FirstOrDefault();
        _cinematicPlayers = players;
        _cinematicClipSources = clipSources;
        _cinematicPlayback = selectedPlayback;
        Renderer.ShowGameHidden = false;
        _followCinematicCamera = true;
        _cinematicCameraWasFollowing = false;
        Renderer.CinematicHorizontalFov = null;
        long framingStartedAt = Stopwatch.GetTimestamp();
        if (SelectedObject != null) Renderer.FrameAll();
        double framingMilliseconds = Stopwatch.GetElapsedTime(framingStartedAt).TotalMilliseconds;
        _cinematicViewportCamera = Renderer.CaptureViewportCameraState();
        _statusMessage = $"Cinematic scene loaded: {sourceWorld.Name} · {clipSources.Length} clips · "
            + $"assembly {assemblyMilliseconds:F0} ms · first clip {playbackMilliseconds:F0} ms · "
            + $"framing {framingMilliseconds:F0} ms";
    }

    private void LoadCinematicClip(AnimationClip sourceClip)
    {
        if (_activeJob != null || Document == null || _cinematicWorld == null
            || _cinematicPlayback?.Key == sourceClip.Key)
            return;

        SceneDocument document = Document;
        WorldData cinematicWorld = _cinematicWorld;
        ResourceResolver resolver = Resolver;
        IReadOnlySet<CinematicScriptOwnerId> disabledCallbackOwners =
            new HashSet<CinematicScriptOwnerId>(_disabledCinematicCallbackOwners);
        IReadOnlySet<CinematicScriptOwnerId> previewedEventOwners =
            new HashSet<CinematicScriptOwnerId>(_previewedCinematicSceneEventOwners);
        IReadOnlyDictionary<CinematicScriptOwnerId, float> previewedEventStartTimes =
            new Dictionary<CinematicScriptOwnerId, float>(_previewedCinematicSceneEventStartTimes);
        IReadOnlyDictionary<int, bool?> savedFlags = GetCinematicSavedFlagStates();
        IReadOnlyList<Entry>? archiveEntries = Archive?.Entries;
        _cinematicPlayback?.Deactivate();
        ResetCinematicAudio();
        _cinematicPlayback = null;
        _cinematicPlayers = [];

        StartJob($"Preparing cinematic clip {sourceClip.Name}...", token =>
        {
            token.ThrowIfCancellationRequested();
            CinematicPlayback? playback = CinematicPlayback.DiscoverClip(sourceClip, document,
                cinematicWorld, resolver, disabledCallbackOwners, previewedEventOwners,
                previewedEventStartTimes, archiveEntries);
            if (playback == null)
                throw new InvalidDataException("This clip has no shared actor or object tracks to preview.");
            playback.BindSavedFlagAssumptions(savedFlags);
            playback.Activate();
            token.ThrowIfCancellationRequested();
            return () =>
            {
                _cinematicPlayers = [playback];
                _cinematicPlayback = playback;
                _cinematicVideoPostRollActive = false;
                _cinematicVideoPostRollCompleted = false;
                _cinematicVideoPostRollSeconds = 0;
                _followCinematicCamera = true;
                _cinematicCameraWasFollowing = false;
                Renderer.CinematicHorizontalFov = null;
                _statusMessage = $"Cinematic clip loaded: {playback.Name} · {playback.ParticipantCount} tracks";
            };
        });
    }
}
