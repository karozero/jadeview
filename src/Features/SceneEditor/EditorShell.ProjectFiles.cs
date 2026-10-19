using Forms = System.Windows.Forms;

namespace JadeView;
public sealed partial class EditorShell
{
    public void LoadModProject(string projectPath)
    {
        try
        {
            var project = ModProject.Load(projectPath);
            string? archivePath = FindProjectArchive(project);
            bool needsArchive = Archive == null || (archivePath != null && !string.Equals(Archive.PathName, Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase));
            if (needsArchive)
            {
                if (archivePath == null)
                    SetError($"Base archive not found: {project.BaseBfPath} ({project.BaseBfFileName})");
                else
                    LoadProjectArchive(project, projectPath, archivePath);
                return;
            }

            if (!project.VerifyBaseBf(Archive!.Sha256, out var hashError))
            {
                SetError(hashError!);
                return;
            }

            if (SwitchToProjectScene(project, projectPath))
                return;
            ApplyLoadedProject(project, projectPath);
        }
        catch (Exception error)
        {
            SetError(error.Message);
        }
    }

    private static string? FindProjectArchive(ModProject project)
    {
        bool hasOriginalPath = !string.IsNullOrEmpty(project.BaseBfPath) && File.Exists(project.BaseBfPath);
        if (hasOriginalPath)
            return project.BaseBfPath;
        if (File.Exists(project.BaseBfFileName))
            return Path.GetFullPath(project.BaseBfFileName);
        return File.Exists(App.DefaultArchive) ? App.DefaultArchive : null;
    }

    private void LoadProjectArchive(ModProject project, string projectPath, string archivePath)
    {
        StartJob($"Loading base archive {Path.GetFileName(archivePath)}...", token =>
        {
            var archive = new Archive(archivePath);
            token.ThrowIfCancellationRequested();
            var scenes = BuildArchiveSceneIndex(archive, token);
            if (!project.VerifyBaseBf(archive.Sha256, out var hashError))
                return () => SetError(hashError!);
            var resolver = new ResourceResolver();
            resolver.LoadFixContainer(archive);
            var sceneEntry = FindProjectScene(archive, project);
            SceneDocument? document = null;
            if (sceneEntry != null)
            {
                resolver.LoadSceneContainer(archive, sceneEntry);
                document = SceneLoader.Load(archive, sceneEntry);
                resolver.ResolveSceneDocument(document);
            }

            token.ThrowIfCancellationRequested();
            return () =>
            {
                ActivateProjectArchive(archive, scenes, resolver, document);
                ApplyLoadedProject(project, projectPath);
            };
        });
    }

    private static Entry? FindProjectScene(Archive archive, ModProject project)
    {
        Entry? entry = null;
        if (!string.IsNullOrEmpty(project.ContainerName))
            entry = archive.Entries.FirstOrDefault(candidate => MatchesContainer(candidate, project.ContainerName));
        else if (project.ContainerKey != 0)
            entry = archive.Entries.FirstOrDefault(candidate => candidate.Key == project.ContainerKey);
        return entry ?? archive.Entries.FirstOrDefault(candidate => candidate.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase)) ?? archive.Entries.FirstOrDefault(candidate => candidate.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesContainer(Entry entry, string container) => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase) || entry.Name.Equals(container, StringComparison.OrdinalIgnoreCase);
    private void ActivateProjectArchive(Archive archive, List<ArchiveSceneItem> scenes, ResourceResolver resolver, SceneDocument? document)
    {
        Archive = archive;
        _archiveScenes = scenes;
        Document = document;
        Renderer.ClearGpuCache();
        ActiveWorld = document != null ? InitialWorld(document) : null;
        Resolver = resolver;
        Renderer.Document = document;
        Renderer.Resolver = resolver;
        Renderer.ActiveWorld = ActiveWorld;
        SelectedObject = ActiveWorld?.Objects.FirstOrDefault();
        BuildCatalogFolders();
        FilterCatalog();
    }

    private bool SwitchToProjectScene(ModProject project, string projectPath)
    {
        bool needsScene = !string.IsNullOrEmpty(project.ContainerName) && (Document == null || !string.Equals(Document.ContainerName, project.ContainerName, StringComparison.OrdinalIgnoreCase));
        if (!needsScene)
            return false;
        var entry = Archive!.Entries.FirstOrDefault(candidate => MatchesContainer(candidate, project.ContainerName));
        if (entry == null)
            return false;
        OpenSceneContainer(entry, onLoaded: () =>
        {
            if (Document != null)
                ApplyLoadedProject(project, projectPath);
        });
        return true;
    }

    private void ApplyLoadedProject(ModProject project, string projectPath)
    {
        if (Document == null)
        {
            CurrentProject = project;
            _projectPath = projectPath;
            _statusMessage = $"Project loaded: {Path.GetFileName(projectPath)}";
            return;
        }

        var applied = project.ApplyToDocument(Document);
        if (!applied.IsSuccess)
        {
            SetError(string.Join("; ", applied.Errors));
            return;
        }

        CurrentProject = project;
        _projectPath = projectPath;
        History.Reset();
        _statusMessage = $"Mod project loaded: {Path.GetFileName(projectPath)} (updated {applied.ObjectsUpdated} objects)";
    }

    public bool SaveCurrentProject(string? saveAsPath = null)
    {
        if (CurrentProject == null || Document == null)
            return false;
        string path = saveAsPath ?? _projectPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{CurrentProject.Name}.bgemod");
        if (saveAsPath == null && _projectPath == null)
        {
            using var dialog = new Forms.SaveFileDialog
            {
                Title = "Save BGE mod project",
                Filter = "Jade mod project (*.bgemod)|*.bgemod|JSON (*.json)|*.json",
                FileName = $"{Path.GetFileNameWithoutExtension(Document.ContainerName)}_mod.bgemod"};
            if (dialog.ShowDialog() != Forms.DialogResult.OK)
                return false;
            path = dialog.FileName;
        }

        try
        {
            CurrentProject.Save(path);
            _projectPath = path;
            History.IsDirty = false;
            _statusMessage = $"Project saved: {path}";
            return true;
        }
        catch (Exception ex)
        {
            SetError($"Failed to save project: {ex.Message}");
            return false;
        }
    }
}
