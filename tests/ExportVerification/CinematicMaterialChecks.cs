using JadeView;

internal static class CinematicMaterialChecks
{
    internal static void Verify()
    {
        const uint materialKey = 0x47000001, rootKey = 0x47000002;
        byte[] bytes = new byte[76];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        for (int level = 0; level < 2; level++)
        {
            BitConverter.GetBytes((ushort)0xf860).CopyTo(bytes, 38 + level * 20);
            BitConverter.GetBytes(0x00010c00u).CopyTo(bytes, 40 + level * 20);
        }
        var materials = new Dictionary<uint, ResolvedMaterial>
        {
            [materialKey] = new() { Id = new(materialKey), Type = 5, SourceBytes = bytes },
            [rootKey] = new() { Id = new(rootKey), Type = 4 }
        };
        materials[rootKey].SubMaterialKeys.Add(materialKey);
        var world = new WorldData();
        world.Objects.Add(new() { GaoKey = 7, MaterialKey = rootKey });
        var clip = new AnimationClip
        {
            Tracks = [new() { Target = 7, Bone = -1, Keys =
            [CinematicTestEvents.Call(1, 7, 15006, materialKey, 1, 1),
             CinematicTestEvents.Call(2, 7, 15008, 3, 1, 101), // Native modulo submaterial lookup.
             CinematicTestEvents.Call(3, 7, 15010, materialKey, 1, 255),
             CinematicTestEvents.Call(4, 7, 15006, materialKey, 1, 0)] }]
        };
        var state = new CinematicMaterialState(clip, world, materials);
        var original = JadeMaterialLayer.Read(bytes)[1];
        state.Evaluate(3.5f);
        var evaluated = state.Apply(materialKey, 1, original);
        Require(!evaluated.Disabled && evaluated.Custom == 0x6060 && (evaluated.Flags >> 22 & 0xfc) == 252,
            "Native material enable, five-bit local alpha or six-bit threshold was lost.");
        Require(state.Apply(materialKey, 0, original) == original, "A command changed an unrelated layer.");
        state.Evaluate(4.5f);
        Require(state.Apply(materialKey, 1, original).Disabled, "Material disable event was ignored.");
        state.Evaluate(.5f);
        Require(state.Apply(materialKey, 1, original) == original && JadeMaterialLayer.Read(bytes)[1] == original,
            "Material reverse seek or immutable source state was lost.");
        VerifySpriteAlpha();
        Console.WriteLine("PASS: timed material activation, native key/index lookup, packed alpha/threshold, layer isolation and reverse seek");
    }

    private static void VerifySpriteAlpha()
    {
        var owner = new SceneObject { RliColors = [0x20ffffff] };
        var geometry = new Geometry { VertexColors = [0x20ffffff, 0x40ffffff, 0x40ffffff, 0x40ffffff, 0x40ffffff] };
        geometry.Billboards.Add(new(1, default, 1, 1, false));
        var layer = new JadeMaterialLayer(0, 0x10000, 0, 0, 0);
        if (Math.Abs(GeometryVertexColor.InstanceAlpha(owner, geometry, 1, 0x40ffffff) - 64 / 255f) > .0001f
            || Math.Abs(MaterialLayerTint.Evaluate(owner, geometry, 1, null, layer).W - 64 / 255f) > .0001f
            || GeometryVertexColor.InstanceAlpha(owner, new(), 5, 0) != 1)
            throw new InvalidDataException("Generated SPG alpha was replaced by opaque carrier fallback or diverged during export.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
