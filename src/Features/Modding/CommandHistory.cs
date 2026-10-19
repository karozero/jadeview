using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Менеджер стека команд Undo / Redo с отслеживанием признака изменений (IsDirty)
/// и событиями уведомления пользовательского интерфейса (Агент A6 -> A5).
/// </summary>
public sealed class CommandHistory
{
    private readonly List<IEditCommand> _history = [];
    private int _currentIndex = 0; // Индекс следующей команды для выполнения (или сколько команд сейчас выполнено)
    private int _savedIndex = 0;   // Индекс, на котором проект был сохранен (чистое состояние)
    private readonly int _maxHistory;

    public int MaxHistory => _maxHistory;
    public int UndoCount => _currentIndex;
    public int RedoCount => _history.Count - _currentIndex;
    public bool CanUndo => _currentIndex > 0;
    public bool CanRedo => _currentIndex < _history.Count;

    public string? CurrentUndoDescription => CanUndo ? _history[_currentIndex - 1].Description : null;
    public string? CurrentRedoDescription => CanRedo ? _history[_currentIndex].Description : null;
    public string? LastUndoDescription => CurrentUndoDescription;
    public string? LastRedoDescription => CurrentRedoDescription;
    public IEditCommand? LastExecutedCommand => CanUndo ? _history[_currentIndex - 1] : null;

    /// <summary>
    /// Признак наличия несохраненных изменений в документе/проекте.
    /// Возвращает false, если текущее состояние стека совпадает с состоянием последнего сохранения.
    /// </summary>
    public bool IsDirty
    {
        get => _savedIndex < 0 || !_history.Take(_currentIndex).Where(c => c.AffectsDocument)
            .SequenceEqual(_history.Take(_savedIndex).Where(c => c.AffectsDocument));
        set
        {
            if (!value)
            {
                _savedIndex = _currentIndex;
            }
            else if (_savedIndex == _currentIndex)
            {
                _savedIndex = -1;
            }
        }
    }

    public void Reset() => Clear();
    // Authoring edits outside the command stack must remain dirty even after transform undo.
    public void MarkExternalChange()
    {
        bool wasDirty = IsDirty;
        _savedIndex = -1;
        if (!wasDirty) OnDirtyChanged?.Invoke(true);
    }

    public event Action? OnHistoryChanged;
    public event Action<bool>? OnDirtyChanged;
    public event Action<IEditCommand>? OnCommandExecuted;
    public event Action<IEditCommand>? OnCommandUndone;
    public event Action<IEditCommand>? OnCommandRedone;

    public CommandHistory(int maxHistory = 200)
    {
        _maxHistory = Math.Max(10, maxHistory);
    }

    /// <summary>
    /// Выполняет новую команду и добавляет её в стек истории, очищая ветку Redo.
    /// </summary>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        bool wasDirty = IsDirty;

        command.Execute();

        // Если находились в середине истории, отсекаем ветку Redo
        if (_currentIndex < _history.Count)
        {
            if (_savedIndex > _currentIndex)
                _savedIndex = _history.Skip(_currentIndex).Take(_savedIndex - _currentIndex).Any(c => c.AffectsDocument)
                    ? -1 : _currentIndex;
            _history.RemoveRange(_currentIndex, _history.Count - _currentIndex);
        }

        _history.Add(command);
        _currentIndex++;

        // Restrictedие глубины стека
        if (_history.Count > _maxHistory)
        {
            int removeCount = _history.Count - _maxHistory;
            _history.RemoveRange(0, removeCount);
            _currentIndex -= removeCount;
            if (_savedIndex >= 0)
            {
                _savedIndex -= removeCount;
                if (_savedIndex < 0) _savedIndex = -1;
            }
        }

        OnCommandExecuted?.Invoke(command);
        OnHistoryChanged?.Invoke();

        if (IsDirty != wasDirty)
        {
            OnDirtyChanged?.Invoke(IsDirty);
        }
    }

    /// <summary>
    /// Отменяет последнюю выполненную команду (Undo).
    /// </summary>
    public bool Undo()
    {
        if (!CanUndo) return false;

        bool wasDirty = IsDirty;
        _currentIndex--;
        var command = _history[_currentIndex];

        command.Undo();

        OnCommandUndone?.Invoke(command);
        OnHistoryChanged?.Invoke();

        if (IsDirty != wasDirty)
        {
            OnDirtyChanged?.Invoke(IsDirty);
        }

        return true;
    }

    /// <summary>
    /// Повторяет отмененную команду (Redo).
    /// </summary>
    public bool Redo()
    {
        if (!CanRedo) return false;

        bool wasDirty = IsDirty;
        var command = _history[_currentIndex];
        _currentIndex++;

        command.Execute();

        OnCommandRedone?.Invoke(command);
        OnHistoryChanged?.Invoke();

        if (IsDirty != wasDirty)
        {
            OnDirtyChanged?.Invoke(IsDirty);
        }

        return true;
    }

    /// <summary>
    /// Помечает текущую позицию в истории как сохраненное состояние (IsDirty = false).
    /// Вызывается после сохранения проекта мода.
    /// </summary>
    public void MarkSaved()
    {
        bool wasDirty = IsDirty;
        _savedIndex = _currentIndex;

        if (wasDirty)
        {
            OnDirtyChanged?.Invoke(false);
            OnHistoryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Очищает всю историю команд.
    /// </summary>
    public void Clear()
    {
        bool wasDirty = IsDirty;
        _history.Clear();
        _currentIndex = 0;
        _savedIndex = 0;

        OnHistoryChanged?.Invoke();
        if (wasDirty)
        {
            OnDirtyChanged?.Invoke(false);
        }
    }

    public IReadOnlyList<IEditCommand> GetUndoCommands() =>
        _history.Take(_currentIndex).ToList().AsReadOnly();

    public IReadOnlyList<IEditCommand> GetRedoCommands() =>
        _history.Skip(_currentIndex).ToList().AsReadOnly();
}

