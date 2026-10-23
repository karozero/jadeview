namespace JadeView;

internal static class CinematicScriptSchedulerAnalyzer
{
    private const uint WorldAiCallbackTableFlag = 0x20;
    private const uint SerializedStateToActivationGateBit = GameScriptSerializedObjectState.SerializedInactiveBit;
    private const uint IrisSpecialTimeActivationBit = GameScriptObjectActivation.SpecialTimeActivationBit;
    private const uint IrisInitialStateMask = GameScriptSerializedObjectState.PreservedStateMask;
    private const uint OrdinaryActivationBlockMask = GameScriptObjectActivation.NormalTimeBlockMask;
    private const uint DisabledAiRunInstanceBit = GameObjectRuntimeFlags.Inactive;

    internal static string[] Analyze(CinematicScriptOwner owner)
    {
        bool hasWorldAiCallbackTableFlag = (owner.SerializedGaoFlags & WorldAiCallbackTableFlag) != 0;
        bool serializedActivationGateBitSet = (owner.SerializedStateFlags & SerializedStateToActivationGateBit) != 0;
        bool rawRuntimeMaskBitSurvivesIrisLoad = (owner.SerializedStateFlags & IrisInitialStateMask
            & 0x00400000) != 0;
        var findings = new List<string>
        {
            hasWorldAiCallbackTableFlag
                ? "Serialized GAO flag 0x20 is set; Iris uses it to include the activated object in AICallbacksTablePF when building the world's EOT tables."
                : "Serialized GAO flag 0x20 is clear; Iris does not add this object to AICallbacksTablePF when building the world's EOT tables."
        };

        findings.Add(serializedActivationGateBitSet
            ? $"The serialized state flags set source-mapped runtime flags2 0x{DisabledAiRunInstanceBit:X8}, which blocks ordinary world activation while ENG_gf_SpecialTime is zero because WOR_ActivateObjectIfFlagsAllow rejects flags2 intersecting 0x{OrdinaryActivationBlockMask:X8}; this owner therefore has a source-confirmed ordinary-activation blocker at load unless runtime state changes."
            : $"Serialized state bit 0x40000000 is clear, so runtime flags2 0x{DisabledAiRunInstanceBit:X8} is not set by that source mapping. The raw 0x00400000 bit {(rawRuntimeMaskBitSurvivesIrisLoad ? "survives" : "is discarded by")} Iris's initial state mask 0x{IrisInitialStateMask:X8}; other runtime activation gates remain.");

        uint projectedFlags2 = GameScriptSerializedObjectState.ProjectInitialFlags2(owner.SerializedStateFlags);
        bool specialTimeActivationBitSet = (projectedFlags2 & IrisSpecialTimeActivationBit) != 0;
        findings.Add(specialTimeActivationBitSet
            ? "When ENG_gf_SpecialTime is nonzero, Iris's alternate activation branch requires runtime flags2 0x04000000; that bit is present in this serialized state and survives the initial 0x7B9C0080 mask. Other live activation flags can still prevent membership."
            : "When ENG_gf_SpecialTime is nonzero, Iris's alternate activation branch requires runtime flags2 0x04000000; the serialized-state projection does not provide that bit because Iris's initial 0x7B9C0080 mask discards raw 0x04000000. Later runtime changes could set it.");

        findings.Add("WOR_ActivateObjectIfFlagsAllow also requires the object not already be active; at ordinary time it rejects runtime flags2 masks 0x04400000 and 0x10, while special-time activation requires 0x04000000. The BF snapshot cannot reveal later flag changes or final activated-table membership.");
        findings.Add($"AI_MainCall dispatches only valid, not-yet-processed callback-table entries; extraFlag 0x4 additionally requires nonzero distance, hierarchy references can redirect processing to a parent, and the resolved object must have an AIInstance. It skips AI_RunInstance while runtime flags2 0x{DisabledAiRunInstanceBit:X8} is set. Runtime distance, hierarchy, instance allocation, scheduling and callback results remain unknown.");
        findings.Add("AI_RunInstance also checks its model's disabled state, callback-track range, per-track remaining delay and function pointer. A positive delay is reduced by TIM_gf_dt before the callback can run; serialized assignment alone does not make the callback due.");
        findings.Add("AI_RunInstance returning zero stops the remaining AI_MainCall table traversal for this world. Iris maps function result sentinel 3 to that zero return; later owners cannot be assumed to run independently of earlier callback results.");
        findings.Add("These source-derived conditions cannot prove that the owner's callbacks ran in the game.");
        return findings.ToArray();
    }
}
