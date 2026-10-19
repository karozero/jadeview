using JadeView;

internal static class SallyExpressionChecks
{
    internal static void Verify()
    {
        Check(SallyExpressionPresets.Commands(2).Select(command => command.Group).SequenceEqual(new[] { 1, 2, 10, 31, 0 }), "PC kit 2 group table changed");
        Check(SallyExpressionPresets.Commands(3).Select(command => command.Group).SequenceEqual(new[] { 1, 2, 11, 34, 52 }), "PC kit 3 group table changed");
        Check(SallyExpressionPresets.Commands(4).Select(command => command.Group).SequenceEqual(new[] { 1, 2, 16, 35, 31 }), "PC kit 4 group table changed");
        Check(SallyExpressionPresets.Commands(3).SequenceEqual(SallyExpressionPresets.Commands(5)), "PC kit 3/5 equivalence was lost");
        var command = SallyExpressionPresets.Decode(4, 52515)!;
        Check(command.Group == 52 && command.Weight == .149993896484375f, "Packed PC float32 residual lost its native rounding");
        Check(SallyExpressionPresets.Decode(0, 0) == null, "Zero command must leave the native slot unchanged");
        Check(SallyExpressionPresets.Decode(0, .5f) == null, "Native half-to-even zero must leave the slot unchanged");
        var boundary = SallyExpressionPresets.Decode(0, 999.5f)!;
        Check(boundary.Group == 1 && boundary.Weight == -5,
            "Native nearest-integer conversion was replaced by truncation across a group boundary");
        var evenTie = SallyExpressionPresets.Decode(0, 1000.5f)!;
        Check(evenTie.Group == 1 && evenTie.Weight == -5, "Native midpoint conversion must round to the even integer");
        var oddTie = SallyExpressionPresets.Decode(0, 1001.5f)!;
        var rounded = SallyExpressionPresets.Decode(0, 1002f)!;
        Check(oddTie.Group == rounded.Group && oddTie.Weight == rounded.Weight,
            "Native odd midpoint must round upward");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, 2147483648f })
        {
            try { SallyExpressionPresets.Decode(0, invalid); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid packed expression command was accepted");
        }
        Console.WriteLine("PASS: original PC expression kit groups, shared presets, packed-command decoding and float32 residual");
    }

    internal static void VerifySally(SceneActor actor, SceneObject head, WorldData world,
        ResourceResolver resolver, string output)
    {
        var preview = head.MorphPreview!;
        byte[] source = (byte[])preview.Modifier.Data.Clone();
        foreach (int kit in SallyExpressionPresets.Kits)
        {
            preview.PreviewSallyExpression(kit);
            var expected = SallyExpressionPresets.SettledWeights(kit, 55);
            Check(!preview.ChannelsActive && !preview.BlinkPlaying && preview.CopyWeights().SequenceEqual(expected),
                "Expression preset did not take over the preview pose");
            string path = Path.Combine(output, $"sally-pc-expression-{kit}.glb");
            SceneExporter.Export(path, world, resolver, new() { Animations = true, Textures = true }, actor.Root);
            using var glb = new GlbReader(path);
            var mesh = glb.Root.GetProperty("meshes").EnumerateArray().First(mesh =>
                mesh.TryGetProperty("weights", out var weights) && weights.GetArrayLength() == 55);
            Check(mesh.GetProperty("weights").EnumerateArray().Select(value => value.GetSingle()).SequenceEqual(expected),
                "Expression pose changed on GLB export");
            SkinExportChecks.VerifyActorMeshes(glb, actor, resolver, [0, actor.Clip!.Duration]);
        }
        Check(preview.Modifier.Data.SequenceEqual(source), "Expression preview modified the native BF payload");
        preview.Reset(); actor.Evaluate(0);
        Console.WriteLine("PASS: real Sally settled expression poses, mode takeover, GLB weights/skin and unchanged source");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
