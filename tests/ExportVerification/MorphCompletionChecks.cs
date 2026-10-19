using JadeView;

internal static class MorphCompletionChecks
{
    internal static void Verify()
    {
        var channel = new FaceMorphChannel(0, BitConverter.SingleToUInt32Bits(2), [0, 1]);
        var pending = new PcMorphTransitionState(channel, 1, 0, 2, 1, 2, PcMorphTransitionPhase.Pending);
        var nearEnd = Advance(pending, .995f);
        Require(nearEnd.Phase == PcMorphTransitionPhase.Transitioning, "Factor-aware controller settled before progression reached 2");
        var atEnd = Advance(nearEnd, .005f);
        Require(atEnd.Progression == 2 && atEnd.Phase == PcMorphTransitionPhase.Settled && atEnd.CurrentFactor == 1,
            "Factor-aware controller did not snap a converged endpoint");

        var farFromTarget = pending with { Phase = PcMorphTransitionPhase.Transitioning, Progression = 2, CurrentFactor = .8f };
        var stillBlending = Advance(farFromTarget, .1f);
        Require(stillBlending.Progression == 2 && stillBlending.Phase == PcMorphTransitionPhase.Transitioning,
            "Progression alone incorrectly completed a factor-aware transition");
        var toleranceBoundary = farFromTarget with { TargetFactor = 0, CurrentFactor = .05f, Speed = 0 };
        Require(Advance(toleranceBoundary, 0).Phase == PcMorphTransitionPhase.Transitioning,
            "Factor tolerance comparison must be strict");
        var converged = Advance(toleranceBoundary with { CurrentFactor = .049f }, 0);
        Require(converged.Phase == PcMorphTransitionPhase.Settled && converged.CurrentFactor == 0
            && converged.Channel.WeightBits == 0, "Converged factor was not snapped in both snapshot and channel");

        int[] sequence = Enumerable.Repeat(2, 32).ToArray();
        var cycle = new PcMorphMouthCycleState(farFromTarget, 0, true);
        var waiting = PcMorphMouthCycleEvaluator.Advance(cycle, sequence, 3, .1f,
            PcMorphTransitionCompletion.ProgressionAndFactor);
        Require(waiting.NextFormIndex == 0, "Mouth cycle advanced before factor convergence");
        var ready = PcMorphMouthCycleEvaluator.Advance(waiting, sequence, 3, 1,
            PcMorphTransitionCompletion.ProgressionAndFactor);
        Require(ready.NextFormIndex == 0 && ready.Transition.Phase == PcMorphTransitionPhase.Settled,
            "Newly settled mouth must wait until the next update to select another form");
        var restarted = PcMorphMouthCycleEvaluator.Advance(ready, sequence, 3, 0,
            PcMorphTransitionCompletion.ProgressionAndFactor);
        Require(restarted.NextFormIndex == 1, "Converged mouth cycle did not restart");
        Console.WriteLine("PASS: PC factor-aware completion, strict tolerance, exact endpoint and mouth-cycle gating");
    }

    private static PcMorphTransitionState Advance(PcMorphTransitionState state, float delta) =>
        PcMorphTransitionEvaluator.Advance(state, 3, delta, PcMorphTransitionCompletion.ProgressionAndFactor);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
