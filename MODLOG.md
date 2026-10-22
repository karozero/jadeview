# Modding investigation log

## 2026-10-22: expose static script analysis without cinematic playback

When a cinematic world has no linked shared animation clip, the Cinematic
Timeline now keeps a world-scoped static script inspector available. It lists
script owners, callback slots and Iris-derived scan spans, then reuses existing
source/archive analyzers for scheduler state, OVA values, event commands,
typed messages, action-kit merges and linked-object calls. A regression check
covers world-only scope and cross-world owner links. This is static evidence;
it does not simulate runtime activation or VM execution. Full
ExportVerification passes. The fresh apphost is
`artifacts/jadeview-script-inventory-current/Release/net10.0-windows/JadeView.exe`.
Production check on `00_02_home_decryptage_cine` traced the 131-command
`CINE_APRES_MDISK_EVT02.gao` SceneEvents record to drone visibility changes,
the drone's separate Iris brain/model callbacks, and the ventilation timeline
that enables the motor modifier and transfers control flags from the fan bone
to the rotor object. The data supports static timeline interpretation; it
does not prove runtime AI execution or undocumented flag semantics. Findings
are in `docs/gameplay-script-system-findings.md` and
`artifacts/callback-span-audit-v68/FF005833-BF00218E-script-graph.json`.
Changes remain unstaged.

## 2026-10-22: show the derived model callback scan span

The Cinematic Timeline now derives the inclusive callback scan span from the
lowest and highest configured script-model slots and notes that empty slots
inside the span are skipped. Iris `AI_ReinitInstance` computes those bounds;
`AI_RunInstance` scans them in ascending order, invoking only configured slots
whose callback timer is nonpositive. Empty model callback sets produce no scan
span. Tests cover empty and sparse slot sets; this remains static initial
configuration, not runtime execution evidence. Source: `external/irisEng/ai_common.c`
lines 387-397 and 729-747. Full ExportVerification passes. Changes remain
unstaged.

## 2026-10-22: distinguish script-model callbacks from instance OVA overrides

Iris `AI_ul_CallbackLoadVars` reads five callback function references into
`tAIVars.callBacks`; instance reinitialization copies those model callbacks to
runtime slots, while the instance OVA overlays named variable values into the
model's variable buffer. The Cinematic Timeline now labels them **Script-model
callback slots** and explains that boundary. This avoids implying that owner
instance OVA data replaces its callback set. Source: `external/irisEng/ai_common.c`
lines 389-404, 586-627, and 1292-1297. Changes remain unstaged.

## 2026-10-22: audit unmapped callback keys against BF and Jaded

The all-world script report now adds named BF-record candidates for each
unmapped callback ID. In the current `sally_clean.bf` scan, 12 slots reduce to
8 IDs and none has a named archive record; case-insensitive searches also find
none in the supplied Iris source tree. Jaded's `AI_ul_CallbackLoadFunction`
matches loaded function keys against `AI2C_gat_fctdefs` before assigning a C
function name (`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1331-1349`),
but the supplied `AI2C_fctdefs.h` is empty. The keys therefore remain raw and
their behavior remains unknown; the older Jaded snapshot cannot establish
retail semantics. The audit is
`artifacts/unmapped-callback-resource-audit-v65/archive-gameplay-script-index.json`.
Full ExportVerification passes and the all-world scan still resolves 15,950 /
15,950 brain owners. Changes remain unstaged.

## 2026-10-22: preserve ambiguous resource keys in script and event links

The shared scene-resource index no longer chooses the last record when the same
typed key appears more than once in a world. Preferred-world and shared-world
collisions now remain unresolved, and event-link diagnostics identify that
ambiguity. This avoids order-dependent `Events` / `SceneEvents`, script-model,
and actor-resource joins. Regression checks cover local precedence, shared
fallback, unique external references, cross-world collisions, and duplicate
keys within one world. Full ExportVerification passes. This changes static
reference certainty only; it does not add script execution to cinematic
playback. Production script-graph audits still decode 544 calls / 83 owners in
`3500B8EA`, 131 / 31 in `BF00218E`, and 11 / 14 in `BF003BD1`. The runnable
build is `artifacts/cinematic-resource-resolution-v64/Release/net10.0-windows/JadeView.exe`.
The fresh archive-wide pass examined all 91 containers and retained 15,950 / 15,950
brain-owner links and 435 script models; callback coverage remained 16,204
configured and 12 unmapped slots. No brain/model collisions were present in
this archive pass, so those aggregate totals match v62. Changes remain unstaged.

## 2026-10-22: map Sally cinematic-mode messages from Iris

Message 47 now receives a static description only when its resolved execution
owner reaches Iris callback `Sally_sa_exec_message_read`. The inspector
distinguishes mode entry/action selection from mode exit and reports the
source-confirmed track, collision-activation and expression-control handoff.
Three production script-graph audits found 14, 10 and 8 type-47 messages in
`3500B8EA`, `BF00218E` and `BF003BD1`; all 32 resolve to Sally's callback
context. Type-30 audit JSON also retains receiver, line, priority and audio
metadata for embedded EVE messages (71/32, 99/48 and 23/12 requests/resolved
slots across the same three clips). Full ExportVerification passes on
`sally_clean.bf`; Release build v32 is published in
`artifacts/cinematic-script-analysis-flow-v32` with Windows x64 native assets.
Changes remain unstaged.

## 2026-10-22: expose dialogue queue priority and selected-line voice settings

Type-30 timeline inspection now shows the effective static queue priority for
the resolved receiver and line, distinguishing explicit `Int2`, receiver
default priority, and negative priorities ignored by Iris. For the selected OVA
slot it also shows voice sound/variation fields, volume and near/far settings,
including Iris-confirmed zero-value fallbacks. The message queue, option gates,
and live audio state remain unevaluated. Regression checks cover explicit,
default, and ignored priorities plus selected-line metadata; full
ExportVerification passes on `sally_clean.bf`. Release build v31 is available
under `artifacts/cinematic-script-analysis-flow-v31`; it contains only the
Windows x64 native runtime assets. Changes remain unstaged.

## 2026-10-22: distinguish unset event-list references from unresolved tracks

The generic `TRA*` resolver now reports literal-null receivers and effective
zero OVA object references as explicit no-target cases. This avoids presenting
the reusable door brain's unset optional opening/closing event references as
missed EVE links. Regression checks cover both zero forms. The retail
`FF00791B / BF003BD1` graph confirms `o_evt_fermeture=0` and
`o_evt_ouverture=0`, matching Iris's source pointer guards; no list is
fabricated. Unknown nonzero references remain unresolved.

## 2026-10-22: follow indirect helper-text callback references

Expanded the generated Iris callback graph to recognize `fctr_` indirect
function-reference variables. JadeView can now trace helper text from its
assigned init callback through the condition-gate and wait callbacks to the
message-30 display callback, exposing why an init-only GAO can still reach
dialogue submission. A regression fixture verifies the full path. The inspector
still reports source reachability, not runtime scheduling or successful gate
evaluation; the retail example's message 37 / Int1=4 send comes from a different
queue owner than the configured helper gate. Indirect dispatch evidence now
retains the resolver assignment at line 1175, verifies key `7100E7B3` against
the callback name table, and points to the actual call at line 2227.
Production graph audits passed for `BF00218E`, `3500B8EA`, and `BF003BD1`.

## 2026-10-21: follow nested source callbacks for typed messages

Generated an Iris source call graph for direct `AIExecute`, `AITrackChange`,
and `AITrackCurChangeNow` dispatches. The inspector starts from the owner's
actual callback slots, follows only children also referenced by that script
model, and includes a nested path when describing a typed message. This fixes
the Sally case where slot 2 is `Sally_sa_init`, which changes to
`Sally_sa_reflex`, which executes `Sally_sa_exec_message_read` to consume
message 53. The code explains the available source path without treating
branch conditions or runtime dispatch as proven. The generated graph has 569
function nodes and 1,295 unique dispatch edges extracted from 1,590 source call
sites. The owner inspector now has a collapsed list of reachable nested
functions and their shortest source paths, even when no cinematic message
references them. Graph-audit JSON records paths for local owners and actor
participants. Synthetic checks verify the Sally path and model-membership
filter; message semantics remain tied to the resolved execution owner.

## 2026-10-21: expose direct cinematic-to-owner links

The script inspector's incoming-link section now includes the selected clip's
literal command targets and typed-message references. Message execution object,
GAO, and GAO2 stay distinct. Selecting a clip-origin link pauses and seeks to
its timestamp. The graph audit records these edges as `SelectedClipIncomingLinks`.
Production audit of `FF005833 / BF00218E` shows the monitor capability command
at 44.783 s and fan-effect capability changes from 42.733 to 43.533 s. These
are serialized links, not evidence of runtime activation. Synthetic checks
cover target roles, timestamps, sorting, and invalid-key filtering.

## 2026-10-21: map camera type-60 EVE messages by callback family

The script inspector now describes type-60 messages only when the EVE execution
owner has Iris's `camera_cam_1_mainloop` callback. Source-backed `Int1=1/2`
descriptions map the destination/look-target GAOs, camera/target vectors, focal
value and transition speed; `Int1=0` maps the reset branch. Other `Int1` values
remain unresolved because the callback routes them through camera/effect
branches. A verification case guards against attributing a type-55 message to
this callback family. In `FF005833 / D90057CC`, the camera-targeted type-60
messages and type-55 Mdisk message remain distinct in the inspector.

## 2026-10-21: distinguish EVE execution objects from message recipients

The typed-message inspector previously called the EVE track target a message
"context", which could be mistaken for a separate recipient field. Renamed the
model/UI/audit field to `ExecutionObject` and label it as the EVE target.
Jaded's `EVE_Event_AIFunction_Play` sets the current AI object to the track
target (or owner); Iris `MSGSendAll` stores the message in that object's
extended message list. In `FF005833 / D90057CC`, channel 5 targets
`_PJ_Camera.gao`, and its type-55 payload references the Mdisk controller and
WP object. The camera main-loop reads its queue and only has a type-60 branch;
this type-55 message is not handled there. Sally's separate type-55 handler
cannot be attributed to it. Documented this boundary instead of inferring
recipient semantics from message fields.

## 2026-10-21: map shared gencam_mario readiness gates

Added callback-derived analysis for model `8D005218`. Iris's activation
callback establishes camera readiness by checking the five-entry saved-flag
on/off arrays, manual camera ownership, capability `0x2` rejection and `0x4`
override, cinematic target mode, BV-pivot collision, optional `0x100` actor
capability, and the fight-target/priority branch. The normal callback owns
ongoing camera motion and transitions; JadeView reports that boundary rather
than claiming to reproduce it.

OVA snapshots now retain typed bounded integer-array values and merge instance
prefixes over model arrays while preserving the model tail and per-slot source.
The same effective slots feed camera flag graph edges. Production audit of
`FF005833 / 00_02_home_decryptage` confirmed the shared controller on
`CAM_principale_00_02.gao`, `CAM_Ecran.gao`, and `CAM_Mdisk.gao`; the archive-wide
index counts 699 model owners across 65 containers. Verification covers array
decoding/overlay, both encoded flag polarities, collision/capability gates and
the explicit non-emulation boundary.

## 2026-10-21: inspect the shared SFX_HaloSimple controller

Added callback-derived inspector findings for the reusable `SFX_HaloSimple`
family, without owner/scene-name matching. Iris source confirms init creates
native GFX effect 21 for nonzero type values, binds an optional material GAO,
and chooses effect parameters by type. The move callback follows owner position
(and sight for its default type branch), updates color from capability 1/2,
and can mark the owner inactive without removing the separate effect instance.
Blink mode uses configured frequency/speed and a color-phase sound request.
Missing init callbacks are called out so a move callback alone is not treated as
proof of a live effect.

The all-world archive index maps model `62001249` to 945 owners across 61
containers. Production example `FF005833 / 00_02_home_decryptage`:
`SFX_lgt@4519c24.gao` has instance type 1, radius 1, color `0xFF4D76DF`, and
material target `SFX_light&smoke.gao` (`62000F8F`). Tests cover type branches,
effective parameters, and missing-init behavior. This is static analysis;
GFX instances, live capabilities and script scheduling are not emulated.

## 2026-10-21: expand shared event-command semantics

The cinematic inspector's engine-function catalog now describes 23 more
serialized commands using recovered IrisEngine implementations: action plus
action-frame selection, animation-track frequency conversion, collision
activation-mask set/clear, group-wide visibility/activity changes,
draw-mask set/clear, multi-texture-level toggles, local-alpha writes,
sound-modifier play/pause/stop and flag masks, plus embedded event-track
start/stop/pause/reset.
Source shows local alpha stored in a five-bit field (read back in steps of
eight); sound modifier ID -1 addresses all modifiers on the target and -10 is
ignored. Track selectors address the target owner's EVE data; start leaves an
already-running cursor alone, while reinit returns the track to time zero and
honors its auto-run flag. The catalog applies by selector everywhere; it does
not branch on scene or actor names. Added checks cover selector descriptions,
argument order and group propagation. `docs/cinematic-script-system.md`
records the semantics and the limit that these descriptions do not prove
execution.

Read-only production audio audits now verify this modifier join on two clips:
`FF005833 / BF00218E` resolves modifier 0 on `BF00218D` to
`61009A6F_014_apres_m-disk_FR.wad`; `FF0045A2 / 3500B8EA` resolves five cues,
including modifier 0 on `100045A4` to `61009A64_002_big_intro_FR.wad` and later
ambience/fight cues on `BD0001BF`. Both audits produced non-silent decoded PCM.
This verifies archive resolution, not that every serialized command executed
in the original game.

The real `FF005833 / BF00218E` script graph also contains a gameplay event owner
`EVT_Peyd'j_repare_Mdisk.gao` that clears sound-modifier mask `0x08000000` at
time 0 and sets it at clip end. Iris resolves those calls to sound-instance
runtime flags on modifier 0; the inspector now displays the owner, modifier ID
and mask operation. The graph proves the serialized calls and target owner, not
that this event owner ran during the cinematic.

Validation: Release build succeeded with zero warnings/errors, the complete
ExportVerification suite passed, and `git diff --check` was clean.

## 2026-10-20: apply recovered source timing and camera pairs

Implemented bounded time-curve decoding plus the native held-factor integral.
Retain raw payload provenance, but stop counting this decoded subtype as unknown.
Actor/root and cinematic camera/object/helper interpolation share one amount and
Bezier implementation. Export sampling includes curve breakpoints. All 65
editor/retail-identical translation curves pass 1,021 independent interval-area
reference samples (max absolute error 9.537e-7 seconds).

Camera message 60 (mode 1/2) identifies explicit position and target objects.
Stable zero-offset pairs now take priority over name pairing in preview and GLB,
including arbitrary helper names. The UI labels source vs fallback binding.
Nonzero offsets, changing pairs, native transition smoothing, shake and focal
conversion remain unsupported; this is not full camera-script emulation.

Started placement diagnosis requested by the user. The apparent extra 6.016 m
jump in 3500A446 was traced to an authored absolute Sally placement at frame
1578 (26.3 seconds), not established as an action-loop defect. Cumulative float
addition decoded that key at 26.300001, deferring it to 26.316668 in a 60 Hz
sample. Key times now derive from integer frame totals; the same placement
fires at 26.3. The audit's source-placement classification also now compares
actual preceding sample timestamps, avoiding subtractive float rounding.
Do not hide authored placements with smoothing; inspect native camera transitions
and gameplay state when comparing how these cuts appear in game.

All synthetic verification tests pass, including long-track frame alignment,
camera references with arbitrary names, time curves and existing playback/export
checks. Retail playback/export audits pass for BF00218E, 20007991 and 3500A446.
Remaining actor overshoot/lag is tracked in docs/BACKLOG.md; not declared fixed.
Fresh BF00218E GLB also passes Blender 5.2.2 import with three changing facial
meshes, three moving body rigs and source cameras sharing one animation track.

## 2026-10-20: local Rayman/Jaded BF research

Read-only census of C:/Users/Dan/Desktop/rayman/rayman4.bf: BIG 0x24,
293,554 live named resources; 65,026 under the BGE level subtree. All five
current cinematic fixture keys exist. Their 303 editor tracks parse completely;
1,157/1,157 AI events match retail semantic node operands/targets/frame times.
All 65 inspected translation time-curve payloads match retail hashes.

The source tree is the same hogsy/jaded commit as external/jaded, not a new
BGE 2003 source release. None of the 28,555 BGE GAO headers has AI bit 0x20;
known retail BGE brain/controller keys are absent from the entire directory
index. Treat this as preserved scene/asset/event data, not a complete BGE game.

New priority: EVE format 0x8 is an integrated time-factor curve, currently
retained as unsupported extra-transform-controls and ignored by our sampler.
Detailed evidence, limitations and reproduction tools are recorded in
docs/rayman4-bge-archive-analysis.md. No runtime changes made in this audit.

## 2026-10-20: body handoffs and continuous facial helpers

User fixtures: FF00791B / 20007991 (militaire direction/jitter), FF00791B /
BF003BD1 (password interaction), FF006B96 / 3500A446 (repeated body actions),
FF005833 / BF00218E (missing Pey'j/Secundo mouths and accelerated Sally mouth).

The BF003BD1 type-47 literal messages contain middle-byte compiler annotations
on numeric operands and a nonzero unused END-row value. The earlier strict
template rejected them. Decode literal category/type independently from those
bytes; continue rejecting dynamic operands. irisEng's Sally message handler
sets an action on nonzero cinematic mode and restores the gameplay AI track on
mode zero. The preview now reports that handoff and holds its last pose; it
does not invent the gameplay actions that follow.

Body motion previously discarded non-bone action tracks and collapsed empty
cinematic intervals. Accumulate action-root displacement/rotation through item
repetitions in actor space, with source absolute tracks taking precedence during
their active intervals. Cache a deterministic 60 Hz path with exact event/key
boundaries for reverse seek/export. This is not native DYN/collision simulation
or action-transition blending; verify direction and transitions visually in game.

The facial binding (8262) and animated helper transform are separate tracks in
retail scenes. The old evaluator sampled only the event track, once at its key.
Sample all transform tracks of the addressed helper throughout the binding's
duration. irisEng ai_gameobjectfunctions.c's AnimMorphFactorNoKey reads current
helper matrix X and Y+1, confirming the data path. Preserve the end-of-interval
sample. SceneRenderer also called actor.Evaluate(frameDelta) after the cinematic
facial update, advancing mouth cycles twice; cinematic actors now own their
clock until deactivation. Synthetic tests cover continuous facial values,
independent viewport updates, pause/seek, source annotations, root loops and
handoff, plus dynamic operand rejection.

BF00218E resolves sound modifier 0 on owner BF00218D to
61009A6F_014_apres_m-disk_FR.wad, starting at zero; offline PCM decoding succeeds.
This cue is a whole scene mix, not separate tagged speaker/phoneme clips. Keep
authored morph helpers and supported mouth commands on its clock; no amplitude
heuristics or guessed per-character sound mapping have been added.

Selected cinematic export now carries an explicit clip key so unrelated actors'
default clips cannot introduce additional cinematic timelines into the GLB.

Validation: build succeeds (only offline NuGet NU1900); all verification tests
pass. Archive playback/export audits pass for 20007991, BF003BD1 and 3500A446.
Blender 5.2.2 imports BF00218E with changing facial curves for Sally (15 curves),
Pey'j (11) and Secundo (8), three moving body rigs and two animated cameras on a
shared track. 3500A446 imports with two changing faces and two moving body rigs.
These tests verify data evaluation/export, not visual parity with a recorded game
run; recheck the reported motion direction and Sally's mouth speed in JadeView.

## 2026-10-20: cinematic sound and conditional visibility

FF0045A2 / 3500B8EA owns CINE_BIGINTRO_EVENTS.gao (100045A4), whose sound
modifier 0 selects slot 0 in serialized bank 3500BAA0. The loaded `.snk` bank
contains 61009A64 (`002_big_intro_FR.wad`) at that slot; the source 12058
sound-modifier play call is at 1.2 seconds. The separate intro-event GAO
3500DEF5 uses slot 6, 61009A63 (`001_intro_keck_FR.wad`). The scene's chosen
bank language is French. Five cues resolve into a non-silent PCM mix for the main clip,
including later music/ambience; no name-based sound matching is used.

Engine comparisons: `sound.c` bank callback reads a speed-mode count followed
by key/extension pairs; `gensoundmodifier.c` version 273 supplies modifier ID,
source bank key, gain, sound slot and delay. `ai_soundfunctions.c` selects
modifier type 8 by ID for play/stop. `gameobject.c` maps saved GAO flags at
offset 12: saved bits 0x20000000 and 0x40000000 initialize runtime invisible
0x00200000 and inactive 0x00400000. These previously parsed-but-unused flags
explain why conditional scene objects appeared immediately.

The viewer now uses a preview-only state overlay for literal 8115, 8152 and
8153 calls, with a declared animation owner as the fallback for target-zero
tracks. Unused actor roots are hidden for the selected clip. Ordinary asset
inspection remains unchanged after the overlay is cleared. PCM is decoded in
the background and streamed from the shared source clock, with pause, restart,
seek, mute and gain controls. Facial channel events retain explicit recipients
and qualified literal float operands are accepted in message templates.

Limits: no full VM, dynamic triggers/spawning, native audio spatialization,
reverb, phoneme schedule, or language switching. GLB does not embed audio or
encode the conditional-visibility overlay. This is supported source-event
preview, not a complete native runtime.

## 2026-10-20: monitor material investigation

The pink/red checker on `Cine_ecran_explik_invent.gao` in FF005833 is an
unsupported video texture, not a missing ordinary image. Material BF000DA8
references BF000F07 in its RGB layers. The autonomous texture-registry audit
identifies a 96-byte type-5/subtype-3 descriptor at FF805833 offset 0x2429.
Its Bink reference 61006334 resolves to `propaganda_01.bik`; archive bytes start
with `BIKi`, and its frame dimensions are 128 x 128. Comparative IrisEng
`proceduraltextures.c` registers subtype 3 as the movie texture loader.
JadeView consumes these descriptors but does not decode or upload their frames.
Playback remains pending in `docs/BACKLOG.md`; no fake replacement was applied.

## 2026-10-20: cinematic body action playback

The shared cinematic clip can contain root motion and action commands without
embedded skeletal motion. In FF006F33 / 3500E491, Sally and Pey'j receive donor
action kits at time zero through selector 13208 (0x3398), slots 200..230.
Literal type-47 messages select the initial action; later selector 13201
(0x3391) calls select further body actions. The previous player ignored these
calls, leaving skeletons in their rest pose despite exported transform tracks.

Source checks: `external/jaded/.../AIinterp/Sources/Functions/AIdeffct.h` maps
13211 to ActionFrameSet, not the approximate IrisTool "flags" label; 13212 is
ActionFrequencySet. IrisEng `actions.c` and `animation.c` establish kit slot
merging, 19-byte action items in the PC stream, frequency conversion, repetition
counts and the final return-item index. Its Sally message handler conditionally
maps a type-47 message's Int1 to the body action. These sources are comparative
engine evidence, not proof of full retail script-VM parity.

The player now evaluates these literal commands and combines body bone poses
with source cinematic placement. The same actor evaluator drives GLB sampling.
Dynamic expressions and action-transition blending remain unimplemented.

Verification: source action bindings resolve without missing-kit/action warnings
for FF006F33 and FF005833. An audit removes actor root motion before comparing
bone poses. Blender 5.2.2 imports actual varying pose-bone curves on all four and
three participant rigs respectively, in the same track as facial weights and
camera motion. Synthetic checks cover message startup with float-operand metadata,
kit merge, action changes, initial frame, frequency, running-action phase and seek.

## 2026-10-19 12:12: fresh dump and orphaned barrier animation fix

ProcDump captured `hangar-bankfix/BGE.exe_261019_121235.dmp` after the user
reproduced the return crash with the source-bank q/t allocation. Recovered x86
CONTEXT at dump offset `0x45138`: EIP `00445FE8`, EAX `2745BFFE`,
EDX `0019EDE8`, ESP `0019ED74`, EBP `0019EE0C`. The failing t request remains
`BF000A0A`, reading q payload `5B0002AF / D9005D50` as a descriptor count.

The first divergence is earlier than that q. Native g `8F00194B` contains
sample counts `(55,76,3)`, the preceding g `8F001949`'s payload; its own
payload has `(35,45,2)`. Native g `8F00194D` in turn contains `(35,45,2)`,
instead of `(39,57,2)`. q `D9005D51` then queues model `7100B94E` and variables
`00278305`, the first words of the g `8F00194D` frame, instead of its own
model `5B0002AF` and variables `D9005D50`. Thus the stream is one animation
frame behind after a cache hit on `8F001949`.

Important correction to the prior diagnostic interpretation: the 16-byte rows
near `00D5C3B0` are the dependency request queue (key, output address, callback,
flags), not cached-resource entries. Actual key caches are hashed buckets at
`00D4EE00 + (((key >> 8) + key) & 255) * 16`; `00D4EDF0` is the reverse map.
The queue alone did not prove a stale q cache entry.

The typed source graph ties g `8F001949` to f `8F001948`, owned by
`DEC_murG_00_01.gao` (`1F000119`). Previous exports removed only this GAO's
primary membership in the hangar. Four cinematic event resources still
targeted it (`3500A8AC`, `3500A8B4`, `BF00027F`, `3500A92F`), and
`Cine_SFX_RaiLumiere.gao` still requested it as an external parent. The GAO
and its animation therefore remained loadable without their primary world
owner. This explains why deleting this particular wall can leave its animation
cached at return, before the location's edited records are read.

The writer now identifies removed GAOs which have no remaining primary owner
in the container, removes them from prefab lists, removes entire cinematic
channels targeting them (updating channel counts and frame sizes), and clears
other typed GAO references, including hierarchy links and deferred OVA values.
All reference positions come from the parser. Opaque payloads are not searched
for matching integers. GAOs still owned by another primary world are retained.
Rebuilt streams must contain neither the retired GAO nor any typed reference
to it. This fixes the structural ownership defect; native return stability is
still pending an in-game run of the newly built archive.

Validation: build has zero warnings/errors. Barrier-deletion verification
checks the wall and f/g resources are absent, all parsed references are clear,
unrelated cinematic channel bytes are preserved, pickup/portal payloads are
unchanged, and a no-op rebuild is byte-identical. Pickup duplication checks
also pass. A recovered project and a full fixed BF were generated under
`artifacts/hangar-return-fix` from the backup plus the currently installed
scene delta: one deleted wall and one gyro copy. World inventories, names,
geometry/material keys, draw flags and transforms match the installed scene.
All other BF containers are retained from the installed archive. The fixed
FF0 was verified again after saving the full archive.

The fixed archive was installed at 12:28 after confirming BGE was stopped and
verifying the staged FF0 hash. The prior installed archive is preserved as
`sally_clean.before-reference-fix-20261019-122830.bf` in the game directory;
`sally_cmlean1.bf` remains the reconstruction source. ProcDump PID 13380 was
started with process access for the next BGE launch, output directory
`%TEMP%/jadeview-procdump/hangar-reference-fix`. Awaiting the user's return-entry
test; do not describe native crash stability as verified yet.

## 2026-10-19: captured return-crash context

Microsoft ProcDump captured a full termination dump after reproduction:
`%TEMP%/jadeview-procdump/hangar/BGE.exe_261019_034658.dmp` (about 1 GB).
The dump's only live thread is a termination thread; its thread context is
not the original exception context. Scanning captured memory found three
matching saved x86 CONTEXT copies and exception records for the original
fault. One CONTEXT is at dump file offset `0x4518C` (flags `0x1007F`).

Recovered registers: EIP `00445FE8`, EAX `27442FFE`, EDX `0019EDE8`,
EBP `0019EE0C`, ESP `0019ED74`. The access violation reads across the page
boundary at `27443000`. Raw stack return candidate `00471695` is immediately
after a call to the four-byte reader in `00471560`. Bounded native
decompilation identifies that caller as the OVA variable-descriptor reader.
Its descriptor loop is consuming a nonsensical count; this is a stream
alignment/incorrect-resource-input symptom, not evidence of a sector
controller destructor crash.

The input pointer saved in that reader points at bytes
`AF 02 00 5B 50 5D 00 D9 62 01 00 00 54 00 00 00 ...`.
In both backup and modified FF0057FD, q record `D9005D51` at offset
1882305 has payload keys `5B0002AF / D9005D50`; the following t record
`BF000A0A` starts at 1882317 and has 354 bytes. The faulting OVA reader is
instead reading the q payload as an OVA descriptor count. Why that preceding
record is not consumed on the failing return path remains unresolved.
Do not claim that the decoder/export dependency order is lifecycle-safe.

The installed container differs in membership by deletion of
`DEC_murG_00_01.gao` and addition of one `OBJ_Gyro.gao Copy`. Both corrected
sector records are present. Further exporter changes need to explain the
misaligned q/t consumption or pass an isolated runtime comparison.

Own debug-event collector failed to capture the native exception even with
first-chance capture; ProcDump also reported only process exit, but its
termination dump preserved saved contexts in memory. Prefer ProcDump for
this target. No game executable patch or additional exporter guess was
applied during this investigation. Temporary native snapshots/decompilation
are outside the repository; dump files are ignored.

## 2026-10-19: duplicated state key bank correction

The current installed export contains duplicated pickup state resources at
`FCFFFFFF` (`t`) and `FCFFFFFE` (`q`). These keys were generated by the editor;
the source `OBJ_Gyro.gao` state resources use the `3D` bank. `jade.spe` has no
entries in the `FC` bank. The game accepts the copies on first entry, but the
same-process return crash occurs while reading an existing q resource. This
does not prove that the generated bank caused the cursor mismatch, but it made
the previous allocation an unnecessary native-cache variable.

The writer now allocates copied q/t keys from the respective source key bank,
skips reserved `jade.spe` keys, and checks the loaded FF0 and resolver keys for
collisions. For the observed gyro source this produces `3D` keys. Build passed
with zero warnings/errors. The user rebuilt the archive at 04:06 and reproduced
the crash at 04:08. WER again reports `0xC0000005` at RVA `0x45FE8`. The latest
container parses completely; it contains the expected `3DFFFFFE` q and
`3DFFFFFF` t clone keys, and a typed scan of all 91 FF0 containers found no
other occurrences of those keys. The bank change did not resolve the crash.
WER retained only `Report.wer`, not the new minidump, so no newer register
context is available yet. A ProcDump monitor is now waiting for the next BGE
process to capture a full termination dump. The queue/cache divergence remains
unresolved.

Further read-only comparison of the current and backup archives confirms the
faulting pair's frames are byte-for-byte at the same offsets in both files:
`D9005D51` (`q`) at `0x001CB8BD` (frame size 12), immediately followed by
`BF000A0A` (`t`) at `0x001CB8C9`. The old dump's input pointer is at the q
payload while the native dispatcher invokes the t parser. Both keys occur only
in `FF0057FD`; the generated `3DFFFFFE`/`3DFFFFFF` keys likewise occur only in
that modified container. This rules out a cross-container key collision for
these records. It does not identify which prior resource/cache hit left the
native stream cursor one frame behind. No new dump has been captured since the
04:08 crash, so a code change to cache eviction or record ordering would still
be speculative.

## 2026-10-19: return crash persists after controller correction

The user reproduced the same native crash after rebuilding. Application
event 1000 at local 03:32:42 again reports `0xC0000005`, RVA `0x45FE8`.
The installed BF now has both controllers at flags `0x00802080`, without
brain fields. Thus the previous controller-layout correction is installed
and did not resolve the crash. Do not describe it as the confirmed cause.

Read-only examination of unpacked process memory and bounded Ghidra
disassembly identify `0x445FE8` as `MOV EAX,[EAX]`, preceded by
`MOV EAX,[EDX]`; the routine subsequently advances `[EDX]` by four.
The caller, bad pointer value and resource provenance remain unknown.

`tools/CrashCapture` provides an external Windows debug-event collector.
Launch under debugger did not reach the game window. Normal launch followed
by attach worked when granted process access. A restricted attach initially
failed with Win32 error 5; the helper's unhandled managed exception caused
the separate `0xE0434352` dialog. The helper now catches and logs setup errors.
One repeated entry succeeded, then a later transition exited with
`0xC0000005` without a second-chance exception dump. Capture now includes
first-chance access violations at the verified fault address, before the
game's own termination handling. No additional exporter guesses are applied.

Dump files are ignored by Git. `inspect_dump.py` reads x86 register contexts
and raw stack candidates without treating those candidates as an unwound
call stack. Runtime capture is required to progress beyond this boundary.

## 2026-10-19: hangar return crash investigation

Windows Application events 1000/1001 report repeated `BGE.exe` access
violations (`0xC0000005`) at module offset `0x45FE8`. Archived WER reports
are present, but their referenced minidumps are no longer available.
The on-disk executable's code at that RVA is packed, so the failing native
operation and its relation to the scene edits are not established.

The installed modified BF contains `SCT_1.gao` and `SCT_2.gao` with identity
flags `0x008020A0` and brain key `FFFFFFFF`; neither null-brain record occurs
in the backup scene. This is the previous visibility workaround's output.
The writer now removes the `0x20` identity flag AND its four-byte serialized
field, updating the payload size and preserving the rest of the record.
It no longer leaves a behavior-bearing identity with a missing resource.
Known legacy hangar controller records are repaired on subsequent structural
exports of that world, including addition-only exports. No-op exports remain
byte-identical. Other null-brain objects are not normalized by name guessing.

Validation: barrier deletion reloads with no brain field/flag/dependency,
unchanged prefab reference, and exactly four fewer payload bytes per
controller. A second no-op serialization of that modified scene is identical.
Pickup duplication on the installed legacy-modified BF also verifies repair.
These checks establish format consistency, not native lifecycle safety.
Exiting and returning to the location in the game is still required to
confirm whether this change resolves the reported crash.

## 2026-10-19: visibility after deleting hangar barriers

Archive `FF0057FD`, world `00_01_home_sas_hangar`, contains sector
controllers `SCT_1.gao` / `SCT_2.gao`. Their OVA descriptors name
`des_ao_portes` (GAO-reference array) and `des_o_cacher_groupe_de`
(GAO reference). Both reference `SCT_Porte_1_2.gao`; their prefab
groups contain the hangar's objects and lights. Story animation `3500A92F`
targets that portal, lights and the three movable wall objects. Removing
`DEC_murG_00_01.gao` from primary membership does not reproduce those events.

The export workaround for deletion of collidable visual geometry disconnects
recognized sector hide-controller brains in that world. Recognition checks
typed OVA variable names and types, plus prefab membership; it does not
disable arbitrary gameplay or cinematic brains. Sector groups, portal GAOs
and their geometry remain intact. Removing a controller in the same batch
does not create a second replacement for it.

Tradeoff: this bypasses group visibility optimization for the affected world
and may increase rendering work. It is not a portal-topology reconstruction
or an implementation of the original story event. Rebuild from a BF that
still contains the barrier so the deletion is recorded. Previously baked
deletions cannot be identified automatically.

`tests/Verify-BarrierDeletion.ps1` checks the removed wall, both disconnected
controllers, and byte-identical pickup/portal records after full FF0 reload.
Rendering, save/load behavior and controller initialization still require
in-game validation; static serialization checks do not establish these.

## 2026-10-19: duplicated pickups

The installed PC archive's `FF0057FD`, world
`00_01_home_sas_hangar` (in the same container as
`00_01_home_sas_hangar_cine`), contains `OBJ_Gyro.gao` and two
previously exported copies. All three request behavior instance `3D001C04`
(typed FF0 handler `q`), which requests variable block `3D001C03` (`t`).
The copies already have distinct GRO keys. Thus copying GRO alone did not
isolate their behavior dependencies. The user reports that collecting any
one makes the other two uncollectable.

The writer now clones each added duplicate's eight-byte behavior-instance
payload and its variable block under fresh keys. The GAO points to its new
instance; the instance points to its new variables. The behavior model
(`s`) and code dependencies remain shared. Original resources remain intact.
Parser-recorded field offsets are used, rather than searching arbitrary
bytes for matching integers. Rebuilt state frames are checked byte for byte.

`tests/Verify-PickupDuplicates.ps1` checks the original and two new copies
for independent instance and variable keys, preserving the model key.
This is serialization evidence, not runtime proof of independent pickup or
save persistence. Existing duplicates already baked into a source BF are
not rewritten automatically; recreate them or rebuild their original project.
Other referenced GAOs, triggers and script-specific identifiers are not
recursively cloned by this change.

## 2026-10-19: pickup respawn after returning to the hangar

The installed BF contains the original `OBJ_Gyro.gao` and an exported copy.
Their independent behavior/variable resources still both contain
`des_SF_active_on_destroy = 771` and `des_SF_destroy_on_load = 771`.
The original stock instance has those same values. The problem therefore
survives resource-key isolation.

Static inspection of the previously captured, unpacked PC process image
resolves model `9E001C44` through its OFC registration table at `00B77E80`.
The pickup event routine at `0084B400` reads its activation flag and chooses
between the bit-set (`00AE3000`) and bit-clear (`00AE3080`) helpers.
The load check at `0079FBA0` reads the destruction flag with `00AE31A0`.
Thus collecting two objects sharing flag 771 toggles it off again, explaining
why even the original returns. Distinct GAO/GRO/brain/OVA keys do not isolate
this explicitly configured global flag.

The exporter now assigns a separate paired activation/load flag to each
supported duplicated pickup. The original's story flag stays unchanged.
Assignments are retained by object ID in the project's `pickupSaveFlags`
map, including tombstones after deletion, and are serialized before the
editor saves a project or snapshots a BF build. Byte serialization does not
rewrite the source pickup or shared behavior model. Zero/negative flags or
unequal activation/load flags retain their existing semantics.

Allocation is scoped to the audited PC executable SHA-256
`70ae232d58bafe5cbf5c27712538af91e8b13a98104ae1bf6aea6dcf3506f5db`.
The captured SF array has 192 words / 6144 bits. A bounded candidate list in
6000..6139 excludes every 32-bit literal pattern present anywhere in the
unpacked code. Before use, every typed OVA in all source FF0 containers is
screened for matching integers at both word alignments; project allocations
also reserve their bits. This is a conservative compatibility screen, not
proof that arbitrary computed indices in untraced code can never address
these bits. Other executable versions are rejected for persistent pickup
export until separately audited. BGE.exe must be beside the source archive
for export verification; project metadata can still be saved without it.
The candidate pool is finite; exhaustion is reported instead of sharing bits.

Validation: editor builds without warnings; original plus two copies have
independent instance, variable and paired story flags. All eight subsets of
collected items preserve independent bits when replaying the observed native
toggle/load rules. JSON round-trip and removal retain flag assignments;
conflicting assignments are rejected. All 91 no-op FF0 exports remain
byte-identical, and the barrier-deletion regression passes.

Limit: this is static/native-rule validation, not an in-game return or
save/reload test. Rebuild the mod from its original BF/project: already baked
copies do not carry editor provenance and are not normalized by name. Older
saves cannot reveal which individual copies were collected while their flag
was shared; those copies need collecting once under the corrected build.
Unpacked images and decompiled output remain in the private temporary
analysis directory, outside the repository.

External comparative references for facial/cutscene analysis were cloned under
`external/` at recorded shallow revisions. The irisEng source contains a
`Cine_lipsynch_auto` message-53 reader and a 32-form automatic mouth cycle using
keys `3500B773`/`3500B77E`; its version provenance is not proven identical to
the installed 2003 PC release. IrisTool supports asset/animation preview but
documents no scripting or cutscene playback. Jaded's morph-key event code is
from a later engine snapshot and is not attributed to the 2003 BGE runtime.
Details and limits: `docs/external-jade-project-review.md`.

## 2026-10-20: cinematic trigger path for facial-message clips

The archive census now joins all 442 literal type-53 event candidates to their
animation-owning GAOs and follows each owner to its brain/model/instance OVA
resources. All 62 linked owners resolve a brain and decode initial OVA values.
61 use the common model `350091C3` and the same 11 cinematic script keys; one
owner, `WP_Paye_Facture.gao`, uses a separate `4901FC1D` model and three-code
package. Owner rows now include the source container explicitly.

The shared source controller describes the lifecycle from first initialization
through save-flag/actor/zone/priority checks to `InitialiseCine`, `GoToEvents`,
and `TRAPlayAll`. The installed PC image independently contains a correlated
large conditional procedure at `0x009B96E0` (REA evidence
`ev_bdc1c13dab8f3ac055f57b26e674aaf5342405ffa710d3a4b8b83236263c89c0`). This
corroborates the gated nature of the start path; external source build identity
and exact source/native semantic parity remain unproven.

Worked example `CINE_OUVRE_SAS_EVENTS.gao` in `FF00791B`:

- owner `2000798C`, brain `20007990`, model `350091C3`, clip `20007991`;
- three type-53 facial commands target Sally key `71003F5A` at 1.000,
  13.383333, and 13.816667 seconds;
- start defaults include save test bit `0x7F5`, main actor selector 1 against
  the owner's own zone, and completion-associated flag `0xF0D`;
- the common initializer removes an already-completed object and the end path
  sets its associated flag, so this configured event is one-shot by save state.

The only package exception, `WP_Paye_Facture.gao`, maps to the invoice manager
`INV_SecundoEtLaFacture_firstinit/init/loop`: it prepares Secundo and invoice
feedback, calls `TRAPlayAll`, then enters its payment loop. Its seven facial
events target Secundo in the same clip. This is a gameplay transaction
animation path, not the common cinematic priority controller.

`dotnet build tests/ExportVerification/ExportVerification.csproj --no-restore`
passes with zero errors (one offline NuGet vulnerability-feed warning). The
archive census rerun produced 27 containers and 62/62 decoded owner defaults.
This is static archive/native evidence, not playback simulation: save state,
collision, runtime priority, key activation, message queue acceptance, and
delivery were not exercised. Other dynamic gameplay dialogue packages are
still outside this census. The remaining scope is documented in
`docs/face-morph-pc-dialogue-chain.md`.

## 2026-10-20 — BF00218E playback stability

- Hold camera/object transforms across empty source intervals, independently
  from active-only root placement during body movement.
- Avoid redundant viewport evaluation of cinematic-owned actors.
- Deduplicate cinematic/base-world props by GAO identity, preserving separate
  actor instances. Retail FF005833/BF00218E: 282 objects, zero duplicate keys.
- Added held-transform and assembly-precedence regression checks.
- Verification: build and ExportVerification passed; retail synchronized GLB
  imported in Blender with three moving body rigs/faces and two animated cameras.
- Open: Pey'j departure/gliding around 36s. Source action208/BF0021AC has
  frequency64, 4.952m travel over5.8s; do not conceal the issue with a speed hack.

## 2026-10-20 — outliner clipboard and packed bone rotations

- Object context menus now copy the exact object name in asset and cinematic
  outliners. Cinematic source, selected shared track and participant rows also
  expose Copy name. Existing scene-browser clipboard menus are retained.
- Decode packed rotation formats 0x100/0x200/0x400 using the preceding key,
  native signed quantization and axis-specific deltas. Preserve original bytes;
  missing references and unknown layouts remain explicitly unsupported.
- Source evidence: external/irisEng/events.c EVE interpolation-key loader and
  external/irisEng/math.c MATH_CompressQuaternion/MATH_DecompressQuaternion.
- Reader fixtures cover chained keys, all three wide axes, signed deltas and
  truncated input; the complete verification suite passes.
- BF00218E action-kit audit has no decoded packed-rotation keys. This addition
  does not resolve Pey'j departure gliding; reference-matrix compensation and
  action transitions remain under investigation.

## 2026-10-20 — prevent double body root travel

Pey'j action 208/BF0021AC contains about -4.952m Y travel in its root track
and approximately the same travel in bone 0 (pelvis). Treating both as ordinary
parent-local motion doubled the visible displacement. Native EVE interpolation
converts direct skeleton children from Flash coordinates to MagicBox coordinates.

Cinematic body samples now carry the inverse sampled action-root matrix.
SceneActor compensates authored position/rotation components only on direct
children of the effective actor root. Implicit zero-parent skeleton roots use
the same parent resolution as pose composition; deeper children and direct
cinematic overlay tracks are unaffected. Preview and GLB use the same evaluator.
No animation frequency or per-character exception was introduced.

Regression checks cover translation, rotation, loop accumulation, handoff,
reverse seek, explicit/implicit parents and deeper children. Build and all
ExportVerification checks pass. Retail BF00218E skeleton sampling now shows
pelvis Y within a few centimetres of actor-root Y throughout 36–41.5 seconds,
instead of including another full action displacement. Synchronized GLB export
and Blender import pass. Native visual/timing parity still needs user comparison;
full physics and action-transition blending are not implemented.

## 2026-10-20 — source action-transition tables and bridge clips

Read retail action-item flags, blend frames, design/custom flags and inline
transition tables using ACTload.c's runtime serialization. The table's requested
action key is kept separately from its actual destination action. Native
ANIplay uses (blendFrames+1)/60 for nonzero blending; zero disables blending.
FF005833 loaded 1,043 action items and 2,087 transition entries across its loaded
kits (counts include shared definitions, not unique archive resources).

The cinematic body timeline now plays an explicitly redirected transition
animation before the requested action. It selects the outgoing item table or
the incoming action's declared default transition. New source requests/release
cancel pending destinations; repeated requests for the already pending action
do not restart the bridge. Both skeletal sampling and root motion consume the
same scheduled segments. Blending parameters are retained and reported, but
pose crossfades, progressive phase mapping and kit-wide default transitions
are not implemented yet. Gameplay brain execution remains outside this player.

Verification: synthetic table bounds/flags/timing and bridge/cancellation/seek
checks pass with the existing suite. Retail BF00218E exports and imports in
Blender with all three moving body rigs/faces; 3500A446 and 20007991 load and
complete placement audits. These are structural checks, not native visual parity.

## 2026-10-20 — deterministic stock skeleton blending

Implemented source transition pose blending in CinematicBodyPoseSampler, separate
from SceneActor hierarchy composition and cinematic overlay tracks. Native
ACT_SetAction enables BlendStock; ANI_PlayBlendedAnimation reuses stock local
bone matrices, and ANI_BeforeRender blends those matrices towards the lead pose
with increasing elapsed-time weight. The player reconstructs that recurrence at
60 Hz and interpolates adjacent steps for fractional timestamps. It caches only
the evaluated transition poses and invalidates them on timeline/baseline changes.

New actions can start from an already blended pose. Direct cinematic bone tracks
remain higher priority. Export includes action boundaries, pre-boundary samples
and transition steps/endpoints, using the same evaluator as preview. The existing
Flash-to-MagicBox compensation remains in the body sampling path.

Verification: scalar recurrence/quaternion checks, endpoints, fractional times,
reverse seek, interrupted blends, explicit overlays and export boundaries. Build
and the existing suite pass; BF00218E export imports in Blender with three moving
rigs/faces and two source cameras. Native visual parity is not established.
Remaining: root-motion blending, progressive phase mapping, kit-wide defaults,
per-bone exceptions and vector-rotation translation modes. Runtime frame-cadence
parity is deliberately not claimed for the fixed 60 Hz reconstruction.

## 2026-10-20 — root blends and intro snapshot commands

- Root transitions blend translation only, preserving incoming rotation and the
  corrected accumulated position. FreezeMagicBox and SpeedFromDyna bypass root
  blending. Source cinematic placements still override the result.
- Decode ANIFrequencySet (selector 0x32C9) for the lead animation without restarting
  it. Preserve the source frame, pause/resume at frequency zero, reset overrides
  on a different action item, and retain them on same-item loops. Frequency changes
  are included in root/export sampling boundaries. Frequency changes during queued
  transition bridges still report incomplete destination rescheduling.
- Decode literal-vector OBJ_ScaleSet (0x1FBE) into derived STEP tracks, shared by
  actor root evaluation, props and export. Original event payloads are unchanged.
- OBJDestroy now hides/deactivates preview objects and supports source reactivation.
- Move per-action segment clocks to a separate module.

Retail 3500B8EA investigation, requested interval 38.85–48 seconds:
Jade/Fehn switch to scale2.5 at41.9 and scale1 at42.7. At43.316666 both receive
ANIFrequencySet(0,1), which becomes one source frame/second; previously ignored.
The resulting Jade source frame progresses from19.18 at43.5 to20.18 at44.5,
not normal-speed body playback. Large root placements at41.9,42.7,46.366665 are
authored source keys; do not conceal them with arbitrary smoothing.

Shutter controller chain: Cine_PauseClicClac.gao (3500B8FA) -> brain3500B904 ->
model3500B900 -> native Cine_FigeleMonde entry points3500B8FE/3500B902; default
OVA3500B8FF and instance3500B903. The controller is discovered by those engine
code references, never by actor/scene name. Instance overrides give closing0.02s,
hold0.08s, opening0.02s and a normalized iris center. Source CapaSet bits0x100/0x200
and activation control GFX_Diaphragm (type15). Native WORPause(0x20) targets AI
owners and exempts NoPause objects; this is not a blanket timeline pause.

Added source-bound shutter timeline and rotating octagonal viewport aperture,
with close/hold/open phases, deterministic seek and cleanup. This previews the
native controller's visual effect; the full script scheduler, TIMELock/WORPause
execution and framebuffer photo capture are not reconstructed. GLB does not
encode this screen-space effect. Synthetic clock/root/scale/state/shutter tests
and build pass; retail data matches the decoded frequency and scale commands.

### Shutter verification follow-up

Retail discovery found two native shutter controllers in FF0045A2; 3500B8EA
binds one trigger at43.166668s to3500B8FA. Center=(0.5,0.5), source initial
capabilities=0x300. A hidden-window OpenGL test renders actual SceneRenderer
frames and verifies open, half-open (opaque corners, clear center) and fully
closed states; images are in ignored artifacts/export-verification/shutter-*.png.
The half-open render was visually inspected. Full automated checks pass.
The user's reported approximate44.5s was not substituted for the source trigger.

### Intro export verification

Full 3500B8EA export exposed two general cases: animated PAG carriers without
ordinary triangles, and animation parents with singular transforms. Static and
animated carriers now retain their nodes and particle metadata without creating
invalid meshes. Malformed nonempty triangle indices still fail validation.
For a singular parent, export uses the actor's evaluated local pose before world
composition, preserving zero-scale animation states and child transforms.
Regression checks and the full FF0045A2 cinematic export pass.
Blender 5.2.2 imports the resulting GLB successfully: two animated cameras,
17 rigs with moving bone curves and three meshes with changing morph weights
share the 3500B8EA cinematic track. This checks imported animation structure,
not visual parity with the game's dynamics/AI-controlled movement.

### Dynamics-driven cutscene root motion

Fixed ANI SpeedFromDyna (play-mode 6, including item flags 0x16): the native
ANI_AnimPlayer restores the pre-player MagicBox position and corrects Flash
translations. The preview previously accumulated that action's root displacement.
Action motion now retains rotation but contributes no translation in this mode;
preceding ordinary items and explicit cinematic placements remain intact.
This does not simulate external DYN velocity, forces or gameplay AI.
Sources: irisEng/animation.c ANI_AnimPlayer, jaded ANIstruct.h play-mode definitions.
Synthetic checks cover loops, nonzero start frames, rotation and mixed actions.

Retail shutter chain was checked from BF controller discovery through actual GPU
rasterization, including a full assembled scene at 43.22s (closed frame). Intro
3500B8EA currently decodes one 0.12s shutter envelope starting at 43.166668s.
At 44.5s the small JADE graphic is Cine_SFX_JADE_Intro.gao (3500D343), a separate
3D geometry/material effect, not the aperture. Its size/projection parity remains
unresolved; no arbitrary full-screen scaling or old-resolution workaround applied.
The generic cinematic-frame-audit command saves source-camera viewport frames.

Also recovered Global_glob_reflex message 50 -> GFX_Fade (type 11, slot 10).
Controller roles are discovered through native function 7100BD05, not scene or
object names. Literal events supply five timing phases and both colors. Intro
3500B8EA has four such flashes (43.266666, 43.633335, 44.0, 44.366665s), which
were previously ignored. Ordinary material-9 alpha fades now cover the viewport
before the shutter pass; seek and leaving cinematic mode reset them correctly.
Alternate fade material 8 and VM priority arbitration remain unsupported. These
screen effects do not export to GLB. Source discovery, timeline tests and hidden
GPU full-viewport flash/shutter checks pass; a real 44.5s scene render confirms
the white flash across the viewport. The JADE mesh itself was not rescaled.

Cross-scene verification: source-state audits of 13 containers found 28 supported
fade occurrences in 12 clips across 10 containers, using the same controller-role
and message decoder. Placement audits also complete for BF00218E, 3500A446 and
20007991; the largest militaire step in 20007991 at 3s coincides with an explicit
source placement. Counts/audits establish generic decoding and numerical playback,
not visual parity for every cutscene. No character-name/scene-name branches added.

### Camera lens decoding, viewport projection and Blender export

Traced the small intro JADE graphic to the native tMessage layout: f2 is at
offset 32 and f1 at offset 36. Camera commands use f2 for transition duration and
f1 for the angle in radians. The previous camera decoder treated f1 as duration
and ignored the lens entirely. Corrected both fields and the fade material's f2
test, with shared named offsets to prevent repeating this mistake.

Added a source-clock lens timeline with instant cuts, linear ramps, interrupted
ramps, zero-as-retain semantics and reverse seek. Rendering converts horizontal
FOV for the current viewport aspect and restores the editor FOV when follow ends.
A real 45.3s intro GPU frame now has the expected full-frame JADE composition
without changing the mesh. Exact retail composition remains subject to native
display-format and camera-smoothing parity.

GLB now retains initial source lens, raw commands and lens metadata, and exports
animated yfov through optional KHR_animation_pointer in the shared cinematic
animation. Consumers without the extension retain the initial lens. Blender
5.2.2 successfully imports the synthetic fixture (301 keys) and full intro
(7422 camera lens keys, 17.17–105.87mm). Sampled import errors are below 0.001mm.
The export reference aspect is 4:3; Blender render aspect must match for framing.

Source camera decoding across 13 containers finds 535 nonzero literal lens
commands in 52 clips. Regression checks cover native field order, interruptions,
seeking, GLB pointers, GPU viewport resize and editor-lens restoration. These are
generic source-driven fixes, not character/scene-name branches. Full VM, camera
position smoothing, alternate screen materials and gameplay dynamics remain open.

### Receiver-scoped camera routing and scene dependencies

Camera message 60 now routes each native receiver independently across changing
position markers, target markers and actor references. A null target preserves
the source orientation. Instant cuts apply local offsets without object scale;
repeated position/target references reset positional delay as in the native
controller. Shake commands no longer feed their scalar parameters into the lens.
Native nonzero-transition dynamics and shake evaluation remain unsupported.

Scene assembly and playback discovery now scope clips to their cinematic world.
Literal object arguments also contribute scene dependencies, so camera-only actor
and target references can be loaded without gathering other worlds' clip targets.

GLB camera motion uses separate baked nodes keyed by clip and receiver. Following
an actor therefore preserves that actor's transform and rig; receivers sharing a
position marker remain separate. Camera data and node names include source keys
to remain identifiable after Blender import. Camera UI moved into its own module.

Regression checks cover source switches, actor targets, null targets, rotated
unscaled offsets, repeated references, shared markers, reverse seek, GLB poses
and export state restoration. Retail audits complete for FF000352/3500A438,
FF005520/3500A927 and FF005833/BF00218E. Blender 5.2.2 imports the routing fixture
and the full FF000352 export: sampled camera positions match exactly and angular
errors stay below 0.000001 radians. The lens fixture also imports successfully.
A fresh 44.5s intro GPU frame retains the full-frame JADE composition and flash.
These checks verify decoded playback/export consistency, not full retail parity.

### Bezier movement order, group activation and transition phase

Fixed a source decoding error behind backwards movement and speed spikes:
quadratic/cubic translation vectors were assigned in reverse semantic order.
Both native loaders preserve file order and EVE_GetPos reads the first vector as
the position; subsequent vectors are Bezier controls. AnimationReader now uses
that order for all animations, with no actor or scene-name branches. Byte-level
quadratic/cubic fixtures check decoded fields, independent midpoint references,
actor/object sampling and seeking.

Implemented GRP_FlagInvisibleSet / GRP_FlagInactiveSet using explicit source group
membership. In the intro, the 98s commands address the six-object Sarco group;
ignoring them left its members hidden. A real 103s GPU frame now shows the three
sarcofagi rising. Group references also pull their members into scene assembly.
Commands affect listed members, do not recurse as visibility operations, and do
not modify other actors' instances of shared component keys. Resolved state-change
targets are cached instead of rescanning the complete scene on every frame.

Progressive action transitions now map the outgoing animation's normalized phase
to the incoming duration, including inverse phase, fractional frames and explicit
nonzero frame overrides. Zero-frequency actions retain their requested initial
frame. Native ACTcompute and group function implementations provide the reference;
dedicated regression checks cover phase/rate changes and group seek/cleanup.
Full gameplay dynamics, arbitrary AI and exact retail playback parity remain open.

Verification after the Bezier fix: 53 regression groups pass, retail intro
placement sampling and synchronized GLB export complete, and BF00218E still
exports successfully. In the intro's 41.9–42.7s quadratic segment, the decoded
start/control order now produces forward travel instead of the previous initial
reversal. Explicit source shot placements remain intact; no smoothing or
scene-specific trajectory correction was added.

### Material event playback and procedural mist alpha

Added source-clock MAT_MT_EnableLevel, MAT_MT_SetLocalAlpha and
MAT_MT_SetAlphaTresh evaluation. Material key lookup differs from local index
lookup, as in MAT_ValidateSUBMAT. Native five-bit custom alpha and six-bit
threshold packing are preserved. GPU cache retains disabled source passes so
commands can enable them later; effective pass flags drive sorting and uniforms.
The overlay is separate from archive materials and rebuilds on reverse seek.
Material animation export, arbitrary script expressions and MAT color/UV commands
are not covered by this implementation.

User identified the mist at the start of 3500BB72. Generated SPG vertices extend
beyond the carrier RLI array; renderer fallback incorrectly forced their alpha
to one. Retained generated alpha now reaches both GPU and material baking, while
ordinary carrier vertices preserve their prior fallback. GPU checks verify a
64/255 sprite blend, source-disabled pass activation, timed alpha and backward
seek. A fresh BB72 frame no longer has the dense green veil. This is not a claim
of full native SPG lighting/special-fog/distance-fade parity.

World-cycle controllers are identified by native function roles and bounded,
typed OVA object variables, with instance values overriding model defaults even
for null references. Active controllers contribute environment object dependencies.
FF0045A2 resolves one controller and seven sky/environment/fog-related references.
The 5s main-intro reference-discovery comparison is unchanged: native world-cycle
palette and fog simulation remain unresolved rather than being guessed.

55 regression groups and the dedicated hidden-window GPU material test pass.
Changes remain outside staging.

### Generic sector-activator inspection

Added source-derived `Activator_act` analysis to the cinematic script inspector.
It reports sector registration, configured collision/group references, the
activity-counter gates, visibility-counter semantics, and cutter conditions.
The inspector preserves unknowns for runtime collision/capability state and
unresolved sector targets rather than claiming playback emulation.

Verified production owners in both `FF0045A2` (`00_03_dehors_maison_Intro`)
and `FF00791B` (`01_00_entrepot_sas_entree`), plus synthetic configured and
missing-OVA cases. Release build and the full `ExportVerification` suite pass.
Changes remain outside staging.

User verified the fresh build at the start of `3500BB72`: mist now looks correct.
This confirms the generated SPG alpha correction for that reproduction; sky and
world-cycle palette parity remain separate pending work.

World-cycle preview now reads typed threshold and color-array variables with
instance overrides and samples the native day/dawn/sunset/night palette from the
referenced sun object's current Z. It applies palette constants to the matching
environment/sky submaterials. Saved planet rotation/hour and attack flags are
global runtime values absent from this scene snapshot, so those states and native
light/water/fog outputs remain unsupported. Check 3500BB72 in JadeView.

### Embedded EVE channel inventory and TRA index linking

Loaded GAOs now contribute their serialized Events/SceneEvents channel lists to a world-qualified index, including objects without brain records. The cinematic inspector shows these channel indexes and source keys; literal TRA* calls now join target + index to that serialized channel instead of only showing the raw integer. Channel flag bits remain raw.

Iris events.h/events.c and ai_eventtrackfunctions.c confirm the list/index relationship; Jaded EVEstruct.h/EVEplay.c corroborate the ordered record layout. Audits on BF00218E, 3500B8EA, and BF003BD1 found respectively 349, 2,079, and 72 channels. No literal TRA* calls occurred in those selected clips, so archive verification also runs an explicitly synthetic TRAPlay(0) lookup against each actual nonempty list. This validates the decoder and resolver join, not runtime activation or a real call site.

Verification: clean Release build, full ExportVerification passed, and three real BF graph audits passed. All work remains unstaged.

### Per-channel event payload inspection

The EVE inventory now decodes recognized literal calls and typed messages per serialized channel, rather than only aggregating them at owner level. Expanding a channel in the cinematic inspector shows time, target, arguments, source-backed call summary, and nested TRA lookup. Static transforms and unknown payloads remain explicitly distinct from decoded calls.

Retail audit totals: FF005833/BF00218E has 103 channels with calls, 357 literal calls, and 163 typed messages; FF0045A2/3500B8EA has 199 channels with calls, 1,209 literal calls, and 278 typed messages; FF00791B/BF003BD1 has 18 channels with calls, 84 literal calls, and 62 typed messages. In CINE_APRES_MDISK_EVT02.gao, channel 16 holds drone visibility, completion flag, monitor capability and sound flags; channel 21 holds the fan-part control-flag handoff; channel 25 contains the motor XMEC toggle. No literal TRA* was found in the sampled clips, so no claim is made about which channel a script starts dynamically.

Validation: clean Release build and full ExportVerification pass; production graph audits pass for all three scenes; synthetic index probes resolve every nonempty world-local EVE list. Changes remain unstaged.

The same archive audit found one referenced external EVE list (3 channels) in BF00218E and four (158 channels) in 3500B8EA. In BF00218E, EVT_Peyd'j_repare_Mdisk.gao / C1009398 channel 0 targets Peyj and contains ACTKitMerge(3500A57E, 200, 230) followed by ACTActionSet(200). The UI now exposes this source-world channel when inspecting the linked owner; its independent Cine_Events gate remains a separate runtime condition.

### Activator sector-list field resolution

Follow-up source tracing resolved the Activator loop's raw instance writes:
`(uint32_t*)instanceVarDataCurrent + 130` maps to `i_cptact` (byte offset 520)
and `+131` maps to `i_forceshow` (byte offset 524) in Iris's
`tScriptStruct_Activator`. The inspector now joins sector-list references to
same-world owners with the matching `Activator_act_afterrec` callback before
assigning these meanings. A populated production example is `FF005EFB`, world
`05_01_ilot_plaques`: `SCT_1.gao` links 11 targets through its remove-sector
list. `FF005EFB` also has populated show-sector lists; `SCT_DebH.gao` references
`SCT_Deb.gao`, verifying the +131 write against a retail target instance.
`FF0045A2` and `FF00791B` provide additional cross-world sector-group fixtures.
Release build and all 64 verification groups pass; changes remain outside
staging.

### Extended message-to-gate correlation

The cinematic inspector and per-cinematic/per-world graph audits now correlate
literal `MSGSendEx` commands with configured message-start gates by resolved
queue target, message ID, and the gate's Int1 mask. Iris's source confirms
`MSGSendEx` zeroes Int1, writes the current execution GAO as sender, numerically
casts its signed integer payload to `float`, and queues at most five messages.
The previous preview incorrectly bit-reinterpreted that payload; the inspector,
catalog text, audit JSON, and regression fixture now use the numeric cast.
Action-clip command decoding also inherits the actor root when its track has no
target, matching the typed-message context rule.

Three production cinematic audits (`BF00218E`, `3500B8EA`, `BF003BD1`) and four
world audits (`00_02_home_decryptage`, `00_03_dehors_maison_Intro`,
`01_00_entrepot_sas_entree`, `01_04_entrepot_liberation_mili_cine`) found no
decoded `MSGSendEx` that matches a configured gate. The last world does contain
message-43 calls to `SFX_Generateur_Lobo_*`; they remain object-directed
messages, not proven start-gate transitions. Full ExportVerification passes;
Release build v17 is published in the normal artifact folders. All changes
remain unstaged.

### Callback-reachable helper-text gates and rejected extended messages

Helper-text gate recognition now follows source-mapped callback reachability
from the owner's assigned slots, including `AITrackCurChange` transitions from
`helper_text_hlptxt_init` into `helper_text_hlptxt_tests_depart`. The inspector
and archive graph audits also expose same-target/same-ID `MSGSendEx` calls that
cannot pass a nonzero `Int1` gate mask, separately from real matches and
wrong-target sends. Regression fixtures cover both the source callback chain
and this rejected-send case.

Verified in `FF001FBE / 01_04_entrepot_liberation_mili_cine`:
`TXT_bullrushfoireux.gao` only assigns the init callback, yet the source graph
reaches the gate callback. Its queue resolves to `OBJ_porte_prisomili01.gao`
(`1600272A`); message 37 / Int1 4 in `3500A083` executes on `GST_Global.gao`
(`710047BC`) and is therefore reported as a context mismatch. Full
ExportVerification passes; a fresh Release build is published as v18. Changes
remain unstaged.

### Cinematic script analysis v33

Added callback-scoped decoding of Iris companion status messages (type 49). The
cinematic message inspector now explains requested companion status/action,
position, facing, world, and special status-3 collision placement only when
the owner's callback graph reaches `companion_co_exec_get_message`. This avoids
mislabeling other Iris uses of type 49, including Crochax scaling, aircraft
mode, and SFX lighting. Regression checks cover regular and deferred actions
and reject unrelated callback owners. Details and limits are documented in
`docs/cinematic-script-system.md`. Full ExportVerification passes; the Windows
Release build is published to `artifacts/cinematic-script-analysis-flow-v33`.
All changes remain unstaged.

### Cinematic script analysis v34

Expanded the world AI scheduler inspector with source-ordered activation and
dispatch gates. It now distinguishes ordinary-time activation blocked by the
serialized `flags2 0x00400000` mapping from the alternate special-time path,
then lists Iris `AI_MainCall` filters for processed entries, distance,
hierarchy, AI-instance allocation, and `AI_RunInstance`. This clarifies why a
configured brain may not run while preserving live state as unknown. Added
specific regression assertions and documented the Iris source files and
runtime limits. Full ExportVerification passes; the Windows Release build is
published to `artifacts/cinematic-script-analysis-flow-v34`. Changes remain
unstaged.

### Cinematic script analysis v35

Made source-local saved-flag argument resolution causal: callback-local aliases
now resolve from a unique assignment at or before the engine-call line, ignoring
later assignments and rejecting explicitly ambiguous values. This prevents
false trigger-to-flag links from future writes in the same C function. Added a
regression fixture with before/after assignments and a non-unique assignment;
documented the bounded data-flow limit. Full ExportVerification passes; the
Windows Release build is published to
`artifacts/cinematic-script-analysis-flow-v35`. Changes remain unstaged.

### Cinematic script analysis v36

Corrected the reusable `Detect_o_Zone` timer interpretation. Iris arms the
follow-up for any nonzero duration, and its `TIMEElapsed` predicate is
`currentTime - start >= duration`; a finite negative duration is therefore
already expired after a nonzero start timestamp. The inspector now reports
this source-backed behavior and preserves the configured flag reversals.
Added a regression fixture and documented that collision/time state remain
unevaluated. Full ExportVerification passes; the Windows Release build is
published to `artifacts/cinematic-script-analysis-flow-v36`. Changes remain
unstaged.

### Cinematic script analysis v37

Separated `Detect_o_Zone` writes that use different source semantics: the raw
invert index is set on entry, cleared on exit only when the timer is zero, and
modulo-10000 toggled only after a known nonzero timer expires. Timed `set01` /
`reset01` reversals now carry their resulting bit polarity; unknown timers do
not generate speculative follow-up links. Added regression fixtures and a
short UI navigation note. Full ExportVerification passes. The tested Release
assembly is packaged in `artifacts/cinematic-script-analysis-flow-v37` with
the unchanged v36 Windows host/runtime files; a fresh RID publish could not
restore because NuGet was unreachable. Changes remain unstaged.

Production cross-check: regenerated cinematic graphs for `FF005833/BF00218E`,
`FF0045A2/3500B8EA`, and `FF00791B/BF003BD1`, and world-script graphs for all
15 worlds identified by the archive index as containing `Detect_o_Zone`
owners (24 owners in the loaded world snapshots). The real no-timer raw-index
entry/exit transitions are present; no owner in that census combines a
positive invert index with a nonzero timer, so that modulo-timed case remains
covered by source and synthetic regression evidence only. Reports are in the
`production-audits` and `zone-world-audits` subfolders of the v37 artifact.

### Cinematic source-message readers v38

The script inspector now lists literal `MSGGetByID_C` message IDs read by the
selected owner's reachable callbacks, with callback paths and source locations.
Typed cinematic messages without an existing field-level interpretation can
fall back to this source evidence when the EVE execution object resolves to the
owner. The fallback identifies a possible receiver only; it does not infer
payload fields or claim message delivery or callback execution. Cinematic and
world graph audits now export `SourceMessageReaders` for offline comparison.
Added regression coverage for a source-confirmed Sally reader and an unmatched
ID. Full ExportVerification passes. A Windows build is packaged in
`artifacts/cinematic-script-message-reader-v38`; NuGet vulnerability metadata
could not be refreshed because nuget.org was unreachable. The three production
cinematic audits now export reader paths for cross-world actor owners: Sally
and companion callbacks contribute IDs 9, 16, 37, 46, 47, 49, 53, 67, 78, 85,
and 92; the sas-entry scene also exposes local IDs 1 and 10. These sample clips
produced no new generic message descriptions, so this addition currently makes
the receiver-side source catalog inspectable without changing their playback.
Reports are in the matching scene folders under
`artifacts/msg-reader-verification/`. Changes remain unstaged.

### Static script message routes v39

Added a generated reader catalog for generic `MSGGet_C_CURRENT` handlers that
copy a queued message and compare its `msgID` to a literal, in addition to the
existing direct `MSGGetByID_C` readers. Script-call indexing now retains all
message-buffer field writes with their enclosing source branches. A sender to
receiver route is emitted only when `msgID` has one unbranched literal write,
the `MSGSend_C` target resolves uniquely to the current owner or an object/OVA
reference, the destination has one unique loaded script owner, and a reachable
callback reads the same ID. Ambiguous and conditional ID writes stay unresolved.

The real source chain `action_reaction_ar_exec_send_info` → `TXT_Sally.gao`
(`7200064E`) → `Cine_Textes_CiTxt_reception_messages` is covered by regression
tests. A BF world audit of `FF005520 / 01_05_entrepot_enlevement_peyj` found 12
type-30 routes to `TXT_Sally`; cinematic audits of `BF00218E`, `3500B8EA`, and
`BF003BD1` found 4, 7, and 4 matched routes. Full ExportVerification passes.
Reports are in `artifacts/msg-reader-verification-v4/`; changes remain
unstaged.

### Cross-track script-owner discovery v40

The cinematic timeline now includes script owners reached by serialized
animation and transform-track targets, in addition to actor roots, explicit
commands, and message references. The script-graph audit uses the assembled
cinematic world for participant discovery, matching the live cinematic
assembler instead of reading an empty pre-assembly actor list.

On `FF00791B / BF003BD1`, the audit now lists Peyj as a participant and resolves
his `_basic_peyj` brain (`9E001C77`, model `35001FF8`, callback
`companion_co_init`) and base `Events=35009544`. That 2.367-second record has no
decoded calls, and the active companion action still depends on runtime mode
and branch state absent from the BF snapshot. An archive-wide census found 439
skeletal actor/clip bindings across 1,232 clips: 296 with resolved body actions
and 143 without. This is diagnostic coverage, not a claim that each missing
action is a playback defect. Regression checks and the full ExportVerification
suite pass. The Windows build is `artifacts/cinematic-script-scope-tracks-v42`;
changes remain unstaged.

### SceneEvents target navigation v41

The callback-driven `TRA*` inspector resolves serialized EVE channel target keys
to loaded GAO names and makes targets in the assembled cinematic world
selectable/frameable. The script-graph audit now exports the resolved target
name for every embedded EVE channel as well.

In `FF005833 / BF00218E`, this exposes `Cine_init_cableventilo_fixe.gao`'s
reachable `TRAPlayAll` call for `SceneEvents=BF000B2C`: seven channels target
`Cine_Bone02.gao`, `Cine_Bone03.gao`, `Cine_Bone04_ventilo.gao`, and the
owner/default object. The owner-targeted channel carries time-zero commands
that directly target `DEC_00_02_moteurV.gao` (`1F00007D`) and
`DEC_00_02_helicesV.gao` (`1F00007E`), plus two other serialized targets.
Its `Cine_Events` gate is not evaluated. Production audit output is in
`artifacts/cinematic-event-target-navigation-v43/audit/`. Full
ExportVerification passes. The Windows-only build is
`artifacts/cinematic-event-target-navigation-v43`; changes remain unstaged.

### Inline SceneEvents command inspection

Resolved callback-driven `TRA*` calls now expose an **Inspect addressed EVE
channels** tree inline. It contains the exact channels addressed by that source
call, expands channels with decoded commands/messages, resolves loaded target
names, and permits selecting/framing targets in the assembled cinematic
world. This connects script call -> EVE payload -> command target in one
inspector path; it remains static inspection and does not simulate callback
conditions or execute the script VM. Production graph audits confirm the
same OVA-derived gate-to-SceneEvents join in `FF005833/BF00218E`,
`FF0045A2/3500B8EA`, and `FF00791B/BF003BD1`, with different flags and event
lists per owner. Live save flags, zone collision and callback activation remain
unknown. `ExportVerification` passes; `git diff --check` is clean. The
framework-dependent Windows apphost build (requires the .NET desktop runtime)
is `artifacts/cinematic-sceneevents-inline-v44/JadeView.exe`. Restore/publish
with a self-contained `win-x64` runtime could not be refreshed because NuGet
is unreachable; the available build succeeded using the local package cache.

The production saved-flag graph further links fan gate `2166` to
`CINE_APRES_MDISK_EVT02.gao`'s channel 16, which writes the flag at 44.783 s,
and to readers in both the cinematic and gameplay worlds. The relation is
visible in the existing **Saved flag / event links** inspector, while the
runtime order and current save value remain unknown.

The callback call tree now annotates a direct current-owner
`AI_EvalFunc_OBJ_FlagsIdentityTest_C` when its mask is a literal, evaluating
the `if` / `else` branch against serialized GAO flags. The `OBJ_fusible_Pompe`
guard for `0x02000000` is false for its saved flags `0x0800F060`. This is a
static hint only; runtime flag changes are not tracked. See
`docs/cinematic-script-system.md` for where to find the control in JadeView.
The refreshed framework-dependent Windows apphost is
`artifacts/cinematic-script-graph-v45/JadeView.exe` (requires .NET Desktop
Runtime).

Direct current-owner `OBJ_CapaTest` predicates are now described too, including
negation: Iris tests whether any bit in the mask intersects the current runtime
capabilities. JadeView deliberately reports this result as unknown because the
BF snapshot does not contain the live capability word. Graph-audit JSON now
includes the same branch analysis for both resolved and unresolved `TRA*` calls.

Production audits regenerated from the installed `sally_clean.bf` for
`FF005833/BF00218E`, `FF0045A2/3500B8EA`, and `FF00791B/BF003BD1` confirm the
capability annotation occurs in multiple cinematics. The `OBJ_fusible_Pompe`
identity-flag branch remains correctly reported false only for serialized
flags. Audit files are in `artifacts/cinematic-branch-predicate-audits-v46/`.
The refreshed apphost including both identity-flag and runtime-capability
annotations is `artifacts/cinematic-script-graph-v46/JadeView.exe`.

Nested EVE navigation now recursively follows literal `TRA*` commands from a
channel into the target owner's `SceneEvents` list, preserving world scope,
ambiguous links, missing tracks, cycles and bounded traversal. In the UI, open
an EVE channel and expand **Follow nested SceneEvents calls**. An archive-wide
audit parsed all 91 FF containers and found 18 channels with nested calls,
149 resolved route edges and 6 cycle stops. It recovered the elevator sequence
`35009EF6` -> `1_1_obj_verin_axe.gao` `SceneEvents` channel 0. This confirms a
serialized route, not that the runtime started it. Tests cover recursive paths,
`TRAPlayAll`, event-list scope, cross-world ambiguity and cycles. The fresh
audit is `artifacts/archive-nested-eve-v48/archive-nested-event-track-audit.json`.

The channel inspector now summarizes decoded key components and payload kinds
and offers **Raw EVE key payloads** for unknown serialized rows. The four engine
commands whose source signatures take a game-object argument are linked to the
resolved scene object and can select/frame it. On the elevator channel this
revealed 14 `OBJRotateLocalY` increments and five `TRAPlay` calls in EVE 0;
four child channels 2–5 orient toward referenced objects, and channel 1 applies
a 12-radian local-Y spin over one second. Float-typed literal arguments (`0x22`)
were added to the general command decoder, so these calls now appear in the
script inspector with source-derived meanings. JadeView still does not simulate
the nested EVE time flow in preview. Regression checks cover raw payload
summaries, object-argument metadata and float-bit retention. The decoder was
also audited on three other clips (31/131, 83/544 and 14/11
owners/decoded calls). Only `3500B8EA` produced axis-rotation calls, with
serialized values `-0.02` and `0` radians; reports are under
`artifacts/float-call-audits-v56/`. The framework-dependent Windows apphost at
that checkpoint is `artifacts/cinematic-nested-events-v58/JadeView.exe`.

The controller callback graph also links `Cine_Events_Cine_exec_GoToEvents` to
`TRAPlayAll` for all 32 channels of `35009EF6`; the source path includes a live
`OBJ_CapaTest(0x100)` alternative, so this proves a route in source, not runtime
activation. The graph audit is `artifacts/cinematic-elevator-float-v57/`.

To make the script findings easier to locate, the top of Cinematic Timeline now
has a search field for SceneEvents owners, GAO/event keys, channel targets,
decoded command names, and loaded command targets. Matching lists are filtered
in the lower EVE inventory and opened while a query is active. This is a
navigation aid for the static inspector; it does not indicate runtime
activation or change cinematic playback.

The archive-wide script index now scopes OVA target labels and script-owner
links by container/world. Unique external owners remain linkable, while reused
GAO keys with multiple world candidates are preserved as unresolved. A fresh
all-world scan of `sally_clean.bf` resolved 13,399 OVA-to-owner references and
kept 1,715 ambiguous references explicit; the report is under
`artifacts/script-owner-reference-audit-current/`. Tests cover local
precedence, globally unique external resolution and cross-world key collisions.
The corresponding apphost, including the EVE search field, is
`artifacts/cinematic-world-safe-links-v60/JadeView.exe`.

The generic `Detect_o_Zone` trigger summary now evaluates which collision route
the serialized owner identity flags select: bit `0x400` chooses ZDE pivot 2,
while a clear bit chooses BV overlap. It still marks that as an initial saved
route, since earlier runtime writes could change identity flags. Production
data in `FF005EFB / D9005EE0` confirms three trigger owners choose BV; a source
fixture covers ZDE. Tests and the 302-owner world script audit pass. The latest
apphost with this trigger detail is
`artifacts/cinematic-trigger-route-v61/JadeView.exe`.

The source-backed saved-flag graph now handles Iris `ARRBitSetRange` and
`ARRBitClearRange` when the call resolves to `Flag_speciaux_sauve` and both
inclusive bounds are constant, ordered, positive and within the 4488-byte
buffer. Reads/writes become per-flag graph edges with call-site provenance;
unrelated buffers and invalid ranges are omitted. Regression checks cover
inclusive endpoints, set/clear polarity, reversed bounds and buffer limits.
The fresh all-world audit still resolves 15,950/15,950 brain owners and found
no direct range-call sites in the current recovered callback corpus, so this
adds format coverage without changing existing archive counts. The apphost is
`artifacts/cinematic-saved-flag-ranges-v62/JadeView.exe`; the full report is
`artifacts/script-range-audit-v62/archive-gameplay-script-index.json`.
