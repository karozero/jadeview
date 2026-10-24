namespace JadeView;

// Codes follow iristool/Jaded and Iris's algorithm bodies. Iris's directional
// and cone-light function identifiers are transposed; use the serialized code.
public enum SceneLightType : uint
{
    Omni = 0,
    Direct = 1,
    Spot = 2,
    Fog = 3,
    AddMaterial = 4
}

/// <summary>
/// The fixed PC light component payload, cross-checked against
/// <c>external/irisEng/graphicsobjects/light.c</c> and
/// <c>external/iristool/src/BFDataFileLib/BFChunkParsers/Map/GAOInnerParsers/GAOInnerParserLight.java</c>.
/// Flags are retained raw because most bits are not fully named or validated for retail BGE.
/// </summary>
public sealed record SceneLight(
    uint Flags,
    uint Color,
    float Near,
    float Far,
    float Density,
    float I8,
    uint LightGaoKey)
{
    public const uint NoLightGaoKey = uint.MaxValue;
    public const uint TypeMask = 0x7;

    public uint TypeCode => Flags & TypeMask;

    /// <summary>Returns only the low-bit types explicitly identified by Iris/iristool.</summary>
    public SceneLightType? KnownType => TypeCode <= (uint)SceneLightType.AddMaterial
        ? (SceneLightType)TypeCode
        : null;

    public bool HasLightGaoReference => LightGaoKey != NoLightGaoKey;
}
