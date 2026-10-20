using System.Collections;
using System.Reflection;
using JadeView;

internal static class TextureDescriptorAudit
{
    internal static void Write(string archivePath, string container, uint textureKey)
    {
        var archive = new Archive(archivePath);
        var scene = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        uint textureContainerKey = 0xff800000 | (scene.Key & 0x0007ffff);
        byte[] bytes = archive.Read(archive.Entries.Single(entry => entry.Key == textureContainerKey));
        var loader = typeof(SceneActor).Assembly.GetType("JadeView.PcTextureLoader")!;
        var result = loader.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [archive.Read(scene), bytes])!;
        var reads = (IEnumerable)result.GetType().GetProperty("Reads")!.GetValue(result)!;
        foreach (object read in reads)
        {
            var type = read.GetType();
            if ((uint)type.GetProperty("Key")!.GetValue(read)! != textureKey) continue;
            int offset = (int)type.GetProperty("Offset")!.GetValue(read)!;
            int size = (int)type.GetProperty("Size")!.GetValue(read)!;
            Console.WriteLine($"TEXTURE {textureKey:X8} type={type.GetProperty("Type")!.GetValue(read)} offset={offset:X8} size={size}");
            Console.WriteLine(Convert.ToHexString(bytes.AsSpan(offset + 4, size)));
            if (size >= 80 && BitConverter.ToUInt16(bytes, offset + 4 + 6) % 256 == 5)
            {
                Console.WriteLine($"PROCEDURAL subtype={BitConverter.ToUInt16(bytes, offset + 4 + 42)}");
                foreach (int referenceOffset in new[] { 48, 52, 76 })
                {
                    uint reference = BitConverter.ToUInt32(bytes, offset + 4 + referenceOffset);
                    var entry = archive.Entries.FirstOrDefault(candidate => candidate.Key == reference);
                    if (entry == null) continue;
                    byte[] payload = archive.Read(entry);
                    Console.WriteLine($"REFERENCE {reference:X8} {entry.Name}: {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 40)))}");
                }
            }
        }
    }
}
