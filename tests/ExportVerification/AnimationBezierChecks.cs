using System.Numerics;
using JadeView;

internal static class AnimationBezierChecks
{
    internal static void Verify()
    {
        VerifyCurve([Vector3.Zero, new(2, 4, 0)], new(10, 0, 0), new(3.5f, 2, 0));
        VerifyCurve([Vector3.Zero, new(1, 3, 0), new(5, -2, 0)], new(8, 0, 0), new(3.25f, .375f, 0));
        Console.WriteLine("PASS: native quadratic/cubic Bezier byte order, source endpoints, independent midpoints and actor/object sampling");
    }

    private static void VerifyCurve(Vector3[] storedVectors, Vector3 end, Vector3 midpoint)
    {
        byte[] bytes = EncodedCurve(storedVectors, end);
        var clip = AnimationReader.Read(bytes, 0, bytes.Length);
        var track = clip.Tracks.Single();
        var first = track.Keys[0];
        if (first.Position != storedVectors[0] || first.Control1 != storedVectors[1]
            || storedVectors.Length == 3 && first.Control2 != storedVectors[2])
            throw new InvalidDataException("Bezier position/control vectors were reordered while decoding.");
        Near(CinematicTransformSampler.Evaluate(new(), [track], 0).Translation, storedVectors[0]);
        Near(CinematicTransformSampler.Evaluate(new(), [track], .5f).Translation, midpoint);
        Near(CinematicTransformSampler.Evaluate(new(), [track], 1).Translation, end);
        var root = new SceneObject { GaoKey = 1 };
        var bone = new SceneObject { GaoKey = 2, ActorNode = new() { Parent = 1 } };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone], Clip = clip, Loop = false };
        foreach (float time in new[] { .5f, 1, 0, .5f })
        {
            actor.Time = time;
            actor.Evaluate(0);
            Near(bone.PreviewMatrix!.Value.Translation, time == .5f ? midpoint : time == 1 ? end : storedVectors[0]);
        }
    }

    private static byte[] EncodedCurve(Vector3[] vectors, Vector3 end)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write((short)0); writer.Write(0u); writer.Write(2u); writer.Write(2u);
        writer.Write((ushort)60); writer.Write((ushort)128);
        writer.Write((ushort)(4 + vectors.Length * 12)); writer.Write((ushort)vectors.Length);
        foreach (var vector in vectors) WriteVector(vector);
        writer.Write((ushort)0); writer.Write((ushort)128);
        writer.Write((ushort)16); writer.Write((ushort)1); WriteVector(end);
        return stream.ToArray();

        void WriteVector(Vector3 vector) { writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z); }
    }

    private static void Near(Vector3 actual, Vector3 expected)
    {
        if (Vector3.Distance(actual, expected) > .0001f)
            throw new InvalidDataException($"Bezier sample {actual} differs from independent reference {expected}.");
    }
}
