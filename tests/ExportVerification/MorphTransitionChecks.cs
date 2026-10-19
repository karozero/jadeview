using JadeView;

internal static class MorphTransitionChecks
{
    internal static void Verify()
    {
        uint[] sourceReferences = [1, 2, uint.MaxValue, 4];
        var channel = new FaceMorphChannel(BitConverter.SingleToUInt32Bits(.5f),
            BitConverter.SingleToUInt32Bits(2f), sourceReferences);
        var pending = new PcMorphTransitionState(channel, 1, .5f, 2, 1, 3, PcMorphTransitionPhase.Pending);
        var first = PcMorphTransitionEvaluator.Advance(pending, 5, .25f);
        Require(first.Channel.GroupReferences.SequenceEqual(new uint[] { 2, 3, uint.MaxValue, 4 }), "Retarget lost old endpoint or trailing references");
        Require(sourceReferences.SequenceEqual(new uint[] { 1, 2, uint.MaxValue, 4 }), "Retarget mutated source references");
        Require(first.Progression == 1.25f && first.CurrentFactor == .625f, "Native progression or lerp coefficient mismatch");
        var weights = FaceMorphChannelEvaluator.Evaluate(5, [first.Channel]);
        Require(weights[2] == .46875f && weights[3] == .15625f, "Retargeted channel deformation does not crossfade endpoints");
        var second = PcMorphTransitionEvaluator.Advance(first, 5, .25f);
        Require(second.CurrentFactor == .71875f && second.Progression == 1.5f, "Transition retargeted twice or used a linear factor ramp");
        var settled = PcMorphTransitionEvaluator.Advance(second, 5, 1);
        Require(settled.Phase == PcMorphTransitionPhase.Settled && settled.Progression == 2 && settled.CurrentFactor == 1, "Large step did not clamp lerp/progression");
        Require(ReferenceEquals(settled, PcMorphTransitionEvaluator.Advance(settled, 5, 1)), "Settled controller slot continued updating");
        var nearEnd = PcMorphTransitionEvaluator.Advance(pending, 5, .995f);
        Require(nearEnd.Phase == PcMorphTransitionPhase.Settled && nearEnd.Progression < 2, "Native 1.99 threshold was replaced with a snap to 2");
        var zeroStep = PcMorphTransitionEvaluator.Advance(pending with { Speed = 0 }, 5, 1);
        Require(zeroStep.Progression == 1 && zeroStep.CurrentFactor == .5f, "Zero speed changed the current factor");
        var interrupted = PcMorphTransitionEvaluator.Advance(first with
            { Phase = PcMorphTransitionPhase.Pending, TargetGroup = 4 }, 5, 0);
        Require(interrupted.Channel.GroupReferences[0] == 3 && interrupted.Channel.GroupReferences[1] == 4
            && interrupted.Progression == 1 && interrupted.CurrentFactor == first.CurrentFactor,
            "Interrupted transition must restart from the prior endpoint, not invent a blended source target");
        var invalidEndpoint = pending with { Channel = channel with { GroupReferences = [1, 99] } };
        var retained = PcMorphTransitionEvaluator.Advance(invalidEndpoint, 5, 0);
        Require(retained.Channel.GroupReferences[0] == 1, "Native setter must ignore an out-of-range positive endpoint");
        var holeEndpoint = pending with { Channel = channel with { GroupReferences = [1, uint.MaxValue] } };
        var hole = PcMorphTransitionEvaluator.Advance(holeEndpoint, 5, 0);
        Require(hole.Channel.GroupReferences[0] == uint.MaxValue, "Native signed setter lost a hole word");
        Reject(pending with { TargetGroup = -1 });
        Reject(pending with { Speed = -1 });
        Reject(pending with { CurrentFactor = float.NaN });
        Console.WriteLine("PASS: PC morph retarget, immutable source references, exponential factor blend, threshold and channel deformation");
    }

    private static void Reject(PcMorphTransitionState state)
    {
        try { PcMorphTransitionEvaluator.Advance(state, 5, .1f); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Unqualified transition state was accepted");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
