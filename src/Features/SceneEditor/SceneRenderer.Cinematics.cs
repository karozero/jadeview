using System.Numerics;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace JadeView;

public sealed partial class SceneRenderer
{
    public CinematicShutterState? CinematicShutter { get; set; }
    public Vector4? CinematicScreenFade { get; set; }
    public float? CinematicHorizontalFov { get; set; }
    internal CinematicFlashState[] CinematicFlashes { get; set; } = [];
    internal CinematicLightningState[] CinematicLightnings { get; set; } = [];
    internal CinematicMaterialState? CinematicMaterials { get; set; }
    private JadeMaterialLayer EffectiveLayer(SceneObject sceneObject, GpuMaterialPass pass)
    {
        var layer = CinematicMaterials?.Apply(pass.MaterialKey, pass.LayerIndex, pass.Layer) ?? pass.Layer;
        return CinematicMaterials?.ApplyEnvironmentAlpha(sceneObject.GaoKey, pass.SubMaterialIndex,
            pass.LayerIndex, layer) ?? layer;
    }
    private Vector4 CinematicRliColor => CinematicMaterials?.RliColor ?? Vector4.Zero;
    private float CinematicRliFactor => CinematicMaterials?.RliFactor ?? 0;
    private float? previousCameraFov;

    private void ApplyCinematicProjection(int width, int height)
    {
        if (CinematicHorizontalFov is > 0 and < MathF.PI)
        {
            previousCameraFov ??= Camera.FovY;
            Camera.FovY = CinematicCameraLens.VerticalAngle(CinematicHorizontalFov.Value,
                (float)width / height) * 180 / MathF.PI;
        }
        else if (previousCameraFov is { } previous)
        {
            Camera.FovY = previous;
            previousCameraFov = null;
        }
    }

    private void DrawCinematicFade(int width, int height)
    {
        if (CinematicScreenFade is not { W: > 0 } fade) return;
        fade = Vector4.Clamp(fade, Vector4.Zero, Vector4.One) * 255;
        Raylib.DrawRectangle(0, 0, width, height, new Color((byte)fade.X, (byte)fade.Y, (byte)fade.Z, (byte)fade.W));
    }

    private void DrawCinematicShutter(int width, int height)
    {
        if (CinematicShutter is not { Closure: > 0 } shutter) return;
        if (shutter.Closure >= .99f)
        {
            Raylib.DrawRectangle(0, 0, width, height, Color.Black);
            return;
        }
        var center = shutter.Center * new Vector2(width, height);
        float horizontal = Math.Max(center.X, width - center.X);
        float vertical = Math.Max(center.Y, height - center.Y);
        float diagonal = MathF.Sqrt(horizontal * horizontal + vertical * vertical);
        float inner = (1 - shutter.Closure) * diagonal;
        float outer = diagonal * 4;
        float angle = shutter.Closure * MathF.PI + MathF.PI / 8;
        // GFX_Diaphragm uses a rotating eight-sided aperture. The outer ring
        // extends beyond the viewport; clipping leaves the opaque shutter blades.
        for (int edge = 0; edge < 8; edge++)
        {
            float first = angle + edge * MathF.PI / 4;
            float next = first + MathF.PI / 4;
            var direction = new Vector2(MathF.Cos(first), MathF.Sin(first));
            var nextDirection = new Vector2(MathF.Cos(next), MathF.Sin(next));
            var innerFirst = center + direction * inner;
            var innerNext = center + nextDirection * inner;
            var outerFirst = center + direction * outer;
            var outerNext = center + nextDirection * outer;
            Raylib.DrawTriangle(innerFirst, outerNext, outerFirst, Color.Black);
            Raylib.DrawTriangle(innerFirst, innerNext, outerNext, Color.Black);
        }
    }

    private void DrawCinematicFlashes()
    {
        if (CinematicFlashes.Length == 0) return;
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var flash in CinematicFlashes)
        {
            var viewDirection = Vector3.Normalize(Camera.Target - Camera.Position);
            if (Vector3.Dot(flash.Position - Camera.Position, viewDirection) <= 0) continue;
            var center = Raylib.GetWorldToScreen(flash.Position, Camera);
            if (!float.IsFinite(center.X + center.Y)) continue;
            var cameraRight = Vector3.Normalize(Vector3.Cross(viewDirection, Camera.Up));
            var cameraUp = Vector3.Normalize(Camera.Up);
            float pixelsPerWorldUnit = Vector2.Distance(center,
                Raylib.GetWorldToScreen(flash.Position + cameraRight, Camera));
            float verticalPixelsPerWorldUnit = Vector2.Distance(center,
                Raylib.GetWorldToScreen(flash.Position + cameraUp, Camera));
            uint packed = flash.Color;
            for (int rayIndex = 0; rayIndex < flash.RayCount; rayIndex++)
            {
                bool smallRay = (rayIndex & 1) != 0;
                float rayLength = (smallRay ? flash.SmallLength : flash.BigLength) * pixelsPerWorldUnit;
                float rayWidth = (smallRay ? flash.SmallWidth : flash.BigWidth) * verticalPixelsPerWorldUnit;
                if (rayLength <= 0 || rayWidth <= 0) continue;
                float angle = rayIndex * (2 * MathF.PI / flash.RayCount) + .17f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var side = new Vector2(-direction.Y, direction.X);
                var origin = center - direction * rayWidth * .35f;
                var tip = center + direction * rayLength;
                float alphaFactor = Math.Clamp(smallRay ? flash.SmallAlpha : flash.BigAlpha, 0, 1);
                var color = new Color((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed,
                    (byte)Math.Clamp((int)(255 * flash.Opacity * alphaFactor), 0, 255));
                Raylib.DrawTriangle(origin + side * rayWidth, tip, origin - side * rayWidth, color);
            }
        }
        Raylib.EndBlendMode();
    }

    private void DrawCinematicLightnings()
    {
        if (CinematicLightnings.Length == 0) return;
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var lightning in CinematicLightnings)
        {
            var axis = lightning.End - lightning.Start;
            float length = axis.Length();
            if (!float.IsFinite(length) || length < .001f) continue;
            axis /= length;
            var reference = Math.Abs(Vector3.Dot(axis, Vector3.UnitY)) > .9f ? Vector3.UnitX : Vector3.UnitY;
            var viewDirection = Vector3.Normalize(Camera.Position - (lightning.Start + lightning.End) * .5f);
            var sideDirection = Vector3.Cross(axis, viewDirection);
            var side = sideDirection.LengthSquared() > .0001f
                ? Vector3.Normalize(sideDirection) : Vector3.Normalize(Vector3.Cross(axis, reference));
            var depth = Vector3.Normalize(Vector3.Cross(axis, side));
            float radius = Math.Max(.01f, lightning.Radius);
            int segments = Math.Clamp(lightning.SegmentCount, 1, 64);
            int packedAlpha = (int)(lightning.Color >> 24);
            var color = new Color((byte)(lightning.Color >> 16), (byte)(lightning.Color >> 8),
                (byte)lightning.Color, (byte)Math.Clamp((int)((packedAlpha == 0 ? 255 : packedAlpha) * lightning.Opacity), 0, 255));
            var previousCenter = lightning.Start;
            float previousWidth = 0;
            for (int index = 1; index <= segments; index++)
            {
                float along = (float)index / segments;
                float envelope = index == segments ? 0 : MathF.Sin(along * MathF.PI);
                float jitter = DeterministicJitter(lightning.Seed, index, lightning.Time);
                float crossJitter = DeterministicJitter(lightning.Seed ^ 0x9E3779B9u, index, lightning.Time);
                var currentCenter = lightning.Start + axis * (length * along)
                    + (side * jitter + depth * crossJitter) * radius * envelope;
                float currentWidth = lightning.Width * .5f * envelope;
                var previousLeft = previousCenter - side * previousWidth;
                var previousRight = previousCenter + side * previousWidth;
                var currentLeft = currentCenter - side * currentWidth;
                var currentRight = currentCenter + side * currentWidth;
                Raylib.DrawTriangle3D(previousLeft, previousRight, currentRight, color);
                Raylib.DrawTriangle3D(previousLeft, currentRight, currentLeft, color);
                previousCenter = currentCenter;
                previousWidth = currentWidth;
            }
        }
        Raylib.EndBlendMode();
    }

    private static float DeterministicJitter(uint seed, int segment, float time)
    {
        uint value = seed + (uint)segment * 0x85EBCA6Bu + (uint)Math.Max(0, time * 12) * 0xC2B2AE35u;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value / (float)uint.MaxValue) * 2 - 1;
    }
}
