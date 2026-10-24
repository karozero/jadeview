using JadeView;

internal static class CinematicOwnerStartLifecycleChecks
{
    internal static void Verify()
    {
        VerifyResolvedArchiveChainStillReportsRuntimeBarriers();
        VerifyAssignedFirstInitResolvesTheDepartureCallbackChain();
        VerifyMissingEffectiveCallbackRemainsPartial();
        VerifyConfiguredCineEventsWaitsRemainRelativeToRuntimeGates();
        VerifyExplicitPreviewTimingAppliesOnlySupportedConfiguredWaits();
        VerifyEveLifecycleProjectsFreshLoadDispatchEligibility();
    }

    private static void VerifyResolvedArchiveChainStillReportsRuntimeBarriers()
    {
        var owner = new CinematicScriptOwner(
            0x35001234, 0x71000001, 0x71000002, 0xFF005833,
            0x71000003, 0x3500B8EA, "Cinematic owner",
            ["Cine_Events_cine_tests_depart"], [],
            [new("f_first_delay", PcVariableFormat.FloatType, "model", "0"),
             new("des_f_tempo", PcVariableFormat.FloatType, "model", "0"),
             new("f_timecode", PcVariableFormat.FloatType, "model", "0")])
        {
            SerializedGaoFlags = 0x02000000,
            CallbackSlots =
            [
                new(0, 0x71000004, "Cine_Events_cine_tests_depart"),
                new(1, 0, null)
            ]
        };
        CinematicScriptEventTrack[] tracks =
        [
            new(owner.WorldKey, owner.OwnerKey, "SceneEvents", owner.SceneEventsKey,
                "cinematic events", 2, [], [])
        ];

        CinematicOwnerStartLifecycle report = CinematicOwnerStartLifecycleAnalyzer.Analyze(owner, tracks);
        CinematicOwnerLifecyclePhase callbacks = report.Phases.Single(phase => phase.Title == "AI callback assignment");
        CinematicOwnerLifecyclePhase activation = report.Phases.Single(phase => phase.Title == "World and EOT activation");
        CinematicOwnerLifecyclePhase start = report.Phases.Single(phase => phase.Title == "Cine_Events start predicate");
        CinematicOwnerLifecyclePhase timing = report.Phases.Single(phase => phase.Title == "Configured start timing");
        CinematicOwnerLifecyclePhase eve = report.Phases.Single(phase => phase.Title == "EVE scheduling");

        bool completeArchiveChain = report.Phases.Length == 6
            && report.Phases[0].Summary.Contains("owner 35001234")
            && callbacks.Evidence == CinematicOwnerLifecycleEvidence.ArchiveResolved
            && callbacks.Summary.Contains("1 non-empty callback")
            && start.Evidence == CinematicOwnerLifecycleEvidence.ArchiveResolved
            && activation.Evidence == CinematicOwnerLifecycleEvidence.RuntimeUnknown
            && activation.Summary.Contains("requests event-table participation")
            && activation.Details.Any(detail => detail.Contains("EVE tracks")
                && detail.Contains("GAO identity flags 0x02000000"))
            && timing.Summary.Contains("0.000s")
            && timing.Details.Any(detail => detail.Contains("not an absolute clip start time"))
            && eve.Summary.Contains("SceneEvents EVE 3500B8EA")
            && eve.Summary.Contains("Actor Events key: 71000003")
            && eve.Summary.Contains("1 linked SceneEvents track")
            && eve.Details.Any(detail => detail.Contains("Fresh-load EVE dispatch projection", StringComparison.Ordinal)
                && detail.Contains("archive-derived eligibility", StringComparison.Ordinal))
            && eve.Details.Any(detail => detail.Contains("universe-table slot is active", StringComparison.Ordinal))
            && eve.Details.Any(detail => detail.Contains("not proof"));
        Require(completeArchiveChain,
            "Lifecycle report should resolve serialized owner/callback/EVE links while preserving runtime activation barriers and the Events vs SceneEvents distinction. "
            + $"timing='{timing.Summary}', timing detail='{string.Join(" | ", timing.Details)}', EVE='{eve.Summary}'.");
    }

    private static void VerifyMissingEffectiveCallbackRemainsPartial()
    {
        var owner = new CinematicScriptOwner(
            0x35001235, 0, 0x71000002, 0xFF005833,
            0, 0, "Model-only callback", [], [], [])
        {
            CallbackSlots = [new(0, 0, null), new(1, uint.MaxValue, null)],
            ModelFunctionNames = ["Cine_Events_cine_tests_depart"]
        };

        CinematicOwnerStartLifecycle report = CinematicOwnerStartLifecycleAnalyzer.Analyze(owner, []);
        CinematicOwnerLifecyclePhase callbacks = report.Phases.Single(phase => phase.Title == "AI callback assignment");
        CinematicOwnerLifecyclePhase start = report.Phases.Single(phase => phase.Title == "Cine_Events start predicate");
        CinematicOwnerLifecyclePhase eve = report.Phases.Single(phase => phase.Title == "EVE scheduling");

        Require(callbacks.Evidence == CinematicOwnerLifecycleEvidence.ArchiveResolved
            && report.Phases[0].Evidence == CinematicOwnerLifecycleEvidence.PartiallyResolved
            && callbacks.Summary.Contains("0 non-empty callback")
            && callbacks.Details.Any(detail => detail.Contains("no startup-chain entry point is assigned"))
            && start.Evidence == CinematicOwnerLifecycleEvidence.PartiallyResolved
            && eve.Summary.Contains("No SceneEvents EVE list is linked")
            && eve.Summary.Contains("Actor Events key: none"),
            "Model function references must not be mistaken for effective callback assignment or linked world EVE scheduling.");
    }

    private static void VerifyAssignedFirstInitResolvesTheDepartureCallbackChain()
    {
        var owner = new CinematicScriptOwner(
            0x35001239, 0x71000001, 0x71000002, 0xFF001FBE,
            0x71000003, 0x35009655, "Military cinematic owner",
            [CinematicStartCallbackChain.FirstInit], [], [])
        {
            CallbackSlots = [new(2, 0x26004C6C, CinematicStartCallbackChain.FirstInit)],
            ModelFunctionNames =
            [
                CinematicStartCallbackChain.FirstInit,
                CinematicStartCallbackChain.Initialize,
                CinematicStartCallbackChain.DepartureTest
            ]
        };

        CinematicOwnerStartLifecycle report = CinematicOwnerStartLifecycleAnalyzer.Analyze(owner, []);
        CinematicOwnerLifecyclePhase callbacks = report.Phases.Single(phase => phase.Title == "AI callback assignment");
        CinematicOwnerLifecyclePhase start = report.Phases.Single(phase => phase.Title == "Cine_Events start predicate");

        Require(callbacks.Evidence == CinematicOwnerLifecycleEvidence.ArchiveResolved
            && callbacks.Details.Any(detail => detail.Contains("firstinit", StringComparison.Ordinal)
                && detail.Contains("schedules", StringComparison.Ordinal))
            && start.Evidence == CinematicOwnerLifecycleEvidence.ArchiveResolved
            && start.Summary.Contains("firstinit", StringComparison.Ordinal)
            && !start.Summary.Contains("assignment is unresolved", StringComparison.Ordinal),
            "An effective cine_firstinit slot must resolve the source-proven path through cine_init to cine_tests_depart.");
    }

    private static void VerifyConfiguredCineEventsWaitsRemainRelativeToRuntimeGates()
    {
        var owner = new CinematicScriptOwner(
            0x35001236, 0x71000001, 0x71000002, 0xFF005833,
            0x71000003, 0x3500B8EA, "Delayed cinematic owner",
            ["Cine_Events_cine_tests_depart"], [],
            [new("f_first_delay", PcVariableFormat.FloatType, "model", "1.25"),
             new("des_f_tempo", PcVariableFormat.FloatType, "model", "2.5"),
             new("f_timecode", PcVariableFormat.FloatType, "model", "0"),
             new("des_f_tempo", PcVariableFormat.FloatType, "instance", "3.75")]);

        CinematicOwnerStartLifecycle report = CinematicOwnerStartLifecycleAnalyzer.Analyze(owner, []);
        CinematicOwnerLifecyclePhase timing = report.Phases.Single(phase => phase.Title == "Configured start timing");

        Require(timing.Evidence == CinematicOwnerLifecycleEvidence.ArchiveResolved
            && timing.Summary.Contains("Configured waits total 5.000s")
            && timing.Summary.Contains("des_f_tempo=3.750s")
            && timing.Summary.Contains("f_timecode=0.000s")
            && timing.Details.Any(detail => detail.Contains("gates can delay or prevent GoToEvents"))
            && timing.Details.Any(detail => detail.Contains("do not shift EVE tracks")),
            "Cine_Events timing should use effective instance-over-model values and report configured waits without claiming a concrete scene-time start.");

        CinematicScriptOwner malformed = owner with
        {
            Variables = [new("f_first_delay", PcVariableFormat.FloatType, "model", "NaN"),
                new("des_f_tempo", PcVariableFormat.FloatType, "model", "-1")]
        };
        CinematicOwnerLifecyclePhase incompleteTiming = CinematicOwnerStartLifecycleAnalyzer
            .Analyze(malformed, []).Phases.Single(phase => phase.Title == "Configured start timing");
        Require(incompleteTiming.Evidence == CinematicOwnerLifecycleEvidence.PartiallyResolved
            && incompleteTiming.Summary.Contains("no wait total is computed"),
            "Non-finite or negative timing values must not be accepted as valid start-delay evidence.");
    }

    private static void VerifyExplicitPreviewTimingAppliesOnlySupportedConfiguredWaits()
    {
        var owner = new CinematicScriptOwner(
            0x35001237, 0x71000001, 0x71000002, 0xFF005833,
            0x71000003, 0x3500B8EA, "Delayed cinematic owner",
            ["Cine_Events_cine_tests_depart"], [],
            [new("f_first_delay", PcVariableFormat.FloatType, "model", "1.25"),
             new("des_f_tempo", PcVariableFormat.FloatType, "model", "2.5"),
             new("f_timecode", PcVariableFormat.FloatType, "model", "0")]);

        CinematicOwnerStartTimingEstimate signalBeforeInit =
            CinematicOwnerStartTimingEstimator.Estimate(owner, .5f);
        CinematicOwnerStartTimingEstimate signalAfterInit =
            CinematicOwnerStartTimingEstimator.Estimate(owner, 4);
        Require(signalBeforeInit.UsedConfiguredTiming
            && Math.Abs(signalBeforeInit.PreviewStartTime - 3.75f) < .0001f
            && signalAfterInit.UsedConfiguredTiming
            && Math.Abs(signalAfterInit.PreviewStartTime - 6.5f) < .0001f,
            "Explicit Cine_Events preview start should wait for both first-init and post-predicate delays without starting before the signal.");

        CinematicOwnerStartTimingEstimate runtimeStateUnknown =
            CinematicOwnerStartTimingEstimator.Estimate(owner with
            {
                Variables = [.. owner.Variables,
                    new("f_timecode", PcVariableFormat.FloatType, "instance", "0.2")]
            }, 4);
        CinematicOwnerStartTimingEstimate notCineEvents =
            CinematicOwnerStartTimingEstimator.Estimate(owner with
            {
                CallbackNames = ["other_callback"]
            }, 4);
        Require(!runtimeStateUnknown.UsedConfiguredTiming
            && runtimeStateUnknown.PreviewStartTime == 4
            && !notCineEvents.UsedConfiguredTiming && notCineEvents.PreviewStartTime == 4,
            "Unknown runtime f_timecode and non-Cine_Events owners should retain the explicit signal time.");

        Console.WriteLine("PASS: explicit SceneEvents preview start estimates only source-configured Cine_Events waits");
    }

    private static void VerifyEveLifecycleProjectsFreshLoadDispatchEligibility()
    {
        var owner = new CinematicScriptOwner(0x35001238, 0x71000001, 0x71000002, 0xFF005833,
            0x71000003, 0x3500B8EA, "EVE lifecycle owner", [], [], [])
        {
            SerializedGaoFlags = 0x02000000
        };
        CinematicOwnerLifecyclePhase eligible = CinematicOwnerStartLifecycleAnalyzer
            .Analyze(owner, []).Phases.Single(phase => phase.Title == "EVE scheduling");

        CinematicOwnerLifecyclePhase tableNotRequested = CinematicOwnerStartLifecycleAnalyzer
            .Analyze(owner with { SerializedGaoFlags = 0 }, [])
            .Phases.Single(phase => phase.Title == "EVE scheduling");
        CinematicOwnerLifecyclePhase initialActivationBlocked = CinematicOwnerStartLifecycleAnalyzer
            .Analyze(owner with { SerializedGaoFlags = 0x02000001 }, [])
            .Phases.Single(phase => phase.Title == "EVE scheduling");

        Require(eligible.Evidence == CinematicOwnerLifecycleEvidence.RuntimeUnknown
            && eligible.Details.Any(detail => detail.Contains("EVE-table candidate", StringComparison.Ordinal)
                && detail.Contains("ENG_gf_SpecialTime == 0", StringComparison.Ordinal))
            && tableNotRequested.Details.Any(detail => detail.Contains("flag 0x02000000 is clear", StringComparison.Ordinal))
            && initialActivationBlocked.Details.Any(detail => detail.Contains("both modeled initial activation paths are blocked", StringComparison.Ordinal))
            && initialActivationBlocked.Details.Any(detail => detail.Contains("Later runtime activation", StringComparison.Ordinal)),
            "EVE lifecycle should project the serialized fresh-load gates while keeping actual dispatch unknown.");

        Console.WriteLine("PASS: EVE lifecycle reports fresh-load dispatch candidates without claiming runtime execution");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
