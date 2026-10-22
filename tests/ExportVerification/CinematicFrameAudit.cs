using System.Numerics;
using JadeView;
using Raylib_cs;

internal static class CinematicFrameAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, float time, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == source.CinematicWorldKey));
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        foreach (uint materialRootKey in new uint[] { 0x62000F8C, 0x62000F8F })
        {
            if (!resolver.Materials.TryGetValue(materialRootKey, out var materialRoot))
            {
                Console.WriteLine($"FLASH MATERIAL: root={materialRootKey:X8} missing");
                continue;
            }

            Console.WriteLine($"FLASH MATERIAL: root={materialRootKey:X8} type={materialRoot.Type} "
                + $"children={string.Join(",", materialRoot.SubMaterialKeys.Select(key => key.ToString("X8")))}");
            if (materialRoot.SubMaterialKeys.Count <= 11) continue;
            uint materialKey = materialRoot.SubMaterialKeys[11];
            var material = resolver.Materials.GetValueOrDefault(materialKey);
            var layers = JadeMaterialLayer.Read(material?.SourceBytes);
            Console.WriteLine($"FLASH MATERIAL: sub11={materialKey:X8} type={material?.Type} "
                + string.Join(" | ", layers.Select(layer =>
                {
                    bool found = resolver.TryResolveTexture(layer.Texture, out var texture);
                    if (found && materialRootKey == 0x62000F8C && layer.Texture == 0x490198CF)
                        File.WriteAllBytes(Path.Combine(output, $"flash-texture-{layer.Texture:X8}.tga"), texture.TgaData);
                    return $"tex={layer.Texture:X8} flags={layer.Flags:X8} blend={layer.Blend} disabled={layer.Disabled} "
                        + (found ? $"resolved={texture.Id} {texture.Width}x{texture.Height} fallback={texture.IsFallback}" : "missing");
                })));
        }
        var playback = CinematicPlayback.Discover(document, world, resolver).Single(item => item.Key == clipKey);
        playback.Activate();
        try
        {
            playback.Seek(time);
            Console.WriteLine($"FLASH AUDIT: controllers={document.CinematicFlashControllers.Count}; "
                + $"active={playback.Flashes.Length}; "
                + string.Join(" | ", document.CinematicFlashControllers.Values.Select(controller =>
                    $"{controller.OwnerKey:X8} rays={controller.RayCount} caps={controller.InitialCapabilities:X4} "
                    + $"alpha={controller.BigAlpha:F2}/{controller.SmallAlpha:F2} lengths={controller.BigLengthMin:F1}-{controller.BigLengthMax:F1} "
                    + $"widths={controller.BigWidthMin:F1}-{controller.BigWidthMax:F1}/{controller.SmallWidthMin:F1}-{controller.SmallWidthMax:F1} "
                    + $"ratio={controller.LengthRatio:F2} extrude={controller.ExtrudeDistance:F1} "
                    + $"appear={controller.AppearDelay:F3} disappear={controller.DisappearDelay:F3} color={controller.Color:X8}")));
            Console.WriteLine($"LIGHTNING AUDIT: controllers={document.CinematicLightningControllers.Count}; "
                + $"active={playback.Lightnings.Length}; "
                + string.Join(" | ", document.CinematicLightningControllers.Values.Select(controller =>
                    $"{controller.OwnerKey:X8} -> {controller.DestinationKey:X8} mode={controller.Mode} "
                    + $"capa={controller.CapabilityActorKey:X8}/{controller.CapabilityMask:X} "
                    + $"segments={controller.SegmentCount} delays={controller.OnDelayMin:F2}-{controller.OnDelayMax:F2}/"
                    + $"{controller.OffDelayMin:F2}-{controller.OffDelayMax:F2} fade={controller.FadeIn:F2}/{controller.FadeOut:F2} "
                    + $"width={controller.Width:F2} radius={controller.Radius:F2} colors={controller.OffColor:X8}/"
                    + $"{controller.OnColorFirst:X8}/{controller.OnColorSecond:X8}")));
            foreach (var lightning in playback.Lightnings)
                Console.WriteLine($"LIGHTNING STATE: {lightning.Seed:X8} {lightning.Start} -> {lightning.End} "
                    + $"color={lightning.Color:X8} opacity={lightning.Opacity:F2} width={lightning.Width:F2} radius={lightning.Radius:F2}");
            foreach (string diagnostic in document.Diagnostics.Where(message => message.StartsWith("Cinematic lightning", StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"LIGHTNING DIAGNOSTIC: {diagnostic}");
            foreach (string diagnostic in document.Diagnostics.Where(message => message.StartsWith("Cinematic flash", StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"FLASH DIAGNOSTIC: {diagnostic}");
            Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
            Raylib.InitWindow(960, 540, "Cinematic frame audit");
            try { Render(); }
            finally { Raylib.CloseWindow(); }
        }
        finally { playback.Deactivate(); }

        void Render()
        {
            using var renderer = new SceneRenderer { GameView = true, ShowGrid = false, ShowGizmo = false };
            var pose = playback.Camera?.RenderPose(time)
                ?? throw new InvalidDataException("Cinematic has no source camera.");
            Matrix4x4.Decompose(pose, out _, out var rotation, out var position);
            renderer.Camera.Position = position;
            renderer.Camera.Target = position + Vector3.Transform(-Vector3.UnitZ, rotation);
            renderer.Camera.Up = Vector3.Transform(Vector3.UnitY, rotation);
            renderer.CinematicShutter = playback.Shutter;
            renderer.CinematicFlashes = playback.Flashes;
            renderer.CinematicLightnings = playback.Lightnings;
            renderer.CinematicScreenFade = playback.ScreenFade;
            renderer.CinematicMaterials = playback.MaterialState;
            renderer.CinematicHorizontalFov = playback.Camera?.Lens?.Evaluate(time);
            renderer.Render(960, 540, document, world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            string timestamp = time.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(output, $"{container}-{clipKey:X8}-{timestamp}-frame.png");
            try
            {
                Raylib.ImageFlipVertical(ref image);
                if (!Raylib.ExportImage(image, path)) throw new IOException("Could not save cinematic frame.");
            }
            finally { Raylib.UnloadImage(image); }
            Console.WriteLine($"CINEMATIC FRAME: {path}; shutter={playback.Shutter?.Closure}; objects={renderer.StatVisibleObjects}");
        }
    }
}
