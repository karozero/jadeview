using System.Numerics;
using JadeView;

internal static class SceneLightEvaluationChecks
{
    private const uint DynamicFlags = 0x00000100;
    private const uint StaticFlags = 0x00000200;
    private const uint LightedDrawMask = 0x08000000;

    internal static void Verify()
    {
        VerifyDirectUsesPositiveLightYAndIgnoresPositionAndRange();
        VerifyOmniRangeAndPaint();
        VerifySpotHemisphereRangeAndCone();
        VerifyPackedChannelsAndSaturatingArithmetic();
        VerifyEligibilityAndImmutableSource();
        VerifyAttenuationOverridesAndInputBounds();
        Console.WriteLine("PASS: pure scene light evaluator direct/omni/spot math, packed channels, gates, attenuation and immutable inputs");
    }

    private static void VerifyDirectUsesPositiveLightYAndIgnoresPositionAndRange()
    {
        SceneLight light = CreateLight(SceneLightType.Direct, color: 0x80402010, near: 1000, far: 0.1f);
        SceneLightEvaluationResult upwardFacing = Evaluate(light, normal: Vector3.UnitY);
        SceneLightEvaluationResult sideways = Evaluate(light, normal: Vector3.UnitX);
        SceneLightEvaluationResult opposite = Evaluate(light, normal: -Vector3.UnitY);
        SceneLightEvaluationResult movedLight = Evaluate(light, lightWorld: Matrix4x4.CreateTranslation(500, -700, 900), normal: Vector3.UnitY);
        SceneLightEvaluationResult rotatedAxis = Evaluate(light, lightWorld: Matrix4x4.CreateRotationX(MathF.PI / 2), normal: Vector3.UnitZ);

        Require(upwardFacing.Intensity == 1f && upwardFacing.ContributionColor == light.Color,
            "Direct type 1 must apply the source +Y Lambert factor.");
        Require(sideways.Intensity == 0f && opposite.Intensity == 0f,
            "Direct type 1 must reject normals outside the +Y lit hemisphere.");
        Require(movedLight.Intensity == upwardFacing.Intensity && movedLight.ContributionColor == upwardFacing.ContributionColor,
            "Direct type 1 must not depend on light position or near/far fields.");
        Require(rotatedAxis.Intensity == 1f,
            "Direct type 1 must transform the source light's local +Y axis into object space.");
    }

    private static void VerifyOmniRangeAndPaint()
    {
        SceneLight light = CreateLight(SceneLightType.Omni, color: 0xFFFFFFFF, near: 1, far: 3);
        SceneLightEvaluationResult insideNear = Evaluate(light,
            position: new(0, 0, 1), normal: -Vector3.UnitZ);
        SceneLightEvaluationResult halfwayOut = Evaluate(light,
            position: new(0, 0, 2), normal: -Vector3.UnitZ);
        SceneLightEvaluationResult beyondFar = Evaluate(light,
            position: new(0, 0, 3.01f), normal: -Vector3.UnitZ);
        SceneLightEvaluationResult painted = Evaluate(light with { Flags = light.Flags | 0x10 },
            position: new(0, 0, 2), normal: Vector3.UnitY);

        Near(insideNear.Intensity, 1f, "Omni normal incidence inside near range");
        Near(halfwayOut.Intensity, 0.625f, "Omni Lambert incidence and squared-range attenuation");
        Require(beyondFar.Intensity == 0f && beyondFar.ContributionColor == 0,
            "Omni light must contribute nothing beyond its far radius.");
        Near(painted.Intensity, 0.5f, "Paint omni linear-distance attenuation without normal weighting");

        SceneLight constantPaintLight = light with { Flags = light.Flags | 0x00100000 | 0x10 };
        SceneLightEvaluationResult constantPaint = Evaluate(constantPaintLight,
            position: new(0, 0, 2), normal: Vector3.UnitY);
        Near(constantPaint.Intensity, 0.5f,
            "Paint mode takes precedence over OmniConst and keeps the linear-distance paint path.");
    }

    private static void VerifySpotHemisphereRangeAndCone()
    {
        SceneLight light = CreateLight(SceneLightType.Spot, color: 0xFFFFFFFF, near: 1, far: 3,
            density: 0.2f, i8: 0.8f);
        SceneLightEvaluationResult onAxis = Evaluate(light,
            position: new(0, -2, 0), normal: Vector3.UnitY);
        SceneLightEvaluationResult angled = Evaluate(light,
            position: new(2f * MathF.Sin(0.5f), -2f * MathF.Cos(0.5f), 0), normal: Vector3.UnitY);
        SceneLightEvaluationResult outsideCone = Evaluate(light,
            position: new(2f * MathF.Sin(1.0f), -2f * MathF.Cos(1.0f), 0), normal: Vector3.UnitY);
        SceneLightEvaluationResult behindLight = Evaluate(light,
            position: new(0, 2, 0), normal: -Vector3.UnitY);
        SceneLightEvaluationResult beyondFar = Evaluate(light,
            position: new(0, -3.01f, 0), normal: Vector3.UnitY);
        SceneLightEvaluationResult paint = Evaluate(light with { Flags = light.Flags | 0x10 },
            position: new(0, -2, 0), normal: -Vector3.UnitX);

        Near(onAxis.Intensity, 0.625f, "Spot on-axis range attenuation");
        float expectedAngle = MathF.Cos(0.5f) * 0.625f
            * (MathF.Sin(0.8f) - MathF.Sin(0.5f)) / (MathF.Sin(0.8f) - MathF.Sin(0.2f));
        Near(angled.Intensity, expectedAngle, "Spot normal and inner/outer cone attenuation");
        Require(outsideCone.Intensity == 0f && behindLight.Intensity == 0f && beyondFar.Intensity == 0f,
            "Spot must reject the outer cone, rear hemisphere, and points beyond far range.");
        Near(paint.Intensity, 0.625f, "Paint spot must retain range/cone but skip normal weighting");
    }

    private static void VerifyPackedChannelsAndSaturatingArithmetic()
    {
        SceneLight light = CreateLight(SceneLightType.Direct, color: 0x80402010);
        SceneLightEvaluationResult noAlpha = Evaluate(light with { Flags = light.Flags & ~(uint)0x40 }, normal: Vector3.UnitY);
        SceneLightEvaluationResult alphaOnly = Evaluate(light with { Flags = (light.Flags & ~(uint)0x20) | 0x40 }, normal: Vector3.UnitY);
        SceneLightEvaluationResult half = Evaluate(light, normal: new(0, 0.5f, MathF.Sqrt(0.75f)));
        SceneLightEvaluationResult saturated = Evaluate(light, normal: Vector3.UnitY, accumulatedColor: 0xF0F0F0F0);
        SceneLightEvaluationResult subtracted = Evaluate(light with { Flags = light.Flags | 0x80 },
            normal: Vector3.UnitY, accumulatedColor: light.Color);

        Require(noAlpha.ContributionColor == 0x00402010,
            "Clearing UseAlpha must mask only the packed high alpha byte.");
        Require(alphaOnly.ContributionColor == 0x80000000,
            "Clearing UseColor must retain only the packed high alpha byte.");
        Require(half.ContributionColor == 0x40201008,
            "The source linear light table must quantize a half-intensity packed color correctly.");
        Require(saturated.AccumulatedColor == 0xFFFFFFFF,
            "Light contributions must use saturating channel addition.");
        Require(subtracted.AccumulatedColor == 0,
            "Absorb bit 0x80 must subtract each packed channel with a zero floor.");
    }

    private static void VerifyEligibilityAndImmutableSource()
    {
        SceneLight light = CreateLight(SceneLightType.Direct, color: 0xFFFFFFFF, reference: 0x1234);
        SceneLight original = light;
        SceneLightEvaluationResult inactive = Evaluate(light with { Flags = light.Flags & ~(uint)0x8 }, normal: Vector3.UnitY);
        SceneLightEvaluationResult wrongReceiver = Evaluate(light with { Flags = light.Flags & ~DynamicFlags }, normal: Vector3.UnitY);
        SceneLightEvaluationResult missingDrawFlag = Evaluate(light, normal: Vector3.UnitY, drawMask: 0);
        SceneLightEvaluationResult wrongOwner = Evaluate(light, normal: Vector3.UnitY, initialActorKey: 0x4321);
        SceneLightEvaluationResult staticReceiver = Evaluate(light with { Flags = (light.Flags & ~DynamicFlags) | StaticFlags },
            normal: Vector3.UnitY, objectFlags2: 0);

        Require(inactive.Status == SceneLightEvaluationStatus.Inactive && inactive.AccumulatedColor == 0,
            "A light whose current component flags are inactive must be ignored.");
        Require(wrongReceiver.Status == SceneLightEvaluationStatus.ReceiverMaskRejected,
            "Dynamic objects must require the dynamic-light receiver bit.");
        Require(missingDrawFlag.Status == SceneLightEvaluationStatus.ReceiverMaskRejected,
            "The object Lighted draw-mask bit must gate the evaluator.");
        Require(wrongOwner.Status == SceneLightEvaluationStatus.LightReferenceMismatch,
            "A referenced light object must match the supplied initial actor key.");
        Require(staticReceiver.Intensity == 1f,
            "Non-dynamic objects must use the static-light receiver bit.");
        Require(light == original,
            "Evaluation must not mutate the serialized source light record.");
    }

    private static void VerifyAttenuationOverridesAndInputBounds()
    {
        SceneLight light = CreateLight(SceneLightType.Direct, color: 0xFFFFFFFF);
        SceneLightEvaluationResult attenuated = Evaluate(light, normal: Vector3.UnitY,
            visualAttenuation: 63, displayAttenuation: 0.5f);
        SceneLightEvaluationResult componentOverride = Evaluate(light with { Flags = light.Flags | 0x00200000 },
            normal: Vector3.UnitY, visualAttenuation: 63, displayAttenuation: 0.5f);
        SceneLightEvaluationResult drawMaskOverride = Evaluate(light, normal: Vector3.UnitY,
            visualAttenuation: 63, displayAttenuation: 0.5f, drawMask: LightedDrawMask | 0x40000000);
        SceneLightEvaluationResult invalidAttenuation = Evaluate(light, normal: Vector3.UnitY,
            visualAttenuation: 128);

        Require(attenuated.ContributionColor != light.Color && attenuated.ContributionColor != 0,
            "Valid visual and display attenuation must scale the packed light contribution.");
        Require(componentOverride.ContributionColor == light.Color && drawMaskOverride.ContributionColor == light.Color,
            "Either source DontAttenuate bit must bypass visual/display attenuation.");
        Require(invalidAttenuation.Status == SceneLightEvaluationStatus.InvalidInput,
            "Visual distance attenuation outside the source 0..127 range must be rejected.");
    }

    private static SceneLight CreateLight(SceneLightType type, uint color, float near = 0, float far = 1,
        float density = 0.2f, float i8 = 0.8f, uint reference = SceneLight.NoLightGaoKey) =>
        new(0x8 | 0x100 | 0x20 | 0x40 | (uint)type, color, near, far, density, i8, reference);

    private static SceneLightEvaluationResult Evaluate(SceneLight light,
        Matrix4x4? lightWorld = null,
        Matrix4x4? objectWorld = null,
        Vector3? position = null,
        Vector3? normal = null,
        uint objectFlags2 = 0x8,
        uint drawMask = LightedDrawMask,
        uint initialActorKey = 0x1234,
        byte visualAttenuation = 0,
        float displayAttenuation = 0,
        uint accumulatedColor = 0) =>
        SceneLightEvaluation.Evaluate(new(
            light,
            lightWorld ?? Matrix4x4.Identity,
            objectWorld ?? Matrix4x4.Identity,
            position ?? Vector3.Zero,
            normal ?? Vector3.UnitY,
            Vector3.Zero,
            objectFlags2,
            drawMask,
            initialActorKey,
            visualAttenuation,
            displayAttenuation,
            accumulatedColor));

    private static void Near(float actual, float expected, string label)
    {
        if (MathF.Abs(actual - expected) > 1e-5f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
