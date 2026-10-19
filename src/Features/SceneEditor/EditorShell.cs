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
/// 3. Справа: Outliner (дерево миров и objects SceneObject, поиск, выбор).
/// 4. Между viewport и Outliner: Asset Inspector (Properties, Animation, Morphs; GaoKey, Transform X/Y/Z с редактированием и Undo/Redo, возможности Capabilities, геометрия и материалы).
/// 5. Снизу: Content Browser (ресурсы мира, вкладки All Assets / Textures / Audio / Text / Diagnostics).
/// 6. Горячие клавиши (W, F, H, Delete, Ctrl+C/V, Ctrl+Z/Y, Ctrl+S) и подтверждение при выходе.
/// </summary>
public sealed partial class EditorShell : IDisposable
{
    private static readonly Vector4 Green = EditorTheme.Green;
    private static readonly Vector4 Yellow = EditorTheme.Yellow;
    private static readonly Vector4 Red = EditorTheme.Red;
    private static readonly Vector4 Muted = EditorTheme.Muted;
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
                var owningWorld = Document?.Worlds.FirstOrDefault(candidateWorld => candidateWorld.Objects.Contains(value));
                if (owningWorld != null)
                {
                    ActiveWorld = owningWorld;
                    Renderer.ActiveWorld = owningWorld;
                }
            }
        }
    }

    private bool _openExport;
    private string _addGeometrySearch = "";
    private SceneObject? _exportTarget;
    private readonly SceneExportOptions _exportOptions = new();
    private bool _showPlayModal;
    private bool _showAboutModal;
    private string _statusMessage = "Ready. Open sally_clean.bf or a project.";
    private string _errorMessage = "";
    private List<ArchiveSceneItem> _archiveScenes = [];
    // Настройки привязки и осей
    public bool SnappingEnabled { get => Renderer.SnappingEnabled; set => Renderer.SnappingEnabled = value; }
    public float SnappingStep { get => Renderer.SnappingStep; set => Renderer.SnappingStep = value; }

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
            if (obj != null)
            {
                _searchOutliner = "";
                _outlinerFilterType = 0;
            }
        };
        Renderer.OnObjectTransformed += (obj, command) =>
        {
            History.Execute(command);
            RecordObjectChangeInProject(obj);
        };
        // Связываем события истории команд с проектом модификации
        History.OnCommandExecuted += OnCommandExecuted;
        History.OnCommandUndone += OnCommandUndone;
        History.OnCommandRedone += OnCommandRedone;
    }

    public string ErrorMessage => _errorMessage;
    public string StatusMessage => _statusMessage;

    public void SetStatus(string message) => _statusMessage = message;
    public void SetError(string error)
    {
        _errorMessage = error;
        _statusMessage = "Error: " + error;
    }

    public void Update()
    {
        // Завершение фоновых задач
        if (_activeJob is { IsCompleted: true })
        {
            var job = _activeJob;
            _activeJob = null;
            try
            {
                job.GetAwaiter().GetResult()();
            }
            catch (OperationCanceledException)
            {
                _statusMessage = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }

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
            catch (Exception ex)
            {
                SetError(ex.Message);
            }

            _catalogPreviewTask = null;
        }

        HandleKeyboardShortcuts();

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
        DrawWorkspace();
        // 6. Statusная строка в самом низу
        DrawStatusBar();
        // Модальные окна
        DrawModals();
        DrawExportDialog();
        ImGui.End();
    }
}
