# Source layout

JadeView currently builds as one application. Source folders group responsibilities; they are not separate assemblies. Types retain the `JadeView` namespace and their existing visibility so this first restructuring preserves callers and behavior.

## Modules

- `src/Application`: entry point and application/window lifecycle.
- `src/Features/Archive`: BIG archive reading, extraction and writing; `Compression` contains LZO decoding. `Models` contains archive records.
- `src/Features/AssetBrowser`: asset parsing, resource resolution and PC texture readers. `Models` contains geometry, materials, textures, audio, text and resolved resource data; `Readers` contains parsers.
- `src/Features/SceneEditor`: scene loading, editor panels, viewport rendering and scene preview. `Models` contains scene documents, objects, transforms, gizmos and GPU render data.
- `src/Features/Animation`: animation loading and evaluation. `Models` contains actors, bones, clips, tracks and keys.
- `src/Features/Modding`: mod project workflows, command history and validated scene writes. `Models` contains project/change/write-plan data; `Commands` contains edit commands.
- `src/Features/Export`: GLB scene export. `Models` contains export options and results.
- `src/Features/GameIntegration`: game discovery, launch, installation, backup and restoration. `Models` contains operation results.
- `src/Features/SceneEditor/SceneRenderer.Verification.cs`: existing renderer verification routines, invoked by `--test-renderer`.
- `formats`: embedded texture mapping data. Resource names remain unchanged.

## Facial preview and material export

- `Animation/Morphs/Formats/Pc`: type-4 payload reader and shared packed expression-command decoder.
- `Animation/Morphs/Models`: source records, keys, timelines, expression descriptions and playback state.
- `Animation/Morphs/Preview`: direct weights, channels, transient blink and expression preview. The partial class shares state; its files divide behaviors rather than define separate services.
- `Animation/Morphs/Profiles/Pc`: verified source qualification and expression tables. Geometry keys, channel signatures and known source issues stay here, outside the generic preview and UI. These are still explicit profiles, not automatically recovered controller bindings.
- `Animation/Appearances`: shape application, session overrides and appearance discovery. `Models` describes companion bindings; `Profiles/Pc` contains the temporary verified head/accessory pairing.
- `SceneEditor/Morphs`: ImGui controls. They consume preview capabilities and preset descriptions rather than independently recognizing characters.
- `Export/Materials`: material selection and composition rules, atlas rasterization, UV projection, texture writing and alpha conversion. `Models` owns atlas/layout/selection data. `MaterialLayerRules` and `MaterialAtlasBaker` do not depend on actor names or mutable glTF output state; `SceneExport.*` files coordinate resources and output.

Known profiles are deliberately separate from binary-format rules. A new geometry
can still expose its source morph targets without matching a blink/expression
profile. Adding a profile must not imply native timing or shader parity.

## Main flow

`Application → Archive / AssetBrowser → SceneEditor → SceneRenderer`

Editing uses commands and `ModProject`; `WritePlan` and `SceneWriter` validate and apply patches before `Archive` writes a separate BF. Export uses `SceneExporter` and the resolved scene resources.

The project explicitly compiles `src/**/*.cs`. Build and run commands remain the same; the project file, manifest and embedded data remain at their existing paths.

## Scope of the first cleanup

Code is grouped by application feature, and each feature owns its model classes in its `Models` directory. Individual data types stay in their own files and retain the `JadeView` namespace. Editor panels and renderer verification use partial classes to retain shared private state and the existing API. No algorithms, serialization formats, UI behavior or supported capabilities were intentionally changed.

Folder boundaries do not yet enforce dependency direction: scene loading still refers to editing matrix types, the renderer still contains viewport input/UI, and the editor coordinates file operations. These remain visible follow-up refactoring points. Partial files improve navigation but do not create independent services.

## Export implementation boundaries

`SceneExport.cs` selects objects and coordinates an export. `SceneExport.Nodes.cs`
handles scene nodes and placement; `SceneExport.Accessors.cs` writes binary views
and accessors; `SceneExport.Document.cs` assembles JSON and writes GLB chunks.
`SceneExport.FileNames.cs` owns output filename rules, and `Materials/SceneExport.Png.cs`
encodes texture images. These partial files still share one export lifetime.

`Export/Models/GlbFormat.cs` names container and accessor constants.
`ExportPlacement` preserves the UI's existing integer placement values.
`AssetBrowser/Models/JadeMaterialFlags.cs` names shared PC material bit flags;
material parsing and morph parsing keep record sizes beside their readers.
This cleanup does not extend material support; the RLI overlay task remains deferred.

Animation export samples poses into `Export/Models/ExportAnimationTrack.cs`,
which owns TRS samples and quaternion hemisphere continuity. Actor hierarchy
construction and parent-relative poses are in `SceneExport.ActorHierarchy.cs`.
Morph preview deformation is isolated in `FaceMorphPreview.Deformation.cs`;
preview state, channels and transient playback remain separate partial files.

Mesh export separates primitive assembly (`SceneExport.Geometry.cs`) from corner
seams and vertex buffers (`SceneExport.Vertices.cs`, `Models/ExportMeshVertices.cs`).
Index accessors live with the other binary accessors. Skinning and planar UV
projection share the same palette-link resolver to avoid divergent fallback rules.
`Materials/MaterialLayerTint.cs` evaluates reflection/detail tint separately from
UV projection and output writing; it does not enable deferred RLI overlay baking.

`Animation/Morphs/PcMorphTransitionEvaluator.cs` models one explicitly supplied,
qualified PC controller-slot transition. Its snapshot and phase live in the
feature's `Models` directory. It reuses the existing channel evaluator for
weights, and owns no actor names, speech triggers, UI state or source mutation.
Preview and export call it through the mouth-cycle evaluator; it owns no recording state.

`Animation/Morphs/PcMorphMouthCycleEvaluator.cs` wraps that transition for the
qualified automatic slot-4 cycle. Its caller supplies the sequence and controller
snapshot (`PcMorphMouthCycleState`); it selects at most one form per update and
does not infer character bindings, initial state or speech timing. It is also
independent of preview/UI/export orchestration.

`PcMorphTransitionCompletion` selects either progression-threshold completion
or progression plus factor convergence with endpoint snapping. Selection belongs
to a qualified controller binding; neither evaluator infers it from actor names.

`ActorModuleMap` and `SceneActor.Modules` resolve module selectors through source
appearance tables. Shape changes preserve FF selector inheritance. This lookup
is separate from appearance application and facial controller timing; malformed
maps do not fall back to guessed component indices.

`FaceMorphPreview.Mouth` owns a transient manual cycle, baseline restoration and
preview-mode takeover. The source definition comes from the verified PC profile;
the UI additionally requires the selected mesh to resolve through its module
selector. Project capture saves the pre-cycle authoring state, while GLB receives
the evaluated current pose. Optional `SceneExport.MouthCycle` samples a copy into
a separate experimental GLB clip through `FaceMorphMouthCycleSampler`; it never
records authoring keys or changes the running controller. Provenance and STEP
interpolation are carried by `ExportMorphTimeline` alongside ordinary authored
LINEAR timelines.

`PcMorphControllerDiscovery` follows root brain/script-model resources to a
supported native controller family. `PcScalarVariableReader` reads bounded OVA
scalars with model/default overlay semantics. `PcMorphControllerBinding` qualifies
the resulting sequence against the selected module's morph data. Mouth discovery
is independent of geometry/name profiles; unknown families remain unsupported.

OVA block validation and offsets live in `PcVariableStorageReader`; scalar lookup
stays in `PcScalarVariableReader`. `PcVariableFormat` names record sizes and types,
and storage layouts/scalar values live in the feature's `Models` directory.
`PcMorphControllerSequences` owns supported code signatures and family selection.

`FaceMorphMouthPlaybackFactory` builds the initial controller snapshot and affected
groups. `FaceMorphMouthCycleSettings` shares preview defaults and limits with the UI;
playback takeover and baseline restoration remain in `FaceMorphPreview.Mouth`.
Export-specific option validation stays in `SceneExport.MouthCycle`.

The verification executable dispatches optional audit commands through
`VerificationCommands`; its main entry point runs the verification suite.
