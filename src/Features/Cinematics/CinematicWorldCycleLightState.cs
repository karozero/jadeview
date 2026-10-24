namespace JadeView;

/// <summary>
/// Converts the palette's light-related entries to immutable receiver overrides, then applies
/// literal cinematic setters over that base. Callback scheduling is not modeled; the ordering
/// diagnostic makes this deterministic preview precedence explicit.
/// </summary>
internal sealed class CinematicWorldCycleLightState
{
    private const uint ActiveBit = 1u << 3;
    private const int SetColor = 0x2135;
    private const int SetNearFar = 0x2136;
    private const int SetFlag = 0x213B;

    private readonly CinematicEnvironmentController[] controllers;
    private readonly WorldData world;

    internal CinematicWorldCycleLightState(IEnumerable<CinematicEnvironmentController> controllers, WorldData world)
    {
        this.world = world;
        HashSet<uint> worldObjectKeys = world.Objects.Select(sceneObject => sceneObject.GaoKey).ToHashSet();
        this.controllers = controllers
            .Where(controller => worldObjectKeys.Contains(controller.OwnerKey))
            .ToArray();
    }

    internal CinematicWorldCycleLightEvaluation Evaluate(float attackFactor)
    {
        var overrides = new List<CinematicWorldCycleLightOverride>();
        var diagnostics = new List<CinematicWorldCycleLightDiagnostic>();
        Span<uint> palette = stackalloc uint[9];

        foreach (CinematicEnvironmentController controller in controllers)
        {
            SceneObject? owner = Resolve(controller, controller.OwnerKey, "controller owner", diagnostics);
            if (owner == null) continue;
            uint ownerFlags = owner.PreviewControlFlags ?? owner.ControlFlags;
            if ((ownerFlags & CinematicObjectState.Inactive) != 0) continue;

            if (controller.NormalPalettes.Length != 4
                || controller.NormalPalettes.Any(colors => colors.Length < 9))
                continue;

            SceneObject? sun = Resolve(controller, controller.SunKey, "sun", diagnostics);
            if (sun == null) continue;
            float sunHeight = sun.PreviewMatrix?.M43 ?? sun.Transform.Translation.Z;
            if (!float.IsFinite(sunHeight))
            {
                diagnostics.Add(new(controller.OwnerKey, controller.SunKey, "sun",
                    CinematicWorldCycleLightDiagnosticCode.MissingTarget,
                    "The sampled sun height is non-finite; world-cycle light colors were not applied."));
                continue;
            }

            CinematicWorldCyclePalette.SamplePalette(controller, sunHeight, attackFactor, palette);
            if (sun.Light is not SceneLight sunLight)
            {
                diagnostics.Add(new(controller.OwnerKey, controller.SunKey, "sun.color",
                    CinematicWorldCycleLightDiagnosticCode.TargetHasNoLight,
                    "The configured sun GAO has no parsed SceneLight component."));
            }
            else
            {
                CinematicLightFields sunFields = CinematicLightFields.Color;
                uint flagMask = 0;
                SceneLight updatedSun = sunLight with { Color = palette[2] };
                if (sunHeight < controller.DawnHeight)
                {
                    updatedSun = updatedSun with { Flags = updatedSun.Flags & ~ActiveBit };
                    sunFields |= CinematicLightFields.Flags;
                    flagMask = ActiveBit;
                }
                else if (sunHeight >= controller.SunriseHeight)
                {
                    updatedSun = updatedSun with { Flags = updatedSun.Flags | ActiveBit };
                    sunFields |= CinematicLightFields.Flags;
                    flagMask = ActiveBit;
                }
                else
                {
                    diagnostics.Add(new(controller.OwnerKey, controller.SunKey, "sun.active",
                        CinematicWorldCycleLightDiagnosticCode.TwilightActiveStatePreserved,
                        "Sun active bit 3 depends on the previous world-cycle callback inside the dawn-to-sunrise hysteresis interval; source baseline was retained."));
                }
                MergeOverride(overrides, diagnostics, new(sun, updatedSun, sunFields, flagMask), controller.OwnerKey);
            }

            if (controller.FogKey is > 2 and < uint.MaxValue)
            {
                SceneObject? fog = Resolve(controller, controller.FogKey, "fog", diagnostics);
                if (fog != null)
                {
                    if (fog.Light is not SceneLight fogLight)
                    {
                        diagnostics.Add(new(controller.OwnerKey, controller.FogKey, "fog",
                            CinematicWorldCycleLightDiagnosticCode.TargetHasNoLight,
                            "The configured fog GAO has no parsed SceneLight component."));
                    }
                    else if (fogLight.KnownType != SceneLightType.Fog)
                    {
                        diagnostics.Add(new(controller.OwnerKey, controller.FogKey, "fog",
                            CinematicWorldCycleLightDiagnosticCode.WrongLightType,
                            $"The configured fog light has PC type {fogLight.TypeCode}, not the source fog type 3."));
                    }
                    else
                    {
                        var updatedFog = fogLight with
                        {
                            Color = palette[1],
                            Near = Lerp(controller.FogNear, controller.AttackFogNear, attackFactor),
                            Far = Lerp(controller.FogFar, controller.AttackFogFar, attackFactor)
                        };
                        MergeOverride(overrides, diagnostics,
                            new(fog, updatedFog, CinematicLightFields.Color | CinematicLightFields.Near
                                | CinematicLightFields.Far, 0), controller.OwnerKey);
                    }
                }
            }

            if (controller.MoonKey is > 2 and < uint.MaxValue
                || controller.MoonLightKey is > 2 and < uint.MaxValue)
            {
                uint moonReferenceKey = controller.MoonLightKey is > 2 and < uint.MaxValue
                    ? controller.MoonLightKey
                    : controller.MoonKey;
                diagnostics.Add(new(controller.OwnerKey, moonReferenceKey, "moon-light",
                    CinematicWorldCycleLightDiagnosticCode.MoonRuntimeUnresolved,
                    "Moon light position/color/active state depends on the initialized moon-local offset and stateful phase/fade timer; no guessed override was applied."));
            }
        }

        return new(Array.AsReadOnly(overrides.ToArray()), Array.AsReadOnly(diagnostics.ToArray()));
    }

    internal static CinematicLightStateEvaluation Compose(
        CinematicLightStateEvaluation literalState,
        CinematicWorldCycleLightEvaluation cycleState)
    {
        if (cycleState.Overrides.Count == 0 && cycleState.Diagnostics.Count == 0)
            return literalState;
        if (cycleState.Overrides.Count == 0)
            return literalState with { WorldCycleDiagnostics = cycleState.Diagnostics };

        var composed = literalState.Lights.ToDictionary(state => state.Receiver, state => state.Light);
        var diagnostics = new List<CinematicWorldCycleLightDiagnostic>(cycleState.Diagnostics);
        foreach (CinematicWorldCycleLightOverride cycleOverride in cycleState.Overrides)
        {
            if (!composed.TryGetValue(cycleOverride.Receiver, out var current)) continue;
            composed[cycleOverride.Receiver] = ApplyFields(current, cycleOverride.Light,
                cycleOverride.Fields, cycleOverride.FlagMask);
        }

        foreach (CinematicLightCommandDiagnostic command in literalState.Diagnostics)
        {
            if (command.Status != CinematicLightCommandStatus.Applied
                || !TryGetLiteralFields(command.Call, out CinematicLightFields fields, out uint flagMask))
                continue;

            CinematicWorldCycleLightOverride? collision = cycleState.Overrides.FirstOrDefault(item =>
                item.Receiver.GaoKey == command.Call.Target
                && HasFieldOverlap(item, fields, flagMask));
            if (collision == null) continue;

            SceneLight? finalLiteral = literalState.Lights
                .Where(state => state.Receiver.GaoKey == command.Call.Target)
                .Select(state => (SceneLight?)state.Light)
                .SingleOrDefault();
            if (finalLiteral is not SceneLight literalLight
                || !composed.TryGetValue(collision.Receiver, out var current))
                continue;

            composed[collision.Receiver] = ApplyFields(current, literalLight, fields, flagMask);
            diagnostics.Add(new(0, command.Call.Target, FieldNames(fields, flagMask),
                CinematicWorldCycleLightDiagnosticCode.LiteralOrderingApproximation,
                "Both world-cycle and literal cinematic state write this light field. Preview deterministically applies the literal timeline last; native same-frame callback order is unknown."));
        }

        CinematicLightState[] lights = literalState.Lights
            .Select(state => state with { Light = composed[state.Receiver] })
            .ToArray();
        return literalState with
        {
            Lights = Array.AsReadOnly(lights),
            WorldCycleDiagnostics = Array.AsReadOnly(diagnostics.ToArray())
        };
    }

    private SceneObject? Resolve(CinematicEnvironmentController controller, uint key, string role,
        List<CinematicWorldCycleLightDiagnostic> diagnostics)
    {
        SceneObject? found = null;
        int count = 0;
        foreach (SceneObject candidate in world.Objects)
        {
            if (candidate.GaoKey != key) continue;
            found = candidate;
            count++;
        }
        if (count == 1) return found;
        diagnostics.Add(new(controller.OwnerKey, key, role,
            count == 0 ? CinematicWorldCycleLightDiagnosticCode.MissingTarget
                : CinematicWorldCycleLightDiagnosticCode.AmbiguousTarget,
            count == 0 ? $"Configured {role} GAO {key:X8} is not in this world."
                : $"Configured {role} GAO {key:X8} resolves to {count} objects; no name-based choice was made."));
        return null;
    }

    private static void MergeOverride(List<CinematicWorldCycleLightOverride> overrides,
        List<CinematicWorldCycleLightDiagnostic> diagnostics,
        CinematicWorldCycleLightOverride next, uint controllerKey)
    {
        int existingIndex = overrides.FindIndex(item => ReferenceEquals(item.Receiver, next.Receiver));
        if (existingIndex < 0)
        {
            overrides.Add(next);
            return;
        }

        CinematicWorldCycleLightOverride previous = overrides[existingIndex];
        CinematicLightFields overlap = previous.Fields & next.Fields;
        uint overlappingFlags = previous.FlagMask & next.FlagMask;
        bool flagCollision = (overlap & CinematicLightFields.Flags) != 0 && overlappingFlags != 0;
        if ((overlap & ~CinematicLightFields.Flags) != 0 || flagCollision)
        {
            diagnostics.Add(new(controllerKey, next.Receiver.GaoKey, FieldNames(overlap, overlappingFlags),
                CinematicWorldCycleLightDiagnosticCode.ControllerTargetCollision,
                "Multiple active world-cycle controllers target the same light field; later controller order wins in this preview."));
        }

        SceneLight merged = ApplyFields(previous.Light, next.Light, next.Fields, next.FlagMask);
        overrides[existingIndex] = previous with
        {
            Light = merged,
            Fields = previous.Fields | next.Fields,
            FlagMask = previous.FlagMask | next.FlagMask
        };
    }

    private static SceneLight ApplyFields(SceneLight target, SceneLight source,
        CinematicLightFields fields, uint flagMask)
    {
        SceneLight result = target;
        if ((fields & CinematicLightFields.Color) != 0) result = result with { Color = source.Color };
        if ((fields & CinematicLightFields.Near) != 0) result = result with { Near = source.Near };
        if ((fields & CinematicLightFields.Far) != 0) result = result with { Far = source.Far };
        if ((fields & CinematicLightFields.Flags) != 0)
            result = result with { Flags = (result.Flags & ~flagMask) | (source.Flags & flagMask) };
        return result;
    }

    private static bool TryGetLiteralFields(CinematicLiteralCall call,
        out CinematicLightFields fields, out uint flagMask)
    {
        fields = CinematicLightFields.None;
        flagMask = 0;
        switch (call.Selector)
        {
            case SetColor when call.Arguments.Length == 1:
                fields = CinematicLightFields.Color;
                return true;
            case SetNearFar when call.Arguments.Length == 2:
            {
                float near = BitConverter.UInt32BitsToSingle(call.Arguments[0]);
                float far = BitConverter.UInt32BitsToSingle(call.Arguments[1]);
                if (near >= 0) fields |= CinematicLightFields.Near;
                if (far >= 0) fields |= CinematicLightFields.Far;
                return fields != CinematicLightFields.None;
            }
            case SetFlag when call.Arguments.Length == 2 && call.Arguments[0] < 32:
                fields = CinematicLightFields.Flags;
                flagMask = 1u << (int)call.Arguments[0];
                return true;
            default:
                return false;
        }
    }

    private static bool HasFieldOverlap(CinematicWorldCycleLightOverride cycle,
        CinematicLightFields literalFields, uint literalFlagMask) =>
        ((cycle.Fields & literalFields) & ~CinematicLightFields.Flags) != 0
        || ((cycle.Fields & literalFields & CinematicLightFields.Flags) != 0
            && (cycle.FlagMask & literalFlagMask) != 0);

    private static string FieldNames(CinematicLightFields fields, uint flagMask)
    {
        var names = new List<string>();
        if ((fields & CinematicLightFields.Color) != 0) names.Add("color");
        if ((fields & CinematicLightFields.Near) != 0) names.Add("near");
        if ((fields & CinematicLightFields.Far) != 0) names.Add("far");
        if ((fields & CinematicLightFields.Flags) != 0 && flagMask != 0)
            names.Add($"flags 0x{flagMask:X8}");
        return string.Join(", ", names);
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * Math.Clamp(amount, 0, 1);
}
