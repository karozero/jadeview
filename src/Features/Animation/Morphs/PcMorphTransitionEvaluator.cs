namespace JadeView;

// Isolated nonnegative-speed transitions from PC 004F7D30 / 005ADBF0 / 004410A0.
// Speech triggers, automatic mouth sequencing and actor gates are outside this evaluator.
public static class PcMorphTransitionEvaluator
{
    private const float StartProgression = 1;
    private const float EndProgression = 2;
    private const float MaximumProgressionExclusive = 2147483648f;
    private const float SettledThreshold = 1.99f;
    private const float SettledFactorTolerance = .05f;

    public static PcMorphTransitionState Advance(PcMorphTransitionState state, int groupCount, float delta,
        PcMorphTransitionCompletion completion = PcMorphTransitionCompletion.ProgressionThreshold)
    {
        Validate(state, groupCount, delta);
        if (completion is not (PcMorphTransitionCompletion.ProgressionThreshold or PcMorphTransitionCompletion.ProgressionAndFactor))
            throw new ArgumentException("Unqualified PC morph completion mode.", nameof(completion));
        if (state.Phase == PcMorphTransitionPhase.Settled) return state;
        var references = (uint[])state.Channel.GroupReferences.Clone();
        float progression = state.Progression;
        if (state.Phase == PcMorphTransitionPhase.Pending)
        {
            // 0046C680 checks the signed upper bound, retaining negative hole words.
            bool canCopyEndpoint = unchecked((int)references[1]) < groupCount;
            if (canCopyEndpoint) references[0] = references[1];
            references[1] = (uint)state.TargetGroup;
            progression = StartProgression;
        }
        // Native stores speed*delta as float before passing it into the x87 lerp.
        float step = (float)((double)state.Speed * delta);
        double blend = Math.Clamp((double)step, 0, 1);
        float factor = (float)((double)state.TargetFactor * blend + (1 - blend) * state.CurrentFactor);
        progression = Math.Min(EndProgression, (float)((double)state.Speed * delta + progression));
        bool settled = IsSettled(progression, factor, state.TargetFactor, completion);
        if (settled && completion == PcMorphTransitionCompletion.ProgressionAndFactor)
            factor = state.TargetFactor;
        var phase = settled ? PcMorphTransitionPhase.Settled : PcMorphTransitionPhase.Transitioning;
        var channel = new FaceMorphChannel(BitConverter.SingleToUInt32Bits(factor),
            BitConverter.SingleToUInt32Bits(progression), references);
        return state with { Channel = channel, CurrentFactor = factor, Progression = progression, Phase = phase };
    }

    private static bool IsSettled(float progression, float factor, float target,
        PcMorphTransitionCompletion completion) => completion switch
    {
        PcMorphTransitionCompletion.ProgressionThreshold => progression >= SettledThreshold,
        // 005ADBF0 stores the absolute x87 difference as float before comparing.
        PcMorphTransitionCompletion.ProgressionAndFactor => progression == EndProgression
            && (float)Math.Abs((double)factor - target) < SettledFactorTolerance,
        _ => false
    };

    private static void Validate(PcMorphTransitionState state, int groupCount, float delta)
    {
        bool finite = float.IsFinite(state.CurrentFactor) && float.IsFinite(state.TargetFactor)
            && float.IsFinite(state.Progression) && float.IsFinite(state.Speed) && float.IsFinite(delta);
        bool supportedTiming = state.Progression >= 0 && state.Progression < MaximumProgressionExclusive
            && state.Speed >= 0 && delta >= 0 && double.IsFinite((double)state.Speed * delta);
        bool validTarget = state.TargetGroup >= 0 && state.TargetGroup < groupCount;
        bool supportedPhase = state.Phase is PcMorphTransitionPhase.Pending
            or PcMorphTransitionPhase.Transitioning or PcMorphTransitionPhase.Settled;
        if (!finite || !supportedTiming || !validTarget || !supportedPhase || state.Channel.GroupReferences.Length < 2)
            throw new ArgumentException("Unqualified PC morph transition state.", nameof(state));
    }
}
