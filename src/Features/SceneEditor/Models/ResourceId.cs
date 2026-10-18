using System.Numerics;

namespace JadeView;

public readonly record struct ResourceId(uint Key, string Scope = "")
{
    public override string ToString() => string.IsNullOrEmpty(Scope) ? $"{Key:X8}" : $"{Scope}::{Key:X8}";
}

