using System.Numerics;
using JadeView;

internal static class CinematicScriptHierarchyExportChecks
{
    internal static void Verify(string outputDirectory)
    {
        const uint worldKey = 0x100;
        const uint childKey = 0x401;
        const uint parentKey = 0x402;
        const uint secondChildKey = 0x404;
        const uint secondParentKey = 0x405;
        const uint clipKey = 0x3500BEEF;
        const uint secondClipKey = 0x3500BEF0;

        var child = new SceneObject { Name = "Script-parented prop", GaoKey = childKey };
        var parent = new SceneObject { Name = "Animated hierarchy parent", GaoKey = parentKey };
        var secondChild = new SceneObject { Name = "Second script-parented prop", GaoKey = secondChildKey };
        var secondParent = new SceneObject { Name = "Second hierarchy parent", GaoKey = secondParentKey };
        var source = new AnimationClip
        {
            Key = clipKey,
            Name = "Script hierarchy export fixture",
            Loaded = true,
            Cinematic = true,
            SceneMotion = true,
            CinematicWorldKey = worldKey,
            Duration = 1,
            Tracks =
            [
                new() { Bone = -1, Target = parentKey, Keys =
                    [new() { Time = 0, Duration = 1, Position = Vector3.Zero },
                        new() { Time = 1, Position = new(10, 0, 0) }] },
                new() { Bone = -1, Target = 0, Keys =
                    [CinematicTestEvents.Call(.5f, childKey, 0x1fdc, parentKey)] }
            ]
        };
        var secondSource = new AnimationClip
        {
            Key = secondClipKey,
            Name = "Second script hierarchy export fixture",
            Loaded = true,
            Cinematic = true,
            SceneMotion = true,
            CinematicWorldKey = worldKey,
            Duration = 1,
            Tracks =
            [
                new() { Bone = -1, Target = secondParentKey, Keys =
                    [new() { Time = 0, Duration = 1, Position = Vector3.Zero },
                        new() { Time = 1, Position = new(-10, 0, 0) }] },
                new() { Bone = -1, Target = 0, Keys =
                    [CinematicTestEvents.Call(.25f, secondChildKey, 0x1fdc, secondParentKey)] }
            ]
        };
        var world = new WorldData { Key = worldKey, Name = "Hierarchy export fixture" };
        world.Objects.AddRange([child, parent, secondChild, secondParent]);
        world.CinematicClips.Add(clipKey, source);
        world.CinematicClips.Add(secondClipKey, secondSource);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clipKey, source);
        document.CinematicClips.Add(secondClipKey, secondSource);

        var playback = CinematicPlayback.Discover(document, world, new ResourceResolver())
            .Single(player => player.Key == clipKey);
        playback.Activate();
        try
        {
            playback.Seek(.75f);
            Matrix4x4 previewPose = child.RenderMatrix;
            float previewTime = playback.Time;
            string path = Path.Combine(outputDirectory, "cinematic-script-hierarchy-export.glb");
            SceneExporter.Export(path, world, new ResourceResolver(), new()
            {
                Animations = true,
                UnknownObjects = true,
                GameHidden = true,
                Textures = false,
                CinematicDocument = document,
                AllClips = true,
                CinematicPlayback = playback
            });

            using var glb = new GlbReader(path);
            Vector3 atAttach = ReadTranslation(source.Name, child, .5f);
            Vector3 afterParentMoves = ReadTranslation(source.Name, child, 1f);
            if (Vector3.Distance(atAttach, Vector3.Zero) > .001f
                || Vector3.Distance(afterParentMoves, new(5, 0, 0)) > .001f)
                throw new InvalidDataException($"GLB did not preserve preview hierarchy motion: at attach {atAttach}, later {afterParentMoves}.");
            Vector3 secondClipMotion = ReadTranslation(secondSource.Name, secondChild, 1f);
            if (Vector3.Distance(secondClipMotion, new(-7.5f, 0, 0)) > .001f)
                throw new InvalidDataException($"GLB did not sample script hierarchy in the non-active clip: {secondClipMotion}.");
            if (Math.Abs(playback.Time - previewTime) > .0001f
                || MatrixDistance(child.RenderMatrix, previewPose) > .001f)
                throw new InvalidDataException("Hierarchy export sampling did not restore the active preview playhead and pose.");

            Vector3 ReadTranslation(string animationName, SceneObject target, float time)
            {
                var animations = glb.Root.GetProperty("animations");
                var animation = animations.EnumerateArray().Single(item =>
                    item.GetProperty("name").GetString() == animationName);
                var nodeRows = glb.Root.GetProperty("nodes");
                int targetNode = Enumerable.Range(0, nodeRows.GetArrayLength()).Single(index =>
                    nodeRows[index].GetProperty("name").GetString() == target.Name
                    && nodeRows[index].GetProperty("extras").GetProperty("gaoKey").GetString() == target.GaoKey.ToString("X8"));
                var channel = animation.GetProperty("channels").EnumerateArray().Single(item =>
                    item.GetProperty("target").GetProperty("node").GetInt32() == targetNode
                    && item.GetProperty("target").GetProperty("path").GetString() == "translation");
                var sampler = animation.GetProperty("samplers")[channel.GetProperty("sampler").GetInt32()];
                float[][] samples = glb.Accessor(sampler.GetProperty("input").GetInt32());
                float[][] values = glb.Accessor(sampler.GetProperty("output").GetInt32());
                int sampleIndex = FindSample(samples, time);
                return ReadVector(values[sampleIndex]);
            }
        }
        finally { playback.Deactivate(); }

        Console.WriteLine("PASS: GLB all-clips export samples script-set parent motion and restores the active preview pose");

        static int FindSample(float[][] samples, float time)
        {
            int index = Array.FindIndex(samples, sample => sample[0] == time);
            return index >= 0 ? index : throw new InvalidDataException($"GLB omitted hierarchy event sample at {time}s.");
        }

        static Vector3 ReadVector(float[] values) => new(values[0], values[1], values[2]);

        static float MatrixDistance(Matrix4x4 first, Matrix4x4 second) =>
            MathF.Sqrt(
                Square(first.M11 - second.M11) + Square(first.M12 - second.M12) + Square(first.M13 - second.M13) + Square(first.M14 - second.M14)
                + Square(first.M21 - second.M21) + Square(first.M22 - second.M22) + Square(first.M23 - second.M23) + Square(first.M24 - second.M24)
                + Square(first.M31 - second.M31) + Square(first.M32 - second.M32) + Square(first.M33 - second.M33) + Square(first.M34 - second.M34)
                + Square(first.M41 - second.M41) + Square(first.M42 - second.M42) + Square(first.M43 - second.M43) + Square(first.M44 - second.M44));

        static float Square(float value) => value * value;
    }
}
