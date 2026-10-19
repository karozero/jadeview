namespace JadeView;

// Exact bytes and clip-relative offset; classification describes storage, not gameplay semantics.
public sealed record AnimationOpaquePayload(string Kind, int SourceOffset, byte[] Data);
