using System.Reflection;
using System.Globalization;
using System.Text.Json;

namespace JadeView;

internal static class CinematicEngineFunctionCatalog
{
    private const string ResourceName = "JadeView.EngineFunctionSelectors";
    private static readonly Lazy<IReadOnlyDictionary<int, string>> Names = new(ReadNames);

    internal static string NameFor(int selector) => Names.Value.TryGetValue(selector, out string? name)
        ? name.Replace("AI_EvalFunc_", "", StringComparison.Ordinal)
        : $"EngineFunction_{selector:X4}";

    internal static int[] GameObjectArgumentIndices(CinematicLiteralCall call) => NameFor(call.Selector) switch
    {
        "OBJ_OrientTo" or "OBJ_HierarchySet" or "OBJ_Rejoin" or "OBJ_RejoinAndOrient"
            when call.Arguments.Length > 0 => [0],
        _ => []
    };

    internal static string Describe(CinematicLiteralCall call)
    {
        string worldCommandDescription = CinematicWorldCommandResolver.Describe(call);
        if (worldCommandDescription.Length > 0) return worldCommandDescription;

        string function = NameFor(call.Selector);
        return function switch
        {
            "ARRGlobalIntFlagSet" when call.Arguments.Length >= 1 =>
                $"set global saved flag {call.Arguments[0]}",
            "ARRGlobalIntFlagClear" when call.Arguments.Length >= 1 =>
                $"clear global saved flag {call.Arguments[0]}",
            "ARRGlobalIntFlagTest" when call.Arguments.Length >= 1 =>
                $"test whether global saved flag {call.Arguments[0]} is set",
            "IoPifSet" when call.Arguments.Length >= 1 =>
                DescribePif(call.Arguments[0]),
            "IoPafSet" when call.Arguments.Length >= 2 =>
                DescribePaf(call.Arguments[0], call.Arguments[1]),
            "Gsp_AE_MASTEREVT" when call.Arguments.Length >= 3 =>
                DescribeAfterEffectsMasterEvent(call),
            "OBJDestroy" => "destroy target; Iris removes/detaches it when supported, otherwise hides/deactivates it and stops its sound",
            "OBJ_OrientTo" when call.Arguments.Length >= 1 =>
                $"rotate target to face object {call.Arguments[0]:X8} while preserving target position",
            "OBJRotateLocalX" or "OBJRotateLocalY" or "OBJRotateLocalZ"
                or "OBJRotateGlobalX" or "OBJRotateGlobalY" or "OBJRotateGlobalZ"
                when call.Arguments.Length >= 1 => DescribeAxisRotation(function, call.Arguments[0]),
            "OBJ_HierarchySet" when call.Arguments.Length >= 1 =>
                $"attach target under object {call.Arguments[0]:X8}, retaining its current world transform",
            "OBJ_HierarchyReset" =>
                "clear target's parent-hierarchy binding and remove it from the world's hierarchy update table",
            "OBJReinit" when call.Arguments.Length >= 1 =>
                $"reinitialize target (mode {call.Arguments[0]}) and reattach it to the current world",
            "MSGSendEx" when call.Arguments.Length >= 2 =>
                DescribeExtendedMessage(call.Arguments[0], call.Arguments[1]),
            "OBJ_FlagInvisibleSet" when call.Arguments.Length >= 1 => call.Arguments[0] != 0
                ? "hide target" : "show target",
            "OBJ_FlagInactiveSet" when call.Arguments.Length >= 1 => call.Arguments[0] != 0
                ? "mark target inactive and refresh world visibility/registration"
                : "clear target inactive state and refresh world visibility/registration",
            "OBJ_CapaSet" when call.Arguments.Length >= 2 =>
                $"set capabilities 0x{call.Arguments[0]:X}, remove 0x{call.Arguments[1]:X}",
            "OBJDrawMaskSet" when call.Arguments.Length >= 2 =>
                $"add draw-mask bits 0x{call.Arguments[0]:X}, remove 0x{call.Arguments[1]:X}",
            "OBJ_FlagsControlSet" when call.Arguments.Length >= 2 =>
                $"add control flags 0x{call.Arguments[0]:X}, remove 0x{call.Arguments[1]:X}",
            "OBJ_CustomBitsSet" when call.Arguments.Length >= 2 =>
                $"set target custom bits 0x{call.Arguments[0] & 0xFF00:X}, "
                + $"clear 0x{call.Arguments[1] & 0xFF00:X} (engine applies only bits 8–15)",
            "OBJ_RejoinAndOrient" when call.Arguments.Length >= 1 =>
                $"copy the target's absolute transform from object {call.Arguments[0]:X8}",
            "OBJ_Rejoin" when call.Arguments.Length >= 1 =>
                $"move target to the absolute position of object {call.Arguments[0]:X8}",
            "OBJ_FlagsIdentitySet" when call.Arguments.Length >= 2 =>
                $"add identity flags 0x{call.Arguments[0]:X}, remove 0x{call.Arguments[1]:X}",
            "OBJ_CustomBitsTest" when call.Arguments.Length >= 1 =>
                $"test whether target flags2 bits 8–15 intersect mask 0x{call.Arguments[0] & 0xFF00:X}",
            "OBJFreezeModifier" when call.Arguments.Length >= 2 =>
                $"{(call.Arguments[1] != 0 ? "freeze" : "unfreeze")} target modifier type {call.Arguments[0]}",
            "OBJ_BoneDisplayOrder" when call.Arguments.Length >= 2 =>
                DescribeBoneDisplayOrder(call.Arguments[0], call.Arguments[1]),
            "OBJ_AnimMorphFactorNoKey" when call.Arguments.Length >= 3 =>
                $"drive morph channel {call.Arguments[2]} on object {call.Arguments[0]:X8} "
                + $"(channel selector {call.Arguments[1]}) from this carrier object's X/Y transform",
            "OBJ_SetModuleChannelData" when call.Arguments.Length >= 4 =>
                $"set module-channel entry {call.Arguments[1]}.{call.Arguments[2]} "
                + $"to {call.Arguments[3]} on target (channel selector {call.Arguments[0]})",
            "SNDPlayModifier" when call.Arguments.Length >= 1 =>
                DescribeSoundModifierCommand("play", call.Arguments[0]),
            "SNDPauseModifier" when call.Arguments.Length >= 1 =>
                DescribeSoundModifierCommand("pause", call.Arguments[0]),
            "SNDStopModifier" when call.Arguments.Length >= 1 =>
                DescribeSoundModifierCommand("stop", call.Arguments[0]),
            "SNDSetFlagModifier" when call.Arguments.Length >= 2 =>
                DescribeSoundModifierFlagCommand("set", call.Arguments[0], call.Arguments[1]),
            "SNDResetFlagModifier" when call.Arguments.Length >= 2 =>
                DescribeSoundModifierFlagCommand("clear", call.Arguments[0], call.Arguments[1]),
            "TRAPlay" when call.Arguments.Length >= 1 =>
                DescribeEventTrackCommand("start", call.Arguments[0]),
            "TRAStop" when call.Arguments.Length >= 1 =>
                DescribeEventTrackCommand("stop", call.Arguments[0]),
            "TRAPause" when call.Arguments.Length >= 1 =>
                DescribeEventTrackCommand("pause", call.Arguments[0]),
            "TRAUnPause" when call.Arguments.Length >= 1 =>
                DescribeEventTrackCommand("unpause", call.Arguments[0]),
            "TRAReinit" when call.Arguments.Length >= 1 =>
                DescribeEventTrackCommand("reset", call.Arguments[0]),
            "TRAPlayAll" => "start every embedded event track on the target object",
            "TRAStopAll" => "stop every embedded event track on the target object",
            "TRAPauseAll" => "pause every embedded event track on the target object",
            "TRAUnPauseAll" => "unpause every embedded event track on the target object",
            "TRAReinitAll" => "reset every embedded event track on the target object",
            "COLSetActivationSet" when call.Arguments.Length >= 2 =>
                $"set collision activation bits 0x{call.Arguments[0]:X}, clear 0x{call.Arguments[1]:X}",
            "COLMapActivationSet" when call.Arguments.Length >= 2 =>
                $"set target collision-map activation bits 0x{call.Arguments[0]:X}, clear 0x{call.Arguments[1]:X}",
            "WORColorAmbiantSet" when call.Arguments.Length >= 2 =>
                $"set world-table slot {call.Arguments[0]} ambient color 1 to 0x{call.Arguments[1]:X8}",
            "WORColorAmbiant2Set" when call.Arguments.Length >= 2 =>
                $"set world-table slot {call.Arguments[0]} ambient color 2 to 0x{call.Arguments[1]:X8}",
            "LightSetFlag" when call.Arguments.Length >= 2 =>
                $"{(call.Arguments[1] != 0 ? "set" : "clear")} light flag bit {unchecked((sbyte)call.Arguments[0])} on target",
            "GRPSectInvisibleSet" when call.Arguments.Length >= 2 =>
                DescribeGroupSectionVisibility(call.Arguments[0], call.Arguments[1]),
            "ANICurrentFrameSet" when call.Arguments.Length >= 2 =>
                DescribeAnimationFrameSet(call.Arguments[0], call.Arguments[1]),
            "SNDPrefetchModifier" when call.Arguments.Length >= 1 =>
                DescribeSoundModifierPrefetch(call.Arguments[0]),
            "VIEWAssignObject" when call.Arguments.Length >= 1 =>
                $"assign target object to world view slot {call.Arguments[0]} (returns the previous assigned object)",
            "CIN_OnOff" when call.Arguments.Length >= 1 =>
                $"Iris stub consumes value {call.Arguments[0]} and emits a debug trace; no cinematic state change is implemented here",
            "DYNOn" => "enable target dynamics and register it for the world's dynamics update",
            "DYNOff" => "disable target dynamics and remove it from the world's dynamics update",
            "DYNFlagsSet" when call.Arguments.Length >= 2 =>
                $"add dynamics flags 0x{call.Arguments[0]:X}, remove 0x{call.Arguments[1]:X}",
            "IoJoyFreeze" when call.Arguments.Length >= 1 =>
                $"replace the global joystick mask with bitwise complement of 0x{call.Arguments[0]:X8}",
            "LightSetColor" when call.Arguments.Length >= 1 =>
                $"set target light packed color to 0x{call.Arguments[0]:X8}",
            "ANIShapeSelect" when call.Arguments.Length >= 2 =>
                $"request shape {call.Arguments[1] & 0xFF} on target animation channel {call.Arguments[0] & 0xFF}",
            "GRPSectInactiveSet" when call.Arguments.Length >= 2 =>
                $"Iris stub consumes section arguments {call.Arguments[0]} and {call.Arguments[1]} and emits a debug trace; no inactive-state change is implemented here",
            "SNDMicroPosSet" =>
                "use target object as the global microphone-position source for subsequent audio requests",
            "SNDRequest" when call.Arguments.Length >= 2 =>
                $"request sound slot {call.Arguments[0]} on target with flags 0x{call.Arguments[1]:X8}"
                + ((call.Arguments[1] & 0x800) != 0 ? "; Iris also starts playback immediately" : ""),
            "MATEnableMltxtLvlMT" when call.Arguments.Length >= 4 =>
                $"update multi-texture material {call.Arguments[0]:X8}, level selectors {call.Arguments[1]}/{call.Arguments[2]}, "
                + $"index {call.Arguments[3]} (mode {(call.Arguments[3] != 0 ? "clears" : "sets")} native flag 0x800)",
            "MATEnableMltxtLvl" when call.Arguments.Length >= 3 =>
                $"{(call.Arguments[2] != 0 ? "enable" : "disable")} multi-texture level {call.Arguments[1]} "
                + $"on material {call.Arguments[0]:X8}",
            "SetLocalAlpha" when call.Arguments.Length >= 3 =>
                $"set local alpha on material {call.Arguments[0]:X8}, texture layer {call.Arguments[1]} "
                + $"to {call.Arguments[2]} (stored in steps of 8)",
            "SetALPHATRESH" when call.Arguments.Length >= 3 =>
                $"set alpha threshold on material {call.Target:X8}, submaterial {call.Arguments[0]}, "
                + $"texture layer {call.Arguments[1]} to {call.Arguments[2]} (stored in six-bit steps of 4)",
            "XMEC_OnOff" when call.Arguments.Length >= 2 =>
                DescribeXmecCommand(call.Arguments[0], call.Arguments[1]),
            "XMEN_OnOff" when call.Arguments.Length >= 2 =>
                DescribeXmenCommand(call.Arguments[0], call.Arguments[1]),
            "ACTActionSet" when call.Arguments.Length >= 1 =>
                $"set target action {call.Arguments[0]} and restart its action clock",
            "ACTActionFrameSet" when call.Arguments.Length >= 2 =>
                $"set target action {call.Arguments[0]} and place its action clock at frame {call.Arguments[1]}",
            "ACTActionFrequencySet" when call.Arguments.Length >= 2 =>
                $"set target action {call.Arguments[0]} and restart it at frequency {call.Arguments[1]} "
                + $"(engine stores {call.Arguments[1] * 60.0 / 64.0:0.###})",
            "ACTKitMerge" when call.Arguments.Length >= 3 =>
                DescribeActionKitMerge(call.Arguments, overwriteExisting: false),
            "ACTKitMergeOver" when call.Arguments.Length >= 3 =>
                DescribeActionKitMerge(call.Arguments, overwriteExisting: true),
            "ANIFrequencySet" when call.Arguments.Length >= 2 =>
                $"set animation track {call.Arguments[0]} frequency to {call.Arguments[1]} "
                + "(engine converts the value by 60/64 before storing it)",
            "GRPInvisibleSet" when call.Arguments.Length >= 1 => call.Arguments[0] != 0
                ? "hide every object in the target group"
                : "show every object in the target group",
            "GRPInactiveSet" when call.Arguments.Length >= 1 => call.Arguments[0] != 0
                ? "mark every object in the target group inactive"
                : "clear inactive state on every object in the target group",
            _ => ""
        };
    }

    private static string DescribeSoundModifierCommand(string action, uint serializedId)
    {
        int modifierId = unchecked((int)serializedId);
        if (modifierId == -10)
            return $"{action} command is ignored by the engine's sound-modifier handler";
        if (modifierId == -1)
            return $"{action} all sound modifiers attached to the target object";
        return $"{action} sound modifier {modifierId} attached to the target object";
    }

    private static string DescribeSoundModifierPrefetch(uint serializedId)
    {
        int modifierId = unchecked((int)serializedId);
        if (modifierId == -10)
            return "sound-modifier prefetch is ignored by Iris";
        string target = modifierId == -1
            ? "all sound modifiers attached to the target object"
            : $"sound modifier {modifierId} attached to the target object";
        return $"prefetch sound resources for {target}";
    }

    private static string DescribeAnimationFrameSet(uint trackIndex, uint frame)
    {
        string seconds = (frame / 60.0).ToString("0.###", CultureInfo.InvariantCulture);
        return $"set animation track {trackIndex} time to frame {frame} ({seconds} seconds at 60 frames per second)";
    }

    private static string DescribeGroupSectionVisibility(uint serializedSection, uint serializedOperation)
    {
        int section = unchecked((int)serializedSection);
        int operation = unchecked((int)serializedOperation);
        string sectionName = section is -1 or 0xffff ? "all sections" : $"section {section}";
        string action = operation switch
        {
            -1 => "reset visibility suppression",
            0 => "decrement visibility suppression",
            _ => "increment visibility suppression"
        };
        return $"{action} on target group and {sectionName}";
    }

    private static string DescribeExtendedMessage(uint messageId, uint serializedPayload)
    {
        string payload = unchecked((float)(int)serializedPayload)
            .ToString("0.###", CultureInfo.InvariantCulture);
        return $"queue extended message {messageId} to the target script owner with float payload {payload}; "
            + "sender is the current execution object";
    }

    private static string DescribeAxisRotation(string function, uint serializedAngle)
    {
        string space = function.Contains("Local", StringComparison.Ordinal) ? "local" : "global";
        char axis = function[^1];
        float angle = BitConverter.UInt32BitsToSingle(serializedAngle);
        if (!float.IsFinite(angle))
            return $"rotate target around the {space} {axis} axis (non-finite serialized angle 0x{serializedAngle:X8})";
        return $"rotate target around the {space} {axis} axis by {angle.ToString("0.###", CultureInfo.InvariantCulture)} radians; "
            + "during EVE playback, the angle is distributed across the event's frame intervals";
    }

    private static string DescribeXmecCommand(uint serializedUserId, uint serializedOnOff)
    {
        string action = serializedOnOff != 0 ? "enable" : "disable";
        return $"{action} XMEC modifier type 10 on the target and actor skeleton group "
            + $"(user ID {serializedUserId}; Iris ignores this parameter)";
    }

    private static string DescribeXmenCommand(uint serializedUserId, uint serializedOnOff)
    {
        string user = serializedUserId == uint.MaxValue ? "all user IDs" : $"user ID {serializedUserId}";
        string action = serializedOnOff != 0 ? "activate" : "deactivate";
        return $"{action} matching XMEN modifiers on target and its skeleton objects ({user}; Jaded source behavior)";
    }

    private static string DescribeEventTrackCommand(string action, uint serializedIndex)
    {
        int trackIndex = unchecked((int)serializedIndex);
        return action switch
        {
            "start" => $"start embedded event track {trackIndex} (an already-running track is not restarted)",
            "stop" => $"stop embedded event track {trackIndex}",
            "pause" => $"pause embedded event track {trackIndex}",
            "unpause" => $"unpause embedded event track {trackIndex}",
            _ => $"reset embedded event track {trackIndex} to time zero and clear pause state"
        };
    }

    private static string DescribeSoundModifierFlagCommand(string action, uint serializedId, uint flagMask)
    {
        int modifierId = unchecked((int)serializedId);
        if (modifierId == -10)
            return $"sound-modifier flag {action} command is ignored by the engine";
        string target = modifierId == -1
            ? "all sound modifiers attached to the target object"
            : $"sound modifier {modifierId} attached to the target object";
        return $"{action} runtime flags 0x{flagMask:X} on {target}";
    }

    private static string DescribeActionKitMerge(uint[] arguments, bool overwriteExisting)
    {
        uint firstAction = arguments[1];
        uint lastAction = arguments[2];
        string rangeDescription = firstAction <= lastAction
            ? $"actions {firstAction}–{lastAction}"
            : $"reversed action range {firstAction}–{lastAction} (source loop copies none)";
        string overwriteDescription = overwriteExisting
            ? "replacing target slots in the range (including empty source slots)"
            : "without overwriting existing target actions";
        return $"merge {rangeDescription} from GAO {arguments[0]:X8}, {overwriteDescription}";
    }

    private static string DescribeBoneDisplayOrder(uint channelSelector, uint displayOrder)
    {
        int channel = unchecked((int)channelSelector);
        string target = channel == -1 ? "target actor" : $"target animation channel {channel}";
        return $"set {target} bone display-list order to {unchecked((sbyte)displayOrder)}";
    }

    private static string DescribePif(uint serializedValue)
    {
        int value = unchecked((int)serializedValue);
        float durationSeconds = Math.Abs((long)value) * 0.032f;
        return $"request controller haptics {(value == 0 ? "off" : "on")}, "
            + $"with duration {durationSeconds:0.###} seconds when enabled";
    }

    private static string DescribePaf(uint serializedIntensity, uint serializedDuration)
    {
        int requestedIntensity = unchecked((int)serializedIntensity);
        int appliedIntensity = requestedIntensity > 255 ? 255 : requestedIntensity;
        int durationUnits = unchecked((int)serializedDuration);
        float durationSeconds = Math.Abs((long)durationUnits) * 0.032f;
        return $"request controller haptic intensity {appliedIntensity} "
            + $"for {durationSeconds:0.###} seconds when enabled";
    }

    private static string DescribeAfterEffectsMasterEvent(CinematicLiteralCall call)
    {
        int mode = unchecked((int)call.Arguments[0]);
        bool enabled = call.Arguments[1] != 0;
        int variant = unchecked((int)call.Arguments[2]);
        string state = enabled ? "enable" : "disable";
        string parameterSource = $"; parameter comes from target object's world X position ({call.Target:X8})";

        return mode switch
        {
            0 => $"{state} depth blur, warp variant {variant}{parameterSource}",
            1 => $"{state} motion blur{parameterSource}",
            2 => $"{state} screen blur{parameterSource}",
            4 => $"{state} motion smoothing",
            5 => $"{state} zoom smoothing{parameterSource}",
            6 => $"{state} secondary zoom smoothing{parameterSource}",
            7 => $"{state} remanence{parameterSource}",
            8 => "unknown After Effects master mode 8; Iris stores its target-X parameter",
            9 => $"set color-balance parameter{parameterSource}",
            11 => $"{state} black-and-white effect{parameterSource}",
            12 => $"{state} texture-factor effect, variant {variant}{parameterSource}",
            14 => $"{state} depth-blur near plane{parameterSource}",
            15 => $"{state} screen opacity{parameterSource}",
            _ => ""
        };
    }

    private static IReadOnlyDictionary<int, string> ReadNames()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded function selector map '{ResourceName}' is missing.");
        var serialized = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("Embedded function selector map is empty.");
        return serialized.ToDictionary(pair => Convert.ToInt32(pair.Key, 16), pair => pair.Value);
    }
}
