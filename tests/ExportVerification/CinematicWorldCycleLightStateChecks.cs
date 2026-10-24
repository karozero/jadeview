using System.Numerics;
using JadeView;

internal static class CinematicWorldCycleLightStateChecks
{
    private const uint SunKey = 0x51000010;
    private const uint FogKey = 0x51000020;
    private const uint MoonKey = 0x51000030;
    private const uint MoonLightKey = 0x51000040;

    internal static void Verify()
    {
        VerifySunAndFogFollowTheSampledPalette();
        VerifyControllerTracksExplicitCinematicActivation();
        VerifyAssembledPlaybackIncludesInitiallyInactiveControllerAssets();
        VerifySunHysteresisAndMoonBoundaryAreDiagnostic();
        VerifyLiteralCommandsComposeAfterCycleFields();
        Console.WriteLine("PASS: world-cycle sun/fog light snapshots compose immutably with literal cinematic setters");
    }

    private static void VerifyAssembledPlaybackIncludesInitiallyInactiveControllerAssets()
    {
        var (baseWorld, controller, palettes, sun, _) = CreateFixture(sunHeight: 2f);
        baseWorld.Name = "Room";
        SceneObject owner = baseWorld.Objects.Single(item => item.GaoKey == controller.OwnerKey);
        owner.ControlFlags = CinematicObjectState.Inactive;
        var cinematicWorld = new WorldData { Key = 0x5200, Name = "Room_cine" };
        var clip = new AnimationClip
        {
            Key = 0x5201,
            Cinematic = true,
            Loaded = true,
            CinematicWorldKey = cinematicWorld.Key,
            Duration = 2,
            Tracks =
            [
                new()
                {
                    Bone = -1,
                    Target = owner.GaoKey,
                    Keys =
                    [
                        new() { Duration = 2, Position = Vector3.Zero },
                        CinematicTestEvents.Call(1, owner.GaoKey, 0x1FD9, 0)
                    ]
                },
                new()
                {
                    Bone = -1,
                    Target = sun.GaoKey,
                    Keys = [new() { Duration = 2, Position = new(0, 0, 2) }]
                }
            ]
        };
        cinematicWorld.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.AddRange([cinematicWorld, baseWorld]);
        document.CinematicClips.Add(clip.Key, clip);
        document.CinematicEnvironmentControllers.Add(new(baseWorld.Key, controller.OwnerKey), controller);

        WorldData assembled = CinematicWorldAssembler.Assemble(document, cinematicWorld);
        if (!assembled.Objects.Contains(owner) || !assembled.Objects.Contains(sun)
            || !assembled.CinematicEnvironmentControllers.Contains(controller))
            throw new InvalidDataException("Scene assembly must retain base-world controller owners and their assets before a later EVE activation.");

        CinematicPlayback playback = CinematicPlayback.Discover(document, assembled, new()).Single();
        playback.Activate();
        playback.Seek(.5f);
        SceneLight initiallyInactiveSun = playback.LightState.Lights
            .Single(item => ReferenceEquals(item.Receiver, sun)).Light;
        Require(initiallyInactiveSun.Color == sun.Light!.Color,
            "An inactive assembled controller must leave the serialized sun untouched before its activation event.");

        playback.Seek(1);
        SceneLight activatedSun = playback.LightState.Lights
            .Single(item => ReferenceEquals(item.Receiver, sun)).Light;
        Require(activatedSun.Color == palettes[2][2],
            "The assembled playback must apply the base-world controller after its explicit activation event.");

        playback.Seek(.5f);
        SceneLight rewoundSun = playback.LightState.Lights
            .Single(item => ReferenceEquals(item.Receiver, sun)).Light;
        Require(rewoundSun.Color == sun.Light.Color,
            "Reverse seeking must restore the inactive controller and serialized sun state in assembled playback.");
        playback.Deactivate();
    }

    private static void VerifyControllerTracksExplicitCinematicActivation()
    {
        var (world, controller, palettes, sun, _) = CreateFixture(sunHeight: 2f);
        SceneObject owner = world.Objects.Single(item => item.GaoKey == controller.OwnerKey);
        owner.ControlFlags = CinematicObjectState.Inactive;
        var clip = new AnimationClip
        {
            Tracks =
            [
                new()
                {
                    Bone = -1,
                    Target = owner.GaoKey,
                    Keys =
                    [
                        CinematicTestEvents.Call(1, owner.GaoKey, 0x1FD9, 0),
                        CinematicTestEvents.Call(2, owner.GaoKey, 0x1FD9, 1)
                    ]
                }
            ]
        };
        var objectState = new CinematicObjectState(clip, world, []);
        var cycleState = new CinematicWorldCycleLightState([controller], world);

        objectState.Evaluate(.5f);
        Require(cycleState.Evaluate(0).Overrides.Count == 0,
            "A controller whose owner is inactive in the source state must not affect scene lights.");

        objectState.Evaluate(1);
        CinematicWorldCycleLightEvaluation activated = cycleState.Evaluate(0);
        Require(activated.Overrides.Count > 0
            && activated.Overrides.Single(item => ReferenceEquals(item.Receiver, sun)).Light.Color == palettes[2][2],
            "An explicit cinematic OBJ_FlagInactiveSet(0) must activate its environment controller at that playhead.");

        objectState.Evaluate(2);
        Require(cycleState.Evaluate(0).Overrides.Count == 0,
            "An explicit cinematic OBJ_FlagInactiveSet(1) must deactivate its environment controller at that playhead.");

        objectState.Evaluate(.5f);
        Require(cycleState.Evaluate(0).Overrides.Count == 0,
            "Reverse seeking before the activation event must restore the serialized inactive owner state.");
        objectState.Clear();
    }

    private static void VerifySunAndFogFollowTheSampledPalette()
    {
        var (world, controller, palettes, sun, fog) = CreateFixture(sunHeight: 2f);
        CinematicWorldCycleLightEvaluation normal = Evaluate(controller, world, attackFactor: 0);
        CinematicWorldCycleLightOverride normalSun = normal.Overrides.Single(item => ReferenceEquals(item.Receiver, sun));
        CinematicWorldCycleLightOverride normalFog = normal.Overrides.Single(item => ReferenceEquals(item.Receiver, fog));

        Require(normalSun.Light.Color == palettes[2][2]
            && (normalSun.Light.Flags & 8) != 0,
            "With no attack blend, the sampled sun palette color and above-sunrise active transition must affect the sun light.");
        Require(normalFog.Light.Color == palettes[2][1]
            && normalFog.Light.Near == 10
            && normalFog.Light.Far == 100,
            "With no attack blend, the sampled fog palette color and normal range must affect the fog light.");

        CinematicWorldCycleLightEvaluation attack = Evaluate(controller, world, attackFactor: .5f);
        CinematicWorldCycleLightOverride attackSun = attack.Overrides.Single(item => ReferenceEquals(item.Receiver, sun));
        CinematicWorldCycleLightOverride attackFog = attack.Overrides.Single(item => ReferenceEquals(item.Receiver, fog));
        // Iris blends packed channels with a 7-bit bit-mask approximation, not arithmetic lerp.
        Require(attackSun.Light.Color == 0xf7010120
            && attackFog.Light.Color == 0xf701011f,
            $"A half attack blend must preserve Iris' packed-color approximation: sun {attackSun.Light.Color:X8}, fog {attackFog.Light.Color:X8}; source {palettes[2][2]:X8}/{palettes[2][1]:X8}.");
        Require(attackFog.Light.Near == 15 && attackFog.Light.Far == 150,
            "The same attack factor must linearly blend fog near/far from normal to attack values.");
        Require(normal.Diagnostics.Count == 0 && attack.Diagnostics.Count == 0,
            "Resolved sun/fog lights without a moon configuration should not produce diagnostics.");
    }

    private static void VerifySunHysteresisAndMoonBoundaryAreDiagnostic()
    {
        var (world, controller, _, sun, _) = CreateFixture(sunHeight: .5f);
        sun.Light = sun.Light! with { Flags = sun.Light.Flags | 8 };
        CinematicWorldCycleLightEvaluation night = Evaluate(controller, world, 0);
        Require((night.Overrides.Single(item => ReferenceEquals(item.Receiver, sun)).Light.Flags & 8) == 0,
            "Below dawn the cycle clears the sun's active bit 3.");

        sun.PreviewMatrix = Matrix4x4.CreateTranslation(0, 0, 1.5f);
        CinematicWorldCycleLightEvaluation twilight = Evaluate(controller, world, 0);
        Require((twilight.Overrides.Single(item => ReferenceEquals(item.Receiver, sun)).Light.Flags & 8) != 0
            && twilight.Diagnostics.Any(item => item.Code == CinematicWorldCycleLightDiagnosticCode.TwilightActiveStatePreserved),
            "Inside the dawn/sunrise hysteresis interval, preserve prior baseline active state and explain the unresolved history.");

        CinematicWorldCycleLightEvaluation moon = Evaluate(controller with
        {
            MoonKey = MoonKey,
            MoonLightKey = MoonLightKey
        }, world, 0);
        Require(moon.Diagnostics.Any(item => item.Code == CinematicWorldCycleLightDiagnosticCode.MoonRuntimeUnresolved
                && item.ReceiverKey == MoonLightKey),
            "Configured moon light should report that phase/timer and initialized offset are not reconstructed.");
    }

    private static void VerifyLiteralCommandsComposeAfterCycleFields()
    {
        var (world, controller, palettes, sun, fog) = CreateFixture(sunHeight: 2f);
        var literalTimeline = new CinematicLightStateTimeline(world.Objects,
        [
            (1, new(SunKey, 0x2135, [0xffabcdef])),
            (2, new(FogKey, 0x2136,
                [BitConverter.SingleToUInt32Bits(20), BitConverter.SingleToUInt32Bits(200)]))
        ]);
        CinematicLightStateEvaluation literal = literalTimeline.Evaluate(2);
        CinematicWorldCycleLightEvaluation cycle = Evaluate(controller, world, .5f);
        CinematicLightStateEvaluation composed = CinematicWorldCycleLightState.Compose(literal, cycle);

        SceneLight resultSun = composed.Lights.Single(item => ReferenceEquals(item.Receiver, sun)).Light;
        SceneLight resultFog = composed.Lights.Single(item => ReferenceEquals(item.Receiver, fog)).Light;
        Require(resultSun.Color == 0xffabcdef,
            "A literal sun color setter wins over a world-cycle sun palette write under the documented preview precedence.");
        Require(resultFog.Color == 0xf701011f && resultFog.Near == 20 && resultFog.Far == 200,
            "A literal fog range setter overrides near/far while preserving the non-overlapping cycle fog color.");
        Require(composed.WorldCycleDiagnostics.Count(item =>
                item.Code == CinematicWorldCycleLightDiagnosticCode.LiteralOrderingApproximation) == 2,
            "Every literal/cycle field collision should expose the preview's ordering approximation.");
        Require(sun.Light!.Color != resultSun.Color && fog.Light!.Near != resultFog.Near,
            "Composed state must not mutate source SceneLight components.");
    }

    private static (WorldData World, CinematicEnvironmentController Controller, uint[][] Palettes,
        SceneObject Sun, SceneObject Fog) CreateFixture(float sunHeight)
    {
        var sun = new SceneObject
        {
            GaoKey = SunKey,
            Name = "sun",
            Transform = new() { Translation = new(0, 0, sunHeight) },
            Light = new(0x00000001, 0xff010203, 0, 0, 0, 0, SceneLight.NoLightGaoKey)
        };
        var fog = new SceneObject
        {
            GaoKey = FogKey,
            Name = "fog",
            Light = new(0x00000003, 0xff040506, 2, 5, 0, 0, SceneLight.NoLightGaoKey)
        };
        var world = new WorldData { Key = 0x5100 };
        var controllerOwner = new SceneObject { GaoKey = 0x51000001, Name = "cycle controller" };
        world.Objects.AddRange([sun, fog, controllerOwner]);
        uint[][] palettes = Enumerable.Range(0, 4)
            .Select(paletteIndex => Enumerable.Range(0, 9)
                .Select(colorIndex => 0xff000000u | (uint)(paletteIndex + 1) << 16
                    | (uint)(paletteIndex + 1) << 8 | (uint)(paletteIndex * 16 + colorIndex)).ToArray())
            .ToArray();
        var controller = new CinematicEnvironmentController(0x51000001, world.Key, [SunKey, FogKey])
        {
            SunKey = SunKey,
            FogKey = FogKey,
            NightHeight = 0,
            DawnHeight = 1,
            SunriseHeight = 2,
            DayHeight = 3,
            FogNear = 10,
            FogFar = 100,
            AttackFogNear = 20,
            AttackFogFar = 200,
            NormalPalettes = palettes
        };
        return (world, controller, palettes, sun, fog);
    }

    private static CinematicWorldCycleLightEvaluation Evaluate(
        CinematicEnvironmentController controller, WorldData world, float attackFactor) =>
        new CinematicWorldCycleLightState([controller], world).Evaluate(attackFactor);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
