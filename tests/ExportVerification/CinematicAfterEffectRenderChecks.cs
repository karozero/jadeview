using System.Numerics;
using System.Reflection;
using JadeView;
using Raylib_cs;
using Color = Raylib_cs.Color;

internal static class CinematicAfterEffectRenderChecks
{
    internal static void Verify()
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(64, 64, "Cinematic after-effect verification");
        try
        {
            using var renderer = new SceneRenderer { ShowGrid = false, GameView = true };
            renderer.CinematicColorBalance = .45f;
            renderer.Render(64, 64, new(), null, null, null);
            Vector3 sourcePixel = ReadCenterPixel(renderer.Target.Texture);
            var target = (RenderTexture2D)(typeof(SceneRenderer)
                .GetMethod("RenderCinematicAfterEffects", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, null)!);
            AssertPixel(target.Texture, CinematicAfterEffectTimeline.ApplyColorBalance(sourcePixel, .45f),
                "Sub-neutral color-balance shader");

            renderer.CinematicColorBalance = .75f;
            renderer.Render(64, 64, new(), null, null, null);
            sourcePixel = ReadCenterPixel(renderer.Target.Texture);
            target = (RenderTexture2D)(typeof(SceneRenderer)
                .GetMethod("RenderCinematicAfterEffects", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, null)!);
            AssertPixel(target.Texture, CinematicAfterEffectTimeline.ApplyColorBalance(sourcePixel, .75f),
                "Above-neutral color-balance shader");

            renderer.CinematicColorBalance = .5f;
            renderer.CinematicOffsetEffect = .75f;
            renderer.Render(64, 64, new(), null, null, null);
            sourcePixel = ReadCenterPixel(renderer.Target.Texture);
            target = (RenderTexture2D)(typeof(SceneRenderer)
                .GetMethod("RenderCinematicAfterEffects", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, null)!);
            AssertPixel(target.Texture, CinematicAfterEffectTimeline.ApplyOffsetEffect(sourcePixel, .75f),
                "PC mode-8 additive offset shader");

            renderer.CinematicColorBalance = .75f;
            renderer.Render(64, 64, new(), null, null, null);
            sourcePixel = ReadCenterPixel(renderer.Target.Texture);
            target = (RenderTexture2D)(typeof(SceneRenderer)
                .GetMethod("RenderCinematicAfterEffects", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, null)!);
            Vector3 expectedCombined = CinematicAfterEffectTimeline.ApplyColorBalance(
                CinematicAfterEffectTimeline.ApplyOffsetEffect(sourcePixel, .75f), .75f);
            AssertPixel(target.Texture, expectedCombined, "Ordered PC mode-8 then mode-9 shader passes");

            renderer.CinematicOffsetEffect = .5f;
            renderer.CinematicColorBalance = .5f;
            renderer.Render(64, 64, new(), null, null, null);
            var neutral = (RenderTexture2D)(typeof(SceneRenderer)
                .GetMethod("RenderCinematicAfterEffects", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, null)!);
            if (neutral.Texture.Id != renderer.Target.Texture.Id)
                throw new InvalidDataException("Neutral color balance must present the original scene target without a post-process pass.");
        }
        finally { Raylib.CloseWindow(); }

        Console.WriteLine("PASS: GPU color-balance pass compiles, changes frame pixels and bypasses the neutral state");
    }

    private static Vector3 ReadCenterPixel(Texture2D texture)
    {
        var image = Raylib.LoadImageFromTexture(texture);
        try
        {
            Color pixel = Raylib.GetImageColor(image, 32, 32);
            return new Vector3(pixel.R, pixel.G, pixel.B) / 255;
        }
        finally { Raylib.UnloadImage(image); }
    }

    private static void AssertPixel(Texture2D texture, Vector3 expected, string label)
    {
        var image = Raylib.LoadImageFromTexture(texture);
        try
        {
            Color pixel = Raylib.GetImageColor(image, 32, 32);
            var actual = new Vector3(pixel.R, pixel.G, pixel.B);
            var expectedBytes = expected * 255;
            if (Vector3.Distance(actual, expectedBytes) > 2)
                throw new InvalidDataException($"{label} produced {pixel}; expected approximately {expectedBytes}.");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
