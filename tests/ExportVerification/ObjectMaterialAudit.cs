using System.Text;
using JadeView;

internal static class ObjectMaterialAudit
{
    internal static void Run(string archivePath, string objectName, string output, string? container = null)
    {
        var archive = new Archive(archivePath);
        foreach (var entry in archive.Entries.Where(entry => container == null || entry.Id == container))
        {
            byte[] bytes = archive.Read(entry);
            if (!Encoding.Latin1.GetString(bytes).Contains(objectName.Replace(".gao", ""), StringComparison.OrdinalIgnoreCase)) continue;
            var document = SceneLoader.Load(bytes, entry.Name, archive.PathName);
            foreach (var world in document.Worlds)
            foreach (var sceneObject in world.Objects.Where(item => item.Name.Contains(objectName.Replace(".gao", ""), StringComparison.OrdinalIgnoreCase)))
            {
                var resolver = new ResourceResolver();
                resolver.LoadFixContainer(archive);
                resolver.LoadSceneContainer(archive, entry);
                Console.WriteLine($"OBJECT {sceneObject.Name} {entry.Id} {world.Name} {sceneObject.GaoKey:X8} geometry={sceneObject.RenderGeometryKey:X8} material={sceneObject.MaterialKey:X8} RLI={sceneObject.RliColors.Length}");
                var rootMaterial = resolver.Materials.GetValueOrDefault(sceneObject.MaterialKey);
                var materials = rootMaterial?.Type == 4
                    ? rootMaterial.SubMaterialKeys.Select(key => resolver.Materials.GetValueOrDefault(key))
                    : [rootMaterial];
                foreach (var material in materials.Where(material => material != null))
                {
                    Console.WriteLine($"MATERIAL {material!.Id} type={material.Type}");
                    foreach (var layer in JadeMaterialLayer.Read(material.SourceBytes))
                        Console.WriteLine($"LAYER flags={layer.Flags:X8} custom={layer.Custom:X4} uv={layer.UvMode} color={layer.ColorMode} blend={layer.Blend} texture={layer.Texture:X8} rgb={layer.WriteRgb} alpha={layer.WriteAlpha} disabled={layer.Disabled}");
                }
                var result = SceneExporter.Export(Path.Combine(output, SceneExporter.SafeFileName(objectName) + "-audit.glb"), world, resolver, new() { Textures = true }, sceneObject);
                foreach (var warning in result.Warnings) Console.WriteLine($"WARNING {warning}");
                return;
            }
        }
        throw new InvalidOperationException($"Object {objectName} was not found");
    }
}








