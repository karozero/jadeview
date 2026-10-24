# Diffuse light verification

Verified on 2026-10-23 after connecting current-pose GLB diffuse baking and
static PC AABB light-range checks. These checks establish the listed behavior,
not complete retail lighting parity.

- The complete `tests/ExportVerification` synthetic suite passed, including
  bounds parsing, receiver flags/reference reconstruction, light selection,
  multipart root transforms, frozen GLB colors, raw material isolation,
  scripted light-off state and source immutability.
- `--dynamic-light-render-check` passed on the GPU. It checks computed/raw
  modes, activation, receiver masks, cache invalidation and the display toggle.
- Running the verification executable with the original `sally_clean.bf`
  passed the real archive export checks for Sally, militaire and Pey'j,
  including morphs, skinning and material/accessory regressions.
- `--cinematic-frame-audit` rendered and saved intro `FF0045A2/3500B8EA` at
  23.68 seconds. The image was inspected; this is one camera/time sample.
- A muted realtime audio/render audit at 960x540 over 23.68–25.68 seconds
  produced 119 frames in 2.01 seconds: average 16.91 ms, p95 17.03 ms,
  maximum 33.71 ms (one frame over 33.33 ms). Audio queue drains/resyncs
  were both zero; measured audio lead/lag was 21/5 ms. This short run does
  not establish performance for every scene or resolution.

Logs and captures are under `artifacts/light-component-integration/`:
`dynamic-integrated-current.log`, `dynamic-gpu-current.log`,
`current-real-export.log`, `current-real-frame.log`, `current-av.log` and
`current-real-frame/FF0045A2-3500B8EA-23.680-frame.png`.

Remaining limits include animated skeleton-derived bounds, sphere/OBBox
range gates, runtime sector membership, specular, animated exported light
changes and complete world-cycle/scheduler state. See
[world-cycle light audit](world-cycle-light-state-audit.md) for the next
shared-state integration and its ordering boundary.

## Export scope and diagnostics

The diffuse exporter uses the selected world's ordered object list for light
candidates. Additional actor members are available only as receiver hierarchy
and matrix context; they cannot introduce lights absent from that world list.
Unsupported preparation cases are deduplicated into export warnings even when
no supported light contributes. Inactive lights, receiver-mask exclusions,
reference mismatches and ordinary range misses remain normal filtering rather
than incomplete-export warnings. GLB regression fixtures cover both the
off-world actor-member light and the distinction between inactive and unknown
light types.

## World-cycle integration (2026-10-24)

The complete synthetic suite and GPU checks passed after adding world-cycle
sun/fog composition, the activation forecast and export diagnostics/scope
regressions. The GPU test also verifies that a new snapshot containing
unchanged light values reuses the prepared plan and uploaded vertex colors.

The real intro frame at 23.68 seconds reports evaluated sun
`62000994` (`ENV_HIL_03-Soleil_LGT.gao`, color `9121518A`) and fog
`62000983` (`ENV_HIL_03-Brouillard.gao`, color `00565F7D`, range 0–6000).
It explicitly reports preserved twilight sun activation and unresolved moon
runtime state. Thus this fixture exercises the new world-cycle connection;
it does not prove that the unmodeled state matches the game.

The corresponding two-second 960x540 muted AV run averaged 16.92 ms per
frame, p95 17.17 ms, maximum 33.03 ms; queue drains/resyncs were zero.
See `world-cycle-integrated.log`, `world-cycle-gpu.log`,
`world-cycle-frame.log` and `world-cycle-av.log` in the same artifact folder.

## Material constants and full cinematic export (2026-10-24)

`owner-material-integrated.log` records the passing complete synthetic suite,
including lifecycle reporting, custom-bit gates, moon material palette entry 6,
frozen environment/shockwave constants, per-submaterial GLB factors and baked
mode-2 tint. `owner-material-gpu.log` records the passing material-render checks.

The new `--cinematic-export-audit <archive> <container> <clip> <time>` command
uses the same sampled material/light/facial state as UI export, checks the
resulting GLB and verifies restoration of preview matrices. Intro
`FF0045A2/3500B8EA` at 23.68 seconds exports 112 meshes, 131 textures and one
synchronized animation with 57 explicit format/coverage warnings. This count
does not mean those limitations are fixed.

Blender 5.2.3 LTS imported the generated file successfully using
`blender_cinematic_check.py`: two animated cameras, moving Sally and Fehn
armatures, and changing morph weights on `B sal_tete.gao` and
`B_BoukBoy_Torse.gao` share the same cinematic track. This verifies imported
animation structure and changing channels, not frame-by-frame retail parity.
Artifacts are `intro-scene-export.log` and the `intro-scene-export/` directory
under the same artifact folder, including the GLB, Blender log and JSON report.

## Scrolling UV snapshot

UV transforms now use the frozen material sample time for both ordinary mesh
UVs and the static reflection-reference bake. Without a material sample the
export retains its time-zero default. This preserves the selected frame; it
does not add UV animation channels. `CinematicUvSampleChecks` verifies an
independently specified .25-unit U offset at one second and unchanged source
UVs. The complete suite passes in `uv-sample-integrated.log`.
