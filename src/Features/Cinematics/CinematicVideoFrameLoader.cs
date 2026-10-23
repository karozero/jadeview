namespace JadeView;

internal sealed record CinematicVideoFrame(int Index, byte[] Rgba);

internal static class CinematicVideoFrameLoader
{
    internal static Task<CinematicVideoFrame> DecodeAsync(int frameIndex, Func<int, byte[]> decode) =>
        Task.Run(() => new CinematicVideoFrame(frameIndex, decode(frameIndex)));
}
