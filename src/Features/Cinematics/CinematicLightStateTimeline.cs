namespace JadeView;

/// <summary>
/// Replays only the bounded, literal object-light setters present in a cinematic
/// call timeline. Each evaluation starts from immutable scene data so seeking is
/// deterministic; it is not an AI callback or VM simulator.
/// </summary>
internal sealed class CinematicLightStateTimeline
{
    private const int LightSetColor = 0x2135;
    private const int LightSetNearFar = 0x2136;
    private const int LightSetRelatedObject = 0x2139;
    private const int LightSetFlag = 0x213B;

    private readonly SceneObject[] sceneObjects;
    private readonly (float Time, CinematicLiteralCall Call)[] commands;
    private readonly Dictionary<uint, SceneObject[]> objectsByKey;
    private readonly Dictionary<SceneObject, SceneLight> baselineLights;
    private readonly CinematicLightStateEvaluation baselineEvaluation;
    private CinematicLightStateEvaluation? cachedEvaluation;
    private int cachedPrefixCount = -1;

    internal CinematicLightStateTimeline(IEnumerable<SceneObject> sceneObjects,
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls)
    {
        this.sceneObjects = sceneObjects.ToArray();
        baselineLights = this.sceneObjects
            .Where(sceneObject => sceneObject.Light != null)
            .ToDictionary(sceneObject => sceneObject, sceneObject => sceneObject.Light!);
        objectsByKey = this.sceneObjects
            .Where(sceneObject => sceneObject.GaoKey is not (0 or uint.MaxValue))
            .GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        commands = calls
            .Where(item => float.IsFinite(item.Time) && IsSupportedSelector(item.Call.Selector))
            .Select((item, inputOrder) => (item.Time, item.Call, InputOrder: inputOrder))
            .OrderBy(item => item.Time)
            .ThenBy(item => item.InputOrder)
            .Select(item => (item.Time, item.Call))
            .ToArray();
        baselineEvaluation = CreateEvaluation(baselineLights, []);
        cachedEvaluation = baselineEvaluation;
        cachedPrefixCount = 0;
    }

    internal CinematicLightStateEvaluation Evaluate(float time)
    {
        int prefixCount = CountCommandsAtOrBefore(time);
        if (prefixCount == cachedPrefixCount && cachedEvaluation != null) return cachedEvaluation;
        if (prefixCount == 0) return Cache(0, baselineEvaluation);

        var lightStates = new Dictionary<SceneObject, SceneLight>(baselineLights);
        var diagnostics = new List<CinematicLightCommandDiagnostic>();
        for (int index = 0; index < prefixCount; index++)
            Apply(commands[index].Time, commands[index].Call, lightStates, diagnostics);

        return Cache(prefixCount, CreateEvaluation(lightStates, diagnostics));
    }

    private int CountCommandsAtOrBefore(float time)
    {
        if (!float.IsFinite(time)) return 0;
        int low = 0;
        int high = commands.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (commands[middle].Time <= time) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private CinematicLightStateEvaluation Cache(int prefixCount, CinematicLightStateEvaluation evaluation)
    {
        cachedPrefixCount = prefixCount;
        cachedEvaluation = evaluation;
        return evaluation;
    }

    private CinematicLightStateEvaluation CreateEvaluation(
        IReadOnlyDictionary<SceneObject, SceneLight> lightStates,
        IEnumerable<CinematicLightCommandDiagnostic> diagnostics)
    {
        var lights = sceneObjects
            .Where(lightStates.ContainsKey)
            .Select(sceneObject => new CinematicLightState(sceneObject, lightStates[sceneObject]))
            .ToArray();
        return new(Array.AsReadOnly(lights), Array.AsReadOnly(diagnostics.ToArray()));
    }

    private void Apply(float commandTime, CinematicLiteralCall call,
        Dictionary<SceneObject, SceneLight> lightStates,
        List<CinematicLightCommandDiagnostic> diagnostics)
    {
        if (!objectsByKey.TryGetValue(call.Target, out SceneObject[]? receiverCandidates)
            || receiverCandidates.Length != 1)
        {
            AddDiagnostic(CinematicLightCommandStatus.UnresolvedReceiver,
                receiverCandidates is { Length: > 1 }
                    ? $"Receiver key {call.Target:X8} is ambiguous in this scene."
                    : $"Receiver key {call.Target:X8} does not resolve to a scene object.");
            return;
        }

        var receiver = receiverCandidates[0];
        if (!lightStates.TryGetValue(receiver, out var light))
        {
            AddDiagnostic(CinematicLightCommandStatus.ReceiverHasNoLight,
                $"Receiver {receiver.Name} ({receiver.GaoKey:X8}) has no parsed SceneLight component.");
            return;
        }

        switch (call.Selector)
        {
            case LightSetColor:
                if (!HasArgumentCount(call, 1)) return;
                lightStates[receiver] = light with { Color = call.Arguments[0] };
                AddDiagnostic(CinematicLightCommandStatus.Applied,
                    $"Set packed light color to 0x{call.Arguments[0]:X8}.");
                return;
            case LightSetNearFar:
                ApplyNearFar();
                return;
            case LightSetRelatedObject:
                ApplyRelatedObject();
                return;
            case LightSetFlag:
                ApplyFlag();
                return;
        }

        void ApplyNearFar()
        {
            if (!HasArgumentCount(call, 2)) return;
            if (light.TypeCode is not (0 or 2 or 3))
            {
                AddDiagnostic(CinematicLightCommandStatus.UnsupportedLightType,
                    $"Iris LightSetNearFar leaves light type {light.TypeCode} unchanged.");
                return;
            }

            float near = BitConverter.UInt32BitsToSingle(call.Arguments[0]);
            float far = BitConverter.UInt32BitsToSingle(call.Arguments[1]);
            if ((!float.IsFinite(near) && near >= 0) || (!float.IsFinite(far) && far >= 0))
            {
                AddDiagnostic(CinematicLightCommandStatus.UnsupportedValue,
                    "Non-finite near/far values are not previewed.");
                return;
            }

            bool updateNear = near >= 0;
            bool updateFar = far >= 0;
            if (!updateNear && !updateFar)
            {
                AddDiagnostic(CinematicLightCommandStatus.UnsupportedValue,
                    "Iris ignores negative near and far inputs.");
                return;
            }

            lightStates[receiver] = light with
            {
                Near = updateNear ? near : light.Near,
                Far = updateFar ? far : light.Far
            };
            string retained = updateNear && updateFar ? "" : " Negative input field(s) retain their previous values.";
            AddDiagnostic(CinematicLightCommandStatus.Applied,
                $"Set near/far to {lightStates[receiver].Near:G}/{lightStates[receiver].Far:G}.{retained}");
        }

        void ApplyRelatedObject()
        {
            if (!HasArgumentCount(call, 1)) return;
            uint relatedKey = call.Arguments[0];
            if (relatedKey == 0)
            {
                lightStates[receiver] = light with { LightGaoKey = SceneLight.NoLightGaoKey };
                AddDiagnostic(CinematicLightCommandStatus.Applied,
                    "Cleared the related light GAO reference (null object operand).");
                return;
            }

            if (relatedKey == uint.MaxValue || !objectsByKey.TryGetValue(relatedKey, out var relatedCandidates)
                || relatedCandidates.Length != 1)
            {
                AddDiagnostic(CinematicLightCommandStatus.UnresolvedRelatedObject,
                    $"Related object operand {relatedKey:X8} does not resolve uniquely; the previous reference is retained.");
                return;
            }

            lightStates[receiver] = light with { LightGaoKey = relatedKey };
            AddDiagnostic(CinematicLightCommandStatus.Applied,
                $"Set related light GAO to {relatedCandidates[0].Name} ({relatedKey:X8}).");
        }

        void ApplyFlag()
        {
            if (!HasArgumentCount(call, 2)) return;
            int bitIndex = unchecked((int)(sbyte)call.Arguments[0]);
            if (bitIndex is < 0 or > 31)
            {
                AddDiagnostic(CinematicLightCommandStatus.InvalidFlagIndex,
                    $"Flag index {bitIndex} is outside the supported 0..31 range.");
                return;
            }

            uint bit = 1u << bitIndex;
            uint flags = call.Arguments[1] != 0 ? light.Flags | bit : light.Flags & ~bit;
            lightStates[receiver] = light with { Flags = flags };
            AddDiagnostic(CinematicLightCommandStatus.Applied,
                $"{(call.Arguments[1] != 0 ? "Set" : "Cleared")} light flag bit {bitIndex}.");
        }

        bool HasArgumentCount(CinematicLiteralCall candidate, int expectedCount)
        {
            if (candidate.Arguments.Length == expectedCount) return true;
            AddDiagnostic(CinematicLightCommandStatus.InvalidArguments,
                $"Expected {expectedCount} literal argument(s); found {candidate.Arguments.Length}.");
            return false;
        }

        void AddDiagnostic(CinematicLightCommandStatus status, string detail) =>
            diagnostics.Add(new(commandTime, call, status, detail));
    }

    private static bool IsSupportedSelector(int selector) => selector is
        LightSetColor or LightSetNearFar or LightSetRelatedObject or LightSetFlag;
}
