using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;
using Forms = System.Windows.Forms;

namespace JadeView;

/// <summary>
/// Главный класс приложения и жизненного цикла окна редактора JadeView.
/// Интегрирует Raylib-cs, rlImGui-cs и UE5 EditorShell.
/// Обеспечивает DPI-масштабирование, тёмную палитру с зеленым акцентом,
/// проверку несохраненных изменений при выходе и автоматизированное smoke-тестирование.
/// </summary>
sealed class App : IDisposable
{
    public const string DefaultArchive = @"C:\GAME\Beyond Good & Evil\sally_clean.bf";

    private static readonly Vector4 Green = new(0.35f, 0.90f, 0.54f, 1.0f);
    private static readonly Vector4 Muted = new(0.56f, 0.62f, 0.60f, 1.0f);

    private readonly EditorShell _shell;
    private readonly float _scale;
    private readonly string? _smokeDir;
    private readonly bool _audioReady;
    private readonly string? _smokeScene;
    private bool _smokeSceneRequested;
    private int _smokeVisibleBeforeMove, _smokeVisibleAfterMove;

    private int _smokeStage;
    private double _smokeNext;

    private const int WM_CLOSE = 0x0010;
    private const int GWLP_WNDPROC = -4;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrA")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly IntPtr _hwnd;
    private readonly IntPtr _prevWndProc;
    private readonly WndProcDelegate _wndProcDelegate;

    public unsafe App(string? path, string? smoke, string? smokeScene = null)
    {
        _smokeDir = smoke;
        _smokeScene = smokeScene;
        if (smoke != null) Directory.CreateDirectory(smoke);

        // Инициализация графического окна Raylib
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(1560, 920, "JadeView — Beyond Good & Evil (UE5 Scene Editor)");
        Raylib.SetWindowMinSize(1100, 720);
        Raylib.SetExitKey(KeyboardKey.Null); // Запрещаем закрытие по Escape, чтобы не терять работу
        Raylib.SetTargetFPS(60);

        // Перехватываем WM_CLOSE для предотвращения случайного закрытия при отмене диалога
        _hwnd = (IntPtr)Raylib.GetWindowHandle();
        _wndProcDelegate = HookWndProc;
        _prevWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        // Инициализация аудиоустройства
        Raylib.InitAudioDevice();
        _audioReady = Raylib.IsAudioDeviceReady();

        _scale = Math.Clamp(Raylib.GetWindowScaleDPI().X, 1.0f, 2.0f);

        // Настройка шрифтов ImGui с поддержкой кириллицы
        rlImGui.LoadFontAwesome = false;
        rlImGui.LoadDefaultFont = false;
        rlImGui.SetupUserFonts = io =>
        {
            string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
            if (File.Exists(fontPath))
            {
                io.Fonts.AddFontFromFileTTF(fontPath, 17.0f * _scale, null, io.Fonts.GetGlyphRangesCyrillic());
            }
        };

        rlImGui.Setup(true, false);
        ImGui.GetIO().NativePtr->IniFilename = null;

        ApplyEditorStyle();

        _shell = new EditorShell(_scale);

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            _shell.LoadBfArchive(path);
        }
    }

    private IntPtr HookWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLOSE && _smokeDir == null && _shell.History.IsDirty)
        {
            var result = Forms.MessageBox.Show(
                "The project has unsaved changes.\nSave before exiting?",
                "JadeView — Confirm exit",
                Forms.MessageBoxButtons.YesNoCancel,
                Forms.MessageBoxIcon.Question);

            if (result == Forms.DialogResult.Cancel)
            {
                // Пользователь отменил закрытие
                return IntPtr.Zero;
            }
            if (result == Forms.DialogResult.Yes)
            {
                bool saved = _shell.SaveCurrentProject();
                if (!saved)
                {
                    // Cancel в диалоге сохранения файла — не выходим
                    return IntPtr.Zero;
                }
            }
        }

        return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    private void ApplyEditorStyle()
    {
        var s = ImGui.GetStyle();
        s.WindowPadding = new Vector2(14, 10);
        s.FramePadding = new Vector2(9, 6);
        s.ItemSpacing = new Vector2(8, 7);
        s.WindowRounding = 0;
        s.ChildRounding = 6;
        s.FrameRounding = 4;
        s.GrabRounding = 4;
        s.ScrollbarRounding = 6;
        s.WindowBorderSize = 0;
        s.ChildBorderSize = 1;
        s.FrameBorderSize = 0;

        // Фирменная тёмно-зеленая цветовая схема JadeView
        s.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.055f, 0.070f, 0.065f, 1.0f);
        s.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.075f, 0.090f, 0.085f, 1.0f);
        s.Colors[(int)ImGuiCol.Text] = new Vector4(0.88f, 0.92f, 0.90f, 1.0f);
        s.Colors[(int)ImGuiCol.TextDisabled] = Muted;
        s.Colors[(int)ImGuiCol.Border] = new Vector4(0.15f, 0.20f, 0.17f, 1.0f);
        s.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.12f, 0.15f, 0.13f, 1.0f);
        s.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.17f, 0.23f, 0.19f, 1.0f);
        s.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.19f, 0.30f, 0.23f, 1.0f);
        s.Colors[(int)ImGuiCol.Button] = new Vector4(0.12f, 0.21f, 0.16f, 1.0f);
        s.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.18f, 0.35f, 0.24f, 1.0f);
        s.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.21f, 0.43f, 0.28f, 1.0f);
        s.Colors[(int)ImGuiCol.Header] = new Vector4(0.13f, 0.29f, 0.19f, 1.0f);
        s.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.17f, 0.32f, 0.22f, 1.0f);
        s.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.19f, 0.40f, 0.25f, 1.0f);
        s.Colors[(int)ImGuiCol.CheckMark] = Green;
        s.Colors[(int)ImGuiCol.SliderGrab] = Green;
        s.Colors[(int)ImGuiCol.SliderGrabActive] = Green;
        s.Colors[(int)ImGuiCol.Tab] = new Vector4(0.10f, 0.15f, 0.12f, 1.0f);
        s.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.16f, 0.30f, 0.20f, 1.0f);
        s.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.19f, 0.36f, 0.24f, 1.0f);
        s.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.10f, 0.14f, 0.115f, 1.0f);
        s.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(0.12f, 0.17f, 0.135f, 0.25f);
        s.Colors[(int)ImGuiCol.PlotHistogram] = Green;

        if (_scale != 1.0f)
        {
            s.ScaleAllSizes(_scale);
        }
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            // Обновление заголовка окна с индикацией dirty-статуса "*"
            UpdateWindowTitle();

            // Отрисовка кадра
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(14, 18, 16, 255));

            rlImGui.Begin();
            _shell.Draw();
            rlImGui.End();

            Raylib.EndDrawing();

            // Автоматизированное smoke-тестирование
            if (_smokeDir != null && RunSmokeTest())
            {
                break;
            }
        }
    }

    private void UpdateWindowTitle()
    {
        string dirty = _shell.History.IsDirty ? " *" : "";
        string proj = _shell.CurrentProject?.Name ?? "Project";
        string container = _shell.Document?.ContainerName ?? "";
        string title = string.IsNullOrEmpty(container)
            ? $"JadeView — Beyond Good & Evil (UE5 Editor) [{proj}{dirty}]"
            : $"JadeView — Beyond Good & Evil [{container} · {proj}{dirty}]";

        Raylib.SetWindowTitle(title);
    }

    private bool RunSmokeTest()
    {
        if (_smokeStage == 0)
        {
            // Ждем завершения загрузки сцены в фоновой задаче
            if (_shell.Document == null || _shell.ActiveWorld == null || _shell.ActiveWorld.Objects.Count == 0 || _shell.Archive == null)
                return false;

            if (_smokeScene != null && !Path.GetFileNameWithoutExtension(_shell.Document.ContainerName).Equals(_smokeScene, StringComparison.OrdinalIgnoreCase))
            {
                if (!_smokeSceneRequested)
                {
                    var entry = _shell.Archive.Entries.First(e => e.Id.Equals(_smokeScene, StringComparison.OrdinalIgnoreCase));
                    _shell.OpenSceneContainer(entry);
                    _smokeSceneRequested = true;
                }
                return false;
            }

            // Даем 0.5с на первую отрисовку сцены вьюпортом Raylib
            if (_smokeNext == 0)
            {
                _smokeNext = Raylib.GetTime() + 0.5;
                return false;
            }
            if (Raylib.GetTime() < _smokeNext) return false;

            // Кадр 0: Вьюпорт и загруженная сцена
            Screenshot("jadeview-viewport.png");
            _smokeVisibleBeforeMove = _shell.Renderer.StatVisibleObjects;
            var forward = Vector3.Normalize(_shell.Renderer.Camera.Target - _shell.Renderer.Camera.Position);
            _shell.Renderer.Camera.Position += forward * (_shell.Renderer.MoveSpeed * .35f);
            _shell.Renderer.Camera.Target = _shell.Renderer.Camera.Position + forward;
            _shell.ContentBrowserTab = 1; // переключаемся на вкладку текстур
            _smokeNext = Raylib.GetTime() + 0.5; // даем ImGui отрисовать кадр с вкладкой текстур
            _smokeStage = 1;
            return false;
        }
        else if (_smokeStage == 1)
        {
            if (Raylib.GetTime() < _smokeNext) return false;

            // Кадр 1: Вкладка текстур в Content Browser
            _smokeVisibleAfterMove = _shell.Renderer.StatVisibleObjects;
            Screenshot("jadeview-after-navigation.png");
            Screenshot("jadeview-textures.png");
            _shell.ContentBrowserTab = 4; // переключаемся на вкладку каталога BF
            _smokeNext = Raylib.GetTime() + 0.5; // даем ImGui отрисовать кадр с каталогом
            _smokeStage = 2;
            return false;
        }
        else if (_smokeStage == 2)
        {
            if (Raylib.GetTime() < _smokeNext) return false;

            // Кадр 2: Вкладка каталога BF
            Screenshot("jadeview-catalog.png");

            try
            {
                // 1. Проверка отсутствия errors интерфейса и загрузки
                if (_smokeVisibleBeforeMove > 0 && _smokeVisibleAfterMove == 0)
                    throw new InvalidOperationException("Сцена исчезла после первого движения камеры.");
                if (!string.IsNullOrEmpty(_shell.ErrorMessage))
                    throw new InvalidOperationException($"В EditorShell зафиксирована ошибка: {_shell.ErrorMessage}");

                if (_shell.Document == null || _shell.Document.Worlds.Count == 0)
                    throw new InvalidOperationException("SceneDocument пуст или миры не загружены.");

                if (_shell.ActiveWorld == null || _shell.ActiveWorld.Objects.Count == 0)
                    throw new InvalidOperationException("ActiveWorld не содержит objects.");

                if (_shell.Resolver.Geometries.Count == 0)
                    throw new InvalidOperationException("ResourceResolver не содержит геометрий.");

                if (_shell.Resolver.Textures.Count == 0)
                    throw new InvalidOperationException("ResourceResolver не содержит текстур.");

                // 2. Верификация редактирования объекта, создания патча и Undo/Redo
                var target = _shell.ActiveWorld.Objects.FirstOrDefault(o => o.Capabilities.CanTranslate)
                    ?? _shell.ActiveWorld.Objects.First();

                var origTranslation = target.Transform.Translation;
                var origRotation = target.Transform.Rotation;
                var origScale = target.Transform.Scale;
                var newTranslation = origTranslation + new Vector3(10.0f, 0, 0);

                // Применяем команду через стек истории
                var cmd = new TransformCommand(target, newTranslation, origRotation, origScale);
                _shell.History.Execute(cmd);

                if (target.Transform.Translation != newTranslation)
                    throw new InvalidOperationException($"TransformCommand не применил координаты: ожидалось {newTranslation}, получено {target.Transform.Translation}");

                if (!_shell.History.CanUndo || !_shell.History.IsDirty)
                    throw new InvalidOperationException("History не зафиксировал CanUndo/IsDirty после редактирования.");

                // Проверяем проект мода и генерацию патча
                if (_shell.CurrentProject == null)
                    throw new InvalidOperationException("CurrentProject missing в EditorShell.");

                var oldTr = new TransformData { Translation = origTranslation, Rotation = origRotation, Scale = origScale };
                var newTr = new TransformData { Translation = newTranslation, Rotation = origRotation, Scale = origScale };
                _shell.CurrentProject.RecordChange(target, _shell.Document.ContainerName, _shell.ActiveWorld.Key, oldTr, newTr, false);

                if (!_shell.CurrentProject.HasChanges)
                    throw new InvalidOperationException("CurrentProject.HasChanges == false после RecordChange.");

                var sceneEntry = _shell.Archive!.Entries.FirstOrDefault(e => e.Id.Equals(_shell.Document.ContainerName, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(_shell.Document.ContainerName, StringComparison.OrdinalIgnoreCase))
                    ?? _shell.Archive.Entries.First(e => e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase));
                byte[] containerBytes = _shell.Archive.Read(sceneEntry);
                var plan = _shell.CurrentProject.BuildWritePlan(_shell.Document, containerBytes);

                if (plan.Patches.Count == 0)
                    throw new InvalidOperationException("BuildWritePlan вернул 0 patches для изменённого объекта.");

                // Проверяем применение патча и чтение измененных координат
                byte[] patchedBytes = SceneWriter.CreatePatchedContainer(containerBytes, plan);
                var reloadedDoc = SceneLoader.Load(patchedBytes, sceneEntry.Name);
                var reloadedObj = reloadedDoc.FindObjectById(target.Id);

                if (reloadedObj == null || Vector3.Distance(reloadedObj.Transform.Translation, newTranslation) > 1e-4f)
                    throw new InvalidOperationException("Запатченный контейнер не сохранил новые координаты при повторном чтении.");

                // Проверяем обратимость Undo
                _shell.History.Undo();
                if (target.Transform.Translation != origTranslation)
                    throw new InvalidOperationException($"Undo не восстановил координаты объекта: ожидалось {origTranslation}, получено {target.Transform.Translation}");

                // Проверяем обратимость Redo
                _shell.History.Redo();
                if (target.Transform.Translation != newTranslation)
                    throw new InvalidOperationException($"Redo не восстановил изменённые координаты: ожидалось {newTranslation}, получено {target.Transform.Translation}");

                // Возвращаем Undo в исходное состояние
                _shell.History.Undo();

                // 3. Формирование подтверждённого отчёта о smoke-тесте
                if (_shell.Renderer.StatVisibleObjects == 0)
                    throw new InvalidOperationException("Scene loaded but viewport rendered zero objects.");
                string report = $"JadeView editor smoke test passed.\n" +
                                $"Scope: UI rendering and in-memory edit/patch/undo; game not tested.\n" +
                                $"Worlds: {_shell.Document.Worlds.Count}\n" +
                                $"Active Objects: {_shell.ActiveWorld.Objects.Count}\n" +
                                $"Rendered Objects: {_shell.Renderer.StatVisibleObjects}\n" +
                                $"First camera step: {_smokeVisibleBeforeMove} -> {_smokeVisibleAfterMove} objects\n" +
                                $"Resolved Geometries: {_shell.Resolver.Geometries.Count}\n" +
                                $"Resolved Textures: {_shell.Resolver.Textures.Count}\n" +
                                $"Selected Object: {_shell.SelectedObject?.Name ?? "none"}\n" +
                                $"Audio ready: {_audioReady}\n" +
                                $"Errors: None\n" +
                                $"Patch Creation: Verified ({plan.Patches.Count} patches generated)\n" +
                                $"Mod Project Apply: Verified (Roundtrip reload coordinates match: {newTranslation})\n" +
                                $"Undo/Redo Reversibility: Verified (Original: {origTranslation} -> Modified: {newTranslation} -> Undo: {origTranslation})\n";

                File.WriteAllText(Path.Combine(_smokeDir!, "ui-smoke.txt"), report);
            }
            catch (Exception ex)
            {
                string failReport = $"JadeView UE5 Shell Smoke Test FAILED.\n" +
                                    $"Error: {ex.Message}\n" +
                                    $"{ex.StackTrace}\n";
                File.WriteAllText(Path.Combine(_smokeDir!, "ui-smoke.txt"), failReport);
            }

            _smokeStage++;
            return true;
        }

        return false;
    }

    private void Screenshot(string fileName)
    {
        Image image = Raylib.LoadImageFromScreen();
        try
        {
            if (image.Width > Raylib.GetScreenWidth() || image.Height > Raylib.GetScreenHeight())
            {
                Raylib.ImageCrop(ref image, new Raylib_cs.Rectangle(
                    0,
                    Math.Max(0, image.Height - Raylib.GetScreenHeight()),
                    Raylib.GetScreenWidth(),
                    Raylib.GetScreenHeight()));
            }

            int length = 0;
            unsafe
            {
                fixed (byte* ext = ".png\0"u8)
                {
                    byte* buffer = Raylib.ExportImageToMemory(image, (sbyte*)ext, &length);
                    if (buffer != null && length > 0)
                    {
                        var bytes = new ReadOnlySpan<byte>(buffer, length).ToArray();
                        File.WriteAllBytes(Path.Combine(_smokeDir!, fileName), bytes);
                        Raylib.MemFree(buffer);
                    }
                }
            }
        }
        finally
        {
            Raylib.UnloadImage(image);
        }
    }

    public void Dispose()
    {
        if (_prevWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _prevWndProc);
        }
        _shell.Dispose();
        rlImGui.Shutdown();
        if (_audioReady)
        {
            Raylib.CloseAudioDevice();
        }
        Raylib.CloseWindow();
    }
}
