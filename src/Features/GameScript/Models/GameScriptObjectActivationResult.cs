namespace JadeView;

internal enum GameScriptObjectActivationResult
{
    Eligible,
    AlreadyActive,
    TimeModeUnknown,
    NormalTimeControlBlocked,
    NormalTimeStatusBlocked,
    SpecialTimeBitMissing
}
