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
public sealed class EditorShell : IDisposable
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

    #region 1. Панель инструментов (Toolbar)

    private void DrawToolbar()
    {
        ImGui.BeginDisabled(_activeJob != null);

        // Кнопки работы с файлами
        if (ImGui.Button("Open BF"))
        {
            using var d = new Forms.OpenFileDialog
            {
                Title = "Open original Beyond Good & Evil archive",
                Filter = "BigFile archive (*.bf)|*.bf|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (d.ShowDialog() == Forms.DialogResult.OK) LoadBfArchive(d.FileName);
        }
        ImGui.SameLine();

        if (ImGui.Button("Open project"))
        {
            using var d = new Forms.OpenFileDialog
            {
                Title = "Open BGE mod project",
                Filter = "Jade mod project (*.bgemod)|*.bgemod|JSON (*.json)|*.json",
                CheckFileExists = true
            };
            if (d.ShowDialog() == Forms.DialogResult.OK) LoadModProject(d.FileName);
        }
        ImGui.SameLine();

        bool isDirty = History.IsDirty;
        if (isDirty) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.42f, 0.25f, 1.0f));
        string saveLabel = isDirty ? "Save project *" : "Save project";
        if (ImGui.Button(saveLabel)) SaveCurrentProject();
        if (isDirty) ImGui.PopStyleColor();
        ImGui.SameLine();

        if (ImGui.Button("Build BF")) BuildModdedBf();
        ImGui.SameLine();

        ImGui.BeginDisabled(ActiveWorld == null);
        if(ImGui.Button("Export")){_exportTarget=null;_openExport=true;}
        ImGui.EndDisabled();
        ImGui.SameLine();

        // Правая часть Toolbar: Кнопка «Play» и «Restore original BF»
        float rightControlsWidth = 430.0f * _scale;
        float curX = ImGui.GetCursorPosX();
        float targetX = ImGui.GetWindowWidth() - rightControlsWidth;
        if (targetX > curX) ImGui.SameLine(targetX);

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.50f, 0.28f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.18f, 0.65f, 0.36f, 1.0f));
        if (ImGui.Button("PLAY (BGE)", new Vector2(120.0f * _scale, 0)))
        {
            _showPlayModal = true;
        }
        ImGui.PopStyleColor(2);
        ImGui.SameLine();

        if (ImGui.Button("Restore original BF"))
        {
            var restoreRes = Launcher.RestoreOriginalBf();
            if (restoreRes.IsSuccess)
            {
                _statusMessage = $"Archive restored: {restoreRes.RestoredPath}";
            }
            else
            {
                SetError(restoreRes.Error ?? "Failed to restore archive.");
            }
        }
        ImGui.SameLine();

        if (ImGui.Button("About")) _showAboutModal = true;

        ImGui.EndDisabled();
    }

    #endregion

    #region 3. Outliner (Дерево objects справа сверху)

    private void DrawOutliner()
    {
        ImGui.TextColored(Green, "OUTLINER");
        ImGui.SameLine();
        ImGui.TextDisabled(ActiveWorld != null ? $"{ActiveWorld.Name} ({ActiveWorld.Objects.Count})" : "No world");

        if (Archive != null && _archiveScenes.Count > 0)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.BeginDisabled(_activeJob != null);
            var activeArchiveScene = _archiveScenes.FirstOrDefault(s =>
                string.Equals(Document?.ContainerName, s.Entry.Name, StringComparison.OrdinalIgnoreCase));
            string scenePreview = activeArchiveScene != null
                ? $"{activeArchiveScene.Label} · {activeArchiveScene.Entry.Id}"
                : "Archive scenes";
            if (ImGui.BeginCombo("##ArchiveScenePicker", scenePreview))
            {
                foreach (var scene in _archiveScenes)
                {
                    string label = $"{scene.Label} · {scene.Entry.Id}";
                    bool selected=Document?.ContainerName.Equals(scene.Entry.Name, StringComparison.OrdinalIgnoreCase)==true;
                    if (ImGui.Selectable(label, selected))
                        OpenSceneContainer(scene.Entry);
                    if (selected) ImGui.SetItemDefaultFocus();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(label + (scene.Diagnostic != null ? "\n" + scene.Diagnostic : ""));
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
        }

        // Выбор активного мира, если их несколько в FF0
        if (Document != null && Document.Worlds.Count > 1)
        {
            if (ImGui.BeginCombo("##WorldPicker", ActiveWorld?.Name ?? "Select world"))
            {
                foreach (var w in Document.Worlds)
                {
                    bool selected=w==ActiveWorld;
                    if (ImGui.Selectable($"{w.Name} ({w.Objects.Count} objects)", selected))
                    {
                        ActiveWorld = w;
                        Renderer.ActiveWorld = w;
                        SelectedObject = w.Objects.FirstOrDefault();
                        Renderer.FrameAll();
                    }
                    if(selected)ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchOutliner", "Search name or GAO key...", ref _searchOutliner, 128);

        // Фильтры категорий
        if (ImGui.RadioButton("All", ref _outlinerFilterType, 0)) { }
        ImGui.SameLine();
        if (ImGui.RadioButton("Static", ref _outlinerFilterType, 1)) { }
        ImGui.SameLine();
        if (ImGui.RadioButton("Restricted", ref _outlinerFilterType, 2)) { }

        ImGui.Separator();

        if (ActiveWorld == null || ActiveWorld.Objects.Count == 0)
        {
            ImGui.TextDisabled("No objects in the active world.");
            return;
        }

        var filtered = ActiveWorld.Objects.Where(o =>
        {
            if (!string.IsNullOrEmpty(_searchOutliner))
            {
                bool matches = o.Name.Contains(_searchOutliner, StringComparison.OrdinalIgnoreCase) ||
                               o.GaoKey.ToString("X8").Contains(_searchOutliner, StringComparison.OrdinalIgnoreCase);
                if (!matches) return false;
            }
            if (_outlinerFilterType == 1 && !o.Capabilities.CanTranslate) return false;
            if (_outlinerFilterType == 2 && o.Capabilities.CanTranslate) return false;
            return true;
        }).ToList();

        ImGui.BeginChild("OutlinerList", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawAnimationControls();
        var shown = filtered.ToHashSet();
        foreach (var actor in ActiveWorld.Actors)
        {
            if(!actor.Members.Any(shown.Contains))continue;
            ImGui.PushID(actor.Root.Id);
            var flags=ImGuiTreeNodeFlags.OpenOnArrow|ImGuiTreeNodeFlags.SpanAvailWidth;
            if(SelectedObject==actor.Root)flags|=ImGuiTreeNodeFlags.Selected;
            if (_revealOutliner?.Actor==actor && _revealOutliner!=actor.Root) ImGui.SetNextItemOpen(true);
            bool open=ImGui.TreeNodeEx($"[Actor] {actor.Root.Name} ({actor.Members.Count-1})",flags);
            DrawObjectContextMenu(actor.Root);
            if(_revealOutliner==actor.Root){ImGui.SetScrollHereY(.5f);_revealOutliner=null;}
            if(ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())SelectedObject=actor.Root;
            if(ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)){SelectedObject=actor.Root;Renderer.FrameSelected();}
            if(open)
            {
                foreach(var component in actor.Members.Where(o=>o!=actor.Root))
                {
                    if(ImGui.Selectable($"{component.Name}##{component.Id}",SelectedObject==component))SelectedObject=component;
                    DrawObjectContextMenu(component);
                    if(_revealOutliner==component){ImGui.SetScrollHereY(.5f);_revealOutliner=null;}
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        foreach(var obj in filtered.Where(o=>o.Actor==null))
        {
            ImGui.PushID(obj.Id);
            if(ImGui.Selectable($"{(Renderer.IsHidden(obj)?"[hidden] ":"")}{obj.Name} ({obj.GaoKey:X8})",SelectedObject==obj))SelectedObject=obj;
            DrawObjectContextMenu(obj);
            if(_revealOutliner==obj){ImGui.SetScrollHereY(.5f);_revealOutliner=null;}
            if(ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)){SelectedObject=obj;Renderer.FrameSelected();}
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private void DrawObjectContextMenu(SceneObject obj)
    {
        if(!ImGui.BeginPopupContextItem("ObjectActions##"+obj.Id))return;
        SelectedObject=obj;
        if(ImGui.MenuItem("Export...")){_exportTarget=obj;_openExport=true;}
        ImGui.EndPopup();
    }

    private void DrawExportDialog()
    {
        if(_openExport){ImGui.OpenPopup("Export GLB");_openExport=false;}
        ImGui.SetNextWindowSize(new Vector2(460*_scale,0),ImGuiCond.Appearing);
        if(!ImGui.BeginPopupModal("Export GLB",ImGuiWindowFlags.AlwaysAutoResize))return;
        ImGui.TextWrapped(_exportTarget==null?$"Level: {ActiveWorld?.Name}":$"Object: {_exportTarget.Name}");
        if(_exportTarget?.Actor is {} actor && _exportTarget==actor.Root)ImGui.TextDisabled($"Actor with {actor.Members.Count-1} components");
        ImGui.Separator();
        ImGui.Checkbox("Embed textures",ref _exportOptions.Textures);
        if(_exportTarget==null)ImGui.Checkbox("Include actors",ref _exportOptions.Actors);
        ImGui.Checkbox("Include collision meshes",ref _exportOptions.Collisions);
        ImGui.Checkbox("Include unknown GAOs",ref _exportOptions.UnknownObjects);
        if(_exportOptions.UnknownObjects)ImGui.TextWrapped("GAOs without decoded geometry become named empty objects with their source keys.");
        ImGui.Checkbox("Include game-hidden objects",ref _exportOptions.GameHidden);
        bool hasAnimations=_exportTarget?.Actor!=null || _exportTarget==null && ActiveWorld?.Actors.Count>0;
        ImGui.BeginDisabled(!hasAnimations);
        ImGui.Checkbox("Animations and skinning",ref _exportOptions.Animations);
        if(_exportOptions.Animations)ImGui.Checkbox("All clips (otherwise current / first)",ref _exportOptions.AllClips);
        ImGui.EndDisabled();
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##ExportPlacement",ref _exportOptions.Placement,new[]{"Current placement","Original game placement","At origin"},3);
        ImGui.TextWrapped("One .glb file for Blender. Jade multi-pass effects are simplified; animations use the current mesh appearance.");
        ImGui.Separator();
        if(ImGui.Button("Export...",new Vector2(120*_scale,0)) && ActiveWorld!=null)
        {
            using var dialog=new Forms.SaveFileDialog{Title="Export Blender-compatible GLB",Filter="Binary glTF (*.glb)|*.glb",DefaultExt="glb",AddExtension=true,FileName=SceneExporter.SafeFileName(_exportTarget?.Name??ActiveWorld.Name)+".glb"};
            if(dialog.ShowDialog()==Forms.DialogResult.OK)
            {
                try
                {
                    Forms.Cursor.Current=Forms.Cursors.WaitCursor;
                    var result=SceneExporter.Export(dialog.FileName,ActiveWorld,Resolver,_exportOptions,_exportTarget);
                    _statusMessage=$"Exported {result.Meshes} meshes, {result.Textures} textures, {result.Animations} animations, {result.EmptyObjects} empty GAOs: {dialog.FileName}";
                    if(result.Warnings.Length>0)_statusMessage+=$" ({result.Warnings.Length} format limitations; see GLB extras)";
                    ImGui.CloseCurrentPopup();
                }
                catch(Exception ex){SetError("Export failed: "+ex.Message);}
                finally{Forms.Cursor.Current=Forms.Cursors.Default;}
            }
        }
        ImGui.SameLine();if(ImGui.Button("Cancel"))ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawAnimationControls()
    {
        var actor=SelectedObject?.Actor;if(actor==null)return;
        if(!ImGui.CollapsingHeader($"Animation · {actor.Root.Name}##ActorAnimation",ImGuiTreeNodeFlags.DefaultOpen))return;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(5,2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(6,3));
        ImGui.SetNextItemWidth(-1);
        if(ImGui.BeginCombo("##ActorClip",actor.Clip?.Name??"Default pose"))
        {
            bool defaultSelected=actor.Clip==null;
            if(ImGui.Selectable("Default pose",defaultSelected))actor.SelectClip(null);
            if(defaultSelected)ImGui.SetItemDefaultFocus();
            for(int i=0;i<actor.Clips.Count;i++)
            {
                var clip=actor.Clips[i];
                bool selected=actor.Clip==clip;
                if(ImGui.Selectable($"{i:D3} · {clip.Name}",selected))actor.SelectClip(clip);
                if(selected)ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        if(actor.Diagnostic!=null)ImGui.TextWrapped(actor.Diagnostic);
        else if(actor.Clips.Count==0)ImGui.TextDisabled("No animations found in loaded packages.");
        if(actor.Clip is {} c)
        {
            if(ImGui.Button(actor.Playing?"Pause":"Play")){if(!actor.Playing && actor.Time>=c.Duration)actor.Time=0;actor.Playing=!actor.Playing;}
            ImGui.SameLine();if(ImGui.Button("Restart")){actor.Time=0;actor.Evaluate(0);}
            ImGui.SameLine();ImGui.Checkbox("Loop",ref actor.Loop);
            ImGui.Checkbox("Root motion",ref actor.RootMotion);
            float time=actor.Time;ImGui.SetNextItemWidth(-1);
            if(ImGui.SliderFloat("##ClipTime",ref time,0,Math.Max(c.Duration,.001f),"%.2f s")){actor.Time=time;actor.Playing=false;actor.Evaluate(0);}
            ImGui.SetNextItemWidth(100);ImGui.SliderFloat("Speed",ref actor.Speed,.1f,3,"%.2fx");
            if(c.Error!=null)ImGui.TextColored(Yellow,c.Error);
            else if(c.Unsupported>0)ImGui.TextColored(Yellow,$"Unsupported events: {c.Unsupported}");
        }
        ImGui.Separator();
        ImGui.PopStyleVar(2);
    }

    #endregion

    #region 4. Details (Панель свойств справа снизу)

    private void DrawDetails()
    {
        ImGui.TextColored(Green, "DETAILS");
        ImGui.Separator();

        if (SelectedObject == null)
        {
            ImGui.TextDisabled("Select an object in the Outliner or viewport.");
            return;
        }

        var obj = SelectedObject;

        // Идентификация
        ImGui.TextUnformatted(obj.Name);
        ImGui.TextDisabled($"GaoKey: 0x{obj.GaoKey:X8} | ID: {obj.Id}");
        ImGui.TextDisabled($"Record: offset 0x{obj.RecordRange.Offset:X8}, Size {obj.RecordRange.Size} B");

        ImGui.Spacing();

        // Status возможностей редактирования (Capabilities)
        if (obj.Capabilities.CanTranslate)
        {
            ImGui.TextColored(Green, "Position editing available (In-place patch)");
        }
        else
        {
            ImGui.TextColored(Yellow, $"Restriction: {obj.Capabilities.RestrictionReason}");
        }

        ImGui.Separator();
        ImGui.TextColored(Muted, "TRANSFORM");

        // Редактирование координат Translation
        Vector3 curPos = obj.Transform.Translation;

        ImGui.BeginDisabled(!obj.Capabilities.CanTranslate);
        if (ImGui.DragFloat3("Position X/Y/Z", ref curPos, SnappingEnabled ? SnappingStep : 0.1f))
        {
            if (SnappingEnabled && SnappingStep > 1e-4f)
            {
                curPos = new Vector3(
                    MathF.Round(curPos.X / SnappingStep) * SnappingStep,
                    MathF.Round(curPos.Y / SnappingStep) * SnappingStep,
                    MathF.Round(curPos.Z / SnappingStep) * SnappingStep
                );
            }
            var cmd = new TransformCommand(obj, curPos, obj.Transform.Rotation, obj.Transform.Scale);
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        }
        ImGui.EndDisabled();

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(obj.Transform.Rotation);
        Vector3 angles = new(
            MathF.Asin(Math.Clamp(-rotationMatrix.M32, -1, 1)),
            MathF.Atan2(rotationMatrix.M31, rotationMatrix.M33),
            MathF.Atan2(rotationMatrix.M12, rotationMatrix.M22));
        angles *= 180 / MathF.PI;
        ImGui.BeginDisabled(!obj.Capabilities.CanRotate);
        if (ImGui.DragFloat3("Rotation X/Y/Z (degrees)", ref angles, 0.5f))
        {
            angles *= MathF.PI / 180;
            var rotation = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(angles.Y, angles.X, angles.Z));
            History.Execute(new TransformCommand(obj, obj.Transform.Translation, rotation, obj.Transform.Scale));
        }
        ImGui.EndDisabled();

        // Масштабирование Scale
        Vector3 curScale = obj.Transform.Scale;
        ImGui.BeginDisabled(!obj.Capabilities.CanScale);
        if (ImGui.DragFloat3("Scale X/Y/Z", ref curScale, 0.05f, 0.01f, 100.0f))
        {
            var cmd = new TransformCommand(obj, obj.Transform.Translation, obj.Transform.Rotation, curScale);
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        }
        ImGui.EndDisabled();
        bool canReset = obj.Capabilities.CanTranslate || obj.Capabilities.CanRotate || obj.Capabilities.CanScale;
        ImGui.BeginDisabled(!canReset);
        if (ImGui.SmallButton("Reset to zero"))
        {
            Vector3 targetPos = obj.Capabilities.CanTranslate ? Vector3.Zero : obj.Transform.Translation;
            Quaternion targetRot = obj.Capabilities.CanRotate ? Quaternion.Identity : obj.Transform.Rotation;
            Vector3 targetScale = obj.Capabilities.CanScale ? Vector3.One : obj.Transform.Scale;

            var cmd = new TransformCommand(obj, targetPos, targetRot, targetScale);
            History.Execute(cmd);
            RecordObjectChangeInProject(obj);
        }
        ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextColored(Muted, "VISUAL ASSETS");

        // Геометрия
        ImGui.TextUnformatted($"Geometry GRO: 0x{obj.GeometryKey:X8}");
        if (Resolver.Geometries.TryGetValue(obj.GeometryKey, out var geo))
        {
            ImGui.TextDisabled($"Vertices: {geo.Positions.Length:N0} · Triangles: {geo.Triangles:N0} · Parts: {geo.Parts.Count}");
        }
        else
        {
            ImGui.TextDisabled("No geometry, or this is a helper object.");
        }

        // Материал
        ImGui.TextUnformatted($"Material GRM: 0x{obj.MaterialKey:X8}");
        if (Resolver.Materials.TryGetValue(obj.MaterialKey, out var mat))
        {
            ImGui.TextDisabled($"Type: {(mat.Type == 4 ? "MultiMaterial" : "MultiTexture")} · Submaterials: {mat.SubMaterialKeys.Count} · Textures: {mat.TextureKeys.Count}");
        }
        else
        {
            ImGui.TextDisabled("No material (default/fallback used).");
        }
    }

    #endregion

    #region 5. Content Browser (Обозреватель ресурсов снизу)

    private static unsafe bool BeginTabItemWithFlags(string label, ImGuiTabItemFlags flags)
    {
        int byteCount = Encoding.UTF8.GetByteCount(label);
        byte* utf8 = stackalloc byte[byteCount + 1];
        fixed (char* chars = label)
        {
            Encoding.UTF8.GetBytes(chars, label.Length, utf8, byteCount);
        }
        utf8[byteCount] = 0;
        return ImGuiNative.igBeginTabItem(utf8, null, flags) != 0;
    }

    private void DrawContentBrowser()
    {
        ImGui.TextColored(Green, "CONTENT BROWSER");
        ImGui.SameLine();

        if (ImGui.BeginTabBar("ContentBrowserTabs"))
        {
            var tab0Flags = _requestedContentBrowserTab == 0 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Scene objects", tab0Flags))
            {
                _contentBrowserTab = 0;
                DrawSceneObjectsTable();
                ImGui.EndTabItem();
            }

            var tab1Flags = _requestedContentBrowserTab == 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Textures", tab1Flags))
            {
                _contentBrowserTab = 1;
                DrawTexturesTab();
                ImGui.EndTabItem();
            }

            var tab2Flags = _requestedContentBrowserTab == 2 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Audio", tab2Flags))
            {
                _contentBrowserTab = 2;
                DrawAudioTab();
                ImGui.EndTabItem();
            }

            var tab3Flags = _requestedContentBrowserTab == 3 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Text", tab3Flags))
            {
                _contentBrowserTab = 3;
                DrawTextTab();
                ImGui.EndTabItem();
            }

            var tab4Flags = _requestedContentBrowserTab == 4 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("BF archive", tab4Flags))
            {
                _contentBrowserTab = 4;
                DrawCatalogTab();
                ImGui.EndTabItem();
            }

            var tab5Flags = _requestedContentBrowserTab == 5 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (BeginTabItemWithFlags("Diagnostics", tab5Flags))
            {
                _contentBrowserTab = 5;
                DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            _requestedContentBrowserTab = null;
            ImGui.EndTabBar();
        }
    }

    private void DrawSceneObjectsTable()
    {
        if (ActiveWorld == null)
        {
            ImGui.TextDisabled("No scene loaded.");
            return;
        }

        if (ImGui.BeginTable("ObjectsTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Object name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("GAO Key", ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
            ImGui.TableSetupColumn("GRO Key", ImGuiTableColumnFlags.WidthFixed, 90 * _scale);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100 * _scale);
            ImGui.TableSetupColumn("Position X,Y,Z", ImGuiTableColumnFlags.WidthFixed, 180 * _scale);
            ImGui.TableHeadersRow();

            foreach (var o in ActiveWorld.Objects)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                bool isSelected = SelectedObject?.Id == o.Id;
                if (ImGui.Selectable($"{o.Name}##{o.Id}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    SelectedObject = o;
                }

                DrawObjectContextMenu(o);
                if(_revealContent==o){ImGui.SetScrollHereY(.5f);_revealContent=null;}
                ImGui.TableNextColumn();
                ImGui.TextDisabled(o.GaoKey.ToString("X8"));

                ImGui.TableNextColumn();
                ImGui.TextDisabled(o.GeometryKey != 0 ? o.GeometryKey.ToString("X8") : "—");

                ImGui.TableNextColumn();
                if (o.Capabilities.CanTranslate)
                    ImGui.TextColored(Green, "Static");
                else
                    ImGui.TextColored(Yellow, "Restricted");

                ImGui.TableNextColumn();
                ImGui.TextDisabled($"{o.Transform.Translation.X:F1}, {o.Transform.Translation.Y:F1}, {o.Transform.Translation.Z:F1}");
            }
            ImGui.EndTable();
        }
    }

    private void DrawTexturesTab()
    {
        var textures = Resolver.TextureVariants.ToList();
        if (textures.Count == 0)
        {
            ImGui.TextDisabled("No resolved textures in the current scene or FIX.");
            return;
        }

        ImGui.TextDisabled($"Textures: {textures.Count} (including scene containers and global FIX)");
        ImGui.Separator();

        float listWidth = 280.0f * _scale;
        ImGui.BeginChild("TextureListArea", new Vector2(listWidth, 0), ImGuiChildFlags.Borders);
        {
            foreach (var tex in textures)
            {
                bool isSelected = _selectedTexture?.RawKey == tex.RawKey;
                string label = $"{tex.Id} ({tex.Width}x{tex.Height})";
                if (tex.IsFallback) label += " [FALLBACK]";

                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedTexture = tex;
                }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("TexturePreviewArea", new Vector2(0, 0), ImGuiChildFlags.Borders);
        {
            if (_selectedTexture != null)
            {
                var tex = _selectedTexture;
                ImGui.TextColored(Green, $"Texture: {tex.Id}");
                ImGui.TextDisabled($"Size: {tex.Width} × {tex.Height} · Format: 0x{tex.FormatType:X4} · {(tex.IsFallback ? "Generated fallback" : "BGE original")}");

                if (ImGui.Button("Export PNG"))
                {
                    using var d = new Forms.SaveFileDialog
                    {
                        Filter = "PNG image (*.png)|*.png",
                        FileName = SceneExporter.SafeFileName(tex.Id.ToString()) + ".png"
                    };
                    if (d.ShowDialog() == Forms.DialogResult.OK)
                    {
                        try
                        {
                            byte[] rgba = Preview.DecodeTga(tex.ToTextureAsset());
                            unsafe
                            {
                                fixed (byte* p = rgba)
                                {
                                    Image img = new() { Data = p, Width = tex.Width, Height = tex.Height, Format = PixelFormat.UncompressedR8G8B8A8, Mipmaps = 1 };
                                    File.WriteAllBytes(d.FileName, Png(img));
                                    _statusMessage = $"Texture exported: {d.FileName}";
                                }
                            }
                        }
                        catch (Exception ex) { SetError(ex.Message); }
                    }
                }

                var gpuTex = Renderer.GetOrUploadTexture(tex.ToTextureAsset());
                if (gpuTex.Id != 0)
                {
                    var avail = ImGui.GetContentRegionAvail();
                    float fit = Math.Min(avail.X / gpuTex.Width, avail.Y / gpuTex.Height);
                    fit = Math.Clamp(fit, 0.1f, 4.0f);
                    Vector2 displaySize = new(gpuTex.Width * fit, gpuTex.Height * fit);
                    ImGui.Image((nint)gpuTex.Id, displaySize);
                }
            }
            else
            {
                ImGui.TextDisabled("Select a texture on the left to preview or export.");
            }
        }
        ImGui.EndChild();
    }

    private void DrawAudioTab()
    {
        if (_catalogPreview?.Audio is not { } audio)
        {
            ImGui.TextDisabled("Select an audio file (.wad, .waa, .wam, .wac) in the BF archive.");
            if (Archive != null && ImGui.Button("Find first audio file"))
            {
                var soundEntry = Archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".waa", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase));
                if (soundEntry != null) SelectCatalogEntry(soundEntry);
            }
            return;
        }

        ImGui.TextColored(Green, $"Audio: {_selectedCatalogEntry?.Name}");
        ImGui.TextDisabled($"Duration: {audio.Seconds:F1} s · Sample rate: {audio.Rate} Hz · Channels: {audio.Channels} · Format: PCM 16-bit");

        if (_waveform.Length > 0)
        {
            ImGui.PlotHistogram("##Waveform", ref _waveform[0], _waveform.Length, 0, "", 0, 1, new Vector2(-1, 80 * _scale));
        }

        ImGui.BeginDisabled(!_soundLoaded);
        if (ImGui.Button(_soundPlaying ? "Pause" : "Play"))
        {
            if (_soundPlaying)
            {
                Raylib.PauseSound(_sound);
                _soundPlaying = false;
            }
            else
            {
                Raylib.PlaySound(_sound);
                _soundPlaying = true;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop"))
        {
            Raylib.StopSound(_sound);
            _soundPlaying = false;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * _scale);
        if (ImGui.SliderFloat("Volume", ref _volume, 0, 1) && _soundLoaded)
        {
            Raylib.SetSoundVolume(_sound, _volume);
        }

        ImGui.SameLine();
        if (ImGui.Button("Export WAV"))
        {
            using var d = new Forms.SaveFileDialog
            {
                Filter = "WAVE Audio (*.wav)|*.wav",
                FileName = Path.GetFileNameWithoutExtension(_selectedCatalogEntry?.Name ?? "sound") + ".wav"
            };
            if (d.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllBytes(d.FileName, audio.Wav);
                _statusMessage = $"Audio exported: {d.FileName}";
            }
        }
    }

    private void DrawTextTab()
    {
        if (_catalogPreview?.Text is not { } txtDoc)
        {
            ImGui.TextDisabled("Select a text file (fd-prefixed .bin) in the BF archive.");
            if (Archive != null && ImGui.Button("Find first text file"))
            {
                var textEntry = Archive.Entries.FirstOrDefault(e => e.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase));
                if (textEntry != null) SelectCatalogEntry(textEntry);
            }
            return;
        }

        ImGui.TextColored(Green, $"Text: {_selectedCatalogEntry?.Name}");
        ImGui.SameLine();

        if (ImGui.Button("Export JSON"))
        {
            using var d = new Forms.SaveFileDialog
            {
                Filter = "Jade text JSON (*.json)|*.json",
                FileName = (_selectedCatalogEntry?.Name ?? "text") + ".json"
            };
            if (d.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllText(d.FileName, txtDoc.Json());
                _statusMessage = $"Text exported: {d.FileName}";
            }
        }

        ImGui.Separator();
        ImGui.BeginChild("TextLinesArea", new Vector2(0, 0), ImGuiChildFlags.None);
        ImGui.TextUnformatted(_catalogPreview?.Readable ?? "");
        ImGui.EndChild();
    }

    private void DrawCatalogTab()
    {
        if (Archive == null)
        {
            ImGui.TextDisabled("No BigFile archive loaded.");
            return;
        }

        // Дерево папок и таблица files из Archive
        float treeWidth = 240.0f * _scale;
        ImGui.BeginChild("CatalogFolders", new Vector2(treeWidth, 0), ImGuiChildFlags.Borders);
        {
            if (ImGui.Selectable($"All folders ({_rootFolder.Count})", _catalogFolder.Length == 0))
            {
                _catalogFolder = "";
                FilterCatalog();
            }
            foreach (var child in _rootFolder.Children.Values)
            {
                DrawFolderNode(child);
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("CatalogFiles", new Vector2(0, 0), ImGuiChildFlags.Borders);
        {
            ImGui.SetNextItemWidth(260 * _scale);
            if (ImGui.InputTextWithHint("##CatSearch", "Search archive...", ref _catalogSearch, 128)) FilterCatalog();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150 * _scale);
            if (ImGui.Combo("##CatFilter", ref _catalogFilter, _catalogFilterNames, _catalogFilterNames.Length)) FilterCatalog();
            ImGui.SameLine();
            ImGui.TextDisabled($"{_visibleCatalogEntries.Count:N0} files");

            if (_selectedCatalogEntry != null && (_selectedCatalogEntry.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase) || _selectedCatalogEntry.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.45f, 0.25f, 1.0f));
                if (ImGui.Button($"Open {_selectedCatalogEntry.Id} as scene"))
                {
                    OpenSceneContainer(_selectedCatalogEntry);
                }
                ImGui.PopStyleColor();
            }

            if (ImGui.BeginTable("CatalogTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Filename", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("FAT key", ImGuiTableColumnFlags.WidthFixed, 85 * _scale);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 85 * _scale);
                ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                unsafe
                {
                    var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                    clipper.Begin(_visibleCatalogEntries.Count);
                    while (clipper.Step())
                    {
                        for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            var e = _visibleCatalogEntries[i];
                            ImGui.PushID(e.Index);
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();

                            bool isSelected = _selectedCatalogEntry?.Index == e.Index;
                            if (ImGui.Selectable(e.Name, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                            {
                                SelectCatalogEntry(e);
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && (e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase) || e.Id.StartsWith("FF0", StringComparison.OrdinalIgnoreCase)))
                                {
                                    OpenSceneContainer(e);
                                }
                            }

                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(e.Id);
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(FormatSize(e.StoredSize));
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled(e.Folder);

                            ImGui.PopID();
                        }
                    }
                    clipper.End();
                    clipper.Destroy();
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawFolderNode(FolderNode node)
    {
        ImGui.PushID(node.Path);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (node.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (_catalogFolder == node.Path) flags |= ImGuiTreeNodeFlags.Selected;

        bool open = ImGui.TreeNodeEx($"{node.Name} ({node.Count})", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            _catalogFolder = node.Path;
            FilterCatalog();
        }
        if (open && node.Children.Count > 0)
        {
            foreach (var c in node.Children.Values) DrawFolderNode(c);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private void DrawDiagnosticsTab()
    {
        ImGui.TextColored(Green, "SCENE AND ASSET DIAGNOSTICS");
        ImGui.Separator();

        if (Document != null)
        {
            ImGui.TextUnformatted($"Base BF: {Document.BaseBfPath}");
            ImGui.TextUnformatted($"SHA-256 BF: {Document.BaseBfSha256}");
            ImGui.TextUnformatted($"Container: {Document.ContainerName}");
            ImGui.Spacing();

            string text = $"Base BF: {Document.BaseBfPath}\nSHA-256: {Document.BaseBfSha256}\nContainer: {Document.ContainerName}\n\n" +
                string.Join("\n", Resolver.TextureLoaderStatus.Select(status => $"{status.Key}: {status.Value}")) + "\n" +
                string.Join("\n", Document.Diagnostics);
            if (ImGui.Button("Copy all")) ImGui.SetClipboardText(text);
            ImGui.InputTextMultiline("##DiagLog", ref text, (uint)Math.Max(4096, System.Text.Encoding.UTF8.GetByteCount(text) + 1),
                new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);

        }
        else
        {
            ImGui.TextDisabled("No scene document loaded.");
        }
    }

    #endregion

    #region 6. Statusная строка

    private void DrawStatusBar()
    {
        if (_activeJob != null)
        {
            ImGui.ProgressBar(_jobProgress, new Vector2(200 * _scale, 14 * _scale), "");
            ImGui.SameLine();
            ImGui.TextDisabled(_jobName);
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel")) _jobCancellation?.Cancel();
        }
        else
        {
            ImGui.TextColored(Green, "●");
            ImGui.SameLine();
            ImGui.TextDisabled(_statusMessage);
        }

        if (History.IsDirty)
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - 140 * _scale);
            ImGui.TextColored(Yellow, "[Unsaved changes *]");
        }
    }

    #endregion

    #region Модальные диалоги (Play, About, Выход)

    private void DrawModals()
    {
        // Модальное окно "Play"
        if (_showPlayModal)
        {
            ImGui.OpenPopup("Launch Beyond Good & Evil");
            _showPlayModal = false;
        }

        if (ImGui.BeginPopupModal("Launch Beyond Good & Evil", ImGuiWindowFlags.AlwaysAutoResize))
        {
            bool running = Launcher.IsGameRunning(out int? pid);
            string exe = Launcher.Installation.ExePath;

            ImGui.TextColored(Green, "Launch the original game to test modifications");
            ImGui.TextDisabled($"Executable: {exe}");
            ImGui.Spacing();

            if (running)
            {
                ImGui.TextColored(Yellow, $"BGE.exe is already running (PID {pid})!");
            }

            ImGui.BeginDisabled(running);

            if (ImGui.Button("1. Launch game with current mod"))
            {
                PlayGameWithCurrentMod();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("2. Restore original BF"))
            {
                var restoreRes = Launcher.RestoreOriginalBf();
                if (restoreRes.IsSuccess)
                {
                    _statusMessage = $"Archive restored: {restoreRes.RestoredPath}";
                }
                else
                {
                    SetError(restoreRes.Error ?? "Failed to restore archive.");
                }
            }

            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // Модальное окно "About"
        if (_showAboutModal)
        {
            ImGui.OpenPopup("About JadeView");
            _showAboutModal = false;
        }

        if (ImGui.BeginPopupModal("About JadeView", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(Green, "JadeView — Beyond Good & Evil scene editor");
            ImGui.TextWrapped("See EDITOR_PLAN.md for the editor design.\nLayout: Toolbar, Viewport, Outliner, Details, Content Browser.\nModules: SceneContract, SceneLoader, SceneWriter, ResourceResolver, ModProject, SceneRenderer, GameLauncher.\nShortcuts: W (move), F (focus), G (game view), H (hide), Shift+H (isolate), Alt+H (show all), Ctrl+Z/Ctrl+Y (undo/redo), Ctrl+S (save).");
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(120 * _scale, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    #endregion

    #region Вспомогательные методы каталога и фоновых задач

    private void BuildCatalogFolders()
    {
        _rootFolder = new("All folders", "");
        if (Archive == null) return;

        foreach (var e in Archive.Entries)
        {
            FolderNode current = _rootFolder;
            current.Count++;
            string path = "";
            foreach (string part in e.Folder.Split('/'))
            {
                path = path.Length == 0 ? part : path + "/" + part;
                if (!current.Children.TryGetValue(part, out var child))
                    current.Children[part] = child = new(part, path);
                child.Count++;
                current = child;
            }
        }
    }

    private void FilterCatalog()
    {
        if (Archive == null) { _visibleCatalogEntries = []; return; }
        _visibleCatalogEntries = Archive.Entries.Where(e =>
            (_catalogFolder.Length == 0 || e.Folder == _catalogFolder || e.Folder.StartsWith(_catalogFolder + "/", StringComparison.Ordinal)) &&
            (_catalogSearch.Length == 0 || (e.Name + " " + e.Id + " " + e.Folder).Contains(_catalogSearch, StringComparison.OrdinalIgnoreCase)) &&
            (_catalogFilter switch
            {
                1 => Path.GetExtension(e.Name).ToLowerInvariant() is ".wad" or ".waa" or ".wam" or ".wac",
                2 => e.Name.StartsWith("ff8", StringComparison.OrdinalIgnoreCase),
                3 => e.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase),
                4 => e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase),
                5 => _stagedReplacements.ContainsKey(e.Index),
                _ => true
            })
        ).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void SelectCatalogEntry(Entry entry)
    {
        if (_catalogPreviewTask != null || _activeJob != null) return;
        _selectedCatalogEntry = entry;
        _catalogPreview = null;

        var a = Archive!;
        _stagedReplacements.TryGetValue(entry.Index, out string? staged);

        _catalogPreviewTask = Task.Run(() =>
        {
            byte[] bytes = staged != null ? File.ReadAllBytes(staged) : a.Read(entry);
            var p = Preview.Parse(entry, bytes);
            return (entry.Index, p);
        });
    }

    private unsafe void LoadCatalogAudio()
    {
        _waveform = [];
        if (_catalogPreview?.Audio is not { } audio) return;

        _waveform = new float[250];
        for (int i = 0; i < _waveform.Length; i++)
        {
            int start = (int)((long)i * audio.Samples.Length / _waveform.Length);
            int end = (int)((long)(i + 1) * audio.Samples.Length / _waveform.Length);
            for (int p = start; p < end; p++)
                _waveform[i] = Math.Max(_waveform[i], Math.Abs((int)audio.Samples[p]) / 32768.0f);
        }

        if (Raylib.IsAudioDeviceReady())
        {
            Wave wave = Raylib.LoadWaveFromMemory(".wav", audio.Wav);
            if (wave.Data != null)
            {
                if (_soundLoaded) Raylib.UnloadSound(_sound);
                _sound = Raylib.LoadSoundFromWave(wave);
                Raylib.UnloadWave(wave);
                _soundLoaded = Raylib.IsSoundValid(_sound);
                if (_soundLoaded) Raylib.SetSoundVolume(_sound, _volume);
            }
        }
    }

    private void StartJob(string name, Func<CancellationToken, Action> work)
    {
        if (_activeJob != null) return;
        _jobName = name;
        _statusMessage = name;
        _jobProgress = 0;
        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        _activeJob = Task.Run(() => work(token), token);
    }

    private static string FormatSize(long bytes)
    {
        return bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):F1} MB" :
               bytes >= 1024 ? $"{bytes / 1024.0:F1} KB" :
               $"{bytes} B";
    }

    private static unsafe byte[] Png(Image image)
    {
        int length = 0;
        fixed (byte* ext = ".png\0"u8)
        {
            byte* buffer = Raylib.ExportImageToMemory(image, (sbyte*)ext, &length);
            if (buffer == null || length <= 0) throw new IOException("PNG export failed.");
            try { return new ReadOnlySpan<byte>(buffer, length).ToArray(); }
            finally { Raylib.MemFree(buffer); }
        }
    }

    public void Dispose()
    {
        Renderer.Dispose();
        Launcher.Dispose();
        if (_soundLoaded)
        {
            Raylib.StopSound(_sound);
            Raylib.UnloadSound(_sound);
        }
        if (Directory.Exists(_cacheDir))
        {
            try { Directory.Delete(_cacheDir, true); } catch { }
        }
    }

    #endregion
}
