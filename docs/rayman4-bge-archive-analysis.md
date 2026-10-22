# BGE content in the local Rayman/Jaded archive

Verified 2026-10-20. Read-only investigation of
`C:/Users/Dan/Desktop/rayman/rayman4.bf` and `source code/` beside it.
No engine/editor processes were launched, assets extracted, or source archives
modified. JadeView runtime behavior was not changed during this investigation.

## Provenance and format

The source checkout is `hogsy/jaded`, commit
`8d55704c80adfcec4f3bcaeb2445dc9f68ef960e` (2026-10-14), with a clean worktree
at inspection. It is the same commit as the existing `external/jaded` checkout.
Its README describes a community fork of a May/June 2006 Jade copy; this is not
the exact BGE 2003 source tree. The running editor's version shown in the user's
screenshot is not proof that its executable was built from this checkout.

The 1,691,863,505-byte archive has `BIG\0`, version `0x24`, one FAT, 293,556
file slots, 293,554 named live entries and 10,544 directory slots. The two
unnamed/invalid file slots are excluded from live-entry counts. Editor file
metadata occupies 88 bytes per slot, including the Perforce revision; directory
metadata occupies 84 bytes. `SDK/Sources/BIGfiles/BIGdefs.h` and `BIGfat.h`
describe these structures. JadeView's runtime archive reader supports `0x22`
and 84-byte file metadata, so simply removing its version check is incorrect.

## BGE content is present, beyond folder names

The `EngineDatas/06 Levels/BGE` subtree contains 65,026 live resources, including:

- 28,555 GAOs, 8,771 GROs and 4,026 GRMs.
- 2,792 TRLs, 1,814 ACTs, 186 ACKs and 208 SHPs.
- 196 WOWs, 196 GOLs and 209 WOLs.

These counts refer only to the BGE level subtree. Shared texture/animation/audio
banks may live elsewhere; absence from this subtree alone does not establish
absence from the entire archive.

All five current cinematic fixtures exist as directly indexed editor resources:

- `20007991`: `CINE_OUVRE_SAS_EVENTS.trl`,
  `BGE/01_entrepot/01_00_entrepot_sas_entree/Events`.
- `3500A446`: `CINE_GRILLE_RETOUR_EVENTS.trl`,
  `BGE/01_entrepot/01_01_entrepot_ascenseur_central/Events`.
- `3500B8EA`: `CINE_BIGINTRO_EVENTS.trl`,
  `BGE/00_home/00_03_dehors_maison_Intro/Events`.
- `BF00218E`: `CINE_APRES_MDISK_EVT02.trl`,
  `BGE/00_home/00_02_home_decryptage/Events`.
- `BF003BD1`: `CINE_OUVRE_SAS_CODE_EVENT.trl`,
  `BGE/01_entrepot/01_00_entrepot_sas_entree/Events`.

Their unoptimized editor TRLs contain respectively 48, 53, 106, 79 and 17 tracks.
All 303 tracks were parsed to the exact end of their resource, including editor
track-name/color fields. Names in these five track headers are empty.

Comparison with retained `sally_clean.bf` motion audits found respectively
140/140, 134/134, 659/659, 203/203 and 21/21 matching nonempty AI events:
**1,157/1,157 total**, with no unmatched events in either direction.
The comparison uses recipient track, frame timestamp and ordered node operands,
categories and type/selector IDs. Compiler annotation bytes and the unused END
operand are normalized explicitly. This establishes event-program alignment for
these fixtures; it is not a claim that whole TRL files are byte-identical.

## The BGE gameplay AI is not intact here

All 28,555 BGE GAO headers were checked using their editor identity field at
offset 12. **Zero have `OBJ_C_IdentityFlag_AI` (`0x20`) set.** The engine's
`OBJload.c` reads an AI-instance reference only when that flag is present.
For example, `_PJ_Sally.gao` (`71003F5A`) and `_PJ_Militaire.gao` (`7100596B`)
both have identity `0421BFC2`, without AI.

The full archive index also lacks the previously verified BGE brain/model keys
`20007990`, `350091C3`, and controller functions `3500B773`, `3500B774`,
`3500B77E`. No `.mdl`, `.fct`, `.ofc` or `.ova` resources are indexed inside
the BGE subtree. The archive does contain thousands of such resources elsewhere;
those have not been classified as equivalent BGE controllers.

Thus this archive cannot be treated as a complete runnable BGE project with
original triggers, gameplay-to-cinematic handoffs and facial AI. The observations
are consistent with a BGE asset collection whose actor AI was removed; the exact
reason/history of that removal is not established. Do not replace retail brains
or infer a missing script implementation from the preserved mesh/event names.

The retail scene-mix key `61009A6F` (`014_apres_m-disk_FR.wad`) is also absent
from this archive's directory index. The existing retail archive remains the
verified source for that cinematic's audio.

## A concrete remaining animation-timing gap

`AIinterp/Sources/Events/EVEnt_interpolationkey.h` defines format bit `0x0008`
as `EVE_InterKeyType_Time`. The loader in the corresponding `.c` file describes
its payload as a point count followed by `2*N-1` floats: an initial factor,
then time/factor pairs. `EVE_f_Event_InterpolationKey_InterpolateTime` integrates
piecewise-constant factors over elapsed event time. The resulting warped time is
used for transform interpolation; these are not extra vertex-control points.

The five editor fixtures contain 10, 8, 36, 5 and 6 inspected translation timing
curves. **All 65 raw timing payload hashes match the corresponding retail
`extra-transform-controls` payloads**, at the same target and frame timestamp.
This is stronger format evidence than a name/key match across engine versions.

At the initial audit, `AnimationReader.cs` preserved these bytes but marked them
unsupported, and the samplers used elapsed time divided by duration. Therefore
nonlinear source timing was missing.
It can affect camera, helper, object and body tracks where that format occurs.
Its contribution to each reported visible jerk or mouth-speed difference still
requires frame-by-frame comparison; this investigation does not claim it explains
every symptom or has already fixed it.

Follow-up implementation: `AnimationTimeCurve` now decodes/integrates those
controls; `AnimationInterpolation` is shared by actor and cinematic samplers.
All 65 retained curves passed 1,021 independent interval-area reference samples
with maximum absolute error below one microsecond. Integer frame accumulation
also replaced repeated float-duration addition. Stable zero-offset camera pairs
are read from literal message 60; source smoothing/shake/lens logic remains open.

The retail `BF002300` SceneEvents record linked by `CINE_ARRIVEE_ALPHA_EVENT.gao`
contains a time-curve point whose time is positive infinity. Iris's native
interpolator walks the serialized points in order and compares each next time
against the finite playhead; this terminal point therefore leaves the preceding
factor active. JadeView now preserves that source behavior and does not add the
infinite point as an export sample. Duplicate timestamps are also retained in
serialized order, matching the native `<=` boundary walk. This lets the scene
record be decoded without claiming the record's runtime gate has passed.

Other engine paths remain useful references: `ACTcompute.c` / `ANIplay.c` for
action transitions, blending and MagicBox movement; `EVEplay.c` for per-track
looping/activation; `AIdeffct.h` for selector signatures. The later direct
`EVEnt_morphkey` implementation must remain distinct from BGE's observed
message-53/controller/helper path until matching retail data demonstrates it.

## Reproduction and next step

The tools only read archives and write JSON metadata under `artifacts/rayman-research`:

- `tools/audit_jade_editor_archive.py`: bounded directory/key census and selected
  payload hashes; supports this single-FAT `0x24` layout and the inspected `0x22`
  layout. Not a general archive writer or asset extractor.
- `tools/audit_jade_editor_tracks.py`: selected unoptimized editor TRLs, exact
  cursor validation, event-node metadata and translation timing-curve hashes.
  Unsupported track/event layouts fail explicitly.
- `tools/compare_jade_editor_events.py`: compares that metadata with retained
  retail `--cinematic-motion-audit` JSON, normalizing only documented fields.

Local reports: `editor-archive.json`, `editor-tracks.json`, `event-comparison.json`.
No game asset payloads were added to source control.

The shared time-curve sampler and reference checks above are implemented. Next,
compare the reported scenes at fixed timestamps and investigate action blending,
camera transitions, root reference resets and gameplay AI handoff separately. Keep the
retail BF as the playback authority; use the editor archive for names, readable
resource boundaries, action structure and cross-checking.
