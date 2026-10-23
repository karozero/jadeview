using Raylib_cs;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private sealed class ProceduralVideoMedia(uint mediaKey)
    {
        internal uint MediaKey { get; } = mediaKey;
        internal Task<BinkFrameDecoder>? Loading { get; set; }
        internal Task<CinematicVideoFrame>? Decoding { get; set; }
        internal CinematicVideoFrame? ReadyFrame { get; set; }
        internal BinkFrameDecoder? Decoder { get; set; }
        internal int RequestedFrameIndex { get; set; } = -1;
        internal string? Error { get; set; }
        internal HashSet<uint> TextureKeys { get; } = [];
    }

    private sealed record ProceduralVideoTexture(ResolvedProceduralTexture Descriptor,
        ProceduralVideoMedia? Media, Texture2D Texture)
    {
        internal int FrameIndex { get; set; } = -2;
    }

    private readonly Dictionary<uint, ProceduralVideoMedia> _proceduralVideoMedia = [];
    private readonly Dictionary<uint, ProceduralVideoTexture> _proceduralVideoTextures = [];
    private string? _proceduralVideoCacheDirectory;
    internal Archive? VideoArchive { get; set; }
    internal float? CinematicVideoTime { get; set; }
    internal CinematicVideoPlaybackTimeline? CinematicVideoPlayback { get; set; }

    internal string ProceduralVideoStatus(uint textureKey)
    {
        if (!_proceduralVideoTextures.TryGetValue(textureKey, out var texture))
            return "Video frame is not requested until its material is rendered.";
        if (texture.Media == null) return "Procedural texture has no decoded Bink media key.";
        if (texture.Media.Error is { } error) return error;
        string? timelineStatus = CinematicVideoPlayback?.Describe(textureKey,
            CinematicVideoTime ?? (float)Raylib.GetTime());
        string decoderStatus = texture.Media.Decoder is { } decoder
            ? texture.Media.Decoding is { IsCompleted: false }
                ? $"Decoding Bink frame {texture.Media.RequestedFrameIndex + 1}/{decoder.FrameCount} in background"
                : texture.FrameIndex >= 0
                    ? $"Bink frame {texture.FrameIndex + 1}/{decoder.FrameCount}"
                    : CinematicVideoPlayback?.StopsAtMediaEnd(textureKey) == true
                        ? "Video cleared by StopAtEnd after one media pass"
                        : "Bink frame held at black"
            : "Loading retail Bink decoder...";
        return timelineStatus == null ? decoderStatus : $"{timelineStatus} · {decoderStatus}";
    }

    internal bool HasContinuingCinematicVideo(float timelineTime)
    {
        if (CinematicVideoPlayback == null) return false;
        foreach (uint textureKey in CinematicVideoPlayback.TextureKeys)
        {
            CinematicVideoPlaybackSample? sample = CinematicVideoPlayback.Evaluate(textureKey, timelineTime);
            bool stopAtMediaEnd = CinematicVideoPlayback.StopsAtMediaEnd(textureKey);
            bool mediaLoadFailed = !TryGetVideoMedia(textureKey, out var media);
            bool mediaMetadataAvailable = media?.Decoder is { };
            bool mediaReachedEnd = false;
            if (media?.Decoder is { } decoder && sample is { } activeSample)
                mediaReachedEnd = BinkFrameDecoder.HasReachedEnd(activeSample.VideoTime,
                    decoder.FrameCount, decoder.FrameRate, decoder.FrameRateDivisor);

            if (CinematicVideoPostRoll.ShouldContinue(sample, stopAtMediaEnd,
                mediaLoadFailed, mediaMetadataAvailable, mediaReachedEnd))
                return true;
        }
        return false;
    }

    private bool TryGetVideoMedia(uint textureKey, out ProceduralVideoMedia? media)
    {
        media = null;
        if (Resolver is not { } resolver
            || !resolver.ProceduralTextures.TryGetValue(textureKey, out var descriptor)
            || descriptor.MediaKey is not uint mediaKey
            || mediaKey is 0 or uint.MaxValue)
            return false;

        _proceduralVideoMedia.TryGetValue(mediaKey, out media);
        return media?.Error == null;
    }

    private Texture2D GetOrCreateProceduralVideoTexture(ResolvedProceduralTexture descriptor)
    {
        if (_proceduralVideoTextures.TryGetValue(descriptor.Key, out var cached)) return cached.Texture;

        var texture = LoadProceduralVideoTexture(descriptor.Width, descriptor.Height);
        ProceduralVideoMedia? media = null;
        if (descriptor.MediaKey is uint mediaKey)
        {
            if (!_proceduralVideoMedia.TryGetValue(mediaKey, out media))
                _proceduralVideoMedia[mediaKey] = media = new(mediaKey);
            media.TextureKeys.Add(descriptor.Key);
            if (media.Loading == null && media.Decoder == null && media.Error == null)
                StartProceduralVideoLoad(media);
        }

        _proceduralVideoTextures.Add(descriptor.Key, new(descriptor, media, texture));
        return texture;
    }

    private unsafe Texture2D LoadProceduralVideoTexture(int width, int height)
    {
        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);
        var pixels = new byte[checked(width * height * 4)];
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        fixed (byte* pixelData = pixels)
        {
            Raylib_cs.Image image = new()
            {
                Data = pixelData,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
            };
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            return texture;
        }
    }

    private void StartProceduralVideoLoad(ProceduralVideoMedia media)
    {
        if (VideoArchive == null)
        {
            media.Error = "Open a BF archive to resolve the source video and retail Bink decoder.";
            return;
        }
        if (media.MediaKey == 0 || media.MediaKey == uint.MaxValue)
        {
            media.Error = "The procedural video descriptor has no valid media key.";
            return;
        }

        Archive archive = VideoArchive;
        Entry? videoEntry = archive.Entries.FirstOrDefault(entry => entry.Key == media.MediaKey);
        if (videoEntry == null)
        {
            media.Error = $"Archive resource {media.MediaKey:X8} is missing.";
            return;
        }

        string gameDirectory = Path.GetDirectoryName(archive.PathName)!;
        string binkDllPath = Path.Combine(gameDirectory, "binkw32.dll");
        if (!File.Exists(binkDllPath))
        {
            media.Error = "Retail x86 binkw32.dll was not found beside the opened BF archive.";
            return;
        }

        string cacheDirectory = _proceduralVideoCacheDirectory ??= Path.Combine(
            Path.GetTempPath(), "JadeView-Bink", Guid.NewGuid().ToString("N"));
        string videoPath = Path.Combine(cacheDirectory, $"{media.MediaKey:X8}.bik");
        media.Loading = Task.Run(() =>
        {
            Directory.CreateDirectory(cacheDirectory);
            if (!File.Exists(videoPath)) File.WriteAllBytes(videoPath, archive.Read(videoEntry));
            return BinkFrameDecoder.Start(binkDllPath, videoPath, cacheDirectory);
        });
    }

    private unsafe void UpdateProceduralVideoTextures(double timeSeconds)
    {
        foreach (var media in _proceduralVideoMedia.Values)
        {
            if (media.Decoder == null && media.Loading is { IsCompleted: true } loading)
            {
                try { media.Decoder = loading.GetAwaiter().GetResult(); }
                catch (Exception error) { media.Error = error.GetBaseException().Message; }
                media.Loading = null;
            }
            if (media.Decoder == null) continue;

            if (media.Decoding is { IsCompleted: true } completedDecode)
            {
                media.Decoding = null;
                media.RequestedFrameIndex = -1;
                try { media.ReadyFrame = completedDecode.GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    media.Error = error.GetBaseException().Message;
                    BinkFrameDecoder failedDecoder = media.Decoder;
                    media.Decoder = null;
                    _ = Task.Run(failedDecoder.Dispose);
                    continue;
                }
            }

            foreach (uint textureKey in media.TextureKeys)
            {
                if (!_proceduralVideoTextures.TryGetValue(textureKey, out var videoTexture)) continue;
                CinematicVideoPlaybackSample? sample = CinematicVideoPlayback?.Evaluate(textureKey, (float)timeSeconds);
                bool stopAtMediaEnd = sample is { Controlled: true, HasStarted: true }
                    && CinematicVideoPlayback?.StopsAtMediaEnd(textureKey) == true
                    && BinkFrameDecoder.HasReachedEnd(sample.Value.VideoTime, media.Decoder.FrameCount,
                        media.Decoder.FrameRate, media.Decoder.FrameRateDivisor);
                bool holdBlack = sample is { Controlled: true } controlledSample
                    && (controlledSample.Ambiguous || !controlledSample.HasStarted || stopAtMediaEnd);
                int frameIndex = holdBlack ? -1 : BinkFrameDecoder.FrameIndexAt(
                    sample is { Controlled: true } controlledVideo ? controlledVideo.VideoTime : timeSeconds,
                    media.Decoder.FrameCount, media.Decoder.FrameRate, media.Decoder.FrameRateDivisor);
                if (videoTexture.FrameIndex == frameIndex) continue;

                if (frameIndex < 0)
                {
                    byte[] blackFrame = CreateBlackFrame(videoTexture.Descriptor.Width, videoTexture.Descriptor.Height);
                    fixed (byte* pixelData = blackFrame) Raylib.UpdateTexture(videoTexture.Texture, pixelData);
                    videoTexture.FrameIndex = -1;
                    continue;
                }

                if (media.ReadyFrame is { } readyFrame && readyFrame.Index == frameIndex)
                {
                    fixed (byte* pixelData = readyFrame.Rgba)
                        Raylib.UpdateTexture(videoTexture.Texture, pixelData);
                    videoTexture.FrameIndex = readyFrame.Index;
                    continue;
                }

                if (media.Decoding == null)
                {
                    BinkFrameDecoder decoder = media.Decoder;
                    media.RequestedFrameIndex = frameIndex;
                    media.Decoding = CinematicVideoFrameLoader.DecodeAsync(frameIndex, decoder.DecodeRgba);
                }
            }
        }
    }

    private static byte[] CreateBlackFrame(int width, int height)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (int alphaOffset = 3; alphaOffset < rgba.Length; alphaOffset += 4)
            rgba[alphaOffset] = 255;
        return rgba;
    }

    private void ClearProceduralVideoTextures()
    {
        foreach (var videoTexture in _proceduralVideoTextures.Values)
            if (videoTexture.Texture.Id != 0) Raylib.UnloadTexture(videoTexture.Texture);
        _proceduralVideoTextures.Clear();

        foreach (var media in _proceduralVideoMedia.Values)
        {
            if (media.Decoder != null)
            {
                BinkFrameDecoder decoder = media.Decoder;
                if (media.Decoding is { } decoding)
                    _ = decoding.ContinueWith(task =>
                    {
                        _ = task.Exception;
                        decoder.Dispose();
                    }, TaskScheduler.Default);
                else
                    _ = Task.Run(decoder.Dispose);
                continue;
            }
            if (media.Loading is { } loading)
                _ = loading.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully) task.Result.Dispose();
                }, TaskScheduler.Default);
        }
        _proceduralVideoMedia.Clear();
    }
}
