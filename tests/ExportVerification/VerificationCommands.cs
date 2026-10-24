internal static class VerificationCommands
{
    public static bool TryRun(string[] arguments, string output)
    {
        switch (arguments)
        {
            case ["--script-owner-resource-fixture", var archive, var container, var ownerKey]:
                ScriptOwnerResourceFixture.Write(archive, container, Convert.ToUInt32(ownerKey, 16), output);
                break;
            case ["--script-model-variable-audit", var archive, var container]:
                ScriptModelVariableAudit.Write(archive, container, output);
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
            case ["--nested-eve-transform-audit", var archive, var container, var clipKey, ..]:
                CinematicNestedEventTransformAudit.Write(archive, container,
                    Convert.ToUInt32(clipKey, 16), output);
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
            case ["--actor-audit", var archive, var actorName, ..]:
                ActorAssetAudit.Run(archive, actorName, output);
                break;
            case ["--morph-discovery", var archive, ..]:
                MorphDiscoveryAudit.Run(archive, output, OptionalArgument(arguments, 2));
                break;
            case ["--archive-cinematic-census", var archive, ..]:
                ArchiveCinematicCensus.Write(archive, output);
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
            case ["--action-kit-resource-audit", var archive, var container, var ownerKey, ..]:
                ActionKitResourceAudit.Write(archive, container, Convert.ToUInt32(ownerKey, 16), output);
                break;
            case ["--world-script-graph-audit", var archive, var container, var worldKey, ..]:
                WorldScriptGraphAudit.Write(archive, container, worldKey, output);
                break;
            case ["--archive-gameplay-script-index", var archive, var sourceRoot, ..]:
                ArchiveCinematicScriptIndex.WriteAllWorlds(archive, sourceRoot, output);
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
