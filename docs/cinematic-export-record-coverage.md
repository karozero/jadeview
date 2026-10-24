# Cinematic export record coverage: 3500B8EA

## Finding

The previous “659 unsupported records” warning for `Cinematic · 00_03_dehors_maison_Intro_cine · 3500B8EA` was a parser-level count of non-empty animation key payloads tagged `word-pair-list`. It did **not** mean that 659 arbitrary binary records were discarded. The inspected GLB contains all 659 payloads in `animations[].extras.jadeViewSourceRecords.records`; they are archived as hexadecimal source bytes. Several payload shapes are also decoded into preview timelines and sampled into glTF animation channels. The warning conflated “opaque to the generic transform reader” with “not interpreted anywhere / missing from export.”

Evidence: [AnimationReader.cs](../src/Features/Animation/AnimationReader.cs#L35) adds one opaque payload and increments `clip.Unsupported` once per key when a non-empty pair list is encountered. [SceneExport.Animations.cs](../src/Features/Export/SceneExport.Animations.cs#L20) now reports coverage inventory rather than labeling every increment as discarded. [SceneExport.AnimationEvents.cs](../src/Features/Export/SceneExport.AnimationEvents.cs#L8) writes every opaque payload to GLB extras with its original bytes, key/time, track, source offset, stable record index and classification; a parallel `jadeViewCinematicEvents` schema records decoded calls/messages and links them to raw records without claiming runtime execution.

## Measured inventory

I inspected the existing export `artifacts/light-component-integration/intro-scene-export/FF0045A2-3500B8EA-23.680.glb` (a full cinematic export; the filename records the captured source frame). Its single cinematic animation contains 698 source metadata records:

| Payload family / function selector | Count | Current interpretation |
| --- | ---: | --- |
| `OBJ_CapaSet` (`0x1FC5`) | 91 | `CinematicObjectState` records capability changes; this does not by itself prove a visible/rendered result. |
| `OBJ_FlagsControlSet` (`0x1FB3`) | 63 | Control-flag updates are evaluated by `CinematicObjectState`; only supported state such as visibility has a corresponding exported channel. |
| `OBJDestroy` (`0x1FB1`) | 57 | Evaluated as hidden + inactive for the supported authored-object preview path; source record is not deleted. |
| `OBJ_FlagInvisibleSet` (`0x1FD8`) | 53 | Evaluated as visibility state. |
| `ACTActionSet` (`0x3391`) | 53 | Resolved against actor action kits by `CinematicBodyTimeline`; missing action/kit remains diagnostic/unresolved. |
| `OBJ_SetModuleChannelData` (`0x2047`) | 36 | Recognized by the cinematic channel-form reader when the payload matches its supported shape. |
| `OBJ_AnimMorphFactorNoKey` (`0x2046`) | 29 | Recognized as morph-factor driven by a helper object's transform; target/helper/module must resolve. |
| `Gsp_AE_MASTER` (`0x3CF0`) | 23 | Evaluated only for the supported offset-effect/color-balance modes. |
| `SetLocalAlpha` (`0x3AA0`) | 21 | Evaluated into cinematic material-layer alpha when material/level resolves. |
| `ACTActionFrameSet` (`0x339B`) | 19 | Recognized as a body-action start-frame change. |
| `ANIFrequencySet` (`0x32C9`) | 19 | Recognized as body-action frequency change. |
| `OBJ_FlagInactiveSet` (`0x1FD9`) | 15 | Evaluated as inactive state. |
| `OBJ_ScaleSet` (`0x1FBE`) | 13 | Converts vector calls into transform scale tracks used in playback/export. |
| `IoPafSet` (`0x1B63`) | 12 | Named in the selector catalog; no cinematic export evaluator was found in this audit. |
| `OBJDrawMaskSet` (`0x1FF1`) | 11 | Named in the selector catalog; no corresponding cinematic draw-mask evaluator was found. |
| `GRPInvisibleSet` (`0x3BC4`) | 6 | Evaluated against explicit group members by `CinematicObjectState`. |
| `ARRGlobalIntFlagSet` (`0x2916`) | 6 | Timeline flag analyzer reports writes/gates; the exporter does not run arbitrary script branches from them. |
| `SNDPlayModifier` (`0x2F1A`) | 5 | Consumed by the audio cue builder if the target/modifier resolves. |
| `GRPInactiveSet` (`0x3BC5`) | 5 | Evaluated against explicit group members by `CinematicObjectState`. |
| `ARRGlobalIntFlagClear` (`0x2917`) | 4 | Timeline flag analyzer reports writes/gates; no general script VM execution. |
| `OBJ_RejoinAndOrient` (`0x1FDB`) | 4 | Catalogued, but no dedicated cinematic evaluator was found for this selector. |
| `SetALPHATRESH` (`0x3AA2`) | 4 | Evaluated into cinematic material-layer alpha threshold when material/level resolves. |
| `MATEnableMltxtLvl` (`0x3A9E`) | 2 | Evaluated into cinematic material-layer enablement when material/level resolves. |
| `Gsp_AE_MASTEREVT` (`0x3CF3`) | 2 | Catalogued and described; no corresponding visual cinematic evaluator was found. |
| `COLSetActivationSet` (`0x2C24`) | 2 | Catalogued; collision activation is not exported as runtime collision behavior. |
| `OBJ_BoneDisplayOrder` (`0x1FF9`) | 2 | Catalogued; no corresponding cinematic export evaluator was found. |
| `IoPifSet` (`0x1B62`), `SNDStopModifier` (`0x2F1C`), `ACTKitMerge` (`0x3398`), `OBJ_CustomBitsSet` (`0x1FC3`) | 4 | Individually named/recognized; respectively no visual evaluator found, consumed by audio, used in body-kit resolution, and inspected by the bounded gate analyzer rather than a full script runtime. |
| **Function-call payloads subtotal** | **561** | Generic literal-call decoding succeeds for these payload families; domain coverage varies as shown. |
| Message records, 19 rows (type breakdown below) | **98** | Decoded as cinematic messages; a subset is interpreted by camera/audio/facial/fade/body evaluators. |
| **Total counted as unsupported** | **659** | Per non-empty opaque pair-list payload/key, not count of rows or distinct event types. |

The 39 other exported metadata records are `extra-transform-controls`. Those are parsed into `AnimationTimeCurve` and used by the sampler; they are not counted by `clip.Unsupported`. The count is per non-empty pair-list payload/key, not the number of `(value, tag)` rows inside those lists.

The 98 message payloads break down by decoded message `Type`:

| Type | Count | Current handling found |
| ---: | ---: | --- |
| 60 | 54 | `CinematicCameraEvent` reads the message and supplies source-camera routing/evaluation where its mode and references resolve. |
| 30 | 25 | Dialogue/audio resolution inspects the dialogue and text-bank references. The current PC text reader does not resolve the voice key; diagnostics say so. |
| 53 | 5 | Facial playback resolves the script owner/callback before applying a handler-specific mouth/morph event. Unknown or ambiguous handlers are skipped. |
| 50 | 4 | Screen fade evaluator handles only the supported receiver and ordinary-alpha (`f2 == 0`) path. Other branches are not reconstructed. |
| 47 | 2 | Recognized as an actor body-action request by `CinematicActionEvent`; it still depends on actor targeting and an available action kit. |
| 1, 46, 49, 84 | 8 total | No corresponding message-type evaluator was found in the inspected playback/export paths. Preserve as unknown source messages; do not infer behavior from the IDs alone. |

The function-selector counts are decoded from the second-last word-pair row and resolved using the checked-in selector catalog. The 39 other exported metadata records are `extra-transform-controls`; they are parsed into `AnimationTimeCurve` and used by the sampler, and are not counted as unsupported. These counts come from the GLB's preserved bytes. They are not an assertion that each record has a valid owner, resolves to an actor, passes runtime gates, or produces a visible result.

## Why this is not full cinematic export

The export has two distinct paths. Actor/object transforms are sampled into glTF channels; source event payloads are copied into `extras.jadeViewSourceRecords`. A glTF importer does not execute those extras. Existing evaluators cover selected state (destroy/visibility, supported scale, action-kit/body motion, camera, facial messages, and limited screen fades), but engine callbacks, unknown message types, unsupported branches, and runtime gates are not generically executed by export. Therefore “all source bytes retained” is true for this artifact, while “all game effects reproduced in Blender” is false.

## Export metadata implementation and next step

Implemented: each exported animation now carries `extras.jadeViewCinematicEvents` schema version 1 with `clipKey`, `rawRecordCount`, `decodedRecordCount`, `unknownRecordCount`, `nonEventRecordCount`, `events[]`, and an interpretation note. Each event includes `sourceRecordIndex`, track/key, time/duration, execution context, kind, and `status=decoded_not_executed_by_exporter`; literal calls carry target, selector, catalog name and argument words; messages carry message type and the decoded message fields. Raw source metadata schema version 2 adds the linked `sourceRecordIndex`. The replacement warning distinguishes decoded/unknown/non-event counts and states that GLB extras do not execute game behavior.

This is a decode inventory, not a coverage proof: `decodedRecordCount` does not say an owner, receiver, target, or action kit resolved, and does not mean the callback was simulated. A next slice should add a separate evaluator outcome/status layer for domain-specific preview resolution (evaluated, gated, unresolved, unsupported) and only then make narrowly tested standard glTF tracks for exact resolved visual state, such as supported `OBJDestroy`/visibility transitions. Preserve the raw source record and do not ask Blender to run game callbacks.

## Source map and bounds

- Generic pair-list capture and unsupported counting: [AnimationReader.cs](../src/Features/Animation/AnimationReader.cs#L28).
- Export warning: [SceneExport.Animations.cs](../src/Features/Export/SceneExport.Animations.cs#L8).
- Raw event serialization into GLB extras: [SceneExport.AnimationEvents.cs](../src/Features/Export/SceneExport.AnimationEvents.cs#L8).
- Literal-call/pair-list decoding: [CinematicLiteralCall.cs](../src/Features/Cinematics/Models/CinematicLiteralCall.cs#L31), [CinematicFacialEvent.cs](../src/Features/Cinematics/Models/CinematicFacialEvent.cs#L213).
- Source names for selectors: [engine-function-selectors.json](../src/Features/Cinematics/Data/engine-function-selectors.json#L193).
- Body actions: [CinematicActionEvent.cs](../src/Features/Cinematics/Models/CinematicActionEvent.cs#L9), [CinematicBodyTimeline.cs](../src/Features/Cinematics/Models/CinematicBodyTimeline.cs#L30).
- Visibility/state: [CinematicObjectState.cs](../src/Features/Cinematics/Models/CinematicObjectState.cs#L20) and [SceneExport.CinematicObjects.cs](../src/Features/Export/SceneExport.CinematicObjects.cs#L8).
- Scale calls: [CinematicScriptTransformTracks.cs](../src/Features/Cinematics/CinematicScriptTransformTracks.cs#L5).
- Message routing: [CinematicTimelineMessage.cs](../src/Features/Cinematics/Models/CinematicTimelineMessage.cs#L19); consumers include [CinematicAudioTimeline.cs](../src/Features/Cinematics/Audio/CinematicAudioTimeline.cs#L119), [CinematicCameraEvent.cs](../src/Features/Cinematics/Models/CinematicCameraEvent.cs#L10), [CinematicPlayback.Facial.Events.cs](../src/Features/Cinematics/Models/CinematicPlayback.Facial.Events.cs#L10), and [CinematicScreenFadeTimeline.cs](../src/Features/Cinematics/CinematicScreenFadeTimeline.cs#L15).
- Independent export sanity artifact: `artifacts/light-component-integration/intro-scene-export/blender-report.json` reports the exported cinematic track, two animated armatures/cameras, and moving facial meshes. It demonstrates evaluated animation channels exist in this export, but does not verify every event against the game.

This is a static audit of the checked-in exporter, its current source readers, and one existing GLB. It does not launch BGE or Blender, and does not claim retail-runtime parity for any event whose engine callback/gate is not reconstructed.
