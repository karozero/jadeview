using System.Numerics;
using JadeView;
using Raylib_cs;

internal static class CinematicFrameAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, float time, string output,
        string? focusObjectName = null)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        ScriptFunctionMetadataAudit.Write(document);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == source.CinematicWorldKey));
        Console.WriteLine($"ENVIRONMENT AUDIT: sourceWorld={source.CinematicWorldKey:X8} "
            + $"controllers={document.CinematicEnvironmentControllers.Count} assembledObjects={world.Objects.Count} "
            + $"ambientSource={world.Lighting?.SourceWorldKey:X8} ambient={world.Lighting?.AmbientColor:X8}/{world.Lighting?.EffectiveAmbientColor2:X8}");
        var lightObjects = world.Objects.Where(item => item.Light != null).ToArray();
        Console.WriteLine($"LIGHT INVENTORY: decodedComponents={lightObjects.Length}; "
            + $"unresolvedReferences={world.Objects.Count(item => item.LightResourceKey is not (0 or uint.MaxValue) && item.Light == null)}");
        foreach (var lightObject in lightObjects)
        {
            var light = lightObject.Light!;
            Console.WriteLine($"LIGHT COMPONENT: {lightObject.Name} ({lightObject.GaoKey:X8}) resource={lightObject.LightResourceKey:X8} "
                + $"type={light.KnownType?.ToString() ?? light.TypeCode.ToString()} flags={light.Flags:X8} color={light.Color:X8} "
                + $"near={light.Near:G} far={light.Far:G} density={light.Density:G} i8={light.I8:G} ownerRef={light.LightGaoKey:X8}");
        }
        foreach (var controller in document.CinematicEnvironmentControllers.Values)
        {
            var owner = world.Objects.FirstOrDefault(item => item.GaoKey == controller.OwnerKey);
            var sun = world.Objects.FirstOrDefault(item => item.GaoKey == controller.SunKey);
            string paletteSkyColors = string.Join(",", controller.NormalPalettes
                .Where(palette => palette.Length > 4).Select(palette => $"{palette[4]:X8}"));
            Console.WriteLine($"ENVIRONMENT AUDIT: owner={controller.OwnerKey:X8} ownerPresent={owner != null} "
                + $"ownerControl={owner?.ControlFlags:X8} env={controller.EnvironmentKey:X8} "
                + $"sky={controller.SkyKey:X8} sun={controller.SunKey:X8} "
                + $"sunPos={sun?.Transform.Translation} preview={sun?.PreviewMatrix?.M43} "
                + $"thresholds={controller.NightHeight:F2}/{controller.DawnHeight:F2}/{controller.SunriseHeight:F2}/{controller.DayHeight:F2} "
                + $"skyColors={paletteSkyColors}");
            foreach (uint key in new[] { controller.EnvironmentKey, controller.SkyKey, controller.SunKey })
                foreach (var sceneObject in world.Objects.Where(item => item.GaoKey == key))
                    Console.WriteLine($"ENVIRONMENT OBJECT: {key:X8} {sceneObject.Name} flags={sceneObject.DrawFlags:X8} "
                        + $"control={sceneObject.ControlFlags:X8} geo={sceneObject.RenderGeometryKey:X8} mat={sceneObject.MaterialKey:X8}");
        }
        foreach (var (sourceWorld, sceneObject) in document.Worlds.SelectMany(sourceWorld =>
            sourceWorld.Objects.Select(sceneObject => (sourceWorld, sceneObject)))
            .Where(pair => pair.sceneObject.Name.Contains("sky", StringComparison.OrdinalIgnoreCase)
                || pair.sceneObject.Name.Contains("ciel", StringComparison.OrdinalIgnoreCase)
                || pair.sceneObject.Name.Contains("soleil", StringComparison.OrdinalIgnoreCase)
                || pair.sceneObject.Name.Contains("sun", StringComparison.OrdinalIgnoreCase)
                || pair.sceneObject.Name.Contains("env", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"ENVIRONMENT CANDIDATE: world={sourceWorld.Key:X8} key={sceneObject.GaoKey:X8} "
                + $"{sceneObject.Name} flags={sceneObject.DrawFlags:X8} control={sceneObject.ControlFlags:X8} "
                + $"geo={sceneObject.RenderGeometryKey:X8} mat={sceneObject.MaterialKey:X8}");
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
        int directMaterialCommands = playback.ScriptCalls.Count(item => item.Call.Selector is 15006 or 15008 or 15010);
        int allMaterialCommands = playback.ScriptStateCalls.Count(item => item.Call.Selector is 15006 or 15008 or 15010);
        Console.WriteLine($"MATERIAL COMMAND INVENTORY: direct={directMaterialCommands}; scheduledTotal={allMaterialCommands}");
        playback.Activate();
        try
        {
            foreach (string fadeEvent in playback.ScreenFadeEvents)
                Console.WriteLine($"SCREEN FADE EVENT: {fadeEvent}");
            playback.Seek(time);
            CinematicScriptHierarchyCommand[] hierarchyCommands = playback.ScriptStateCalls
                .Select(item => CinematicScriptHierarchyCommand.Decode(item.Time, item.Call))
                .OfType<CinematicScriptHierarchyCommand>()
                .ToArray();
            Console.WriteLine($"SCRIPT HIERARCHY INVENTORY: literal Set/Reset commands={hierarchyCommands.Length}; "
                + $"commands at playhead={hierarchyCommands.Count(command => command.Time <= time)}");
            foreach (CinematicScriptHierarchyBinding binding in playback.ScriptHierarchyBindingsAtPlayhead(world))
            {
                SceneObject? child = binding.ChildEndpointCount == 1
                    ? world.Objects.FirstOrDefault(item => item.GaoKey == binding.ChildKey) : null;
                string childPose = child is null ? "unresolved" : child.RenderMatrix.Translation.ToString();
                string parentPose = binding.ParentKey is uint parentKey
                    ? binding.ParentEndpointCount == 1
                        ? world.Objects.FirstOrDefault(item => item.GaoKey == parentKey)?.RenderMatrix.Translation.ToString() ?? "unresolved"
                        : "ambiguous"
                    : "detached";
                Console.WriteLine($"SCRIPT HIERARCHY: child={binding.ChildKey:X8} relation={binding.Status} "
                    + $"pose={binding.PoseStatus} parent={binding.ParentKey?.ToString("X8") ?? "none"} "
                    + $"childWorld={childPose} parentWorld={parentPose} changedAt={binding.ChangedAt?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "serialized"}");
            }
            Console.WriteLine($"CAMERA ROUTE INVENTORY: selectedIndex={playback.CameraIndex}; bindings={playback.Cameras.Length}");
            foreach (var binding in playback.Cameras)
                Console.WriteLine($"CAMERA BINDING: receiver={binding.ReceiverKey:X8}; source={binding.Source.GaoKey:X8} {binding.Source.Name}; "
                    + $"target={binding.Target?.GaoKey:X8}; routed={binding.FromSourceEvent}; position={binding.RenderPose(time).Translation}");
            foreach (var command in CinematicCameraEvent.Read(source))
                Console.WriteLine($"CAMERA COMMAND: time={command.Time:G6}; receiver={command.ReceiverKey:X8}; "
                    + $"position={command.PositionKey:X8}; target={command.TargetKey:X8}; mode={command.Mode}; transition={command.Transition:G6}; "
                    + $"positionOffset={command.PositionOffset}; targetOffset={command.TargetOffset}; literal={command.HasLiteralPosition}");
            var cameraKeys = CinematicCameraEvent.Read(source)
                .SelectMany(command => new[] { command.PositionKey, command.TargetKey, command.ReceiverKey })
                .Where(key => key > 2 && key < uint.MaxValue).Distinct();
            foreach (uint cameraKey in cameraKeys)
            {
                var matches = world.Objects.Where(owner => owner.GaoKey == cameraKey).ToArray();
                foreach (var owner in matches)
                    Console.WriteLine($"CAMERA OBJECT: key={cameraKey:X8}; duplicates={matches.Length}; name={owner.Name}; "
                        + $"parent={owner.ParentId}; flags={owner.Flags:X8}; record={owner.RecordRange}; actorRoot={owner.Actor?.Root.GaoKey:X8}; "
                        + $"initial={owner.InitialTransform.Translation}; authored={owner.Transform.Translation}; preview={owner.PreviewMatrix?.Translation}");
            }
            var lightState = playback.LightState;
            Console.WriteLine($"LIGHT COMMAND INVENTORY: lights={lightState.Lights.Count}; "
                + string.Join("; ", lightState.Diagnostics.GroupBy(item => item.Status)
                    .Select(group => $"{group.Key}={group.Count()}")));
            var cameraPagControllers = CinematicCameraPagControllerReader.Read(
                document.CinematicScriptOwners.Values, world);
            Console.WriteLine($"CAMERA PAG AUDIT: controllers={cameraPagControllers.Length}");
            foreach (var controller in cameraPagControllers)
            {
                uint capabilities = controller.Owner.PreviewCapabilities
                    ?? controller.Owner.InitialCapabilities;
                Console.WriteLine($"CAMERA PAG OWNER: {controller.Owner.Name} "
                    + $"({controller.Owner.GaoKey:X8}) cap={capabilities:X4} "
                    + $"previewCamera={playback.Camera?.Source.GaoKey.ToString("X8") ?? "none"} "
                    + $"pag1={DescribePag(controller.FirstEffect)} "
                    + $"pag2={DescribePag(controller.SecondEffect)}");
            }
            var activeEnvironment = document.CinematicEnvironmentControllers.Values.FirstOrDefault(controller =>
                world.Objects.Any(item => item.GaoKey == controller.OwnerKey
                    && (item.ControlFlags & CinematicObjectState.Inactive) == 0)
                && world.Objects.Any(item => item.GaoKey == controller.SkyKey));
            uint skyKey = activeEnvironment?.SkyKey ?? 0;
            var skyObject = world.Objects.FirstOrDefault(item => item.GaoKey == skyKey);
            if (skyObject != null)
                Console.WriteLine($"SKY STATE: hidden={skyObject.PreviewHidden} controlHidden={skyObject.IsControlHidden} "
                    + $"excluded={skyObject.CinematicExcluded} position={skyObject.Transform.Translation} "
                    + $"preview={skyObject.PreviewMatrix} paletteColor={playback.MaterialState.ApplyConstant(skyObject.GaoKey, 0, Vector4.One)}");
            Console.WriteLine($"FLASH AUDIT: controllers={document.CinematicFlashControllers.Count}; "
                + $"active={playback.Flashes.Count}; "
                + string.Join(" | ", document.CinematicFlashControllers.Values.Select(controller =>
                    $"{controller.OwnerKey:X8} rays={controller.RayCount} caps={controller.InitialCapabilities:X4} "
                    + $"alpha={controller.BigAlpha:F2}/{controller.SmallAlpha:F2} lengths={controller.BigLengthMin:F1}-{controller.BigLengthMax:F1} "
                    + $"widths={controller.BigWidthMin:F1}-{controller.BigWidthMax:F1}/{controller.SmallWidthMin:F1}-{controller.SmallWidthMax:F1} "
                    + $"ratio={controller.LengthRatio:F2} extrude={controller.ExtrudeDistance:F1} "
                    + $"appear={controller.AppearDelay:F3} disappear={controller.DisappearDelay:F3} color={controller.Color:X8}")));
            Console.WriteLine($"LIGHTNING AUDIT: controllers={document.CinematicLightningControllers.Count}; "
                + $"active={playback.Lightnings.Count}; "
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
            renderer.CinematicOffsetEffect = playback.OffsetEffect;
            renderer.CinematicColorBalance = playback.ColorBalance;
            renderer.CinematicMaterials = playback.MaterialState;
            renderer.CinematicLights = playback.LightState;
            renderer.VideoArchive = archive;
            renderer.CinematicVideoPlayback = playback.VideoPlayback;
            renderer.CinematicVideoTime = time;
            renderer.CinematicHorizontalFov = playback.Camera?.Lens?.Evaluate(time);
            WorldData renderWorld = world;
            if (focusObjectName != null)
            {
                var focus = world.Objects.SingleOrDefault(item => item.Name.Equals(focusObjectName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Focus object '{focusObjectName}' is absent or ambiguous in the assembled scene.");
                // Material inspection is independent of source activation at this timestamp.
                renderer.ShowGameHidden = true;
                renderer.FrameObject(focus, resolver);
                // Framing an actor evaluates its rest frame for bounds; restore source time.
                playback.Seek(time);
                renderer.CinematicHorizontalFov = null;
                renderWorld = new WorldData { Key = world.Key, Name = world.Name, Lighting = world.Lighting };
                renderWorld.Objects.AddRange(focus.Actor?.Members ?? [focus]);
                renderWorld.Objects.AddRange(world.Objects.Where(item => (item.Flags & 0x10) != 0
                    && !renderWorld.Objects.Contains(item)));
                if (focus.Actor != null) renderWorld.Actors.Add(focus.Actor);
                Console.WriteLine($"FOCUS OBJECT: {focus.Name} ({focus.GaoKey:X8}) draw={focus.DrawFlags:X8}; visibility=inspection override");
            }
            renderer.Render(960, 540, document, renderWorld, null, resolver);
            var displayTarget = (RenderTexture2D)(typeof(SceneRenderer)
                .GetMethod("RenderCinematicAfterEffects", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.Invoke(renderer, null)!);
            var image = Raylib.LoadImageFromTexture(displayTarget.Texture);
            string timestamp = time.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            string focusSuffix = focusObjectName == null ? "" : "-" + SceneExporter.SafeFileName(focusObjectName);
            string path = Path.Combine(output, $"{container}-{clipKey:X8}-{timestamp}{focusSuffix}-frame.png");
            try
            {
                Raylib.ImageFlipVertical(ref image);
                if (!Raylib.ExportImage(image, path)) throw new IOException("Could not save cinematic frame.");
            }
            finally { Raylib.UnloadImage(image); }
            Console.WriteLine($"CINEMATIC FRAME: {path}; shutter={playback.Shutter?.Closure}; "
                + $"screenFade={playback.ScreenFade?.ToString() ?? "none"}; "
                + $"colorBalance={playback.ColorBalance:F3}; objects={renderer.StatVisibleObjects}; "
                + $"diffuseUpdates={renderer.StatDiffuseLightingUpdates}; diffusePreparations={renderer.StatDiffuseLightingPreparations}");
            foreach (var state in playback.LightState.Lights.Where(state => state.Light != state.Receiver.Light))
                Console.WriteLine($"CINEMATIC LIGHT: {state.Receiver.GaoKey:X8} {state.Receiver.Name}; "
                    + $"flags={state.Light.Flags:X8}; color={state.Light.Color:X8}; near={state.Light.Near:G6}; far={state.Light.Far:G6}");
            foreach (var diagnostic in playback.LightState.WorldCycleDiagnostics)
                Console.WriteLine($"CINEMATIC WORLD-CYCLE: {diagnostic.ControllerKey:X8}/{diagnostic.ReceiverKey:X8}; "
                    + $"{diagnostic.Code}; {diagnostic.Field}; {diagnostic.Detail}");
        }

        static string DescribePag(SceneObject? effect)
        {
            if (effect == null) return "unresolved";
            float? frequency = effect.CinematicParticles?.Values.Length > 4
                ? effect.CinematicParticles.Values[4] : null;
            string position = (effect.PreviewMatrix ?? AnimationMath.Trs(effect.Transform))
                .Translation.ToString();
            return $"{effect.Name}({effect.GaoKey:X8}) hidden={effect.IsControlHidden} "
                + $"freq={frequency?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"} "
                + $"pos={position}";
        }
    }
}
