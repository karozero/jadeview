namespace JadeView;

internal sealed class ExportMeshVertices(int cornerCount)
{
    internal List<int> SourceVertices { get; } = [];
    internal List<float> Positions { get; } = [];
    internal List<float> Normals { get; } = [];
    internal List<float> Uvs { get; } = [];
    internal uint[] Indices { get; } = new uint[cornerCount];
}
