using System.Text;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    private static WorldData? InitialWorld(SceneDocument doc) => doc.Worlds.Where(candidateWorld => candidateWorld.Name.Length > 0 && char.IsDigit(candidateWorld.Name[0])).OrderByDescending(candidateWorld => candidateWorld.Objects.Count).FirstOrDefault() ?? doc.ActiveWorld;
    private bool ConfirmDocumentReplacement()
    {
        if (!History.IsDirty)
            return true;
        var answer = Forms.MessageBox.Show("Save project changes before switching scenes?", "JadeView", Forms.MessageBoxButtons.YesNoCancel, Forms.MessageBoxIcon.Question);
        return answer == Forms.DialogResult.No || (answer == Forms.DialogResult.Yes && SaveCurrentProject());
    }

    public void LoadBfArchive(string bfPath)
    {
        if (!ConfirmDocumentReplacement())
            return;
        StartJob("Reading archive and scenes...", token =>
        {
            var arch = new Archive(bfPath);
            token.ThrowIfCancellationRequested();
            // Index the FF0 packs by reading only their container bytes and WOW name fields.
            // Keep failures attached to individual entries so one damaged pack cannot block
            // opening the archive or browsing the remaining scenes.
            var archiveScenes = BuildArchiveSceneIndex(arch, token);
            var res = new ResourceResolver();
            res.LoadFixContainer(arch);
            // Ищем контейнер мира FF007685 (или первый доступный FF0)
            var sceneEntry = arch.Entries.FirstOrDefault(candidateEntry => candidateEntry.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase)) ?? arch.Entries.FirstOrDefault(candidateEntry => candidateEntry.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase));
            SceneDocument? doc = null;
            if (sceneEntry != null)
            {
                res.LoadSceneContainer(arch, sceneEntry);
                doc = SceneLoader.Load(arch, sceneEntry);
                res.ResolveSceneDocument(doc);
            }

            token.ThrowIfCancellationRequested();
            return () =>
            {
                Archive = arch;
                _archiveScenes = archiveScenes;
                _cutsceneMode = false;
                _cinematicPlayback?.Deactivate();
                ResetCinematicAudio();
                _cinematicWorld = null;
                _cinematicPlayback = null;
                _cinematicPlayers = [];
                _loadedCinematicEntry = null;
                ClearCinematicPlayback();
                Document = doc;
                Renderer.ClearGpuCache();
                ActiveWorld = doc != null ? InitialWorld(doc) : null;
                // Передаем контекст в Renderer
                Resolver = res;
                Renderer.Document = doc;
                Renderer.Resolver = res;
                Renderer.VideoArchive = arch;
                Renderer.ActiveWorld = ActiveWorld;
                SelectedObject = ActiveWorld?.Objects.FirstOrDefault();
                CurrentProject = doc != null ? ModProject.CreateForDocument(doc) : null;
                _projectPath = null;
                History.Reset();
                BuildCatalogFolders();
                FilterCatalog();
                if (SelectedObject != null)
                {
                    Renderer.FrameAll();
                }

                _statusMessage = $"Opened {Path.GetFileName(bfPath)} · Worlds: {doc?.Worlds.Count ?? 0} · Objects: {ActiveWorld?.Objects.Count ?? 0}";
            };
        });
    }

    private static List<ArchiveSceneItem> BuildArchiveSceneIndex(Archive archive, CancellationToken token)
    {
        var scenes = new List<ArchiveSceneItem>();
        foreach (var entry in archive.Entries.Where(candidateEntry => candidateEntry.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            var names = new List<string>();
            string? diagnostic = null;
            try
            {
                var bytes = archive.Read(entry);
                var scanDiagnostics = new List<string>();
                var chunks = SceneLoader.ScanChunks(bytes, scanDiagnostics);
                foreach (var chunk in chunks)
                {
                    // WOW starts at chunk.Offset + 4; its fixed name is at WOW + 16.
                    int nameOffset = chunk.Offset + 20;
                    if (chunk.Magic != 0x776F772E || chunk.Size < 80 || nameOffset < 0 || nameOffset + 60 > bytes.Length)
                        continue;
                    var rawName = bytes.AsSpan(nameOffset, 60);
                    int terminator = rawName.IndexOf((byte)0);
                    if (terminator >= 0)
                        rawName = rawName[..terminator];
                    string name = Encoding.GetEncoding(1252).GetString(rawName).Trim();
                    if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                        names.Add(name);
                }

                if (names.Count == 0)
                    diagnostic = scanDiagnostics.FirstOrDefault() ?? "No named .wow worlds found in this container.";
            }
            catch (Exception ex)
            {
                diagnostic = ex.Message;
            }

            var levelNames = names.Where(worldName => worldName.Length > 0 && char.IsDigit(worldName[0])).ToArray();
            var preferredNames = levelNames.Length > 0 ? levelNames : names.Where(worldName => !worldName.Contains("_basic", StringComparison.OrdinalIgnoreCase)).ToArray();
            var displayNames = preferredNames.Length > 0 ? preferredNames : names.ToArray();
            string label = displayNames.Length > 0 ? string.Join(", ", displayNames) : Path.GetFileNameWithoutExtension(entry.Name);
            scenes.Add(new ArchiveSceneItem(entry, label, displayNames, diagnostic));
        }

        return scenes.OrderBy(sceneItem => sceneItem.Label, StringComparer.OrdinalIgnoreCase).ThenBy(sceneItem => sceneItem.Entry.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void OpenSceneContainer(Entry sceneEntry, Action? onLoaded = null)
    {
        if (Archive == null)
            return;
        if (!ConfirmDocumentReplacement())
            return;
        StartJob($"Loading scene {sceneEntry.Id} ({sceneEntry.Name})...", token =>
        {
            var res = new ResourceResolver();
            res.LoadFixContainer(Archive);
            res.LoadSceneContainer(Archive, sceneEntry);
            token.ThrowIfCancellationRequested();
            var doc = SceneLoader.Load(Archive, sceneEntry);
            res.ResolveSceneDocument(doc);
            token.ThrowIfCancellationRequested();
            return () =>
            {
                ClearCinematicPlayback();
                Document = doc;
                Renderer.ClearGpuCache();
                ActiveWorld = InitialWorld(doc);
                Resolver = res;
                Renderer.Document = doc;
                Renderer.Resolver = res;
                Renderer.VideoArchive = Archive;
                Renderer.ActiveWorld = ActiveWorld;
                SelectedObject = ActiveWorld?.Objects.FirstOrDefault();
                CurrentProject = ModProject.CreateForDocument(doc);
                _projectPath = null;
                History.Reset();
                if (SelectedObject != null)
                {
                    Renderer.FrameAll();
                }

                _statusMessage = $"Scene loaded: {sceneEntry.Id} ({sceneEntry.Name}) · Worlds: {doc.Worlds.Count} · Objects: {ActiveWorld?.Objects.Count ?? 0}";
                onLoaded?.Invoke();
            };
        });
    }
}
