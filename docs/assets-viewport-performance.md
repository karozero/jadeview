# Assets viewport performance audit

Scope: Assets mode, not cinematic playback. Container `FF0045A2`, world
`00_03_dehors_maison_Intro_cine` (`3500B8E7`), focus
`Cine_SFX_Faisceau_ascension.gao` (`3500BB78`, GRO `3500BAEF`).
The decoded world contains 1,082 objects and 45 actors.

## Reproduction

Run the verification executable with:

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release --no-restore -- --assets-render-performance-audit "E:/SteamLibrary/steamapps/common/Beyond Good and Evil/sally_clean.bf" FF0045A2 00_03_dehors_maison_Intro_cine Cine_SFX_Faisceau_ascension.gao 3440 1440 bounds
```

The optional final arguments are width, height and camera preset: `all` (Frame all), `bounds`
(normal Frame selection), `close` (closer to the bounds center), or `origin`
(near the selected object's transform origin). The audit opens its own hidden
Raylib window, leaves user applications untouched, warms 20 frames and measures
60 frames. It includes GPU completion (`glFinish`) in total frame time, uses the
normal Assets actor updates, materials, selection wireframe and grid, and exports
a viewport PNG. It does not run cinematic discovery/playback or ImGui panels.

## Confirmed hot path and correction

The previous computed-lighting cache stored one owner per GRO part. Multiple GAO
instances sharing a GRO evicted each other's CPU lighting result every frame.
Samples now belong to the instance and part. A separate upload record tracks the
sample currently in the shared GPU mesh, so changing draw owners still restores
the correct colors. Lighting equations, object visibility, particles and draw
passes are unchanged.

Invalidation retains the existing lighting-plan signature, actor/billboard pose,
appearance revision and RLI inputs. Samples and prepared plans use weak owner keys, so same-count
object replacement cannot retain removed instances. World/object-count changes,
GPU cache clearing and disposal reset samples and upload state.

CPU tests cover independent samples for equal GAO keys, owner switching, changed
sample revisions, cache clearing and collection of removed owners. The GPU pixel
regression draws two differently painted instances of the same GRO, reorders
them and changes appearance; it verifies both cache reuse and correct colors.

## Measurements on 2026-10-24

GPU: NVIDIA GeForce RTX 3060 Ti. Current source, hidden viewport, no ImGui panels:

- 1600x900, bounds camera, before: total average 27.65 ms, p95 29.80 ms;
  205 visible objects, 86,803 triangles, 214 lighting evaluations per frame.
- 1600x900, bounds camera, after: total average 22.02 ms, p95 25.09 ms;
  same visible objects/triangles, 160 lighting evaluations per frame.
- 3440x1440, bounds camera, after: total average 20.61 ms, p95 21.84 ms;
  207 visible objects, 86,837 triangles. Render submission/wait average 19.60 ms,
  geometry preparation 2.10 ms, draw pass 16.14 ms, world actors 0.16 ms.
- 3440x1440, Frame all, after: total average 21.15 ms, p95 25.31 ms;
  218 visible objects, 90,382 triangles, 189 lighting evaluations per frame.
- 3440x1440, origin camera, after: total average 2.81 ms, p95 4.18 ms;
  12 visible objects, 8,587 triangles.

Warm-up/timing-dependent particles can change counts between runs. The 1600x900
before/after uses identical framing and equal final counts, but is a partial
optimization, not proof that every reported stall is fixed.

The headless audit did not reproduce the reported 250 ms frame. The later full UI
audit below captures a similar cold-load spike, but not a sustained 250 ms cost. Read-only
process inspection found the user's running JadeView under
`artifacts/cinematic-script-track-index`, whose DLL was written on 2026-10-22
04:04:55, rather than the current source. This is evidence of a version mismatch,
not proof of the cause of all stalls. Retest the new build with the same camera,
viewport size and display flags. Renderer status now splits preparation and draw
time; total UI frame versus render time can distinguish the next bottleneck.

Logs are in ignored `artifacts/assets-performance-*.log`; screenshots are under
`artifacts/export-verification/assets-performance-*.png`.

## Full editor UI audit

`--assets-ui-performance-audit` runs the actual `EditorShell.Draw` and rlImGui
backend in an independent hidden 3440x1440 window, with the same archive, current
world, focused object, scene picker index and full 10,005-file content catalog.
It initializes the application's fonts/theme and MSAA window flag. It neither
attaches to nor sends input to the user's running editor.

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release --no-restore -- --assets-ui-performance-audit "E:/SteamLibrary/steamapps/common/Beyond Good and Evil/sally_clean.bf" FF0045A2 00_03_dehors_maison_Intro_cine Cine_SFX_Faisceau_ascension.gao
```

The audit measures the default workspace, the `Game-hidden objects` display
variant and a close-to-beam-base camera. Default uses 30 warm-up frames; hidden
profiles use 300, followed by 90 measured frames. It also profiles actual
Outliner, Properties inspector and content-browser draw methods in independent
ImGui panel windows. This second measurement isolates panel CPU cost; its window
size differs from the default workspace, so those numbers are diagnostic rather
than additive frame estimates. No unsaved project data or BF resources are
written. Screenshots/logs are stored under ignored `artifacts`.

Static source review confirms that the Outliner rebuilds matching rows and counts
on every frame, but clips visible rows before drawing. The catalog is rebuilt on
filter changes, not every frame, and its file table also uses ImGuiListClipper.
Properties reads only the selected object and direct resource lookups. Textures
currently enumerates its resolved list each frame; Diagnostics rebuilds its log
string each frame. These are candidates for future targeted optimization only
if measured cost warrants it; they are not evidence of a 250 ms stall.

Full UI measurements (same machine, separate process, current code):

- Default workspace: 23.43 ms average total, 26.27 ms p95; renderer 20.84 ms,
  other shell work 0.42 ms. Outliner/Properties/All Assets did not reproduce a
  sustained 250 ms cost.
- With game-hidden objects enabled and 300 warm-up frames: 847 visible objects,
  47.62 ms average total, 51.60 ms p95. Other shell work remained 0.32 ms.
- Individual panels: Outliner 0.06 ms average, Properties 0.02 ms, All Assets
  0.07 ms, Textures 0.14 ms, Diagnostics 0.07 ms (sizes/windows differ from full UI).
- A separate close-to-beam-base, game-hidden run recorded a cold warm-up maximum
  of 298.46 ms and a peak of 384 geometry cache misses/uploads. After 300 frames,
  steady total was 13.12 ms average, 17.28 ms p95; `glFinish` tail averaged 1.55 ms.
  These first counters establish a cold-load association, not a measurement that
  uploads alone consumed the entire spike.
- An opt-in per-owner hotspot breakdown now records cumulative skin/deformation
  and diffuse-lighting time for each GAO instance. In a rerun of the exact
  `3500B8E7` world with `Cine_SFX_Faisceau_ascension.gao` focused, game-hidden
  objects enabled, and the 3440x1440 full UI, warm-up frame 0 reached 467.21 ms
  total / 404.32 ms renderer, with 410 geometry uploads and 118.08 ms diffuse
  light evaluation. The six largest per-instance contributors were
  `boss_final_cine.gao` (0.64 ms geometry + 9.99 ms light),
  `Cine_SFX_Oeuf_Domzh_04.gao` (0.51 ms geometry + 8.58 ms light),
  `S_BoukBoy_Haut02.gao` (0.97 ms geometry + 0.09 ms skin + 7.11 ms light),
  `b_Crumble_TeteBD.gao` (5.48 ms skin + 2.08 ms light),
  `Cine_Tentacule_long.gao` (0.29 ms geometry + 6.88 ms light), and
  `B_BoukBoy_Torse.gao` (6.02 ms skin + 0.84 ms light). The focused beam was not
  among those six; this does not prove it has zero cost. After warm-up the
  profile averaged 24.03 ms, p95 25.01 ms, with 847 visible objects.
  The evidence points to a distributed cold scene-upload/lighting spike rather
  than a uniquely dominant beam object. It does not identify CPU versus driver
  wait inside native uploads or prove a sustained 250 ms frame in the user's
  running build.

Renderer diagnostic timers now expose procedural video updates, render-target
setup, lighting-frame preparation, clip-range preparation, overlays and final
render-batch/target completion. Successful geometry cache misses also accumulate
the wall time for CPU mesh preparation plus GPU creation/upload (not GPU transfer
alone). These diagnostic changes do not alter rendering or resource selection.

A subsequent high-object-count sample ruled out the suspected video/light-list
scan: video 0.00 ms, target setup 0.00 ms, light-frame preparation 0.07 ms and clip
preparation 0.13 ms. The remaining renderer time requires draw/finalization
interpretation; wall times may include driver waits and are not pure CPU times.

Final cold-frame confirmation (`artifacts/assets-full-ui-cold-performance.log`):

- Requested profile `show-hidden-close`, first frame: total **280.98 ms**, renderer
  212.19 ms, **384 successful geometry cache misses in that same frame**.
- Direct accumulated CPU mesh preparation plus GPU creation/upload: **75.35 ms**.
  Geometry misses explain part of the cold frame, not its entire duration; material
  creation, texture work, shader/driver startup and UI startup remain included.
- After warm-up: total 8.29 ms average, 9.15 ms p95, maximum 10.03 ms;
  renderer 6.50 ms, other shell work 0.31 ms, 71 visible objects.
- Renderer phases: geometry preparation 1.50 ms, draw submission 1.35 ms,
  overlays 0.03 ms, final render-batch/target completion 3.26 ms.
  `glFinish` tail: 0.93 ms. Video update 0.00 ms; light-frame preparation 0.06 ms.

Pass `default`, `show-hidden` or `show-hidden-close` as an optional final argument
to run one profile; omission runs all. These are test-camera presets, not scene
rules. The close camera derives its target from the selected geometry's lower
world-space bounds. No particular GAO name changes application behavior.

The slowest warmed wide-view hidden sample varied between 47.62 and 67.81 ms
across runs, with 847 visible objects; it is a remaining renderer throughput
issue rather than an Outliner/Inspector/content-list bottleneck. The new overlay
and finalization timers were validated in the final close run; their values in
older intermediate logs are not used as evidence for the wide-view remainder.

## Shader uniform submission correction

The warmed hidden-object scene submitted **94,120 custom shader uniform values
per frame**, even when most values matched the previous draw. A cache now skips
only bit-identical values already stored in that renderer's Jade shader program.
It also covers the two custom model-view/planar matrices. Built-in Raylib uniforms
(MVP, normal matrix, texture samplers and `colDiffuse`) remain owned by Raylib and
are not cached by this module.

The cache keys use uniform location within the current shader identity, with
separate unmanaged value types. It resets on shader initialization, disposal or
identity change. Float/vector/matrix comparisons preserve signed zero and NaN
payload bits; integer draw flags never pass through a float representation.
Disabling the cache in the audit still updates its last-value bookkeeping so
switching back cannot reuse stale GPU values. Source audit found no external
writes to these private custom uniform locations after routing the two matrix
writes through the same module.

Paired full-UI runs, same camera, **847 visible objects / 94,507 triangles**:

- Forced native submission: **66.80 ms average**, 75.71 ms p95;
  94,120 uploads out of 94,120 requests, draw phase 61.16 ms.
- Cached submission: **28.97 ms average**, 32.79 ms p95;
  **3,130 uploads** out of 94,120 requests, draw phase 23.81 ms.
- Cold first-frame maxima were 481.47 and 460.49 ms respectively, with 410 geometry
  uploads and approximately 75 ms of mesh preparation/upload in both. These noisy
  cold runs do not demonstrate a resolved cold-start stall.

The control is test-only: set `JADEVIEW_AUDIT_UNCACHED_UNIFORMS=1` before the same
full-UI audit command to force submission; unset it for the optimized path.
Real archive FX continue to use wall-clock animation in these throughput runs,
so their screenshots are not claimed to be pixel-identical. Camera, visible
object and triangle counts are equal. A separate static GPU scene compares every
RGBA byte with cached/uncached submission across material switches and passes
with identical images. The GPU material suite and full synthetic verification
suite also pass. Logs: `artifacts/assets-uniforms-uncached.log`,
`artifacts/assets-uniforms-cached-final.log`, `artifacts/assets-uniforms-gpu-tests.log`,
`artifacts/assets-uniforms-material-tests.log`, `artifacts/assets-uniforms-full-tests.log`.

This correction changes submission work, not lighting equations, material values,
asset visibility, particle capacity or draw order. Earlier timings in this document
predate this correction and remain useful as audit history, not current targets.

## Bounded cold-resource breakdown after the uniform fix

The final instrumented run (`artifacts/assets-resource-cold-final.log`) records
these values from the **same first frame**, at the same wide hidden-object camera:

- Full UI frame **503.53 ms**, renderer **434.93 ms**.
- Shader/resource initialization **35.05 ms**, occurring in viewport Update before
  Render. It is measured as a lifetime-counter delta across `EditorShell.Draw`;
  resetting a Render-only timer would incorrectly report zero.
- Geometry preparation/upload **71.49 ms**, 410 successful cache misses.
- Material-set preparation **14.04 ms inclusive**, 409 sets / 1,972 passes.
  Nested within that total: source texture resolution 0.94 ms, native material
  allocation 0.48 ms, TGA decode 6.18 ms, texture creation/upload 3.66 ms / 86 textures.
- Renderer geometry-preparation phase **155.77 ms inclusive** and draw phase
  **267.64 ms inclusive**. Within the draw phase: CPU light-plan preparation
  **31.94 ms / 653 plans**, vertex input extraction/evaluation **126.99 ms /
  3,376 evaluations**, computed-color conversion/upload **5.30 ms**.
- Other measured renderer stages: actor update 0.25 ms, frame-light bookkeeping
  1.40 ms, target setup 1.23 ms, clipping 2.40 ms, overlays 2.49 ms, finalization 1.20 ms.

Inclusive phases and their nested subphases must **not** be added together.
These wall timers can include first-use JIT/native/driver work; they do not prove
that the entire remaining draw time is GPU execution or shader compilation.
Native GPU timer-query/CPU sampling evidence would be needed for that attribution.

After warm-up this run is **30.68 ms average / 35.16 ms p95**, still 847 visible
objects and 94,507 triangles. The unchanged CPU texture resolution is below 1 ms
in the cold frame, so no speculative rewrite or asynchronous resource-loading
change was introduced. The confirmed remaining CPU cost is initial light-plan
and per-vertex preparation/evaluation; equations and ordering were left unchanged.
Repeated triangle-corner inputs may be a future deduplication candidate, but a
new optimization requires explicit duplicate-input and output-equivalence proof.

This profiling increment adds diagnostics only. Full synthetic verification and
GPU dynamic-light/uniform-image checks passed after instrumentation:
`artifacts/assets-resource-cold-tests.log` and
`artifacts/assets-resource-cold-gpu-tests.log`.

## Assets selection and moving-camera repeat

The repeat runs the complete Assets UI in a separate hidden 3440×1440 window,
not cinematic playback or the user's running JadeView window:

```powershell
& tests/ExportVerification/bin/Release/net10.0-windows/ExportVerification.exe --assets-ui-performance-audit 'E:/SteamLibrary/steamapps/common/Beyond Good and Evil/sally_clean.bf' FF0045A2 00_03_dehors_maison_Intro_cine Cine_SFX_Faisceau_ascension.gao interactions
```

Authoritative log: `artifacts/assets-interactions-final-performance.log`.
Container FF0045A2 / world 3500B8E7 contains 1,082 objects. A cold center-ray pick
actually hits `3500BB78 / Cine_SFX_Faisceau_ascension.gao`; the Properties panel
uses that selected object. Hidden objects, colliders, selection wireframe, grid,
gizmo and frustum culling are enabled; whole-scene wireframe starts disabled.
Camera motion is a scripted orbit, not injected RMB input.

- Cold ray selection: **165.23 ms**, including 414 geometry uploads / 73.83 ms
  geometry preparation. The following first full UI frame: **350.72 ms**,
  renderer 320.16 ms, zero geometry uploads. These are separate operations.
- Wide orbit, 240 measured frames after 120 warm-up frames: **29.13 ms average /
  32.19 ms p95 / 113.53 ms maximum**, 780–850 visible objects, 85,636–94,941
  triangles, zero measured geometry uploads; approximately 224 KB allocated/frame.
- Close orbit: **5.46 ms average / 10.15 ms p95 / 11.36 ms maximum**, 2–210 visible
  objects. This particular close camera sees fewer objects than the wide view.
- Repeated actual selection notifications: **29.24 ms average / 30.77 ms p95**;
  notification callback at most 0.01 ms, zero geometry uploads.
- Repeated ray picking: **31.70 ms average / 34.63 ms p95**; ray work averages
  2.51 ms, maximum 10.52 ms. Whole-scene wireframe averages 13.41 ms.

The wide orbit's first warm-up frame costs **197.39 ms** (renderer 194.81 ms),
reproducing the earlier isolated ~203 ms transition spike. It has **no geometry,
material, texture or light-plan cache misses and no Gen2 collection**. Timers
record 80.56 ms skinning and 61.97 ms vertex-light evaluation; inclusive prep/draw
phases are 48.80 / 144.48 ms. Do not add nested timings. This localizes substantial
first-moving-frame deformation/evaluation work but does not separate all JIT,
native or driver costs without a sampling profiler.

**Persistent ~250 ms frames were not reproduced.** The cold spikes and single
113.53 ms warmed outlier do not establish that symptom. Exact user camera,
application history, viewport layout and per-executable GPU profile remain
unmatched. Read-only inspection found the user's running executable under
`artifacts/cinematic-script-track-index`, with an October 22 DLL, and NVIDIA's
OpenGL driver loaded; that process was not changed or interrupted.

This repeat changes test diagnostics only, with no additional production
optimization. Full synthetic verification passed (exit 0):
`artifacts/assets-interactions-tests.log`. If the current published application
still sustains 250 ms, the next useful evidence is a bounded slow-frame trace
inside that actual executable recording camera/display state, UI/render time,
cache misses, deformation/evaluation and GC across the problematic movement.

## Pose-aware skin and lighting cache invalidation

`SceneActor.Evaluate` previously advanced `PoseRevision` on every call, including
frames where the evaluated world matrices and face-morph weights were unchanged.
The renderer uses that revision to decide when to rebuild skinned mesh positions
and per-vertex diffuse lighting, so an idle pose repeatedly discarded reusable
work. The revision now advances when the evaluated world pose or a member's
face-morph deformation revision changes. Direct weights, timeline samples,
channels, expressions, blink and mouth-cycle updates all feed that deformation
revision. Explicit pose invalidation remains available for scripted changes.

The regression suite checks that a stable actor pose keeps the same revision,
transform changes invalidate it, and changing a morph weight invalidates it. The
full suite passes (`dotnet run --project tests/ExportVerification/ExportVerification.csproj
--configuration Release --no-restore`).

The exact Assets interaction audit was repeated after this change on the same
archive/container/world/object and settings; log:
`artifacts/assets-interactions-final-pose-revision.log`.

- Wide camera orbit: **23.35 ms average / 25.69 ms p95** over 240 frames; first
  orbit warm-up frame **126.64 ms**. Its vertex-light evaluation is now **0 ms**
  because unchanged light samples remain cached; skinning remains about **86 ms**
  for newly visible actors.
- Before pose-aware invalidation, the comparable first wide-orbit warm-up was
  **197.39 ms**, including **61.97 ms** of vertex-light evaluation. The post-fix
  sample therefore removes that repeated light work, though one-time actor
  deformation still makes the first orbit frame slower.
- Cold selection still takes **162 ms** and the first full UI frame after it
  **339 ms** in the hidden audit window. These are one-off cold operations, and
  persistent 250 ms frames were not reproduced. The user's exact executable,
  viewport resolution and camera path remain unmatched.

This optimization does not change lighting equations, animation sampling,
morph weights or export data. GPU image-equivalence and full synthetic checks
remain passing from the renderer audit; real-time comparison in the user's
current executable is still needed to verify their reported 250 ms symptom.

### Current-binary repeat (October 24)

The identical `interactions` command was rerun with the verification binary dated
October 24 05:29 and JadeView DLL dated 05:27 (6,913,536 bytes), exit 0.
Log: `artifacts/assets-interactions-repeat-performance.log`.

Cold picking again hit the exact beam: **173.89 ms**, 414 geometry uploads,
80.84 ms mesh preparation. First full UI frame afterwards: **361.25 ms**,
328.09 ms renderer, 95.26 ms deformation/skinning, 126.70 ms vertex-light
evaluation and 30.17 ms light-plan preparation, with zero geometry uploads.
These inclusive stages are not additive.

Warmed wide orbit: **23.71 ms average / 26.38 ms p95 / 28.18 ms maximum**,
780–850 visible objects and 85,636–94,941 triangles; no geometry uploads. Its
first warm-up moving frame was **133.39 ms**, including 88.78 ms deformation,
zero resource/light-plan misses, zero vertex-light evaluations and no Gen2 GC.
Compared with the earlier run, warmed vertex-light evaluation is now zero in this
fixture; this rerun introduces no production change and is not a controlled
single-change comparison against the earlier binary.

Repeated selection notifications average 28.31 ms (callback at most 0.02 ms);
repeated picking averages 33.79 ms, with the ray itself averaging 2.77 ms.
Close orbit averages 6.41 ms; whole-scene wireframe 15.27 ms.

The repeat confirms expensive cold geometry work and then first-use deformation/
lighting, rather than persistent 250 ms frames in this reproduction. The
remaining warm render is draw-dominated; wireframe changes the draw workload but
is not proof of a specific GPU bottleneck. The first-motion deformation spike
requires CPU/native sampling inside `UpdateSkinCore` to separate skinning,
procedural generation, normals and buffer-upload/driver waits. No unsafe cache,
visibility change or new production optimization was introduced from stage-only
timers. The original sustained 250 ms report remains unconfirmed until traced
with its actual camera and executable configuration.

### Remaining mismatch and focused capture proposal

A source/build comparison after the repeat found the user's existing process
still running `artifacts/cinematic-script-track-index/JadeView.exe` (PID 16500,
DLL October 22 04:04:55). The tested DLL is October 24 05:27; published uniform
optimization build is `artifacts/assets-performance-uniforms` (October 24 04:25).
An unchanged running process cannot acquire a replaced DLL's improvements.
This is a verified build mismatch, not proof that it causes the reported 250 ms.

The existing viewport `Frame` statistic is `Raylib.GetFrameTime()` whereas
`Render` is the renderer stopwatch. They measure different boundaries. The real
application also calls `SetTargetFPS(60)`, initializes audio and runs mouse-capture
and window-title updates; the hidden UI fixture uses the same shell but not that
entire application history. At 250 ms the 60 FPS limiter alone is not a sufficient
explanation. A hidden benchmark also cannot establish behavior under the user's
per-executable driver profile or foreground presentation configuration.

A focused next capture should be opt-in in the actual app, bounded to 300 frames
in a ring buffer, flushing only on explicit stop or a >100 ms frame. Record:

- Executable/DLL timestamp and archive/world/focus keys; window, viewport and DPI;
  camera position/target; Assets mode and Display flags, active Inspector tab.
- Outer-loop wall duration separately from title/mouse capture, Shell.Draw,
  ImGui submission and EndDrawing/presentation. Pair each row with the *previous*
  Raylib frame-time sample to avoid comparing unrelated frame boundaries.
- Visible objects/triangles, resource misses and upload times, skin/deformation,
  light plans/evaluation, material passes/uniform uploads, allocation/GC counts.
- Whether cinematic state/audio survives switching back to Assets; the fresh
  fixture does not test that history.

Then compare the same camera stationary, rotating, and returning after one warm
orbit, both hidden-object settings and selection wireframe settings. A large
EndDrawing cost points toward presentation/driver/GPU work; a large UpdateSkinCore
cost calls for CPU/native sampling of procedural generation, normal recomputation
and upload waits. Use GPU timestamps or an external GPU capture before blaming
alpha overdraw: the wireframe speed difference alone is insufficient proof.
This is an instrumentation proposal, not implemented production behavior.

### Opt-in capture in the actual JadeView executable

Implemented: `src/Application/AssetsFrameTrace.cs`, instrumented only when
`JADEVIEW_ASSETS_FRAME_TRACE=1`. Without that variable the trace is not created.
It retains the latest **300 frames** and independently the latest **32 frames at
or above 100 ms**. It does not call glFinish, change visibility or touch shaders.
Press **F10** after reproducing; normal application exit saves as well. The bounded
capture is written to `<executable directory>/artifacts/assets-frame-trace-<PID>.tsv`.
F10 overwrites that process's log with its current buffers; writing is outside
measured boundaries and may itself cause a one-off stall, so exclude the save
moment when interpreting timings. Tracing formats bounded per-frame diagnostic
strings and adds overhead; compare it with an untraced run if necessary.

Example from the repository root (use a new diagnostic build; do not overwrite
or kill the already running executable):

```powershell
$env:JADEVIEW_ASSETS_FRAME_TRACE = '1'
& .\artifacts\assets-trace-verification\JadeView.exe 'E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf'
Remove-Item Env:JADEVIEW_ASSETS_FRAME_TRACE
```

In **Assets**, choose container `FF0045A2`, subscene/world
`00_03_dehors_maison_Intro_cine` (`3500B8E7`), select
`Cine_SFX_Faisceau_ascension.gao` (`3500BB78`), and press F to focus. First observe
selection/loading, then orbit toward the region with many objects for several
seconds. Reproduce the user's Display settings; record both Show hidden settings
if unsure. Press F10 immediately after the slow section. Keep the prior ordinary
app separate: the trace header identifies the executable, DLL timestamp and PID,
so old/new runs cannot be mistaken for each other.

Columns split whole app-frame wall time, Shell.Draw, shell time outside renderer,
ImGui submission, EndDrawing/presentation (including Raylib's FPS limiter), viewport
render/preparation/draw/finalization, geometry uploads/miss time, visible objects,
triangles, deformation and allocation/Gen2 GC. Context includes world/focus keys,
camera, window/viewport size and Display flags. `cinematicMaterials` indicates
whether that renderer context is present; it is not a full scripting-state dump.
The `ownerHotspots` TSV column lists up to six GAO instances with the highest
combined geometry preparation/upload (`geo`), deformation (`skin`) and
diffuse-lighting (`light`) time for that frame. Values are cumulative when an
instance has multiple parts. Geometry time is attributed to the first visible
instance that requests a shared GRO upload. This breakdown is collected only
with opt-in tracing and helps distinguish a single expensive instance from work
spread across the scene; it does not separate CPU work from native GPU-buffer
waits.
UI-outside-render is a subtraction estimate, not exclusively widget CPU time.
Timings are inclusive and nested; do not sum render with its subphases. No GPU
execution timing is claimed: a large draw/upload/EndDrawing interval still needs
GPU timestamps or CPU/native sampling to distinguish CPU work from driver waits.

Capture verification: isolated build `artifacts/assets-trace-verification`, build
exit 0 with zero warnings; `artifacts/assets-trace-build.log`. Opt-in/default-off,
300/32 ring bounds, retention ordering and file persistence checks pass:
`artifacts/assets-trace-checks.log`. A real full-UI default-profile audit with
tracing enabled also exits 0: `artifacts/assets-trace-enabled-ui-audit.log`.
Its saved capture `artifacts/assets-trace-verification/artifacts/assets-frame-trace-31452.tsv`
contains the exact world/focus, 1569×926 viewport and separate timing boundaries;
it retains the 355.25 ms cold frame and the warmed recent frames. This audit uses
205 visible objects (hidden=False), averages 8.73 ms after warm-up and validates
the capture implementation; it does not supersede the wide hidden=True report.
No user process was restarted or overwritten.

### Source boundary for the remaining deformation spike

The actual-frame trace directory currently contains only our verification captures;
there is no new capture from the user's running JadeView process. Thus there is
still no sampled evidence for *persistent* 250 ms or the user's exact camera.
The trace's `skinMs` is an inclusive deformation/update wall timer, **not CPU-only
skeletal animation time**. Its source path is:

- `SceneRenderer.UpdateSkin` / `UpdateSkinCore` (`SceneRenderer.cs:1268`): actor
  skinning, camera-facing sprites, particle simulation, normal generation, vertex
  conversion/bounds, then `Raylib.UpdateMeshBuffer` for positions/normals/colors.
- `SceneActor.Skin` (`src/Features/Animation/Models/SceneActor.Skinning.cs:15`).
- `SpriteGenerator.FillFaceCamera` (`SpriteGenerator.cs:80`).
- `ParticlePreview.Update` (`ParticlePreview.cs:143`).
- `AnimationMath.FillNormals` (`src/Features/Animation/AnimationMath.cs:44`).

The measured 88.78 ms first-motion interval could include CPU work and native GL
buffer-upload waits in this path. Assigning all of it to particles, normals or
skeletal skinning would be unsupported. Procedural deformation is intentionally
camera/frame dependent; merely skipping it or caching by actor pose alone would
break camera-facing geometry. Shared geometry also requires per-instance owner
checks, so cross-instance reuse is not a safe inferred optimization.

The measured cold frame's ~18 MB allocation does not establish GC causation:
Gen2 delta was zero, and warmed wide frames allocate ~223 KB/frame. Warmed draw
cost is not exclusively GPU time either. Before another optimization, collect the
actual trace; if `skinMs` is dominant, sample the methods above including native
`UpdateMeshBuffer` waits. If `presentMs` dominates, inspect presentation/driver/GPU
with matching viewport resolution. No new production fix is justified by the
current stage-only measurements.
