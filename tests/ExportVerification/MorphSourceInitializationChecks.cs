using System.Text.Json;
using JadeView;

internal static class MorphSourceInitializationChecks
{
    internal static void Verify(string output)
    {
        using var definition = JsonDocument.Parse(File.ReadAllText("docs/data/pc-controller-7100AF12.json"));
        int[] sequence = definition.RootElement.GetProperty("mouthSequence").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var channel = new FaceMorphChannel(0, BitConverter.SingleToUInt32Bits(2), [0, 0]);
        var transition = new PcMorphTransitionState(channel, .8f, 0, 2, 11, 0, PcMorphTransitionPhase.Settled);
        var state = new PcMorphMouthCycleState(transition, 0, true);
        var frames = new List<object>();
        for (int frame = 0; frame < 26; frame++)
        {
            state = PcMorphMouthCycleEvaluator.Advance(state, sequence, 55, 1f / 60,
                PcMorphTransitionCompletion.ProgressionAndFactor);
            float[] weights = FaceMorphChannelEvaluator.Evaluate(55, [state.Transition.Channel]);
            if (weights.Any(weight => !float.IsFinite(weight)))
                throw new InvalidOperationException("Recovered source cycle produced non-finite weights");
            frames.Add(new { Frame = frame, state.NextFormIndex, state.Transition.TargetGroup,
                state.Transition.CurrentFactor, state.Transition.Progression, state.Transition.Phase,
                Weights = weights });
        }
        if (state.NextFormIndex != 3 || state.Transition.TargetGroup != sequence[2])
            throw new InvalidOperationException("Recovered form sequence or factor-aware timing changed");
        File.WriteAllText(Path.Combine(output, "pc-source-mouth-cycle-fixture.json"), JsonSerializer.Serialize(new
        {
            InitializerCodeKey = "7100AF12", Frames = frames,
            Limits = "Recovered sequence and completion rule; explicit assumed zero initial factor/index, references [0,0], command effects -580/speed11 and 60 Hz. Not a live actor or original clip reconstruction."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: recovered 32-form source sequence, factor-aware timing and finite evaluated weights");
    }
}
