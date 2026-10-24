namespace JadeView;

public sealed record WorldLighting(uint AmbientColor, uint AmbientColor2)
{
    public uint SourceWorldKey { get; init; }
    // WOR_ul_World_LoadCallback treats -1 as "reuse the first ambient channel".
    public uint EffectiveAmbientColor2 => AmbientColor2 == uint.MaxValue ? AmbientColor : AmbientColor2;
}
