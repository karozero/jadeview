using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Составная транзакционная команда для пакетного изменения нескольких objects за одно действие.
/// </summary>
public sealed class CompositeEditCommand : IEditCommand
{
    private readonly List<IEditCommand> _commands = [];
    public string Description { get; }

    public CompositeEditCommand(string description, IEnumerable<IEditCommand> commands)
    {
        Description = description;
        _commands.AddRange(commands);
    }

    public void Execute()
    {
        foreach (var cmd in _commands)
        {
            cmd.Execute();
        }
    }

    public void Undo()
    {
        // Cancel выполняется в обратном порядке
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
