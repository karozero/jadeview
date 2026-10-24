using System.Numerics;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private RenderTexture2D cinematicAfterEffectsTarget;
    private Shader cinematicColorBalanceShader;
    private int cinematicOffsetEffectLocation = -1;
    private int cinematicColorBalanceLocation = -1;
    private bool cinematicColorBalanceShaderInitialized;
    internal float CinematicOffsetEffect { get; set; } = .5f;
    internal float CinematicColorBalance { get; set; } = .5f;
    public CinematicShutterState? CinematicShutter { get; set; }
    public Vector4? CinematicScreenFade { get; set; }
    public float? CinematicHorizontalFov { get; set; }
    internal IReadOnlyList<CinematicFlashState> CinematicFlashes { get; set; } = Array.Empty<CinematicFlashState>();
    internal IReadOnlyList<CinematicLightningState> CinematicLightnings { get; set; } = Array.Empty<CinematicLightningState>();
    internal CinematicMaterialState? CinematicMaterials { get; set; }
    private JadeMaterialLayer EffectiveLayer(SceneObject sceneObject, GpuMaterialPass pass)
    {
        var layer = CinematicMaterials?.Apply(pass.MaterialKey, pass.LayerIndex, pass.Layer) ?? pass.Layer;
        return CinematicMaterials?.ApplyEnvironmentAlpha(sceneObject.GaoKey, pass.SubMaterialIndex,
            pass.LayerIndex, layer) ?? layer;
    }
    private Vector4 CinematicRliColor => CinematicMaterials?.RliColor ?? Vector4.Zero;
    private float CinematicRliFactor => CinematicMaterials?.RliFactor ?? 0;
    private Vector4 CinematicFogColor => CinematicMaterials?.FogColor ?? Vector4.Zero;
    private float CinematicFogNear => CinematicMaterials?.FogNear ?? 0;
    private float CinematicFogFar => CinematicMaterials?.FogFar ?? 0;
    private bool TryGetWorldAmbient(WorldData world, uint drawFlags, out Vector4 color)
    {
        color = Vector4.Zero;
        if ((drawFlags & 0x100) == 0) return false;
        bool firstChannel = (drawFlags & 0x00400000) != 0;
        if (CinematicMaterials is { HasAmbient: true } lighting)
            color = firstChannel ? lighting.AmbientColor : lighting.AmbientColor2;
        else if (world.Lighting is { } source)
        {
            uint packed = firstChannel ? source.AmbientColor : source.EffectiveAmbientColor2;
            color = new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, packed >> 24) / 255f;
        }
        else return false;
        return true;
    }
    private float? previousCameraFov;

    internal unsafe RenderTexture2D RenderCinematicAfterEffects()
    {
        bool hasOffsetEffect = float.IsFinite(CinematicOffsetEffect)
            && Math.Abs(CinematicOffsetEffect - .5f) >= .0001f;
        bool hasColorBalance = float.IsFinite(CinematicColorBalance)
            && Math.Abs(CinematicColorBalance - .5f) >= .0001f;
        if (!hasOffsetEffect && !hasColorBalance)
            return _sceneTarget;

        if (!cinematicColorBalanceShaderInitialized)
        {
            cinematicColorBalanceShaderInitialized = true;
            cinematicColorBalanceShader = Raylib.LoadShaderFromMemory(null, """
                #version 330
                in vec2 fragTexCoord;
                in vec4 fragColor;
                uniform sampler2D texture0;
                uniform float offsetEffect;
                uniform float colorBalance;
                out vec4 finalColor;
                void main() {
                    vec4 source = texture(texture0, fragTexCoord) * fragColor;
                    float offsetAmount = floor(abs(offsetEffect - 0.5) * 255.0) / 255.0;
                    vec3 color = offsetEffect >= 0.5
                        ? clamp(source.rgb + vec3(offsetAmount), 0.0, 1.0)
                        : clamp(source.rgb - vec3(offsetAmount), 0.0, 1.0);
                    float balance = clamp(colorBalance, 0.0, 1.0);
                    if (balance < 0.5) {
                        float diffuseAlpha = floor(balance * 510.0) / 255.0;
                        color = mix(color, vec3(128.0 / 255.0), 1.0 - diffuseAlpha);
                    } else {
                        float amount = balance * 2.0 - 1.0;
                        float modulation = floor(amount * 255.0) / 255.0;
                        float centerOffset = floor(amount * 128.0) / 255.0;
                        color = clamp(color + color * modulation - vec3(centerOffset), 0.0, 1.0);
                    }
                    finalColor = vec4(color, source.a);
                }
                """);
            cinematicOffsetEffectLocation = Raylib.GetShaderLocation(cinematicColorBalanceShader, "offsetEffect");
            cinematicColorBalanceLocation = Raylib.GetShaderLocation(cinematicColorBalanceShader, "colorBalance");
        }

        if (cinematicColorBalanceShader.Id == 0 || cinematicOffsetEffectLocation < 0
            || cinematicColorBalanceLocation < 0) return _sceneTarget;
        int width = _sceneTarget.Texture.Width;
        int height = _sceneTarget.Texture.Height;
        if (cinematicAfterEffectsTarget.Id == 0
            || cinematicAfterEffectsTarget.Texture.Width != width
            || cinematicAfterEffectsTarget.Texture.Height != height)
        {
            if (cinematicAfterEffectsTarget.Id != 0) Raylib.UnloadRenderTexture(cinematicAfterEffectsTarget);
            cinematicAfterEffectsTarget = Raylib.LoadRenderTexture(width, height);
        }
        if (cinematicAfterEffectsTarget.Id == 0) return _sceneTarget;

        float offsetEffect = float.IsFinite(CinematicOffsetEffect) ? CinematicOffsetEffect : .5f;
        Raylib.SetShaderValue(cinematicColorBalanceShader, cinematicOffsetEffectLocation,
            &offsetEffect, ShaderUniformDataType.Float);
        float colorBalance = float.IsFinite(CinematicColorBalance) ? CinematicColorBalance : .5f;
        Raylib.SetShaderValue(cinematicColorBalanceShader, cinematicColorBalanceLocation, &colorBalance,
            ShaderUniformDataType.Float);
        Raylib.BeginTextureMode(cinematicAfterEffectsTarget);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(cinematicColorBalanceShader);
        Raylib.DrawTexturePro(_sceneTarget.Texture,
            new Raylib_cs.Rectangle(0, 0, width, -height),
            new Raylib_cs.Rectangle(0, 0, width, height),
            Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
        return cinematicAfterEffectsTarget;
    }

    private void DisposeCinematicAfterEffects()
    {
        if (cinematicAfterEffectsTarget.Id != 0) Raylib.UnloadRenderTexture(cinematicAfterEffectsTarget);
        if (cinematicColorBalanceShader.Id != 0) Raylib.UnloadShader(cinematicColorBalanceShader);
        cinematicAfterEffectsTarget = default;
        cinematicColorBalanceShader = default;
        cinematicColorBalanceShaderInitialized = false;
        cinematicOffsetEffectLocation = -1;
        cinematicColorBalanceLocation = -1;
    }

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
        if (CinematicFlashes.Count == 0) return;
        var materialTexture = ResolveCinematicFlashTexture();
        Rlgl.EnableDepthTest();
        SetDepthFunction(515);
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var flash in CinematicFlashes)
        {
            var viewDirection = Vector3.Normalize(Camera.Target - Camera.Position);
            var cameraToFlash = flash.Position - Camera.Position;
            float cameraDistance = cameraToFlash.Length();
            if (cameraDistance <= 0 || Vector3.Dot(cameraToFlash, viewDirection) <= 0) continue;
            var center = Raylib.GetWorldToScreen(flash.Position, Camera);
            if (!float.IsFinite(center.X + center.Y)) continue;
            var cameraRight = Vector3.Normalize(Vector3.Cross(viewDirection, Camera.Up));
            var cameraUp = Vector3.Normalize(Vector3.Cross(cameraRight, viewDirection));
            float pixelsPerWorldUnit = Vector2.Distance(center,
                Raylib.GetWorldToScreen(flash.Position + cameraRight, Camera));
            float verticalPixelsPerWorldUnit = Vector2.Distance(center,
                Raylib.GetWorldToScreen(flash.Position + cameraUp, Camera));
            float maximumRayRadius = Math.Min(Target.Texture.Width, Target.Texture.Height) * .22f;
            uint packed = flash.Color;
            var rays = CinematicSunRayLayout.Create(center, flash.RayCount, flash.Seed,
                flash.BigAngleJitter, flash.BigLengthMin, flash.BigLengthMax,
                flash.BigWidthMin, flash.BigWidthMax, flash.SmallAngleJitter,
                flash.SmallLengthMin, flash.SmallLengthMax, flash.SmallWidthMin, flash.SmallWidthMax,
                flash.LengthRatio, pixelsPerWorldUnit, verticalPixelsPerWorldUnit, maximumRayRadius);
            var color = new Color((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed,
                (byte)Math.Clamp((int)(255 * flash.Opacity), 0, 255));
            DrawSunRays(rays, center, flash.Position, cameraRight, cameraUp,
                pixelsPerWorldUnit, verticalPixelsPerWorldUnit, color, materialTexture);
        }
        Rlgl.EnableDepthMask();
        Raylib.EndBlendMode();
    }

    private Texture2D? ResolveCinematicFlashTexture()
    {
        // SFX_Flash_SFX_Flash_comportement binds SFX_light&smoke.gao submaterial 11.
        // The compiled BGE script references 62000F8F; sally_clean resolves the usable FIX
        // material graph from root 62000F8C. Submaterial 11 uses a grayscale RGB ray mask, not alpha.
        const uint lightSmokeMaterialKey = 0x62000F8C;
        const int sunRayMaterialIndex = 11;
        if (Resolver == null
            || !Resolver.Materials.TryGetValue(lightSmokeMaterialKey, out var materialRoot)
            || materialRoot.SubMaterialKeys.Count <= sunRayMaterialIndex)
            return null;

        uint materialKey = materialRoot.SubMaterialKeys[sunRayMaterialIndex];
        var material = Resolver.Materials.GetValueOrDefault(materialKey);
        var layer = JadeMaterialLayer.Read(material?.SourceBytes).FirstOrDefault(candidate => !candidate.Disabled);
        if (layer == null || layer.Texture is 0 or uint.MaxValue
            || !Resolver.TryResolveTexture(layer.Texture, out var texture))
            return null;

        return GetOrUploadTexture(texture.ToTextureAsset());
    }

    private static void DrawSunRays(CinematicSunRay[] rays, Vector2 screenCenter, Vector3 planeCenter,
        Vector3 cameraRight, Vector3 cameraUp, float horizontalScale, float verticalScale,
        Color color, Texture2D? texture)
    {
        Rlgl.Begin((int)Raylib_cs.DrawMode.Triangles);
        Rlgl.SetTexture(texture is { Id: > 0 } rayTexture ? rayTexture.Id : 0);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        foreach (var ray in rays)
        {
            Rlgl.TexCoord2f(.5f, 0);
            Vertex(ray.Rear);
            Rlgl.TexCoord2f(0, 1);
            Vertex(ray.ForwardLeft);
            Rlgl.TexCoord2f(1, 1);
            Vertex(ray.ForwardRight);
        }
        Rlgl.End();
        Rlgl.SetTexture(0);

        void Vertex(Vector2 screenPoint)
        {
            Vector3 texturedPoint = ToCameraPlane(screenPoint);
            Rlgl.Vertex3f(texturedPoint.X, texturedPoint.Y, texturedPoint.Z);
        }

        Vector3 ToCameraPlane(Vector2 screenPoint)
        {
            float horizontalOffset = (screenPoint.X - screenCenter.X) / Math.Max(.0001f, horizontalScale);
            float verticalOffset = (screenCenter.Y - screenPoint.Y) / Math.Max(.0001f, verticalScale);
            return planeCenter + cameraRight * horizontalOffset + cameraUp * verticalOffset;
        }
    }

    private void DrawCinematicLightnings()
    {
        if (CinematicLightnings.Count == 0) return;
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
