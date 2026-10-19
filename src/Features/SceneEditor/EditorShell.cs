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
                var owningWorld = Document?.Worlds.FirstOrDefault(candidateWorld => candidateWorld.Objects.Contains(value));
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
            _revealContent = obj;
            if (obj != null)
            {
                _searchOutliner = "";
                _outlinerFilterType = 0;
                ContentBrowserTab = 0;
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

        // Обработка глобальных горячих клавиш (когда нет активного ввода в текстовое поле ImGui)
        if (!ImGui.GetIO().WantTextInput && !ImGui.IsAnyItemActive() && !Renderer.IsFlying && !Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.W))
                Renderer.GizmoMode = TransformGizmoMode.Translate;
            if (Raylib.IsKeyPressed(KeyboardKey.F) && SelectedObject != null)
                Renderer.FrameSelected();
            if (Raylib.IsKeyPressed(KeyboardKey.H) && !Renderer.IsDraggingGizmo)
            {
                bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                var command = Renderer.VisibilityChange(alt ? "show" : shift ? "isolate" : "hide");
                if (command != null)
                    History.Execute(command);
            }

            // Undo / Redo
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.Z))
                History.Undo();
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.Y))
                History.Redo();
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.S))
                SaveCurrentProject();
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
