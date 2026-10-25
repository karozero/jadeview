using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JadeView;

/// <summary>
/// Resolved Jade material resource with its source payload and dependency graph.
/// </summary>
public sealed class ResolvedMaterial
{
    public ResourceId Id { get; init; }
    public int Type { get; init; } // 3 = single, 4 = MultiMaterial, 5 = MultiTexture
    public int Offset { get; init; }
    public uint Diffuse { get; init; }
    /// <summary>Serialized ul_Specular color for single and multi-texture materials.</summary>
    // Serialized GRO type is followed by ambient, diffuse, then specular.
    // Offset 16 is the float specular exponent, not the packed constant color.
    public uint? Specular => Type is 3 or 5 && SourceBytes is { Length: >= 16 } bytes
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, sizeof(uint)))
        : null;
    public float Opacity { get; init; } = 1.0f;
    public uint Flags { get; init; }
    public byte[]? SourceBytes { get; init; }
    public List<uint> SubMaterialKeys { get; } = [];
    public List<uint> TextureKeys { get; } = [];
    public List<ResolvedTexture?> Textures { get; } = [];
    public List<ResolvedMaterial?> SubMaterials { get; } = [];

    public override string ToString() => $"Material {Id} (Type {Type}, {SubMaterialKeys.Count} submaterials, {TextureKeys.Count} textures)";
}

