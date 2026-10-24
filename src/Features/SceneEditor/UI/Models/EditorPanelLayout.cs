using System.Numerics;

namespace JadeView;

internal sealed class EditorPanelLayout
{
    private const float DefaultViewportFraction = .47f;
    private const float DefaultInspectorFraction = .30f;
    private const float DefaultBrowserFraction = .27f;
    private const float DefaultCinematicBrowserFraction = .38f;
    private const float DefaultCinematicTimelineFraction = .55f;
    private float viewportFraction = DefaultViewportFraction;
    private float inspectorFraction = DefaultInspectorFraction;
    private float browserFraction = DefaultBrowserFraction;
    private float cinematicBrowserFraction = DefaultCinematicBrowserFraction;
    private float cinematicTimelineFraction = DefaultCinematicTimelineFraction;

    internal Vector3 GetWidths(float width, float scale)
    {
        float minimum = Math.Min(220 * scale, width * .3f);
        float viewport = Math.Clamp(width * viewportFraction, minimum, width - minimum * 2);
        float inspector = Math.Clamp(width * inspectorFraction, minimum, width - viewport - minimum);
        return new(viewport, inspector, Math.Max(minimum, width - viewport - inspector));
    }

    internal float GetBrowserHeight(float height, float scale, bool cinematicMode = false)
    {
        float minimumBrowser = Math.Min(110 * scale, height * .3f);
        float minimumScene = Math.Min(240 * scale, height * .6f);
        float fraction = cinematicMode ? cinematicBrowserFraction : browserFraction;
        return Math.Clamp(height * fraction, minimumBrowser, height - minimumScene);
    }

    internal float GetCinematicTimelineHeight(float height, float scale)
    {
        float minimumTimeline = Math.Min(96 * scale, height * .35f);
        float minimumBrowser = Math.Min(96 * scale, height * .35f);
        return Math.Clamp(height * cinematicTimelineFraction, minimumTimeline, height - minimumBrowser);
    }

    internal void ResizeColumns(float width, float scale, float delta, bool firstBoundary)
    {
        Vector3 sizes = GetWidths(width, scale);
        float minimum = Math.Min(220 * scale, width * .3f);
        if (firstBoundary)
        {
            float viewport = Math.Clamp(sizes.X + delta, minimum, sizes.X + sizes.Y - minimum);
            sizes.Y += sizes.X - viewport;
            sizes.X = viewport;
        }
        else
        {
            sizes.Y = Math.Clamp(sizes.Y + delta, minimum, sizes.Y + sizes.Z - minimum);
        }
        viewportFraction = sizes.X / width;
        inspectorFraction = sizes.Y / width;
    }

    internal void ResizeBrowser(float height, float scale, float delta, bool cinematicMode = false)
    {
        float resizedFraction = Math.Clamp((GetBrowserHeight(height, scale, cinematicMode) - delta) / height, 0, 1);
        if (cinematicMode) cinematicBrowserFraction = resizedFraction;
        else browserFraction = resizedFraction;
    }

    internal void ResizeCinematicTimeline(float height, float scale, float delta) =>
        cinematicTimelineFraction = Math.Clamp(
            (GetCinematicTimelineHeight(height, scale) + delta) / height, 0, 1);

    internal void ResetCinematicTimeline() => cinematicTimelineFraction = DefaultCinematicTimelineFraction;

    internal void ResetColumns()
    {
        viewportFraction = DefaultViewportFraction;
        inspectorFraction = DefaultInspectorFraction;
    }

    internal void ResetBrowser(bool cinematicMode = false)
    {
        if (cinematicMode) cinematicBrowserFraction = DefaultCinematicBrowserFraction;
        else browserFraction = DefaultBrowserFraction;
    }
}
