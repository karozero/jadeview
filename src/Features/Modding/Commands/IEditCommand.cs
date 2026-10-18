using System.Numerics;

namespace JadeView;

public interface IEditCommand
{
    bool AffectsDocument => true;
    string Description { get; }
    void Execute();
    void Undo();
}

