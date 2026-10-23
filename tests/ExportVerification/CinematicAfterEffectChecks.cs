using System.Buffers.Binary;
using System.Numerics;
using JadeView;

internal static class CinematicAfterEffectChecks
{
    internal static void Verify()
    {
        var clip = new AnimationClip { Key = 1, CinematicOwnerKey = 100, Duration = 4 };
        clip.Tracks.Add(new AnimationTrack
        {
            Target = 100,
            Bone = -1,
            Keys =
            [
                new AnimationKey { Time = 0, OpaquePayloads = [CreateCall(9, .45f), CreateCall(8, .5f)] },
                new AnimationKey { Time = 1, OpaquePayloads = [CreateCall(8, .75f)] },
                new AnimationKey { Time = 2, OpaquePayloads = [CreateCall(9, .6f)] },
                new AnimationKey { Time = 3, OpaquePayloads = [CreateCall(9, float.NaN)] }
            ]
        });

        var timeline = new CinematicAfterEffectTimeline(clip);
        Near(timeline.OffsetEffect, .5f, "initial PC mode-8 parameter");
        Near(timeline.ColorBalance, .5f, "initial color balance");
        timeline.Evaluate(0);
        Near(timeline.OffsetEffect, .5f, "neutral PC mode-8 parameter");
        Near(timeline.ColorBalance, .45f, "source-time color-balance update");
        timeline.Evaluate(1.99f);
        Near(timeline.OffsetEffect, .75f, "held PC mode-8 parameter");
        Near(timeline.ColorBalance, .45f, "held color balance before next command");
        timeline.Evaluate(2);
        Near(timeline.OffsetEffect, .75f, "mode-9 update preserves independent mode-8 state");
        Near(timeline.ColorBalance, .6f, "second color-balance update; unrelated modes are ignored");
        timeline.Evaluate(1);
        Near(timeline.OffsetEffect, .75f, "backward seek restores mode-8 state");
        Near(timeline.ColorBalance, .45f, "backward seek restores previous value");

        Vector3 input = new(.2f, .4f, .8f);
        Near(CinematicAfterEffectTimeline.ApplyOffsetEffect(input, .75f),
            Vector3.Clamp(input + new Vector3(63 / 255f), Vector3.Zero, Vector3.One),
            "PC Iris's additive texture-factor pass");
        Near(CinematicAfterEffectTimeline.ApplyOffsetEffect(input, .25f),
            Vector3.Clamp(input - new Vector3(63 / 255f), Vector3.Zero, Vector3.One),
            "PC Iris's subtractive texture-factor pass");
        Near(CinematicAfterEffectTimeline.ApplyOffsetEffect(input, .5f), input,
            "neutral PC mode-8 parameter");
        Vector3 lowBalance = CinematicAfterEffectTimeline.ApplyColorBalance(input, .45f);
        float diffuseAlpha = 229 / 255f;
        float grayWeight = 1 - diffuseAlpha;
        Near(lowBalance, input * diffuseAlpha + new Vector3(128 / 255f) * grayWeight,
            "PC Iris's sub-neutral diffuse-alpha color-balance pass");
        Near(CinematicAfterEffectTimeline.ApplyColorBalance(input, 0), new Vector3(128 / 255f),
            "minimum PC color-balance value selects the gray diffuse color");
        Near(CinematicAfterEffectTimeline.ApplyColorBalance(input, .499f),
            Vector3.Lerp(input, new Vector3(128 / 255f), 1 - MathF.Floor(.499f * 510) / 255),
            "values approaching neutral preserve almost all source color");
        Near(CinematicAfterEffectTimeline.ApplyColorBalance(input, .5f), input,
            "neutral color balance");
        Vector3 highBalance = CinematicAfterEffectTimeline.ApplyColorBalance(input, .75f);
        Vector3 expectedHighBalance = Vector3.Clamp(input + input * (127 / 255f)
            - new Vector3(64 / 255f), Vector3.Zero, Vector3.One);
        Near(highBalance, expectedHighBalance,
            "PC Iris's three-stage modulation, add and subtract color-balance pass");
        Near(CinematicAfterEffectTimeline.ApplyColorBalance(new Vector3(.8f), 1), Vector3.One,
            "maximum color-balance contrast clamps bright source values");
        Near(CinematicAfterEffectTimeline.ApplyColorBalance(new Vector3(.2f), 1), Vector3.Zero,
            "maximum color-balance contrast clamps dark source values");

        Console.WriteLine("PASS: cinematic PC color-balance event state, backward seek and deterministic preview transform");
    }

    private static AnimationOpaquePayload CreateCall(int mode, float value)
    {
        var rows = new (uint Value, uint Tag)[]
        {
            (0, 0x01000028), (0, 0x02000806),
            ((uint)mode, 0x01000021), (1, 0x01000021), (0, 0x01000021),
            (BitConverter.SingleToUInt32Bits(value), 0x01000022),
            (0, 0x03003CF0), (0, 0x1F000000)
        };
        byte[] payload = new byte[sizeof(uint) + rows.Length * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)rows.Length);
        for (int index = 0; index < rows.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(sizeof(uint) + index * 8), rows[index].Value);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(sizeof(uint) + index * 8 + 4), rows[index].Tag);
        }
        return new("word-pair-list", 0, payload);
    }

    private static void Near(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > .0001f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }

    private static void Near(Vector3 actual, Vector3 expected, string label)
    {
        if (Vector3.Distance(actual, expected) > .0001f)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
