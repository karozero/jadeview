using JadeView;

internal static class CinematicScriptCallbackEntryTimeChecks
{
    internal static void Verify()
    {
        CinematicScriptReachableOvaFieldAccess[] accesses =
        [
            Access("f_time", "read", "v2 = (clock - v2->f_time) / duration;", 12, "fade_in"),
            Access("f_time", "write", "v1->f_time = TIM_gf_MainClock - TIM_gf_LockTime;", 20, "begin_fade"),
            Access("f_time", "write", "v1->f_time = TIM_gf_MainClock - TIM_gf_LockTime;", 24, "begin_fade")
        ];

        Dictionary<string, double?> inputs = CinematicScriptCallbackEntryTimeResolver.Resolve(
            accesses, "fade_in", "begin_fade", 22, 50.65f, _ => true);
        Require(inputs.GetValueOrDefault("f_time") == 50.65f
            && inputs.GetValueOrDefault(
                CinematicScriptCallbackEntryTimeResolver.RelativeClockAliasPrefix + "f_time") == 1,
            "A uniquely written elapsed-clock field immediately before the callback dispatch was not carried into the destination callback.");

        Dictionary<string, double?> ambiguousInputs = CinematicScriptCallbackEntryTimeResolver.Resolve(
            accesses, "fade_in", "begin_fade", 30, 50.65f, _ => true);
        Require(!ambiguousInputs.ContainsKey("f_time")
            && !ambiguousInputs.ContainsKey(
                CinematicScriptCallbackEntryTimeResolver.RelativeClockAliasPrefix + "f_time"),
            "Multiple possible callback-entry clock writes must remain unresolved.");

        Dictionary<string, double?> gatedInputs = CinematicScriptCallbackEntryTimeResolver.Resolve(
            accesses, "fade_in", "begin_fade", 22, 50.65f, _ => false);
        Require(!gatedInputs.ContainsKey("f_time")
            && !gatedInputs.ContainsKey(
                CinematicScriptCallbackEntryTimeResolver.RelativeClockAliasPrefix + "f_time"),
            "A callback-entry clock write behind an unresolved or false gate must not be applied.");

        Console.WriteLine("PASS: callback entry time is carried only from one proven pre-dispatch elapsed-clock write");
    }

    private static CinematicScriptReachableOvaFieldAccess Access(string field, string access,
        string expression, int line, string function) => new(field, access, "instance", "v1",
        expression, [], "fixture.c", line, function, function);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
