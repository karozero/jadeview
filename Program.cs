using System.Text;
using System.Text.Json;

namespace JadeView;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (args.Length >= 4 && args[0] == "--extract")
        { var a = new Archive(args[1]); a.Export(a.Entries.First(e => e.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase)), args[3]); return; }
        if (args.Length >= 4 && args[0] == "--models")
        {
            var a = new Archive(args[1]); var e = a.Entries.First(e => e.Id == args[2]); var scene = Models.Read(a.Read(e), a.PathName);
            var atlas = a.Entries.FirstOrDefault(t => t.Key == (0xFF800000 | (e.Key & 0x7FFFF)));
            if (atlas != null) scene.Textures = Preview.ReadTextures(a.Read(atlas));
            var document = SceneLoader.Load(a, e);
            File.WriteAllText(args[3], JsonSerializer.Serialize(new { SceneObjects = document.Worlds.SelectMany(w => w.Objects.Select(o => new { World = w.Name, o.Name, GaoKey = o.GaoKey.ToString("X8"), GeometryKey = o.GeometryKey.ToString("X8"), MaterialKey = o.MaterialKey.ToString("X8") })), Models = scene.Models.Select(m => new { Key = m.Key.ToString("X8"), m.Name, m.Offset, Vertices = m.Positions.Length, m.Triangles, MaterialKey = m.MaterialKey.ToString("X8"), Assigned = scene.TexturesFor(m).Count(t => t != null), Parts = m.Parts.Count }), Materials = scene.Materials.Select(m => new { Key = m.Key.ToString("X8"), m.Offset, Textures = m.Textures.Select(k => k.ToString("X8")), Children = m.Children.Select(k => k.ToString("X8")) }), Diagnostics = scene.Diagnostics, Textures = scene.Textures.Select(t => t.Name) }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (args.Length >= 4 && args[0] == "--resolve")
        { ResourceResolver.RunDiagnostic(args[1], args[2], args[3]); return; }
        if (args.Length >= 3 && args[0] == "--audit-pc-scenes")
        { PcSceneLoader.Audit(args[1], args[2]); return; }
        if (args.Length >= 4 && args[0] == "--verify-pc-scene")
        { PcSceneLoader.Verify(args[1], args[2], args[3]); return; }
        if (args.Length >= 4 && args[0] == "--verify-pc-textures")
        { PcTextureLoader.Verify(args[1], args[2], args[3], args.Length > 4 ? args[4] : null); return; }
        if (args.Length >= 3 && args[0] == "--audit-pc-textures")
        { PcTextureLoader.Audit(args[1], args[2]); return; }
        if (args.Length >= 4 && args[0] == "--native-materials")
        {
            var archive = new Archive(args[1]);
            var entry = archive.Entries.First(e => e.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase));
            var assets = Models.Read(archive.Read(entry), archive.PathName);
            var row = new { id = entry.Id, Materials = assets.Materials.OrderBy(m => m.Offset)
                .Select(m => new { Offset = m.Offset, Type = m.Type, Bytes = Convert.ToBase64String(m.Bytes ?? []) }).ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
            File.WriteAllText(args[3], JsonSerializer.Serialize(new[] { row }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Exported {row.Materials.Length} ordered native material records for {entry.Id}.");
            return;
        }
        if (args.Length >= 1 && args[0] == "--test-renderer")
        {
            var results = SceneRenderer.RunVerificationTests();
            int passed = results.Count(r => r.Passed);
            Console.WriteLine($"=== Р РµР·СѓР»СЊС‚Р°С‚С‹ РїСЂРѕРІРµСЂРѕРє SceneRenderer (A4): {passed}/{results.Count} РїСЂРѕР№РґРµРЅРѕ ===");
            foreach (var r in results)
            {
                Console.WriteLine($"[{(r.Passed ? "PASS" : "FAIL")}] {r.Name}: {r.Details}");
            }
            if (passed != results.Count) Environment.ExitCode = 1;
            return;
        }
        if (args.Length >= 3 && args[0] == "--inspect")
        {
            var archive = new Archive(args[1]);
            Directory.CreateDirectory(args[2]);
            var rows = new List<object>();
            foreach (var e in archive.Entries)
            {
                try
                {
                    var bytes = archive.Read(e);
                    var p = Preview.Parse(e, bytes);
                    rows.Add(new { e.Index, e.Id, e.Name, e.Folder, e.StoredSize, e.Compressed, Size = bytes.Length, Header = Convert.ToHexString(bytes.AsSpan(0, Math.Min(48, bytes.Length))), p.Kind, p.Details, Images = p.Textures.Count, Sound = p.Audio != null, Text = p.Text != null });
                    if (e.Name.StartsWith("ff8") || e.Name.StartsWith("ff4") || e.Name.StartsWith("fd7") || e.Name.StartsWith("fe7"))
                        File.WriteAllBytes(Path.Combine(args[2], e.ExportName), bytes);
                }
                catch (Exception ex) { rows.Add(new { e.Id, e.Name, Error = ex.Message }); }
            }
            File.WriteAllText(Path.Combine(args[2], "inventory.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        try
        {
            string? path = args.Length > 0 && !args[0].StartsWith("--smoke", StringComparison.Ordinal) ? args[0] : File.Exists(App.DefaultArchive) ? App.DefaultArchive : null;
            string? smoke = args.Length >= 2 && args[0] == "--smoke" ? args[1] : args.Length >= 3 && args[0] == "--smoke-scene" ? args[2] : null;
            string? smokeScene = args.Length >= 3 && args[0] == "--smoke-scene" ? args[1] : null;
            using var app = new App(path, smoke, smokeScene); app.Run();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "JadeView-error.txt"), ex.ToString());
            System.Windows.Forms.MessageBox.Show(ex.Message, "JadeView — Startup error");
        }
    }
}
