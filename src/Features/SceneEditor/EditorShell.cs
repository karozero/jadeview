using System.Numerics;
using System.Text;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;
using Forms = System.Windows.Forms;

namespace JadeView;

/// <summary>
/// Главная оболочка редактора сцен Beyond Good &amp; Evil в стиле Unreal Engine 5 (UE5 Editor Shell).
/// Реализует разделы 4 и 6 плана EDITOR_PLAN.md:
/// 1. Верхняя панель (Toolbar) с режимами Transform (W/E/R), сохранением проекта, сборкой BF и запуском игры.
/// 2. Центральный большой Viewport с рендерингом через SceneRenderer.
/// 3. Справа сверху: Outliner (дерево миров и objects SceneObject, поиск, выбор).
/// 4. Справа снизу: Details (свойства, GaoKey, Transform X/Y/Z с редактированием и Undo/Redo, возможности Capabilities, геометрия и материалы).
/// 5. Снизу: Content Browser (ресурсы мира, вкладки Объекты / Textures / Audio / Text / BF archive / Diagnostics).
/// 6. Горячие клавиши (W/E/R, F, Ctrl+Z, Ctrl+Y, Ctrl+S) и подтверждение при выходе.
/// </summary>
public sealed partial class EditorShell : IDisposable
{
    private static readonly Vector4 Green = new(0.35f, 0.90f, 0.54f, 1.0f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.25f, 1.0f);
    private static readonly Vector4 Red = new(0.95f, 0.35f, 0.35f, 1.0f);
    private static readonly Vector4 Muted = new(0.56f, 0.62f, 0.60f, 1.0f);

    private readonly float _scale;
    private readonly string _cacheDir;

    // Документ и сервисы редактора
    public Archive? Archive { get; private set; }
    public SceneDocument? Document { get; private set; }
    public WorldData? ActiveWorld { get; private set; }
    public ResourceResolver Resolver { get; private set; } = new();
    public SceneRenderer Renderer { get; } = new();
    public GameLauncher Launcher { get; } = new();
    public ModProject? CurrentProject { get; private set; }
    public CommandHistory History { get; } = new();

    // Выделенный объект
    public SceneObject? SelectedObject
    {
        get => Renderer.SelectedObject;
        set
        {
            Renderer.SelectedObject = value;
            if (value != null && ActiveWorld != null && !ActiveWorld.Objects.Contains(value))
            {
                // Если объект из другого мира, переключаем активный мир
                var owningWorld = Document?.Worlds.FirstOrDefault(w => w.Objects.Contains(value));
                if (owningWorld != null)
                {
                    ActiveWorld = owningWorld;
                    Renderer.ActiveWorld = owningWorld;
                }
            }
        }
    }

    // Состояние UI
    private SceneObject? _revealOutliner, _revealContent;
    private string _searchOutliner = "";
    private int _outlinerFilterType = 0; // 0: All, 1: Статические, 2: Restricted
    private int _contentBrowserTab = 0;
    private int? _requestedContentBrowserTab = null;
    public int ContentBrowserTab
    {
        get => _contentBrowserTab;
        set
        {
            _contentBrowserTab = value;
            _requestedContentBrowserTab = value;
        }
    }
    private bool _openExport;
    private SceneObject? _exportTarget;
    private readonly SceneExportOptions _exportOptions = new();
    private bool _showPlayModal;
    private bool _showAboutModal;
    private string _statusMessage = "Ready. Open sally_clean.bf or a project.";
    private string _errorMessage = "";
    private List<ArchiveSceneItem> _archiveScenes = [];

    private sealed record ArchiveSceneItem(Entry Entry, string Label, string[] WorldNames, string? Diagnostic);

    // Настройки привязки и осей
    public bool SnappingEnabled
    {
        get => Renderer.SnappingEnabled;
        set => Renderer.SnappingEnabled = value;
    }
    public float SnappingStep
    {
        get => Renderer.SnappingStep;
        set => Renderer.SnappingStep = value;
    }

    // Ресурсы для вкладок Audio/Text/Каталог
    private Entry? _selectedCatalogEntry;
    private Preview? _catalogPreview;
    private Task<(int Index, Preview Preview)>? _catalogPreviewTask;
    private readonly Dictionary<int, string> _stagedReplacements = new();
    private List<Entry> _visibleCatalogEntries = [];
    private string _catalogFolder = "";
    private string _catalogSearch = "";
    private int _catalogFilter = 0;
    private FolderNode _rootFolder = new("All folders", "");
    private string[] _catalogFilterNames = ["All assets", "Audio", "Textures", "Text", "Models (FF0)", "Replacements"];

    // Аудио вьювер
    private Sound _sound;
    private bool _soundLoaded;
    private bool _soundPlaying;
    private float _volume = 0.7f;
    private float[] _waveform = [];

    // Textурный каталог
    private ResolvedTexture? _selectedTexture;

    // Фоновые задачи
    private Task<Action>? _activeJob;
    private CancellationTokenSource? _jobCancellation;
    private float _jobProgress;
    private string _jobName = "";
    private string? _projectPath;

    private sealed class FolderNode(string name, string path)
    {
        public string Name = name, Path = path;
        public int Count;
        public SortedDictionary<string, FolderNode> Children = new(StringComparer.OrdinalIgnoreCase);
    }

    public EditorShell(float scale)
    {
        _scale = scale;
        _cacheDir = Path.Combine(Path.GetTempPath(), "JadeEditor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);

        // Связываем события рендерера
        Renderer.Resolver = Resolver;
        Renderer.OnSelectionChanged += obj =>
        {
            SelectedObject = obj;
            _revealOutliner = obj;
            _revealContent = obj;
            if(obj!=null){_searchOutliner="";_outlinerFilterType=0;ContentBrowserTab=0;}
        };

        Renderer.OnObjectTransformed += (obj, cmd) =>
        {
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        };

        // Связываем события истории команд с проектом модификации
        History.OnCommandExecuted += OnCommandExecuted;
        History.OnCommandUndone += OnCommandUndone;
        History.OnCommandRedone += OnCommandRedone;
    }

    private void OnCommandExecuted(IEditCommand cmd)
    {
        if (cmd is TransformCommand tc)
        {
            RecordObjectChangeInProject(tc.Target);
        }
    }

    private void OnCommandUndone(IEditCommand cmd)
    {
        if (CurrentProject == null || Document == null) return;
        if (cmd is TransformCommand tc)
        {
            var change = CurrentProject.GetChange(tc.Target.Id, Document.ContainerName);
            if (change != null)
            {
                change.NewTransform = ModTransform.FromTransformData(tc.Target.Transform);
                change.ModifiedAt = DateTimeOffset.UtcNow;
                CurrentProject.ModifiedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private void OnCommandRedone(IEditCommand cmd)
    {
        if (CurrentProject == null || Document == null) return;
        if (cmd is TransformCommand tc)
        {
            var change = CurrentProject.GetChange(tc.Target.Id, Document.ContainerName);
            if (change != null)
            {
                change.NewTransform = ModTransform.FromTransformData(tc.Target.Transform);
                change.ModifiedAt = DateTimeOffset.UtcNow;
                CurrentProject.ModifiedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public string ErrorMessage => _errorMessage;
    public string StatusMessage => _statusMessage;
    public void SetStatus(string message) => _statusMessage = message;
    public void SetError(string error) { _errorMessage = error; _statusMessage = "Error: " + error; }

    private void RecordObjectChangeInProject(SceneObject obj)
    {
        if (CurrentProject == null || Document == null) return;

        var existing = CurrentProject.GetChange(obj.Id, Document.ContainerName);
        TransformData oldTransform;

        if (existing != null)
        {
            oldTransform = new TransformData
            {
                Translation = existing.OldTransform.ToTranslation(),
                Rotation = existing.OldTransform.ToRotation(),
                Scale = existing.OldTransform.ToScale(),
                MatrixByteRange = obj.Transform.MatrixByteRange
            };
        }
        else
        {
            if (History.CanUndo && History.LastExecutedCommand is TransformCommand tc && tc.Target == obj)
            {
                oldTransform = new TransformData
                {
                    Translation = tc.OldTranslation,
                    Rotation = tc.OldRotation,
                    Scale = tc.OldScale,
                    MatrixByteRange = obj.Transform.MatrixByteRange
                };
            }
            else if (obj.InitialTransform != null)
            {
                oldTransform = obj.InitialTransform;
            }
            else
            {
                oldTransform = obj.Transform;
            }
        }

        CurrentProject.RecordChange(
            obj,
            Document.ContainerName,
            ActiveWorld?.Key ?? 0,
            oldTransform,
            obj.Transform,
            translationOnly: obj.Transform.Rotation == oldTransform.Rotation && obj.Transform.Scale == oldTransform.Scale
        );
    }

    private static WorldData? InitialWorld(SceneDocument doc) =>
        doc.Worlds.Where(w => w.Name.Length > 0 && char.IsDigit(w.Name[0]))
            .OrderByDescending(w => w.Objects.Count).FirstOrDefault() ?? doc.ActiveWorld;

    private bool ConfirmDocumentReplacement()
    {
        if (!History.IsDirty) return true;
        var answer = Forms.MessageBox.Show("Save project changes before switching scenes?",
            "JadeView", Forms.MessageBoxButtons.YesNoCancel, Forms.MessageBoxIcon.Question);
        return answer == Forms.DialogResult.No || (answer == Forms.DialogResult.Yes && SaveCurrentProject());
    }

    public void LoadBfArchive(string bfPath)
    {
        if (!ConfirmDocumentReplacement()) return;
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
            var sceneEntry = arch.Entries.FirstOrDefault(e => e.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase))
                ?? arch.Entries.FirstOrDefault(e => e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase));

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
                Document = doc;
                Renderer.ClearGpuCache();
                ActiveWorld = doc != null ? InitialWorld(doc) : null;

                // Передаем контекст в Renderer
                Resolver = res;
                Renderer.Document = doc;
                Renderer.Resolver = res;
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
        foreach (var entry in archive.Entries.Where(e => e.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
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
                    if (terminator >= 0) rawName = rawName[..terminator];
                    string name = Encoding.GetEncoding(1252).GetString(rawName).Trim();
                    if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                }
                if (names.Count == 0)
                    diagnostic = scanDiagnostics.FirstOrDefault() ?? "No named .wow worlds found in this container.";
            }
            catch (Exception ex)
            {
                diagnostic = ex.Message;
            }

            var levelNames = names.Where(n => n.Length > 0 && char.IsDigit(n[0])).ToArray();
            var preferredNames = levelNames.Length > 0 ? levelNames : names.Where(n => !n.Contains("_basic", StringComparison.OrdinalIgnoreCase)).ToArray();
            var displayNames = preferredNames.Length > 0 ? preferredNames : names.ToArray();
            string label = displayNames.Length > 0
                ? string.Join(", ", displayNames)
                : Path.GetFileNameWithoutExtension(entry.Name);
            scenes.Add(new ArchiveSceneItem(entry, label, displayNames, diagnostic));
        }
        return scenes.OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Entry.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void OpenSceneContainer(Entry sceneEntry, Action? onLoaded = null)
    {
        if (Archive == null) return;
        if (!ConfirmDocumentReplacement()) return;

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
                Document = doc;
                Renderer.ClearGpuCache();
                ActiveWorld = InitialWorld(doc);

                Resolver = res;
                Renderer.Document = doc;
                Renderer.Resolver = res;
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

    public void LoadModProject(string projectPath)
    {
        try
        {
            var proj = ModProject.Load(projectPath);
            string? bfToLoad = !string.IsNullOrEmpty(proj.BaseBfPath) && File.Exists(proj.BaseBfPath)
                ? proj.BaseBfPath
                : File.Exists(proj.BaseBfFileName)
                    ? Path.GetFullPath(proj.BaseBfFileName)
                    : File.Exists(App.DefaultArchive) ? App.DefaultArchive : null;

            if (Archive == null || (bfToLoad != null && !string.Equals(Archive.PathName, Path.GetFullPath(bfToLoad), StringComparison.OrdinalIgnoreCase)))
            {
                if (bfToLoad == null)
                {
                    SetError($"Base archive not found: {proj.BaseBfPath} ({proj.BaseBfFileName})");
                    return;
                }

                StartJob($"Loading base archive {Path.GetFileName(bfToLoad)}...", token =>
                {
                    var arch = new Archive(bfToLoad);
                    token.ThrowIfCancellationRequested();
                    var archiveScenes = BuildArchiveSceneIndex(arch, token);

                    // Проверяем хеш
                    if (!proj.VerifyBaseBf(arch.Sha256, out var hashErr))
                    {
                        return () => SetError(hashErr!);
                    }

                    var res = new ResourceResolver();
                    res.LoadFixContainer(arch);

                    var sceneEntry = !string.IsNullOrEmpty(proj.ContainerName)
                        ? arch.Entries.FirstOrDefault(e => e.Id.Equals(proj.ContainerName, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(proj.ContainerName, StringComparison.OrdinalIgnoreCase))
                        : (proj.ContainerKey != 0 ? arch.Entries.FirstOrDefault(e => e.Key == proj.ContainerKey) : null);

                    sceneEntry ??= arch.Entries.FirstOrDefault(e => e.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase))
                        ?? arch.Entries.FirstOrDefault(e => e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase));

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
                        Document = doc;
                        Renderer.ClearGpuCache();
                        ActiveWorld = doc != null ? InitialWorld(doc) : null;
                        Resolver = res;
                        Renderer.Document = doc;
                        Renderer.Resolver = res;
                        Renderer.ActiveWorld = ActiveWorld;
                        SelectedObject = ActiveWorld?.Objects.FirstOrDefault();
                        BuildCatalogFolders();
                        FilterCatalog();

                        if (Document != null)
                        {
                            var applyRes = proj.ApplyToDocument(Document);
                            if (!applyRes.IsSuccess)
                            {
                                SetError(string.Join("; ", applyRes.Errors));
                                return;
                            }
                            CurrentProject = proj;
                            _projectPath = projectPath;
                            History.Reset();
                            _statusMessage = $"Mod project loaded: {Path.GetFileName(projectPath)} (updated {applyRes.ObjectsUpdated} objects)";
                        }
                        else
                        {
                            CurrentProject = proj;
                            _projectPath = projectPath;
                            _statusMessage = $"Project loaded: {Path.GetFileName(projectPath)}";
                        }
                    };
                });
                return;
            }

            // Если архив уже открыт:
            // Проверяем совпадение SHA-256
            if (!proj.VerifyBaseBf(Archive.Sha256, out var hashError))
            {
                SetError(hashError!);
                return;
            }

            // Если текущий открытый контейнер не совпадает с контейнером проекта
            bool needSwitchContainer = !string.IsNullOrEmpty(proj.ContainerName) &&
                (Document == null || !string.Equals(Document.ContainerName, proj.ContainerName, StringComparison.OrdinalIgnoreCase));

            if (needSwitchContainer)
            {
                var sceneEntry = Archive.Entries.FirstOrDefault(e => e.Id.Equals(proj.ContainerName, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(proj.ContainerName, StringComparison.OrdinalIgnoreCase));
                if (sceneEntry != null)
                {
                    OpenSceneContainer(sceneEntry, onLoaded: () =>
                    {
                        if (Document != null)
                        {
                            var applyRes = proj.ApplyToDocument(Document);
                            if (!applyRes.IsSuccess)
                            {
                                SetError(string.Join("; ", applyRes.Errors));
                                return;
                            }
                            CurrentProject = proj;
                            _projectPath = projectPath;
                            History.Reset();
                            _statusMessage = $"Mod project loaded: {Path.GetFileName(projectPath)} (updated {applyRes.ObjectsUpdated} objects)";
                        }
                    });
                    return;
                }
            }

            if (Document != null)
            {
                var applyRes = proj.ApplyToDocument(Document);
                if (!applyRes.IsSuccess)
                {
                    SetError(string.Join("; ", applyRes.Errors));
                    return;
                }
                CurrentProject = proj;
                            _projectPath = projectPath;
                History.Reset();
                _statusMessage = $"Mod project loaded: {Path.GetFileName(projectPath)} (updated {applyRes.ObjectsUpdated} objects)";
            }
            else
            {
                CurrentProject = proj;
                            _projectPath = projectPath;
                _statusMessage = $"Project loaded: {Path.GetFileName(projectPath)}";
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    public bool SaveCurrentProject(string? saveAsPath = null)
    {
        if (CurrentProject == null || Document == null) return false;

        string path = saveAsPath ?? _projectPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{CurrentProject.Name}.bgemod");
        if (saveAsPath == null && _projectPath == null)
        {
            using var d = new Forms.SaveFileDialog
            {
                Title = "Save BGE mod project",
                Filter = "Jade mod project (*.bgemod)|*.bgemod|JSON (*.json)|*.json",
                FileName = $"{Path.GetFileNameWithoutExtension(Document.ContainerName)}_mod.bgemod"
            };
            if (d.ShowDialog() != Forms.DialogResult.OK) return false;
            path = d.FileName;
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

    public void PlayGameWithCurrentMod()
    {
        if (Launcher.IsGameRunning(out int? pid))
        {
            SetError($"BGE.exe is already running (PID {pid}).");
            return;
        }

        bool hasModChanges = CurrentProject != null && CurrentProject.Changes.Any(c => c.HasChanges);
        if (hasModChanges && Archive != null && Document != null && CurrentProject != null)
        {
            try
            {
                var entry = Archive.Entries.FirstOrDefault(e =>
                    e.Id.Equals(Document.ContainerName, StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Equals(Document.ContainerName, StringComparison.OrdinalIgnoreCase))
                    ?? Archive.Entries.FirstOrDefault(e => e.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    SetError($"Container {Document.ContainerName} not found in the base archive.");
                    return;
                }

                byte[] originalBytes = Archive.Read(entry);
                var plan = CurrentProject.BuildWritePlan(Document, originalBytes);
                if (plan.Patches.Count > 0)
                {
                    byte[] patchedBytes = SceneWriter.CreatePatchedContainer(originalBytes, plan);
                    var installRes = Launcher.InstallMod(Archive, plan, patchedBytes, CurrentProject.Name);
                    if (!installRes.IsSuccess)
                    {
                        SetError($"Mod installation failed: {installRes.Error}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                SetError($"Failed to prepare mod: {ex.Message}");
                return;
            }
        }

        var launchRes = Launcher.LaunchGame();
        if (launchRes.IsSuccess)
        {
            _statusMessage = hasModChanges
                ? $"Mod installed and game started (PID {launchRes.ProcessId})"
                : $"Game started (PID {launchRes.ProcessId})";
        }
        else
        {
            SetError(launchRes.Error ?? "Failed to start game.");
        }
    }

    public void BuildModdedBf()
    {
        if (Archive == null || Document == null || CurrentProject == null)
        {
            SetError("No archive or project loaded.");
            return;
        }

        using var d = new Forms.SaveFileDialog
        {
            Title = "Build modified BigFile (BF)",
            Filter = "Beyond Good & Evil archive (*.bf)|*.bf",
            FileName = "sally_modded.bf"
        };
        if (d.ShowDialog() != Forms.DialogResult.OK) return;
        string destBf = d.FileName;

        if (Archive.SamePath(destBf, Archive.PathName) || Archive.SamePath(destBf, GameInstallation.DefaultGameDirectory + "\\" + GameInstallation.CleanBfFileName))
        {
            SetError("Choose a different filename to preserve the original game archive.");
            return;
        }

        StartJob("Building and verifying modified BF archive...", token =>
        {
            var sceneEntry = Archive.Entries.FirstOrDefault(e => e.Name.Equals(Document.ContainerName, StringComparison.OrdinalIgnoreCase))
                ?? Archive.Entries.First(e => e.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase));

            byte[] originalBytes = Archive.Read(sceneEntry);
            var plan = CurrentProject.BuildWritePlan(Document, originalBytes);

            // Создаем точечно пропатченный буфер через SceneWriter
            byte[] patchedBytes = SceneWriter.CreatePatchedContainer(originalBytes, plan);

            // Сборка BF через SceneWriter.BuildAndSaveArchive
            SceneWriter.BuildAndSaveArchive(Archive, destBf, sceneEntry.Index, patchedBytes, p =>
            {
                token.ThrowIfCancellationRequested();
                _jobProgress = p;
            });

            return () =>
            {
                _statusMessage = $"Modified archive built and verified: {destBf}";
            };
        });
    }

    public void Update()
    {
        // Завершение фоновых задач
        if (_activeJob is { IsCompleted: true })
        {
            var job = _activeJob;
            _activeJob = null;
            try { job.GetAwaiter().GetResult()(); }
            catch (OperationCanceledException) { _statusMessage = "Operation cancelled."; }
            catch (Exception ex) { SetError(ex.Message); }
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            _jobProgress = 0;
        }

        // Завершение предпросмотра файла в каталоге
        if (_catalogPreviewTask is { IsCompleted: true })
        {
            try
            {
                var result = _catalogPreviewTask.GetAwaiter().GetResult();
                if (_selectedCatalogEntry?.Index == result.Index)
                {
                    _catalogPreview = result.Preview;
                    LoadCatalogAudio();
                }
            }
            catch (Exception ex) { SetError(ex.Message); }
            _catalogPreviewTask = null;
        }

        // Обработка глобальных горячих клавиш (когда нет активного ввода в текстовое поле ImGui)
        if (!ImGui.GetIO().WantTextInput && !ImGui.IsAnyItemActive() && !Renderer.IsFlying && !Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.W)) Renderer.GizmoMode = TransformGizmoMode.Translate;
            if (Raylib.IsKeyPressed(KeyboardKey.F) && SelectedObject != null) Renderer.FrameSelected();

            if (Raylib.IsKeyPressed(KeyboardKey.H) && !Renderer.IsDraggingGizmo)
            {
                bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                var command = Renderer.VisibilityChange(alt ? "show" : shift ? "isolate" : "hide");
                if (command != null) History.Execute(command);
            }

            // Undo / Redo
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.Z)) History.Undo();
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) History.Redo();
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) SaveCurrentProject();
        }
    }

    public void Draw()
    {
        Update();

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        ImGui.Begin("JadeEditorShell", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus);

        // 1. Верхняя панель инструментов (Toolbar)
        DrawToolbar();

        ImGui.Separator();

        // Расчет геометрии компоновки UE5
        float availX = ImGui.GetContentRegionAvail().X;
        float availY = ImGui.GetContentRegionAvail().Y;
        float statusHeight = 24.0f * _scale;
        float bodyHeight = availY - statusHeight - 6.0f;

        float bottomPanelHeight = Math.Clamp(bodyHeight * 0.32f, 180.0f * _scale, 320.0f * _scale);
        float topPanelsHeight = bodyHeight - bottomPanelHeight - 6.0f;

        float rightPanelWidth = Math.Clamp(availX * 0.28f, 320.0f * _scale, 480.0f * _scale);
        float viewportWidth = availX - rightPanelWidth - 8.0f;

        // ВЕРХНИЙ РЯД: Вьюпорт (слева) + Outliner & Details (справа)
        ImGui.BeginChild("TopRowArea", new Vector2(availX, topPanelsHeight), ImGuiChildFlags.None);
        {
            // Центральный большой Viewport
            ImGui.BeginChild("CentralViewportContainer", new Vector2(viewportWidth, topPanelsHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            Renderer.DrawViewport("MainViewport", ImGui.GetContentRegionAvail());
            ImGui.EndChild();

            ImGui.SameLine();

            // Правая колонка: Outliner (сверху) и Details (снизу)
            ImGui.BeginChild("RightColumn", new Vector2(rightPanelWidth, topPanelsHeight), ImGuiChildFlags.None);
            {
                float outlinerHeight = topPanelsHeight * (SelectedObject?.Actor != null ? 0.68f : 0.46f);
                float detailsHeight = topPanelsHeight - outlinerHeight - 6.0f;

                ImGui.BeginChild("OutlinerPanel", new Vector2(rightPanelWidth, outlinerHeight), ImGuiChildFlags.Borders);
                DrawOutliner();
                ImGui.EndChild();

                ImGui.Spacing();

                ImGui.BeginChild("DetailsPanel", new Vector2(rightPanelWidth, detailsHeight), ImGuiChildFlags.Borders);
                DrawDetails();
                ImGui.EndChild();
            }
            ImGui.EndChild();
        }
        ImGui.EndChild();

        ImGui.Spacing();

        // НИЖНИЙ РЯД: Content Browser
        ImGui.BeginChild("ContentBrowserPanel", new Vector2(availX, bottomPanelHeight), ImGuiChildFlags.Borders);
        DrawContentBrowser();
        ImGui.EndChild();

        // 6. Statusная строка в самом низу
        DrawStatusBar();

        // Модальные окна
        DrawModals();
        DrawExportDialog();

        ImGui.End();
    }







}
