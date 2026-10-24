using System.Numerics;
using System.Reflection;

namespace JadeView;

/// <summary>
/// GLB-level checks for the frozen current-pose diffuse-light snapshot. This does not
/// expect animated light changes to become glTF animation channels.
/// </summary>
internal static class DynamicLightExportChecks
{
    private const uint ReceiverKey = 0xA101;
    private const uint LightKey = 0xA102;
    private const uint GeometryKey = 0xA103;
    private const uint ComputedMaterialKey = 0xA104;
    private const uint RawMaterialKey = 0xA105;
    private const uint RawPaint = 0xFF204060;
    private const uint ExpectedLitPaint = 0xFF2040FF;

    internal static void Verify(string output)
    {
        var resolver = new ResourceResolver();
        var geometry = CreateGeometry();
        Register(resolver, "_geometries", GeometryKey, geometry);
        Register(resolver, "_materials", ComputedMaterialKey, CreateMaterial(0));
        Register(resolver, "_materials", RawMaterialKey, CreateMaterial(3));

        var receiver = new SceneObject
        {
            GaoKey = ReceiverKey,
            Name = "Computed direct-light receiver",
            GeometryKey = GeometryKey,
            MaterialKey = ComputedMaterialKey,
            Flags = 0x00004000,
            DrawFlags = 0x01000008 | 0x08000000 | 0x00000040,
            Transform = new() { Translation = new(8, 2, -4) }
        };
        var rawReceiver = new SceneObject
        {
            GaoKey = 0xA106,
            Name = "Raw mode-three receiver",
            GeometryKey = GeometryKey,
            MaterialKey = RawMaterialKey,
            Flags = 0x00004000,
            DrawFlags = 0x01000008 | 0x08000000 | 0x00000040,
            Transform = new() { Translation = new(8, 2, -4) }
        };
        var lightOwner = new SceneObject
        {
            GaoKey = LightKey,
            Name = "Static direct light",
            Flags = 0x10,
            LightResourceKey = 0xA107,
            Transform = new()
            {
                Translation = new(8, 2, -4),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)
            },
            Light = new SceneLight(0x200 | 0x8 | 0x20 | (uint)SceneLightType.Direct,
                0xFF0000FF, 0, 100, 0, 0, SceneLight.NoLightGaoKey)
        };
        var world = new WorldData { Name = "Dynamic light export snapshot" };
        world.Objects.AddRange([receiver, rawReceiver, lightOwner]);
        // A loaded actor member supplies hierarchy context, but is not automatically
        // a light in this selected world's ordered light list.
        var outsideLight = new SceneObject
        {
            GaoKey = 0xA108,
            Flags = lightOwner.Flags,
            LightResourceKey = 0xA109,
            Transform = lightOwner.Transform,
            Light = lightOwner.Light with { Color = 0xFFFFFFFF }
        };
        var contextActor = new SceneActor { Root = outsideLight };
        contextActor.Members.Add(outsideLight);
        world.Actors.Add(contextActor);

        Vector3[] sourcePositions = (Vector3[])geometry.Positions.Clone();
        uint[] sourceColors = (uint[])geometry.VertexColors.Clone();
        Matrix4x4 receiverMatrix = AnimationMath.Trs(receiver.Transform);
        Matrix4x4 rawReceiverMatrix = AnimationMath.Trs(rawReceiver.Transform);
        Matrix4x4 lightMatrix = AnimationMath.Trs(lightOwner.Transform);
        SceneLight sourceLight = lightOwner.Light!;

        string currentPosePath = Path.Combine(output, "dynamic-light-current-pose.glb");
        SceneExporter.Export(currentPosePath, world, resolver, new() { Textures = false });
        using (var currentPose = new GlbReader(currentPosePath))
        {
            CheckColors(currentPose, receiver.Name, [ExpectedLitPaint, ExpectedLitPaint, ExpectedLitPaint],
                "Mode 0 COLOR_0 must combine raw paint with saturated direct-light red at the captured pose.");
            CheckColors(currentPose, rawReceiver.Name, [RawPaint, RawPaint, RawPaint],
                "Mode 3 must preserve raw COLOR_0 even when the receiver has an active light.");
        }

        var lightTimeline = new CinematicLightStateTimeline(world.Objects,
            [(1f, new CinematicLiteralCall(LightKey, 0x213B, [3, 0]))]);
        string disabledLightPath = Path.Combine(output, "dynamic-light-disabled-state.glb");
        var disabledResult = SceneExporter.Export(disabledLightPath, world, resolver, new()
        {
            Textures = false,
            CinematicLights = lightTimeline.Evaluate(2f)
        });
        using (var disabledLight = new GlbReader(disabledLightPath))
            CheckColors(disabledLight, receiver.Name, [RawPaint, RawPaint, RawPaint],
                "An explicit cinematic LightSetFlag-off snapshot must leave mode 0 at its raw baseline.");
        Check(!disabledResult.Warnings.Any(warning => warning.StartsWith("Diffuse lighting is incomplete:")),
            "An inactive source light is normal filtering, not an unsupported-export warning.");

        lightOwner.Light = sourceLight with { Flags = (sourceLight.Flags & ~SceneLight.TypeMask) | 7 };
        var unsupportedResult = SceneExporter.Export(Path.Combine(output, "dynamic-light-unsupported.glb"),
            world, resolver, new() { Textures = false });
        Check(unsupportedResult.Warnings.Count(warning => warning.StartsWith("Diffuse lighting is incomplete:")) == 1,
            "Unsupported source light types must produce one deduplicated warning even without a modeled contribution.");
        lightOwner.Light = sourceLight;
        string unbakedPath = Path.Combine(output, "dynamic-light-bake-disabled.glb");
        SceneExporter.Export(unbakedPath, world, resolver, new() { Textures = false, DynamicLighting = false });
        using (var unbaked = new GlbReader(unbakedPath))
            CheckColors(unbaked, receiver.Name, [RawPaint, RawPaint, RawPaint],
                "Disabling diffuse baking must retain authored RLI without dynamic contributions.");

        string originPlacementPath = Path.Combine(output, "dynamic-light-at-origin.glb");
        SceneExporter.Export(originPlacementPath, world, resolver, new()
        {
            Textures = false,
            Placement = ExportPlacement.AtOrigin
        });
        using (var originPlacement = new GlbReader(originPlacementPath))
            CheckColors(originPlacement, receiver.Name, [ExpectedLitPaint, ExpectedLitPaint, ExpectedLitPaint],
                "Moving the exported scene origin must not change receiver/light-relative diffuse color.");

        Check(geometry.Positions.SequenceEqual(sourcePositions), "Diffuse export mutated shared source positions.");
        Check(geometry.VertexColors.SequenceEqual(sourceColors), "Diffuse export mutated shared source vertex colors.");
        Check(AnimationMath.Trs(receiver.Transform) == receiverMatrix
            && AnimationMath.Trs(rawReceiver.Transform) == rawReceiverMatrix
            && AnimationMath.Trs(lightOwner.Transform) == lightMatrix,
            "Diffuse export changed a source receiver or light-owner transform.");
        Check(lightOwner.Light == sourceLight, "A cinematic light-state snapshot mutated the source light component.");

        Console.WriteLine("PASS: GLB diffuse-light snapshot, raw mode isolation, scripted light-off state, origin placement, and source immutability");
    }

    private static Geometry CreateGeometry() => new()
    {
        Key = GeometryKey,
        // This winding produces a -Y face normal; the source direct light points along -Y.
        Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
        VertexColors = [RawPaint, RawPaint, RawPaint],
        Parts = [new(0, [0, 1, 2], [])]
    };

    private static ResolvedMaterial CreateMaterial(int colorMode)
    {
        byte[] sourceBytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(sourceBytes, 0);
        BitConverter.GetBytes((uint)colorMode << 12 | 0x7u).CopyTo(sourceBytes, 40);
        return new ResolvedMaterial { Type = 5, SourceBytes = sourceBytes };
    }

    private static void CheckColors(GlbReader glb, string meshName, uint[] expectedColors, string message)
    {
        var mesh = glb.Root.GetProperty("meshes").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == meshName);
        var primitive = mesh.GetProperty("primitives")[0];
        var colors = glb.Accessor(primitive.GetProperty("attributes").GetProperty("COLOR_0").GetInt32());
        Check(colors.Length == expectedColors.Length,
            $"{message} Expected {expectedColors.Length} vertices, got {colors.Length}.");
        for (int vertex = 0; vertex < colors.Length; vertex++)
        {
            float[] expected = ToVector(expectedColors[vertex]);
            Check(colors[vertex].Zip(expected).All(pair => MathF.Abs(pair.First - pair.Second) < .0001f),
                $"{message} Vertex {vertex}: expected {expectedColors[vertex]:X8}, got {Pack(colors[vertex]):X8}.");
        }
    }

    private static float[] ToVector(uint packed) =>
        [(packed & 255) / 255f, (packed >> 8 & 255) / 255f,
            (packed >> 16 & 255) / 255f, (packed >> 24) / 255f];

    private static uint Pack(float[] color) =>
        (uint)Math.Clamp((int)MathF.Round(color[0] * 255), 0, 255)
        | (uint)Math.Clamp((int)MathF.Round(color[1] * 255), 0, 255) << 8
        | (uint)Math.Clamp((int)MathF.Round(color[2] * 255), 0, 255) << 16
        | (uint)Math.Clamp((int)MathF.Round(color[3] * 255), 0, 255) << 24;

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidDataException(message);
    }
}
