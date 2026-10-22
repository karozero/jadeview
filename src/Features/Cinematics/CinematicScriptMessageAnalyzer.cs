using System.Globalization;
using System.Numerics;

namespace JadeView;

internal static class CinematicScriptMessageAnalyzer
{
    private const int CompanionCinematicMessage = 46;
    private const int CompanionStatusMessage = 49;
    private const int SallyCinematicModeMessage = 47;
    private const int FacialMessage = 53;
    private const int CameraMessage = 60;
    private const string CompanionCinematicCallback = "companion_co_action_cine";
    private const string CompanionMessageCallback = "companion_co_exec_reflex";
    private const string CompanionStatusMessageCallback = "companion_co_exec_get_message";
    private const string SallyMessageCallback = "Sally_sa_exec_message_read";
    private const string AutomaticLipSyncCallback = "Cine_lipsynch_auto_Lips_lecture_messages";
    private const string CameraMainLoopCallback = "camera_cam_1_mainloop";

    internal static string? Describe(CinematicTimelineMessage message,
        IEnumerable<CinematicScriptOwner> owners, uint preferredWorldKey)
    {
        var executionOwner = CinematicScriptOwnerResolver.Resolve(message.ExecutionObjectKey, preferredWorldKey, owners);
        if (executionOwner == null)
            return null;
        string executionContext = $"EVE track execution object resolves to '{executionOwner.Name}' ({executionOwner.OwnerKey:X8}); ";

        if (message.Type == FacialMessage)
            return Prefix(executionContext, DescribeFacialMessage(message, executionOwner))
                ?? DescribeSourceMessageReader(message, executionOwner, executionContext);

        if (message.Type == CameraMessage)
            return Prefix(executionContext, DescribeCameraMessage(message, executionOwner))
                ?? DescribeSourceMessageReader(message, executionOwner, executionContext);

        if (message.Type == SallyCinematicModeMessage)
            return Prefix(executionContext, DescribeSallyCinematicModeMessage(message, executionOwner))
                ?? DescribeSourceMessageReader(message, executionOwner, executionContext);

        if (message.Type == CompanionStatusMessage)
            return Prefix(executionContext, DescribeCompanionStatusMessage(message, executionOwner))
                ?? DescribeSourceMessageReader(message, executionOwner, executionContext);

        if (message.Type != CompanionCinematicMessage
            || !CinematicScriptReader.TryDescribeCallbackReachability(executionOwner,
                CompanionCinematicCallback, out string companionPath))
            return DescribeSourceMessageReader(message, executionOwner, executionContext);

        string gazeTarget = message.Gao is 0 or uint.MaxValue
            ? "clears the stored gaze target"
            : $"stores GAO {message.Gao:X8} as the gaze target";
        string turnSpeed = message.Float2 == 0
            ? "resets the requested head-turn speed to the callback's default 1.0"
            : $"sets the requested head-turn speed to {message.Float2:G}";
        return executionContext + $"Iris companion cinematic callback for message 46: {gazeTarget} and {turnSpeed}; the current rotation speed eases toward that value. Source callback path: {companionPath}. Runtime branch and callback execution are not evaluated.";
    }

    private static string? DescribeSourceMessageReader(CinematicTimelineMessage message,
        CinematicScriptOwner executionOwner, string executionContext)
    {
        var readers = CinematicScriptReader.ReachableMessageReaders(executionOwner, message.Type);
        if (readers.Length == 0) return null;

        string readerEvidence = string.Join("; ", readers.Take(4).Select(reader =>
            $"{reader.FunctionName} at {reader.Source}:{reader.Line} via {reader.CallbackPath}"));
        string overflow = readers.Length > 4 ? $" (+{readers.Length - 4} more read sites)" : "";
        return executionContext + $"Iris source reads message ID {message.Type} from this object's message queue "
            + $"at {readerEvidence}{overflow}. The receiver callback's field interpretation is not classified "
            + "here; source reachability does not prove callback execution or message delivery.";
    }

    private static string? Prefix(string prefix, string? description) =>
        description == null ? null : prefix + description;

    private static string? DescribeFacialMessage(CinematicTimelineMessage message, CinematicScriptOwner receiver)
    {
        if (CinematicScriptReader.TryDescribeCallbackReachability(receiver, AutomaticLipSyncCallback,
                out string automaticLipSyncPath))
        {
            string actorOverride = message.Gao is 0 or uint.MaxValue
                ? "The message does not override the configured target actor. "
                : $"The message routes the lip-sync target to GAO {message.Gao:X8}. ";
            return $"Iris automatic lip-sync callback consumes message 53: {actorOverride}Int1 supplies the mouth-channel speed ({message.Int1}); Vector2.Y supplies the progression/form value ({message.Vector2.Y:G}), which the callback splits into a form index and fractional factor. Source callback path: {automaticLipSyncPath}. This is script-level facial animation data, not a phoneme transcript or an audio-driven analysis; runtime branch and execution are not evaluated.";
        }

        if (CinematicScriptReader.TryDescribeCallbackReachability(receiver, CompanionMessageCallback,
                out string companionPath))
            return $"Iris companion callback consumes message 53 and updates expression/mouth morph-channel targets from the message vectors, speed fields, and integer progression. Source callback path: {companionPath}. The callback uses several configured channels; this message is facial control rather than a body-action request, and runtime execution is not evaluated.";

        if (CinematicScriptReader.TryDescribeCallbackReachability(receiver, SallyMessageCallback,
                out string sallyPath))
        {
            string mode = message.Int2 == -1
                ? "clears the cinematic-expression mode"
                : "enables the cinematic-expression mode";
            return $"Iris Sally script consumes message 53 and {mode} (Int2={message.Int2}); the message vectors and speeds feed its expression-control path. Source callback path: {sallyPath}. This does not prove that the nested callback ran or that facial morphs were evaluated in the preview.";
        }

        return null;
    }

    private static string? DescribeCameraMessage(CinematicTimelineMessage message, CinematicScriptOwner executionOwner)
    {
        if (!CinematicScriptReader.TryDescribeCallbackReachability(executionOwner, CameraMainLoopCallback,
                out string cameraPath))
            return null;

        if (message.Int1 is 1 or 2)
        {
            string position = DescribeReference(message.Gao, "camera destination");
            string target = DescribeReference(message.Gao2, "look target");
            string transition = message.Float2 == 0
                ? "Float2=0 requests an immediate cut"
                : $"Float2={message.Float2:G} supplies the transition speed";
            return $"Iris camera main loop handles type 60 / Int1={message.Int1} as a cinematic-camera mode request: "
                + $"GAO is the {position}, GAO2 is the {target}; Vector1 carries the camera position, "
                + $"Vector2 the target offset, Float1 the focal value, and {transition}. Source callback path: {cameraPath}.";
        }

        if (message.Int1 == 0)
            return "Iris camera main loop handles type 60 / Int1=0 as a cinematic-camera reset: it clears the "
                + $"cinematic mode, follow/target objects and camera offsets; Float2 controls the cut/transition branch. Source callback path: {cameraPath}.";

        return $"Iris camera main loop also receives type 60 with Int1={message.Int1}, but this value follows a "
            + "different camera/effect branch; JadeView leaves its exact effect semantics unresolved.";
    }

    private static string? DescribeSallyCinematicModeMessage(CinematicTimelineMessage message,
        CinematicScriptOwner executionOwner)
    {
        if (!CinematicScriptReader.TryDescribeCallbackReachability(executionOwner, SallyMessageCallback,
                out string callbackPath))
            return null;

        if (message.Int2 == 0)
            return "Iris Sally message handler consumes type 47 / Int2=0 as a cinematic-mode exit: it clears automatic mouth cycling and cinematic expression state, restores the saved collision activation, and switches callback track 2 back to Sally's 2D handler. Int1 does not select an action on this exit branch. "
                + $"Source callback path: {callbackPath}. Runtime delivery and state are not evaluated.";

        string actionRequest = message.Int1 == -1
            ? "Int1=-1 leaves the current action unchanged"
            : $"Int1={message.Int1} is passed to ACTActionSet as the requested action";
        string expressionSeed = IsZero(message.Vector1)
            ? "the vectors do not replace the stored expression seed"
            : "the handler also copies Vector1/Vector2 into a type-53 expression-control message";
        return "Iris Sally message handler consumes type 47 with nonzero Int2 as cinematic-mode entry: "
            + $"{actionRequest}; on first entry it saves callback track 2 and collision activation, then switches track 2 to Sally's cinematic action callback; {expressionSeed}. "
            + $"Source callback path: {callbackPath}. Runtime delivery and state are not evaluated.";
    }

    private static string? DescribeCompanionStatusMessage(CinematicTimelineMessage message,
        CinematicScriptOwner executionOwner)
    {
        if (!CinematicScriptReader.TryDescribeCallbackReachability(executionOwner,
                CompanionStatusMessageCallback, out string callbackPath))
            return null;

        string actionBehavior = message.Int1 == 4
            ? $"status 4 stores Int2={message.Int2} as a deferred companion action"
            : message.Int2 == -1
                ? $"status {message.Int1} leaves the current action unchanged (Int2=-1)"
                : $"status {message.Int1} requests action Int2={message.Int2} through ACTActionSet";
        string positionBehavior = IsZero(message.Vector1)
            ? "zero Vector1 uses the companion's current position"
            : $"Vector1 supplies position ({FormatVector(message.Vector1)})";
        string sightBehavior = IsZero(message.Vector2)
            ? "zero Vector2 uses the companion's current sight direction"
            : $"Vector2 supplies sight direction ({FormatVector(message.Vector2)})";
        string worldBehavior = message.Float2 == 0
            ? "Float2=0 uses the current world ID"
            : $"Float2={message.Float2:G} supplies the companion world ID";
        string specialStatus = message.Int1 == 3
            ? " Status 3 also sets the collision-start matrix from Vector1."
            : "";

        return $"Iris companion message handler consumes type 49: Int1 is the requested status; {actionBehavior}; "
            + $"{positionBehavior}; {sightBehavior}; {worldBehavior}. It stores the resulting status, position, sight and world in the shared companion table, then requests companion mode selection.{specialStatus} "
            + "Updates are skipped when status/world are unchanged, except status 3; current companion modes 9 and 17 defer application. "
            + $"Source callback path: {callbackPath}. Runtime delivery and state are not evaluated.";
    }

    private static bool IsZero(Vector3 vector) => vector == Vector3.Zero;

    private static string FormatVector(Vector3 vector) =>
        $"{vector.X.ToString("G", CultureInfo.InvariantCulture)}; {vector.Y.ToString("G", CultureInfo.InvariantCulture)}; {vector.Z.ToString("G", CultureInfo.InvariantCulture)}";

    private static string DescribeReference(uint key, string role) => key switch
    {
        0 => $"null {role}",
        uint.MaxValue => $"invalid {role} key",
        _ => $"{role} GAO {key:X8}"
    };
}
