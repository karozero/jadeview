namespace JadeView;

public sealed record ResolvedProceduralTexture(uint Key, string Scope, int Offset, int Size,
    int Subtype, int Width, int Height, uint? MediaKey, string? MediaName);
