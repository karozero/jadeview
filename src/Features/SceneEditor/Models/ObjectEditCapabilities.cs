using System.Numerics;

namespace JadeView;

public sealed class ObjectEditCapabilities
{
    public EditCapabilities Flags { get; set; } = EditCapabilities.AllTransform;
    public string RestrictionReason { get; set; } = "";
    public bool CanTranslate => (Flags & EditCapabilities.Translate) != 0;
    public bool CanRotate => (Flags & EditCapabilities.Rotate) != 0;
    public bool CanScale => (Flags & EditCapabilities.Scale) != 0;
}

