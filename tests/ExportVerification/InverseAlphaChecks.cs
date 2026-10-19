using System.Drawing;
using System.Reflection;
using JadeView;

internal static class InverseAlphaChecks
{
    internal static int Verify(GlbReader glb, ResourceResolver resolver)
    {
        int checkedMaterials = 0;
        foreach (var material in glb.Root.GetProperty("materials").EnumerateArray())
        {
            if (!material.TryGetProperty("extras", out var extras) ||
                !extras.TryGetProperty("bakedInverseAlphaTest", out _)) continue;
            string name = material.GetProperty("name").GetString()!;
            uint key = Convert.ToUInt32(name[^8..], 16);
            var layer = JadeMaterialLayer.Read(resolver.Materials[key].SourceBytes)
                .First(layer => !layer.Disabled && layer.WriteRgb && (layer.Flags & 0x90) == 0x90);
            if (!resolver.TryResolveTexture(layer.Texture, out var texture)) throw new Exception("Missing source texture");
            var decoder = typeof(SceneExporter).Assembly.GetType("JadeView.Preview")!
                .GetMethod("DecodeTga", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var source = (byte[])decoder.Invoke(null, [texture.ToTextureAsset()])!;
            var pbr = material.GetProperty("pbrMetallicRoughness");
            int textureIndex = pbr.GetProperty("baseColorTexture").GetProperty("index").GetInt32();
            int imageIndex = glb.Root.GetProperty("textures")[textureIndex].GetProperty("source").GetInt32();
            using var stream = new MemoryStream(glb.Image(imageIndex));
            using var image = new Bitmap(stream);
            float factor = (layer.Flags & 0x400) != 0 ? layer.OverrideAlpha : 1;
            float reference = (layer.Flags >> 22 & 0xfc) / 255f;
            for (int row = 0; row < image.Height; row++)
            for (int column = 0; column < image.Width; column++)
            {
                int offset = (row * image.Width + column) * 4;
                bool sourceVisible = source[offset + 3] / 255f * factor < reference;
                bool exportedVisible = image.GetPixel(column, row).A >= 128;
                if (sourceVisible != exportedVisible) throw new Exception("Inverse alpha test was exported with the wrong comparison");
            }
            if (pbr.GetProperty("baseColorFactor")[3].GetSingle() != 1)
                throw new Exception("Baked mask alpha override was applied twice");
            checkedMaterials++;
        }
        Console.WriteLine($"PASS: {checkedMaterials} inverse alpha masks match source visibility at every texel");
        return checkedMaterials;
    }
}
