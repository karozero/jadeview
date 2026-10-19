using JadeView;

internal static class MorphMouthCycleChecks
{
    internal static void Verify()
    {
        // Explicit fixture, not an inferred Sally profile or runtime snapshot.
        int[] sequence = Enumerable.Range(0, 32).Select(index => index % 7).ToArray();
        var channel = new FaceMorphChannel(0, BitConverter.SingleToUInt32Bits(2f), [1, 2]);
        var transition = new PcMorphTransitionState(channel, .8f, 0, 2, 11, 0, PcMorphTransitionPhase.Settled);
        var initial = new PcMorphMouthCycleState(transition, 31, true);
        var first = PcMorphMouthCycleEvaluator.Advance(initial, sequence, 7, 0);
        Require(first.NextFormIndex == 0 && first.Transition.TargetGroup == sequence[31], "Mouth index did not wrap after selecting its current form");
        Require(first.Transition.Phase == PcMorphTransitionPhase.Transitioning && first.Transition.Progression == 1,
            "Automatic form did not enter the normal pending transition");
        Require(channel.GroupReferences.SequenceEqual(new uint[] { 1, 2 }), "Mouth cycle mutated source channel");

        var largeStep = PcMorphMouthCycleEvaluator.Advance(initial, sequence, 7, 10);
        Require(largeStep.NextFormIndex == 0 && largeStep.Transition.Phase == PcMorphTransitionPhase.Settled,
            "Large update consumed more than one mouth form");
        var nextFrame = PcMorphMouthCycleEvaluator.Advance(largeStep, sequence, 7, 0);
        Require(nextFrame.NextFormIndex == 1, "Settled automatic cycle failed to restart on the next update");
        var disabled = PcMorphMouthCycleEvaluator.Advance(initial with { Enabled = false }, sequence, 7, 1);
        Require(disabled.NextFormIndex == 31 && disabled.Transition == transition, "Disabled settled cycle selected another form");
        var finishing = PcMorphMouthCycleEvaluator.Advance(first with { Enabled = false }, sequence, 7, 1);
        Require(finishing.NextFormIndex == first.NextFormIndex && finishing.Transition.Phase == PcMorphTransitionPhase.Settled,
            "Disabling automatic cycling prevented the current transition from finishing");

        VerifySourceCommandInterval(transition, sequence);
        Reject(initial, sequence[..31]);
        Reject(initial with { NextFormIndex = 32 }, sequence);
        int[] invalidSequence = (int[])sequence.Clone();
        invalidSequence[0] = 7;
        Reject(initial, invalidSequence);
        Console.WriteLine("PASS: explicit PC mouth cycle, wrap, one form per update, disable/reset and source-command interval fixture");
    }

    private static void VerifySourceCommandInterval(PcMorphTransitionState transition, int[] sequence)
    {
        // FF00791B / 20007991: -580 -> factor .8 + auto on, speed 11;
        // approximately 26 frames later: 500 -> factor 0 + auto off, speed 9.
        // Sequence, index, frame rate and starting state are explicit assumptions.
        var state = new PcMorphMouthCycleState(transition, 0, true);
        for (int frame = 0; frame < 26; frame++)
            state = PcMorphMouthCycleEvaluator.Advance(state, sequence, 7, 1f / 60);
        Require(state.NextFormIndex == 5, "Source-command interval fixture advanced the wrong number of forms");
        var reset = state with
        {
            Enabled = false,
            Transition = state.Transition with { TargetGroup = 0, TargetFactor = 0, Speed = 9, Phase = PcMorphTransitionPhase.Pending }
        };
        for (int frame = 0; frame < 7; frame++)
            reset = PcMorphMouthCycleEvaluator.Advance(reset, sequence, 7, 1f / 60);
        Require(reset.NextFormIndex == 5 && reset.Transition.TargetGroup == 0
            && reset.Transition.Phase == PcMorphTransitionPhase.Settled, "Reset restarted automatic mouth cycling");
        Require(reset.Transition.CurrentFactor > 0 && reset.Transition.CurrentFactor < state.Transition.CurrentFactor,
            "Reset should decay the current factor rather than snapping it to zero");
    }

    private static void Reject(PcMorphMouthCycleState state, int[] sequence)
    {
        try { PcMorphMouthCycleEvaluator.Advance(state, sequence, 7, .1f); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Unqualified mouth cycle was accepted");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
