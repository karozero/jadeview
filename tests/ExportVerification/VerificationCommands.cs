internal static class VerificationCommands
{
    public static bool TryRun(string[] arguments, string output)
    {
        switch (arguments)
        {
            case ["--cinematic-sight-set-checks"]:
                CinematicSightSetChecks.Verify(output);
                break;
            case ["--assets-frame-trace-checks"]:
                AssetsFrameTraceChecks.Run();
                break;
            case ["--cinematic-body-checks"]:
                CinematicBodyChecks.Verify();
                break;
            case ["--cinematic-owner-start-checks"]:
                CinematicOwnerStartLifecycleChecks.Verify();
                CinematicStartGateEvaluatorChecks.Verify();
                break;
            case ["--engine-function-selector-checks"]:
                CinematicEngineFunctionCatalogChecks.Verify();
                Console.WriteLine("PASS: source-aware engine selector catalog and operation descriptions");
                break;
            case ["--cinematic-script-material-constant-checks"]:
                CinematicScriptMaterialConstantChecks.Verify();
                break;
            case ["--cinematic-script-local-alpha-checks"]:
                CinematicScriptLocalAlphaChecks.Verify();
                break;
            case ["--cinematic-script-rli-display-callback-checks"]:
                CinematicScriptRliDisplayCallbackChecks.Verify();
                break;
            case ["--cinematic-script-callback-window-checks"]:
                CinematicScriptCallbackWindowChecks.Verify();
                break;
            case ["--cinematic-script-material-constant-audit", var archive, var container,
                var clipKey, var materialKey, var start, var end, var step, .. var optional]:
                if (optional.Length % 2 != 0)
                    throw new ArgumentException("Optional material audit arguments are saved-flag index/value pairs.");
                var materialSavedFlags = new Dictionary<int, bool?>();
                for (int index = 0; index < optional.Length; index += 2)
                    materialSavedFlags[int.Parse(optional[index], System.Globalization.CultureInfo.InvariantCulture)] =
                        bool.Parse(optional[index + 1]);
                CinematicScriptMaterialConstantAudit.Write(archive, container,
                    Convert.ToUInt32(clipKey, 16), Convert.ToUInt32(materialKey, 16),
                    float.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(step, System.Globalization.CultureInfo.InvariantCulture),
                    output, materialSavedFlags);
                break;
            case ["--archive-startup-script-audit", var archive]:
                ArchiveStartupScriptAudit.Write(archive, output);
                break;
            case ["--pc-world-list-resource-audit", var archive, var key]:
                PcWorldListResourceAudit.Write(archive, key, output);
                break;
            case ["--pc-world-list-header-checks"]:
                PcWorldListHeaderChecks.Verify();
                break;
            case ["--pc-ai-call-graph-checks"]:
                PcAiFunctionCallGraphChecks.Verify();
                break;
            case ["--pc-ai-call-binding-checks"]:
                PcAiFunctionCallBindingChecks.Verify();
                break;
            case ["--pc-ai-track-mutation-checks"]:
                PcAiTrackMutationChecks.Verify();
                break;
            case ["--pc-ai-constant-branch-checks"]:
                PcAiConstantBranchChecks.Verify();
                break;
            case ["--pc-ai-scalar-assignment-checks"]:
                PcAiScalarConstantAssignmentChecks.Verify();
                break;
            case ["--pc-ai-call-graph-audit", var archive, var rootKey]:
                PcAiFunctionCallGraphAudit.Write(archive, rootKey, output);
                break;
            case ["--script-owner-resource-fixture", var archive, var container, var ownerKey]:
                ScriptOwnerResourceFixture.Write(archive, container, Convert.ToUInt32(ownerKey, 16), output);
                break;
            case ["--script-model-variable-audit", var archive, var container]:
                ScriptModelVariableAudit.Write(archive, container, output);
                break;
            case ["--archive-zone-trigger-census", var archive]:
                ArchiveZoneTriggerCensus.Write(archive, output);
                break;
            case ["--archive-ai-dependency-census", var archive]:
                ArchiveAiDependencyCensus.Write(archive, output);
                break;
            case ["--assets-ui-performance-audit", var archive, var container, var worldName, var focusName, var profile]:
                AssetsUiPerformanceAudit.Write(archive, container, worldName, focusName, output, profile);
                break;
            case ["--assets-ui-performance-audit", var archive, var container, var worldName, var focusName]:
                AssetsUiPerformanceAudit.Write(archive, container, worldName, focusName, output);
                break;
            case ["--assets-render-performance-audit", var archive, var container, var worldName, var focusName,
                var width, var height, var camera]:
                AssetsRenderPerformanceAudit.Write(archive, container, worldName, focusName, output,
                    int.Parse(width), int.Parse(height), camera);
                break;
            case ["--assets-render-performance-audit", var archive, var container, var worldName, var focusName]:
                AssetsRenderPerformanceAudit.Write(archive, container, worldName, focusName, output);
                break;
            case ["--collision-resource-audit", var archive, var container]:
                PcCollisionResourceAudit.Write(archive, container, output);
                break;
            case ["--collision-resource-audit", var archive, var container, var dialect]:
                PcCollisionResourceAudit.Write(archive, container, output,
                    Enum.Parse<JadeView.PcCollisionWireDialect>(dialect, ignoreCase: true));
                break;
            case ["--collision-resource-audit", var archive, var container, var dialect, var readMode]:
                PcCollisionResourceAudit.Write(archive, container, output,
                    Enum.Parse<JadeView.PcCollisionWireDialect>(dialect, ignoreCase: true),
                    Enum.Parse<JadeView.PcCollisionReadMode>(readMode, ignoreCase: true));
                break;
            case ["--cinematic-export-audit", var archive, var container, var clipKey, var time]:
                CinematicExportAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    float.Parse(time, System.Globalization.CultureInfo.InvariantCulture), output);
                break;
            case ["--cinematic-export-all-audit", var archive, var container, var clipKey, var time]:
                CinematicExportAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    float.Parse(time, System.Globalization.CultureInfo.InvariantCulture), output, allClips: true);
                break;
            case ["--cinematic-camera-script-fov-audit", var archive, var container, var clipKey, var ownerKey, var time]:
                CinematicCameraScriptFovAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    Convert.ToUInt32(ownerKey, 16), float.Parse(time,
                        System.Globalization.CultureInfo.InvariantCulture), output);
                break;
            case ["--cinematic-audio-stream-clock-audit"]:
                CinematicAudioStreamClockAudit.Run();
                break;
            case ["--cinematic-realtime-audio-render-audit", var archive, var container, var clipKey, var startTime, var duration]:
                CinematicRealtimeAudioRenderAudit.Run(archive, container, Convert.ToUInt32(clipKey, 16),
                    float.Parse(startTime, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(duration, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case ["--cinematic-realtime-audio-render-audit", var archive, var container, var clipKey,
                var startTime, var duration, var width, var height]:
                CinematicRealtimeAudioRenderAudit.Run(archive, container, Convert.ToUInt32(clipKey, 16),
                    float.Parse(startTime, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(duration, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(width, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(height, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case ["--cinematic-material-render-check"]:
                CinematicMaterialRenderChecks.Verify();
                break;
            case ["--dynamic-light-render-check"]:
                DynamicLightRenderChecks.Verify();
                break;
            case ["--cinematic-flash-check"]:
                CinematicFlashChecks.Verify();
                break;
            case ["--cinematic-lightning-check"]:
                CinematicLightningChecks.Verify();
                break;
            case ["--cinematic-frame-audit", var archive, var container, var clipKey, var time]:
                CinematicFrameAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    float.Parse(time, System.Globalization.CultureInfo.InvariantCulture), output);
                break;
            case ["--cinematic-frame-audit", var archive, var container, var clipKey, var time, var focusObject]:
                CinematicFrameAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    float.Parse(time, System.Globalization.CultureInfo.InvariantCulture), output, focusObject);
                break;
            case ["--cinematic-playback-performance-audit", var archive, var container, var clipKey, .. var sampleTimes]:
                CinematicPlaybackPerformanceAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    sampleTimes.Select(time => float.Parse(time,
                        System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                break;
            case ["--shutter-render-check", var archive, var container, var clipKey]:
                CinematicShutterRenderChecks.Verify(output, archive, container, Convert.ToUInt32(clipKey, 16));
                break;
            case ["--shutter-render-check"]:
                CinematicShutterRenderChecks.Verify(output);
                break;
            case ["--cinematic-placement-audit", var archive, var container, var clipKey]:
                CinematicPlacementAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16), output);
                break;
            case ["--cinematic-timing-audit", var archive, var container, var clipKey, var editorPath]:
                CinematicTimingAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16), editorPath, output);
                break;
            case ["--cinematic-motion-audit", var archive, var container, var clipKey, ..]:
                CinematicMotionAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16), output);
                break;
            case ["--cinematic-root-trajectory-audit", var archive, var container, var clipKey, var actorKey]:
                CinematicRootTrajectoryAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16),
                    Convert.ToUInt32(actorKey, 16), output);
                break;
            case ["--cinematic-audio-audit", var archive, var container, var clipKey, ..]:
                CinematicAudioAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16), output);
                break;
            case ["--world-text-resource-audit", var archive, var container, var outputPath]:
                WorldTextResourceAudit.Write(archive, container, outputPath);
                break;
            case ["--cinematic-sound-resource-audit", var archive, ..]:
                foreach (string soundKey in arguments.Skip(2))
                    CinematicSoundResourceAudit.Write(archive, Convert.ToUInt32(soundKey, 16));
                break;
            case ["--cinematic-sound-fade-audit", var archive, var container]:
                CinematicSoundFadeAudit.Write(archive, container);
                break;
            case ["--cinematic-video-trigger-audit", var archive, var container, var clipKey, ..]:
                CinematicVideoTriggerAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16), output);
                break;
            case ["--bink-frame-audit", var dllPath, var videoPath, var frameIndex, ..]:
                BinkFrameAudit.Write(dllPath, videoPath, int.Parse(frameIndex,
                    System.Globalization.CultureInfo.InvariantCulture), output);
                break;
            case ["--archive-bink-frame-audit", var archive, var mediaKey, var frameIndex, ..]:
                BinkFrameAudit.WriteArchive(archive, Convert.ToUInt32(mediaKey, 16),
                    int.Parse(frameIndex, System.Globalization.CultureInfo.InvariantCulture), output);
                break;
            case ["--nested-eve-transform-audit", var archive, var container, var clipKey, ..]:
                CinematicNestedEventTransformAudit.Write(archive, container,
                    Convert.ToUInt32(clipKey, 16), output);
                break;
            case ["--cinematic-hierarchy-track-audit", var archive, var container]:
                CinematicHierarchyTrackAudit.Write(archive, container, output);
                break;
            case ["--cinematic-state-audit", var archive, var container, ..]:
                CinematicSceneStateAudit.Write(archive, container, output);
                break;
            case ["--texture-descriptor-audit", var archive, var container, var textureKey, ..]:
                TextureDescriptorAudit.Write(archive, container, Convert.ToUInt32(textureKey, 16));
                break;
            case ["--actor-module-audit", var archive, var container, var actorKey, ..]:
                ActorScriptAudit.WriteModules(archive, container, Convert.ToUInt32(actorKey, 16), output);
                break;
            case ["--container-event-audit", var archive, var container, ..]:
                ActorScriptAudit.WriteContainerEvents(archive, container, output);
                break;
            case ["--scene-event-audit", var archive, var container, ..]:
                SceneAnimationEventAudit.Write(archive, container, output);
                break;
            case ["--scene-script-audit", var archive, var container, ..]:
                ActorScriptAudit.WriteScene(archive, container, output);
                break;
            case ["--object-audit", var archive, var objectName, ..]:
                ObjectMaterialAudit.Run(archive, objectName, output, OptionalArgument(arguments, 3));
                break;
            case ["--pc-morph-channel-audit", var archive, var container, var objectKey]:
                PcMorphChannelAudit.Write(archive, container, Convert.ToUInt32(objectKey, 16), output);
                break;
            case ["--cinematic-morph-channel-audit", var archive, var container, var clipKey,
                var objectKey, var start, var end, var step, .. var optional]:
                if (optional.Length is 1 or > 2)
                    throw new ArgumentException("Optional cinematic morph audit arguments are a saved-flag index and value pair.");
                CinematicMorphChannelAudit.Write(archive, container,
                    Convert.ToUInt32(clipKey, 16), Convert.ToUInt32(objectKey, 16),
                    float.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(step, System.Globalization.CultureInfo.InvariantCulture), output,
                    optional.Length > 0 ? int.Parse(optional[0], System.Globalization.CultureInfo.InvariantCulture) : null,
                    optional.Length > 1 ? bool.Parse(optional[1]) : null);
                break;
            case ["--cinematic-rli-callback-audit", var archive, var container, var clipKey,
                var ownerKey, var targetKey, var start, var end, var step, .. var optional]:
                if (optional.Length % 2 != 0)
                    throw new ArgumentException("Optional cinematic RLI audit arguments are saved-flag index/value pairs.");
                var savedFlagAssumptions = new Dictionary<int, bool?>();
                for (int index = 0; index < optional.Length; index += 2)
                    savedFlagAssumptions[int.Parse(optional[index], System.Globalization.CultureInfo.InvariantCulture)] =
                        bool.Parse(optional[index + 1]);
                CinematicRliCallbackAudit.Write(archive, container,
                    Convert.ToUInt32(clipKey, 16), Convert.ToUInt32(ownerKey, 16),
                    Convert.ToUInt32(targetKey, 16),
                    float.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(step, System.Globalization.CultureInfo.InvariantCulture), output,
                    savedFlagAssumptions);
                break;
            case ["--actor-audit", var archive, var actorName, ..]:
                ActorAssetAudit.Run(archive, actorName, output);
                break;
            case ["--morph-discovery", var archive, ..]:
                MorphDiscoveryAudit.Run(archive, output, OptionalArgument(arguments, 2));
                break;
            case ["--archive-cinematic-census", var archive, ..]:
                ArchiveCinematicCensus.Write(archive, output);
                break;
            case ["--archive-engine-function-audit", var archive, var functionName]:
                ArchiveEngineFunctionAudit.Write(archive, functionName, output);
                break;
            case ["--archive-cinematic-script-index", var archive, var sourceRoot, ..]:
                ArchiveCinematicScriptIndex.Write(archive, sourceRoot, output);
                break;
            case ["--archive-nested-event-track-audit", var archive, ..]:
                ArchiveNestedEventTrackAudit.Write(archive, output);
                break;
            case ["--event-track-payload-audit", var archive, var container, var ownerKey, var eventKey, var trackIndex]:
                EventTrackPayloadAudit.Write(archive, container, Convert.ToUInt32(ownerKey, 16),
                    Convert.ToUInt32(eventKey, 16), int.Parse(trackIndex,
                        System.Globalization.CultureInfo.InvariantCulture), output);
                break;
            case ["--cinematic-script-graph-audit", var archive, var container, var clipKey, ..]:
                CinematicScriptGraphAudit.Write(archive, container, Convert.ToUInt32(clipKey, 16), output);
                break;
            case ["--cinematic-owner-action-preview-audit", var archive, var container,
                var clipKey, var ownerKey, var actorKey]:
                CinematicOwnerActionPreviewAudit.Write(archive, container,
                    Convert.ToUInt32(clipKey, 16), Convert.ToUInt32(ownerKey, 16),
                    Convert.ToUInt32(actorKey, 16), output);
                break;
            case ["--action-kit-resource-audit", var archive, var container, var ownerKey, ..]:
                ActionKitResourceAudit.Write(archive, container, Convert.ToUInt32(ownerKey, 16), output);
                break;
            case ["--world-script-graph-audit", var archive, var container, var worldKey, ..]:
                WorldScriptGraphAudit.Write(archive, container, worldKey, output);
                break;
            case ["--archive-gameplay-script-index", var archive, var sourceRoot, ..]:
                ArchiveCinematicScriptIndex.WriteAllWorlds(archive, sourceRoot, output);
                break;
            case ["--archive-world-transition-call-audit", var archive]:
                ArchiveWorldTransitionCallAudit.Write(archive, output);
                break;
            case ["--archive-cinematic-body-coverage", var archive, ..]:
                ArchiveCinematicBodyCoverageAudit.Write(archive, output);
                break;
            case ["--archive-facial-script-census", var archive, var candidates, ..]:
                ArchiveFacialScriptCensus.Write(archive, candidates, output);
                break;
            case ["--cinematic-playback-audit", var archive, var container, var clipKey, ..]:
                CinematicSceneAudit.Write(archive, container, output, Convert.ToUInt32(clipKey, 16));
                break;
            case ["--cinematic-playback-audit", var archive, var container]:
                CinematicSceneAudit.Write(archive, container, output);
                break;
            case ["--cinematic-timeline-inventory", var archive, var container]:
                CinematicTimelineInventoryAudit.Write(archive, container, output);
                break;
            case ["--cinematic-actor-clip-inventory", var archive, var container]:
                CinematicActorClipInventoryAudit.Write(archive, container, output);
                break;
            default:
                return false;
        }
        return true;
    }

    private static string? OptionalArgument(string[] arguments, int index) =>
        arguments.Length > index ? arguments[index] : null;
}
