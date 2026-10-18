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
- `formats`: embedded texture mapping data. Resource names remain unchanged.

## Main flow

`Application → Archive / AssetBrowser → SceneEditor → SceneRenderer`

Editing uses commands and `ModProject`; `WritePlan` and `SceneWriter` validate and apply patches before `Archive` writes a separate BF. Export uses `SceneExporter` and the resolved scene resources.

The project explicitly compiles `src/**/*.cs`. Build and run commands remain the same; the project file, manifest and embedded data remain at their existing paths.

## Scope of the first cleanup

Code is grouped by application feature, and each feature owns its model classes in its `Models` directory. Individual data types stay in their own files and retain the `JadeView` namespace. Editor panels and renderer verification use partial classes to retain shared private state and the existing API. No algorithms, serialization formats, UI behavior or supported capabilities were intentionally changed.

Folder boundaries do not yet enforce dependency direction: scene loading still refers to editing matrix types, the renderer still contains viewport input/UI, and the editor coordinates file operations. These remain visible follow-up refactoring points. Partial files improve navigation but do not create independent services.
