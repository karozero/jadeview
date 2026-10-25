using JadeView;

internal static class CinematicScriptMessageScalarStateChecks
{
    internal static void Verify()
    {
        const uint ownerKey = 0x10004AE8;
        var owner = new CinematicScriptOwner(ownerKey, 0x3D002D78, 0x3D002D5B,
            0xFF0045A2, 0, 0, "shield message state fixture", [], [],
            [new("i_cine", 0, "instance", "0"),
             new("des_f_time1", 0, "instance", "0"),
             new("des_f_time2", 0, "instance", "0")])
        {
            CallbackNames = ["sfx_bouclier_phare_sbp_msg"],
            ModelFunctionNames = ["sfx_bouclier_phare_sbp_msg"]
        };
        var ownerObject = new SceneObject { GaoKey = ownerKey };
        var messageArguments = new uint[13];
        messageArguments[0] = 1;
        messageArguments[9] = BitConverter.SingleToUInt32Bits(0.5f);
        messageArguments[10] = BitConverter.SingleToUInt32Bits(0.25f);
        var timelineCall = new CinematicLiteralCall(ownerKey, 0x1F78, messageArguments);
        Dictionary<string, double?> state = CinematicScriptMessageScalarStateResolver.Resolve(
            owner, ownerObject, [(50.65f, timelineCall)], 55.8f, null);
        Check(state.GetValueOrDefault("des_f_time1") == 0.5
              && state.GetValueOrDefault("des_f_time2") == 0.25,
            "Source-indexed float OVA writes should receive matching fields from a queued cinematic message.");

        Dictionary<string, double?> beforeMessage = CinematicScriptMessageScalarStateResolver.Resolve(
            owner, ownerObject, [(50.65f, timelineCall)], 50.64f, null);
        Check(beforeMessage.Count == 0,
            "A message must not affect callback state before its event time.");

        CinematicLiteralCall otherRecipient = timelineCall with { Target = ownerKey + 1 };
        Dictionary<string, double?> wrongRecipient = CinematicScriptMessageScalarStateResolver.Resolve(
            owner, ownerObject, [(50.65f, otherRecipient)], 55.8f, null);
        Check(wrongRecipient.Count == 0,
            "Message fields must not cross into a different execution object's queue.");

        owner.ModelFunctionNames[0] = "unresolved_source_fixture";
        var unlinkedState = CinematicScriptMessageScalarStateResolver.Resolve(owner, ownerObject,
            [(50.65f, timelineCall)], 55.8f, null);
        Check(unlinkedState.Count == 0,
            "Changing a model's function configuration must invalidate cached message readers and OVA accesses.");
        owner.ModelFunctionNames[0] = "sfx_bouclier_phare_sbp_msg";
        var restoredState = CinematicScriptMessageScalarStateResolver.Resolve(owner, ownerObject,
            [(50.65f, timelineCall)], 55.8f, null);
        Check(restoredState.GetValueOrDefault("des_f_time1") == 0.5,
            "Restoring source function configuration must recover the message-derived state.");

        Console.WriteLine("PASS: source-confirmed message readers carry float OVA state into later callbacks");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
