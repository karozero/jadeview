using System.Numerics;
using JadeView;

internal static class AnimationTimeCurveChecks
{
    internal static void Verify()
    {
        byte[] bytes = ClipBytes();
        var clip = AnimationReader.Read(bytes, 0, bytes.Length);
        var track = clip.Tracks.Single();
        var key = track.Keys[0];
        if (key.TimeCurve?.Points.Count != 2 || clip.Unsupported != 0
            || key.OpaquePayloads.Single().Kind != "extra-transform-controls")
            throw new InvalidDataException("Supported time curve lost its decoded values or original bytes.");
        Near(key.TimeCurve.Evaluate(.25f), .125f);
        Near(key.TimeCurve.Evaluate(.5f), .25f);
        Near(key.TimeCurve.Evaluate(.75f), .625f);
        Near(key.TimeCurve.Evaluate(1), 1);
        var root = new SceneObject { GaoKey = 1 };
        var bone = new SceneObject { GaoKey = 17, ActorNode = new() { Parent = 1, Local = Matrix4x4.Identity } };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        actor.SelectClip(clip);
        actor.Playing = false;
        actor.Time = .75f;
        actor.Evaluate(0);
        Near(bone.PreviewMatrix!.Value.Translation.X, 6.25f);
        Near(CinematicTransformSampler.Evaluate(new(), [track], .75f).Translation.X, 6.25f);
        key.Flags |= AnimationInterpolation.NoInterpolation;
        Near(AnimationInterpolation.Amount(key, .75f), 0);
        Reject([0, 0, 0, 0]);
        Reject(CurveBytes(float.NaN, .5f));
        Reject(CurveBytes(1, 0));
        Console.WriteLine("PASS: native piecewise time-factor integration, shared actor/camera interpolation, retained bytes, STEP and malformed curves");
    }

    private static byte[] ClipBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write((short)0); writer.Write(0u); writer.Write(17u); writer.Write(2u);
        writer.Write((ushort)60); writer.Write((ushort)128);
        writer.Write((ushort)32); writer.Write((ushort)9);
        writer.Write(0f); writer.Write(0f); writer.Write(0f);
        writer.Write(CurveBytes(.5f, .5f));
        writer.Write((ushort)0); writer.Write((ushort)128);
        writer.Write((ushort)16); writer.Write((ushort)1);
        writer.Write(10f); writer.Write(0f); writer.Write(0f);
        return stream.ToArray();
    }

    private static byte[] CurveBytes(float factor, float changeTime)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(2); writer.Write(factor); writer.Write(changeTime); writer.Write(1.5f);
        return stream.ToArray();
    }

    private static void Reject(byte[] bytes)
    {
        try { AnimationTimeCurve.Read(bytes); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Malformed time curve was accepted.");
    }

    private static void Near(float actual, float expected)
    {
        if (MathF.Abs(actual - expected) > .00001f)
            throw new InvalidDataException($"Time curve mismatch: {actual}, expected {expected}");
    }
}
