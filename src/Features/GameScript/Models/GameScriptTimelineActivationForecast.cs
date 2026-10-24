namespace JadeView;

internal sealed record GameScriptTimelineActivationPath(
    bool? InActivatedObjectTable,
    string Explanation);

internal sealed record GameScriptTimelineActivationForecast(
    uint RuntimeFlags2,
    GameScriptTimelineActivationPath NormalTime,
    GameScriptTimelineActivationPath SpecialTime,
    GameScriptEotMembership EotMembership);

/// <summary>
/// Projects the selected object's current cinematic flags2 through Iris's next
/// activation-table rebuild. This is a diagnostic projection, not callback dispatch.
/// </summary>
internal static class GameScriptTimelineActivationForecaster
{
    internal static GameScriptTimelineActivationForecast Evaluate(SceneObject sceneObject)
    {
        uint flags2 = sceneObject.PreviewControlFlags
            ?? GameScriptSerializedObjectState.ProjectInitialFlags2(sceneObject.SerializedStateFlags);
        var objectState = new GameScriptWorldObjectSnapshot(sceneObject, flags2,
            InActivatedObjectTable: false);
        GameScriptTimelineActivationPath normalTime = EvaluatePath(objectState, specialTime: false);
        GameScriptTimelineActivationPath specialTime = EvaluatePath(objectState, specialTime: true);

        return new(flags2, normalTime, specialTime,
            GameScriptEotMembershipResolver.Evaluate(sceneObject.Flags));
    }

    private static GameScriptTimelineActivationPath EvaluatePath(
        GameScriptWorldObjectSnapshot objectState, bool specialTime)
    {
        GameScriptWorldActivationRebuild rebuild = GameScriptWorldActivationRebuildReducer
            .Evaluate([objectState], specialTime);
        GameScriptWorldActivationObjectState result = rebuild.Objects.Single();
        return new(result.InActivatedObjectTable, result.Explanation);
    }
}
