namespace JadeView;

// Isolated slot-4 cycle at PC 004F806F, followed by that slot's transition.
// No actor-name profiles, speech scheduler or inferred initial controller state.
public static class PcMorphMouthCycleEvaluator
{
    private const int NativeSequenceLength = 32;

    public static PcMorphMouthCycleState Advance(PcMorphMouthCycleState state,
        ReadOnlySpan<int> formSequence, int groupCount, float delta,
        PcMorphTransitionCompletion completion = PcMorphTransitionCompletion.ProgressionThreshold)
    {
        ValidateSequence(state, formSequence, groupCount);
        var transition = state.Transition;
        int nextFormIndex = state.NextFormIndex;
        bool startNextForm = state.Enabled && transition.Phase == PcMorphTransitionPhase.Settled;
        if (startNextForm)
        {
            transition = transition with
            {
                TargetGroup = formSequence[nextFormIndex],
                Phase = PcMorphTransitionPhase.Pending
            };
            nextFormIndex = (nextFormIndex + 1) % NativeSequenceLength;
        }
        // At most one cycle entry per update. A large delta does not consume
        // several forms; a newly settled slot can restart on the next update.
        transition = PcMorphTransitionEvaluator.Advance(transition, groupCount, delta, completion);
        return state with { Transition = transition, NextFormIndex = nextFormIndex };
    }

    private static void ValidateSequence(PcMorphMouthCycleState state,
        ReadOnlySpan<int> formSequence, int groupCount)
    {
        bool validIndex = state.NextFormIndex >= 0 && state.NextFormIndex < NativeSequenceLength;
        if (formSequence.Length != NativeSequenceLength || !validIndex)
            throw new ArgumentException("Expected a qualified 32-form PC mouth sequence and index.");
        foreach (int form in formSequence)
        {
            if (form < 0 || form >= groupCount)
                throw new ArgumentException("PC mouth sequence references an unsupported morph form.");
        }
    }
}
