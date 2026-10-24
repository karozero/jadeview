using System.Numerics;

namespace JadeView;

/// <summary>
/// Inputs needed to evaluate one light at one object-space vertex. Flags and attenuation
/// values are supplied by the caller as current state; this evaluator does not run scripts.
/// </summary>
public readonly record struct SceneLightEvaluationInput(
    SceneLight Light,
    Matrix4x4 LightWorldMatrix,
    Matrix4x4 ObjectWorldMatrix,
    Vector3 ObjectLocalPosition,
    Vector3 ObjectLocalNormal,
    Vector3 ObjectLocalBoundsCenter,
    uint ObjectFlags2,
    uint ObjectDrawMask,
    uint InitialActorKey,
    byte VisualDistanceAttenuation,
    float DisplayLightAttenuation,
    uint AccumulatedColor);

public enum SceneLightEvaluationStatus
{
    Applied,
    Inactive,
    ReceiverMaskRejected,
    LightReferenceMismatch,
    UnsupportedLightType,
    UnsupportedLightMode,
    InvalidInput
}

public readonly record struct SceneLightEvaluationResult(
    SceneLightEvaluationStatus Status,
    float Intensity,
    uint ContributionColor,
    uint AccumulatedColor);

internal readonly record struct PreparedSceneLightEvaluation(
    SceneLight Light,
    uint Flags,
    SceneLightType Type,
    Vector3 LocalLightPosition,
    Vector3 LocalLightY,
    Vector3 ObjectLocalBoundsCenter,
    float Near,
    float Far,
    float NearSquared,
    float FarSquared,
    uint AttenuatedLightColor);

/// <summary>
/// Source-derived packed diffuse math for PC light types Omni, Direct, and Spot.
/// This is intentionally a pure per-vertex evaluator: activation callbacks, candidate-light
/// list construction, specular, fog, and material selection remain caller responsibilities.
/// </summary>
public static class SceneLightEvaluation
{
    private const uint Active = 0x00000008;
    private const uint Paint = 0x00000010;
    private const uint UseColor = 0x00000020;
    private const uint UseAlpha = 0x00000040;
    private const uint Absorb = 0x00000080;
    private const uint DynamicReceiver = 0x00000100;
    private const uint StaticReceiver = 0x00000200;
    private const uint OmniConstant = 0x00100000;
    private const uint DontAttenuate = 0x00200000;
    private const uint ExclusiveLight = 0x00400000;
    private const uint CylindricalSpot = 0x08000000;
    private const uint DontAttenuateDrawMask = 0x40000000;
    private const uint LightedDrawMask = 0x08000000;
    private const uint DynamicObjectFlag = 0x00000008;
    private const float LinearTableBias = 49152f;

    public static SceneLightEvaluationResult Evaluate(in SceneLightEvaluationInput input)
    {
        uint previousColor = input.AccumulatedColor;
        if (!TryPrepare(input, out PreparedSceneLightEvaluation prepared, out SceneLightEvaluationStatus status))
            return Skipped(status, previousColor);
        return EvaluatePrepared(prepared, input.ObjectLocalPosition, input.ObjectLocalNormal, previousColor);
    }

    internal static bool TryPrepare(in SceneLightEvaluationInput input,
        out PreparedSceneLightEvaluation prepared, out SceneLightEvaluationStatus status)
    {
        prepared = default;
        status = SceneLightEvaluationStatus.InvalidInput;
        if (!HasFiniteInputs(input) || !Matrix4x4.Invert(input.ObjectWorldMatrix, out Matrix4x4 inverseObjectWorld))
            return false;

        return TryPrepare(input, inverseObjectWorld, out prepared, out status);
    }

    internal static bool TryPrepare(in SceneLightEvaluationInput input, Matrix4x4 inverseObjectWorld,
        out PreparedSceneLightEvaluation prepared, out SceneLightEvaluationStatus status)
    {
        prepared = default;
        status = SceneLightEvaluationStatus.InvalidInput;
        if (!HasFiniteInputs(input) || !IsFinite(inverseObjectWorld))
            return false;

        SceneLight light = input.Light;
        uint flags = light.Flags;
        if ((flags & Active) == 0)
            return SetFailure(SceneLightEvaluationStatus.Inactive, out status);

        if ((input.ObjectDrawMask & LightedDrawMask) == 0)
            return SetFailure(SceneLightEvaluationStatus.ReceiverMaskRejected, out status);

        bool isDynamicObject = (input.ObjectFlags2 & DynamicObjectFlag) != 0;
        uint receiverMask = isDynamicObject ? DynamicReceiver : StaticReceiver;
        if ((flags & receiverMask) == 0)
            return SetFailure(SceneLightEvaluationStatus.ReceiverMaskRejected, out status);

        if (light.HasLightGaoReference && light.LightGaoKey != input.InitialActorKey)
            return SetFailure(SceneLightEvaluationStatus.LightReferenceMismatch, out status);

        if ((flags & ExclusiveLight) != 0)
            return SetFailure(SceneLightEvaluationStatus.UnsupportedLightMode, out status);

        SceneLightType? lightType = light.KnownType;
        if (lightType is not (SceneLightType.Omni or SceneLightType.Direct or SceneLightType.Spot))
            return SetFailure(SceneLightEvaluationStatus.UnsupportedLightType, out status);

        if (lightType == SceneLightType.Spot && (flags & CylindricalSpot) != 0)
            return SetFailure(SceneLightEvaluationStatus.UnsupportedLightMode, out status);

        Matrix4x4 lightToObject = input.LightWorldMatrix * inverseObjectWorld;
        Vector3 localLightPosition = Vector3.Transform(
            new Vector3(input.LightWorldMatrix.M41, input.LightWorldMatrix.M42, input.LightWorldMatrix.M43),
            inverseObjectWorld);
        Vector3 localLightY = Vector3.TransformNormal(Vector3.UnitY, lightToObject);
        if (!IsFinite(localLightPosition) || !IsFinite(localLightY) || localLightY.LengthSquared() < 1e-12f)
            return SetFailure(SceneLightEvaluationStatus.InvalidInput, out status);
        localLightY = Vector3.Normalize(localLightY);

        float attenuation = GetAttenuation(input, flags);
        uint packedLightColor = SelectPackedChannels(light.Color, flags);
        uint attenuatedColor = MultiplyPackedColor(packedLightColor, attenuation);
        float near = light.Near;
        float far = light.Far;
        if (lightType == SceneLightType.Omni)
        {
            float meanInverseScale = MeanBasisLength(inverseObjectWorld);
            near *= meanInverseScale;
            far *= meanInverseScale;
        }
        prepared = new(light, flags, lightType.Value, localLightPosition, localLightY,
            input.ObjectLocalBoundsCenter, near, far, near * near, far * far, attenuatedColor);
        status = SceneLightEvaluationStatus.Applied;
        return true;
    }

    internal static bool TryGetInverseObjectWorld(Matrix4x4 objectWorldMatrix, out Matrix4x4 inverseObjectWorld)
    {
        inverseObjectWorld = default;
        return IsFinite(objectWorldMatrix)
            && Matrix4x4.Invert(objectWorldMatrix, out inverseObjectWorld)
            && IsFinite(inverseObjectWorld);
    }

    private static bool SetFailure(SceneLightEvaluationStatus failure, out SceneLightEvaluationStatus status)
    {
        status = failure;
        return false;
    }

    internal static SceneLightEvaluationResult EvaluatePrepared(
        in PreparedSceneLightEvaluation prepared,
        Vector3 objectLocalPosition,
        Vector3 objectLocalNormal,
        uint accumulatedColor)
    {
        if (!IsFinite(objectLocalPosition) || !IsFinite(objectLocalNormal))
            return Skipped(SceneLightEvaluationStatus.InvalidInput, accumulatedColor);

        Vector3 normal = objectLocalNormal;
        if (normal.LengthSquared() > 1e-12f)
            normal = Vector3.Normalize(normal);

        SceneLight light = prepared.Light;
        uint flags = prepared.Flags;
        float intensity = prepared.Type switch
        {
            SceneLightType.Direct => DirectIntensity(flags, normal, prepared.LocalLightY),
            SceneLightType.Omni => OmniIntensity(objectLocalPosition, prepared.ObjectLocalBoundsCenter,
                flags, prepared.LocalLightPosition, normal, prepared.Near, prepared.Far,
                prepared.NearSquared, prepared.FarSquared),
            SceneLightType.Spot => SpotIntensity(objectLocalPosition, flags, light,
                prepared.Near, prepared.Far, prepared.NearSquared, prepared.FarSquared,
                prepared.LocalLightPosition, prepared.LocalLightY, normal),
            _ => 0f
        };

        if (!float.IsFinite(intensity) || intensity <= 0f)
            return new(SceneLightEvaluationStatus.Applied, 0f, 0, accumulatedColor);

        uint contribution = MultiplyPackedColor(prepared.AttenuatedLightColor, intensity);
        uint accumulated = (flags & Absorb) != 0
            ? SaturatingSubtract(accumulatedColor, contribution)
            : SaturatingAdd(accumulatedColor, contribution);

        return new(SceneLightEvaluationStatus.Applied, intensity, contribution, accumulated);
    }

    private static float DirectIntensity(uint flags, Vector3 normal, Vector3 lightY)
    {
        if ((flags & Paint) != 0)
            return 1f;

        // Jaded negates the light Y axis before applying -dot(normal, direction),
        // which is equivalent to dot(normal, the original light Y axis).
        return MathF.Max(Vector3.Dot(normal, lightY), 0f);
    }

    private static float OmniIntensity(Vector3 objectLocalPosition, Vector3 objectLocalBoundsCenter,
        uint flags,
        Vector3 localLightPosition, Vector3 normal, float near, float far,
        float nearSquared, float farSquared)
    {
        if (!ValidRange(near, far))
            return 0f;

        if ((flags & OmniConstant) != 0 && (flags & Paint) == 0)
        {
            Vector3 lightVector = localLightPosition - objectLocalBoundsCenter;
            float distanceSquared = lightVector.LengthSquared();
            if (!ValidSquaredRange(nearSquared, farSquared))
                return 0f;
            if (distanceSquared > farSquared || distanceSquared < 1e-12f)
                return 0f;

            float range = distanceSquared > nearSquared
                ? (farSquared - distanceSquared) / (farSquared - nearSquared)
                : 1f;

            float lambert = Vector3.Dot(normal, Vector3.Normalize(lightVector));
            return lambert > 0f ? lambert * range : 0f;
        }

        Vector3 toVertex = objectLocalPosition - localLightPosition;
        float vertexDistanceSquared = toVertex.LengthSquared();
        if (!ValidSquaredRange(nearSquared, farSquared))
            return 0f;
        if (vertexDistanceSquared > farSquared)
            return 0f;

        float distance = MathF.Sqrt(vertexDistanceSquared);
        if ((flags & Paint) != 0)
        {
            // Both Jaded's paint branch and Iris's matching branch fade by linear
            // distance; only the ordinary diffuse branch uses squared-distance falloff.
            return distance > near ? (far - distance) / (far - near) : 1f;
        }
        if (distance < 1e-6f)
            return 0f;

        float falloff = vertexDistanceSquared > nearSquared
            ? (farSquared - vertexDistanceSquared) / (farSquared - nearSquared)
            : 1f;
        float lambertFactor = Vector3.Dot(normal, -toVertex);
        return lambertFactor > 0f ? lambertFactor / distance * falloff : 0f;
    }

    private static float SpotIntensity(Vector3 objectLocalPosition, uint flags, SceneLight light,
        float near, float far, float nearSquared, float farSquared,
        Vector3 localLightPosition, Vector3 localLightY, Vector3 normal)
    {
        if (!ValidRange(near, far))
            return 0f;

        Vector3 lightToVertex = objectLocalPosition - localLightPosition;
        float distanceSquared = lightToVertex.LengthSquared();
        if (!ValidSquaredRange(nearSquared, farSquared))
            return 0f;
        if (distanceSquared > farSquared || Vector3.Dot(lightToVertex, localLightY) > 0f)
            return 0f;

        float distance = MathF.Sqrt(distanceSquared);
        if (distance < 1e-6f)
            return 0f;

        float intensity = 1f;
        if ((flags & Paint) == 0)
        {
            float normalFactor = Vector3.Dot(normal, -lightToVertex);
            if (normalFactor <= 0f)
                return 0f;
            intensity = normalFactor / distance;
        }

        if (distanceSquared > nearSquared)
            intensity *= 1f - ((distanceSquared - nearSquared) / (farSquared - nearSquared));

        float sineInnerAngle = MathF.Sin(light.Density);
        float sineOuterAngle = MathF.Sin(light.I8);
        float sineAngle = Vector3.Cross(localLightY, lightToVertex).Length() / distance;
        if (!float.IsFinite(sineAngle) || sineAngle > sineOuterAngle)
            return 0f;

        if (sineAngle > sineInnerAngle)
        {
            float coneRange = sineOuterAngle - sineInnerAngle;
            if (MathF.Abs(coneRange) < 1e-8f)
                return 0f;
            intensity *= (sineOuterAngle - sineAngle) / coneRange;
        }

        return intensity;
    }

    private static float GetAttenuation(in SceneLightEvaluationInput input, uint flags)
    {
        bool attenuationDisabled = (input.ObjectDrawMask & DontAttenuateDrawMask) != 0
            || (flags & DontAttenuate) != 0;
        if (attenuationDisabled)
            return 1f;

        float visualFactor = (127f - input.VisualDistanceAttenuation) / 127f;
        return visualFactor * (1f - input.DisplayLightAttenuation);
    }

    private static uint SelectPackedChannels(uint color, uint flags)
    {
        if ((flags & UseColor) == 0)
            color &= 0xFF000000;
        if ((flags & UseAlpha) == 0)
            color &= 0x00FFFFFF;
        return color;
    }

    private static uint MultiplyPackedColor(uint color, float factor)
    {
        if (factor >= 0.98000002f)
            return color;
        if (factor <= 0.02f)
            return 0;

        // Iris initializes its linear lookup table with index values. The float-add trick
        // selects that table index; the table then approximates the factor in 1/32 steps.
        int tableIndex = BitConverter.SingleToInt32Bits(LinearTableBias + factor) & 0xff;
        uint result = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            uint channel = (color >> shift) & 0xff;
            uint scaledChannel = 0;
            if ((tableIndex & 0x80) != 0) scaledChannel += channel >> 1;
            if ((tableIndex & 0x40) != 0) scaledChannel += channel >> 2;
            if ((tableIndex & 0x20) != 0) scaledChannel += channel >> 3;
            if ((tableIndex & 0x10) != 0) scaledChannel += channel >> 4;
            if ((tableIndex & 0x08) != 0) scaledChannel += channel >> 5;
            result |= (scaledChannel & 0xff) << shift;
        }
        return result;
    }

    private static uint SaturatingAdd(uint first, uint second)
    {
        uint result = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            uint firstChannel = (first >> shift) & 0xff;
            uint secondChannel = (second >> shift) & 0xff;
            result |= Math.Min(firstChannel + secondChannel, 255u) << shift;
        }
        return result;
    }

    private static uint SaturatingSubtract(uint first, uint second)
    {
        uint result = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            uint firstChannel = (first >> shift) & 0xff;
            uint secondChannel = (second >> shift) & 0xff;
            result |= (firstChannel > secondChannel ? firstChannel - secondChannel : 0) << shift;
        }
        return result;
    }

    private static float MeanBasisLength(Matrix4x4 matrix)
    {
        float xScale = Vector3.TransformNormal(Vector3.UnitX, matrix).Length();
        float yScale = Vector3.TransformNormal(Vector3.UnitY, matrix).Length();
        float zScale = Vector3.TransformNormal(Vector3.UnitZ, matrix).Length();
        return (xScale + yScale + zScale) / 3f;
    }

    private static bool ValidRange(float near, float far) =>
        float.IsFinite(near) && float.IsFinite(far) && near >= 0f && far > near;

    private static bool HasFiniteInputs(in SceneLightEvaluationInput input) =>
        IsFinite(input.LightWorldMatrix) && IsFinite(input.ObjectWorldMatrix)
        && IsFinite(input.ObjectLocalPosition) && IsFinite(input.ObjectLocalNormal)
        && IsFinite(input.ObjectLocalBoundsCenter) && float.IsFinite(input.DisplayLightAttenuation)
        && input.VisualDistanceAttenuation <= 127 && input.DisplayLightAttenuation is >= 0f and <= 1f
        && float.IsFinite(input.Light.Near) && float.IsFinite(input.Light.Far)
        && float.IsFinite(input.Light.Density) && float.IsFinite(input.Light.I8);

    private static bool ValidSquaredRange(float nearSquared, float farSquared) =>
        float.IsFinite(nearSquared) && float.IsFinite(farSquared) && farSquared > nearSquared;

    private static bool IsFinite(Vector3 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    private static SceneLightEvaluationResult Skipped(SceneLightEvaluationStatus status, uint color) =>
        new(status, 0f, 0, color);
}

