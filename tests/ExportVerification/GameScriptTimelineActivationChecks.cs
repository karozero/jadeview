using JadeView;

internal static class GameScriptTimelineActivationChecks
{
    internal static void Verify()
    {
        var actor = new SceneObject
        {
            GaoKey = 0x100,
            Flags = (uint)GameScriptEotTable.AnimatedObjects,
            PreviewControlFlags = GameObjectRuntimeFlags.Inactive
        };
        GameScriptTimelineActivationForecast inactive =
            GameScriptTimelineActivationForecaster.Evaluate(actor);
        Require(inactive.RuntimeFlags2 == GameObjectRuntimeFlags.Inactive
            && inactive.NormalTime.InActivatedObjectTable == false
            && inactive.SpecialTime.InActivatedObjectTable == false,
            "Inactive preview flags2 must block a rebuild under both Iris time modes when the special-time bit is absent.");
        Require(inactive.EotMembership.TableNames.Contains("actor animation"),
            "EOT table identities must remain visible in the diagnostic even when activation is blocked.");

        actor.PreviewControlFlags = 0;
        GameScriptTimelineActivationForecast normalOnly =
            GameScriptTimelineActivationForecaster.Evaluate(actor);
        Require(normalOnly.NormalTime.InActivatedObjectTable == true
            && normalOnly.SpecialTime.InActivatedObjectTable == false,
            "Clearing the inactive bit should make the object eligible only in normal time without the special-time activation bit.");

        actor.PreviewControlFlags = 0x04000000;
        GameScriptTimelineActivationForecast specialOnly =
            GameScriptTimelineActivationForecaster.Evaluate(actor);
        Require(specialOnly.NormalTime.InActivatedObjectTable == false
            && specialOnly.SpecialTime.InActivatedObjectTable == true,
            "The special-time activation bit must produce the opposite conditional result.");

        actor.Flags |= 1;
        GameScriptTimelineActivationForecast identityExcluded =
            GameScriptTimelineActivationForecaster.Evaluate(actor);
        Require(identityExcluded.NormalTime.InActivatedObjectTable == false
            && identityExcluded.SpecialTime.InActivatedObjectTable == false,
            "The world-rebuild identity skip bit must override both time-mode predicates.");

        Console.WriteLine("PASS: cinematic timeline flags2 activation forecast and EOT diagnostics");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
