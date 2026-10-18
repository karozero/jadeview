using System.Numerics;

namespace JadeView;

public readonly record struct SourceRange(int Offset, int Size)
{
    public bool IsValid => Offset >= 0 && Size > 0;
}

