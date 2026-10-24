namespace JadeView;

/// <summary>
/// Identifies the engine path that invokes a script command. This is source context,
/// not evidence that a callback or event was executed by the original game.
/// </summary>
internal enum GameScriptExecutionContext
{
    Unknown,
    EveEvent,
    AiCallback
}
