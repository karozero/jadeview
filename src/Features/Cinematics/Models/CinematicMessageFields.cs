namespace JadeView;

internal static class CinematicMessageFields
{
    internal const int Gao = 0;
    internal const int Gao2 = 4;
    internal const int Vector1 = 8;
    internal const int Vector2 = 20;
    // Native tMessage stores f2 BEFORE f1 (gameobject.h), despite their names.
    internal const int Float2 = 32;
    internal const int Float1 = 36;
    internal const int Int1 = 40;
    internal const int Int2 = 44;
    internal const int Type = 48;
}
