using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JadeView;

/// <summary>
/// Разрешённый material Jade (MultiMaterial 0x04 или MultiTexture 0x05) с полным графом связей.
/// </summary>
public sealed class ResolvedMaterial
{
    public ResourceId Id { get; init; }
    public int Type { get; init; } // 4 = MultiMaterial, 5 = MultiTexture
    public int Offset { get; init; }
    public uint Diffuse { get; init; }
    public float Opacity { get; init; } = 1.0f;
    public uint Flags { get; init; }
    public byte[]? SourceBytes { get; init; }
    public List<uint> SubMaterialKeys { get; } = [];
    public List<uint> TextureKeys { get; } = [];
    public List<ResolvedTexture?> Textures { get; } = [];
    public List<ResolvedMaterial?> SubMaterials { get; } = [];

    public override string ToString() => $"Material {Id} (Type {Type}, {SubMaterialKeys.Count} submaterials, {TextureKeys.Count} textures)";
}

