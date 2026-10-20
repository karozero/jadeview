using System.Text.Json;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private Obj CreateDocument(string name)
    {
        var document = D("asset", D("version", "2.0", "generator", "JadeView"),
            "scene", 0, "scenes", new[] { D("name", name, "nodes", roots) }, "nodes", nodes);
        if (meshes.Count > 0)
        {
            document["meshes"] = meshes;
            document["materials"] = materials;
        }
        var extensions = new List<string>();
        if (meshes.Count > 0) extensions.Add("KHR_materials_unlit");
        if (animatedCameraLens) extensions.Add(CameraLensExtension);
        if (extensions.Count > 0) document["extensionsUsed"] = extensions;
        if (images.Count > 0)
        {
            document["images"] = images;
            document["textures"] = textures;
            document["samplers"] = samplers;
        }
        if (skins.Count > 0) document["skins"] = skins;
        if (cameras.Count > 0) document["cameras"] = cameras;
        if (animations.Count > 0) document["animations"] = animations;
        if (binary.Length > 0)
        {
            document["buffers"] = new[] { D("byteLength", (int)binary.Length) };
            document["bufferViews"] = views;
            document["accessors"] = accessors;
        }
        if (warnings.Count > 0) document["extras"] = D("exportWarnings", warnings.ToArray());
        return document;
    }

    private void Write(string path, string name)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(CreateDocument(name));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new BinaryWriter(File.Create(temporary)))
                WriteChunks(output, json);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void WriteChunks(BinaryWriter output, byte[] json)
    {
        int jsonLength = AlignedLength(json.Length);
        int binaryLength = AlignedLength((int)binary.Length);
        int totalLength = GlbFormat.HeaderSize + GlbFormat.ChunkHeaderSize + jsonLength;
        if (binaryLength > 0) totalLength += GlbFormat.ChunkHeaderSize + binaryLength;
        output.Write(GlbFormat.Magic);
        output.Write(GlbFormat.Version);
        output.Write((uint)totalLength);
        output.Write(jsonLength);
        output.Write(GlbFormat.JsonChunk);
        output.Write(json);
        WritePadding(output, jsonLength - json.Length, GlbFormat.JsonPadding);
        if (binaryLength == 0) return;
        output.Write(binaryLength);
        output.Write(GlbFormat.BinaryChunk);
        binary.Position = 0;
        binary.CopyTo(output.BaseStream);
        WritePadding(output, binaryLength - binary.Length, 0);
    }

    private static int AlignedLength(int length) =>
        (length + GlbFormat.Alignment - 1) & ~(GlbFormat.Alignment - 1);

    private static void WritePadding(BinaryWriter output, long count, byte value)
    {
        for (long index = 0; index < count; index++) output.Write(value);
    }
}
