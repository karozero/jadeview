using System.Numerics;

namespace JadeView;

[Flags]
public enum EditCapabilities
{
    None = 0,
    Translate = 1 << 0,
    Rotate = 1 << 1,
    Scale = 1 << 2,
    AllTransform = Translate | Rotate | Scale
}

