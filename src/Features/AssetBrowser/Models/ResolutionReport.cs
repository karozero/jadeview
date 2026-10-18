using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace JadeView;

/// <summary>
/// Статистика и результаты разрешения графа ресурсов сцены.
/// </summary>
public sealed class ResolutionReport
{
    public int TotalObjects { get; set; }
    public int ResolvedGeometries { get; set; }
    public int MissingGeometries { get; set; }
    public int ResolvedMaterials { get; set; }
    public int MissingMaterials { get; set; }
    public int DefaultMaterials { get; set; }
    public int ResolvedTextures { get; set; }
    public int MissingTextures { get; set; }
    public int FallbackTextures { get; set; }
    public int FixGeometriesUsed { get; set; }
    public int FixMaterialsUsed { get; set; }
    public int FixTexturesUsed { get; set; }
    public List<ResolvedObjectInfo> ResolvedObjects { get; } = [];
    public List<string> Details { get; } = [];
    public List<string> UnresolvedDetails { get; } = [];
    private readonly HashSet<string> _seenUnresolved = new(StringComparer.OrdinalIgnoreCase);

    public void AddUnresolved(string message)
    {
        if (_seenUnresolved.Add(message))
            UnresolvedDetails.Add(message);
    }

    public string FormatMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Asset resolution summary");
        sb.AppendLine($"- **Game objects (GAO):** {TotalObjects}");
        sb.AppendLine($"- **Geometry (GRO):** {ResolvedGeometries} resolved (from FIX: {FixGeometriesUsed}), {MissingGeometries} missing");
        sb.AppendLine($"- **Materials (GRM):** {ResolvedMaterials} resolved (from FIX: {FixMaterialsUsed}), {DefaultMaterials} default, {MissingMaterials} missing");
        sb.AppendLine($"- **Textures:** {ResolvedTextures} resolved (from FIX: {FixTexturesUsed}), {MissingTextures} missing, {FallbackTextures} fallback");
        
        var fully = ResolvedObjects.Where(o => o.IsFullyResolved).ToList();
        if (fully.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"#### Fully resolved scene objects ({fully.Count}):");
            foreach (var o in fully.Take(15))
            {
                string texs = string.Join(", ", o.TextureNames.Distinct());
                sb.AppendLine($"- **{o.Name}** ({o.GaoKey:X8}): {o.Vertices} vertices, {o.Triangles} triangles, {o.PartsCount} parts, textures: [{texs}]");
            }
        }

        if (UnresolvedDetails.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"#### Unresolved unique references (Missing/Fallback: {UnresolvedDetails.Count}):");
            foreach (var detail in UnresolvedDetails.Take(25))
                sb.AppendLine($"- {detail}");
            if (UnresolvedDetails.Count > 25)
                sb.AppendLine($"- ...and {UnresolvedDetails.Count - 25} unresolved references");
        }
        return sb.ToString();
    }
}

