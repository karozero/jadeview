using System.Numerics;
using System.Globalization;
using System.Text;

namespace JadeView;

public sealed class SceneAssets
{
    public List<Geometry> Models = [];
    public List<JadeMaterial> Materials = [];
    public List<TextureAsset> Textures = [];
    public Dictionary<uint, uint> ModelMaterials = [];
    public List<string> Diagnostics = [];
    public int DuplicateCount;
    public ResourceResolver? Resolver { get; set; }
    public JadeMaterial? MaterialFor(Geometry model, int part)
    {
        var m = Materials.FirstOrDefault(m => m.Key == model.MaterialKey);
        int id = model.Parts[part].MaterialId;
        if (m?.Children.Length > 0 && id >= 0 && m.Children.Length > 0)
            m = Materials.FirstOrDefault(x => x.Key == m.Children[Math.Min(id, m.Children.Length - 1)]);
        return m;
    }
    public List<TextureAsset?> TexturesFor(Geometry model)
    {
        if (Resolver != null)
            return Resolver.ResolveTexturesForGeometry(model, model.MaterialKey, useFallback: false);
        var material = Materials.FirstOrDefault(m => m.Key == model.MaterialKey);
        var result = new List<TextureAsset?>();
        for (int i = 0; i < model.Parts.Count; i++)
        {
            var m = MaterialFor(model, i);
            uint key = m?.Textures.FirstOrDefault() ?? 0;
            // Original PC BGE allocates .raw and its .tex wrapper consecutively;
            // FF8 links name the raw key, whereas material layers reference the wrapper.
            result.Add(Textures.FirstOrDefault(t => t.Name == key.ToString("X8")) ??
                (key > 0 ? Textures.FirstOrDefault(t => t.Name == (key - 1).ToString("X8")) : null));
        }
        return result;
    }
}

