# Sally face morphs: current findings

Status: resumed on 2026-10-19. The original investigation below was a read-only analysis of the PC
`sally_clean.bf`, JadeView's parser, and the available REBGE Anniversary
analysis. The original game was not launched and project source was not
changed during the analysis.

## Implementation checkpoint (2026-10-19)

### What the experimental mouth cycle reproduces

The mouth cycle uses a **32-form sequence recovered from the PC game's code**;
it is not a random selection of morph targets. The controller selects the next
form, transitions toward it, then advances after the transition settles. At the
end of the list it wraps to the beginning. The supported controller family and
effective source variables determine which recovered sequence and transition
completion rule apply.

JadeView reproduces that form order, factor/progression calculations and the
binding to the head's mouth channel. Controller discovery uses brain/script
resources and module data rather than character names. Current verification
covers Sally, Pey'j and militaire; unknown controller families are not guessed.

**The launch and initial state are still a manual simulation.** Play starts at
sequence index zero with current factor zero; strength and speed are chosen in
the preview. The original game's activation/stop scheduling, interruptions,
saved controller state and synchronization with spoken lines are not replayed.
Consequently, this is a recovered game mouth-motion mechanism triggered by the
user, not a complete original dialogue/lip-sync animation.

Optional GLB export samples a copy of the current simulated cycle at 60 Hz into
a separate experimental clip. It does not record authoring keys or establish
original speech timing. Details and evidence:
[automatic binding](face-morph-pc-automatic-binding.md),
[controller variants](face-morph-pc-controller-variants.md) and
[module binding / export](face-morph-pc-module-binding.md).

### PC runtime breakthrough

The saved crash dumps contain unpacked PC code. Static analysis now establishes
the expression-variable-to-type-4 bridge, ordered channel interpolation and
Sally's blink envelope. See [PC runtime findings](face-morph-pc-runtime.md) for
addresses, evidence identity, exact boundaries and remaining speech work.
`FaceMorphChannelEvaluator` implements explicit channel-to-target conversion;
**PC morph channels** now provides weight/progression preview, `.bgemod`
restoration and GLB pose/key export. Direct target controls remain available.
**PC Sally blink** previews an explicitly triggered PC envelope, and **PC Sally
expressions** previews settled source kits 2/3/4/5. Key creation/replacement and
blink baking are currently hidden at the user's request; existing saved-key
playback/export remains available. Random game scheduling and lip-sync remain unresolved.
Earlier unknown-PC statements
below describe the investigation before this captured-code evidence.

### Original animation event investigation

The archive-wide cinematic/dialogue source inventory, including the recovered
event → message → facial-controller path and its remaining gaps, is tracked in
[PC dialogue and facial-event chain](face-morph-pc-dialogue-chain.md).

`AnimationReader` now retains opaque payloads instead of silently skipping them:
word-pair event lists (including their count), transform reference words,
unsupported packed rotation words and extra transform controls. Each payload
owns its bytes and records its clip-relative offset. Original channel flags and
the first header word are retained alongside the resolved target. These records
are evidence, not interpreted morph curves; playback does not execute them.

The export verification runner writes `*-animation-events.json` with clip keys,
times, source offsets, flags and exact hex bytes. On the original
`sally_cmlean1.bf`, Sally decodes 211/211 clips with 148 retained payloads;
the military actor decodes 88/88 with 88 payloads. There are no decode failures.
All retained payloads in these two actors are word-pair lists. Coverage is
limited to these actors, not every animation in the archive.

Twelve Sally events contain operand `00000035` paired with tag `01000021`.
Their common eight-row structure includes tags `01000028`, `02000806`,
three `01000022` operands, `03002F47` and `1F000000`. For example, clip
`7100B93A` at 0.3 seconds carries operands 53, -1.0, 0.95 and 1.05 when
interpreted as an integer followed by three floats. Those operand types and
the script-call interpretation remain hypotheses; the exact raw words are
preserved in the report. Other matches include `32000122`, `320001E7`,
`32000128`, `26002A72`, `26002A84` and `26002A86`.

The numerical match with Anniversary lip-sync message type `0x35` does not
establish message delivery or a morph channel. The command tag `03002F47`
must first be resolved in the PC VM dispatch table and its argument consumption
verified. No known Anniversary packed facial preset float words (1500, 2540,
10600, 31600, 52515) occur in these payloads. This is a limited negative result,
not proof that facial commands are absent from scripts or dialogue resources.

Follow-up: `0x2F47` is decimal 12103. Across Sally's retained events it occurs
24 times: the preceding integer-tag operands are 52 (11), 53 (12) and 55 (1),
each followed by three float-tag operands. The military actor has no matches.
This broader pattern weakens the isolated `0x35` message-type hypothesis.
The separate REBGE `docs/MODLOG.md` records Anniversary source-selector remapping
and an actor-local resource manager lead for 12103, but also conflicting
physical-call evidence and an unresolved map attachment. Its saved raw reports
are unavailable in the current checkout; this historical lead is not a verified
PC handler identity. The original 47-entry Sally object list cannot directly
resolve indices 52/53/55; do not substitute morph group numbers for these IDs.

Reproduce the source-word census with
`tools/audit_animation_event_words.py <actor-animation-events.json> --output <output.json>`.
It preserves preceding operand/tag words and exact row offsets, without
executing scripts or claiming runtime dispatch indices.

Original facial/lip-sync timeline export is still unresolved. Exported skeletal
GLB clips now include `extras.jadeViewSourceRecords` schema version 1: original
clip key, track/key indices, time/duration, channel headers, flags, payload kind,
clip-relative offset and exact hex bytes. `interpreted: false` explicitly marks
these as archival metadata; Blender does not turn them into facial Actions.
Clips without opaque payloads omit the extras, and user-authored morph clips
retain their separate provenance. GLB tests verify payload bytes/timing/header
retention and isolation between clips; the normal unsupported warning remains.
Synthetic checks cover exact byte retention, relative offsets, event timing,
independent storage and rejection of truncated/overflowing event lists.

### PC brain and expression-variable evidence

The verification runner now exports `*-scripts.json` through `ActorScriptAudit`:
the parsed `q/s/t/r` dependency closure of each actor brain, combining the hangar
`FF0057FD` with fixed resources `FF0084DF`. Records retain their source container,
offset, SHA-256, reference keys and exact bytes. This is the loader-visible brain
closure, not all potential runtime or dialogue dependencies.

In the original PC archive Sally root `71003F5A` points to brain `71003F58`,
script model `71000CDD` and variable resources `71003F60` / `71000CDC`.
The closure contains 81 resources: one brain, one model, two variable resources
and 77 code leaves, with no unresolved queued references. The main variable
resource contains 450 named descriptors. Twenty-two names match the explicit
facial-name filter; this is a discovery filter, not a complete facial schema.
The military actor yields 87 resources and 18 matching names.

Direct PC descriptor evidence includes these names and first-word values:

- `f_blink_progress`: `00000188`; `f_blink_time`: `0000018C`;
  `f_blink_delai`: `00000190`; `i_blink_now`: `000001A0`.
- `i_expression_kit`: `00000198`; `i_expression_oldkit`: `0000019C`;
  `i_expression_de_cinematique`: `000001A4`;
  `m_expression_message`: `000001A8`.
- `i_expr_bouche_blabla_id`, `i_expr_bouche_auto` and
  `ai_expr_bouche_blabla_liste` (32 elements).
- `f_expr_factor_target`, `f_expr_factor_current`, `f_expr_prog_current`,
  `f_expr_speed`, `f_expr_forme`, `f_expr_flag`: seven elements each for Sally,
  five each for the military actor.

These are actual PC variable names, strengthening the procedural-controller
lead independently of target labels. The first-word values resemble storage
offsets, but their base and use by native code have not been verified here.
Seven expression elements do not equal Sally head's six type-4 channels; do not
map slots directly. Descriptor order has not been proven to assign sequential
serialized values to variable names, so the audit deliberately omits interpreted
defaults and does not use apparent float NaNs as evidence of bad game data.

Of Sally's 77 code leaves, 75 contain only 12 bytes; the other two contain 65
and 35 bytes with readable debug text. No event-style instruction stream was
identified in those leaves. This limits what this BF closure alone establishes;
it does not prove where the original executable obtains its implementation.
The next necessary evidence is a verified PC routine that reads these variables
and writes type-4 weights/progression, followed by the speech-event producer.
Selector 12103 remains unconnected to that path.

Run `tools/audit_actor_facial_variables.py <actor-scripts.json> --output <output.json>`
after the archive verification runner. The reader validates descriptor sizes,
name/value consumption and code-reference boundaries against the full payload.
Four synthetic tests reject truncation, partial descriptors and unknown value
strides, and check name/descriptor retention without guessing defaults.

### Manual preview continuation

Project persistence is now implemented in `ModProject.Morphs.cs`. The additive
`morphPreviews` collection preserves manual weights, copied keyframes, time and
loop setting; existing version-1 projects without this collection still load.
Playback restores paused. Profiles bind to container, world index/key, exact
actor/member IDs, geometry key and SHA-256 of geometry positions/topology and
the type-4 payload. Matching vertex counts or shared GAO keys alone are not
accepted. Profiles for another appearance remain stored and restore lazily when
that matching geometry/modifier becomes active. Source/owner mismatches are
reported and retained; saving after a mismatch does not erase the saved keys.

Morph UI edits update project state and mark unsaved authoring changes, including
when transform commands are later undone. Save captures the current preview
pose and time. These records are deliberately excluded from native BF changes
and do not enable a BF build by themselves. Invalid weights/times/counts,
duplicate profile identities and unsorted/duplicate key times are rejected.

Tests cover actual `.bgemod` file save/load, snapshots, exact pose restoration,
paused startup, source/instance isolation, appearance switching, malformed
records and legacy projects. Sally's three-key animation also survives a project
reload and GLB re-export; Blender 5.2.2 verifies all five key/intermediate samples
using `blender_morph_animation_check.py` against `sally-restored-morph.glb`.

User-authored facial timelines are now available in **Manual morph animation**.
Keys capture every direct target weight at an explicit float32 time. Recording
at an existing timestamp replaces that key; keys are sorted, copied and
validated, with linear interpolation, loop/hold playback, seeking, key removal
and clearing. The actor evaluation clock advances the facial preview before
skinning, independently of whether skeletal playback is paused.

With animation/skinning export enabled, **Manual morph animations** writes a
separate `Manual morphs · …` clip for each exported mesh with recorded keys.
The target is the skinned mesh node's `weights`; output values use flattened
SCALAR accessors in time-major, native-target-index order. The clip explicitly
records `originalGameTimeline: false`. Export captures a copy of the keys before
skeletal appearance sampling and preserves the live facial time, playback flag
and weights. Disabling that export option retains manual mesh default weights
but omits the facial clip. `.bgemod` now persists these keys; BF builds still exclude them.

Tests cover ordering/replacement, interpolation, looping/non-looping endpoints,
invalid inputs, the actor clock, export preservation and optional omission.
Blender 5.2.2 imports the three-key Sally example as a separate shape-key Action;
both eyelid weights evaluate to 0, 0.5, 1, 0.5, 0 at 0, 0.05, 0.1, 0.15, 0.2 s.
The fixture is `sally-authored-morph.glb`; the import check is
`tests/ExportVerification/blender_morph_animation_check.py`.
This adds authoring/export capability, not new evidence about PC facial timing.

Known raw-target issue: applying Sally group 37 alone produces visible eyelid
artifacts, reproduced in JadeView and Blender 5.2.2 with every other weight zero.
The ordinary source has 55 sparse entries in this group, the HD source 126; the
largest displacement is about 0.01368 / 0.01495 units, respectively. The ordinary
indices and all delta float32 words match the retained Anniversary group 37
exactly; its source label is `Neutre_N.gao`. HD indices also match that retained
group, but 114 scalar words differ, so its values are not substituted.

These observations establish a problem with using this raw target directly;
they do not establish whether the intended PC controller subtracts a baseline,
combines it with another target or never activates it. No guessed smoothing,
clamping, remapping or data replacement is applied. The morph panel marks the
known original target `[!]`, displays a warning when active, and GLB export
retains all targets plus the issue in metadata and warns for nonzero weight 37.
Identification requires the geometry key and exact SHA-256 of the serialized
group indices followed by XYZ delta words, avoiding a blanket ban on index 37.
The original forms remain available for reverse-engineering. A source/controller
revision is still required before this can be called a faithful geometry fix.

The editor now exposes **Facial / vertex morphs** beside the actor animation
controls. It selects a mesh and adjusts direct target weights (empty slots are
hidden by default). This is an editor deformation experiment, not a decoder of
the six native control channels. Weights start at zero; archived channel words
are not silently interpreted as direct morph weights.

`FaceMorphPreview` applies the sparse weighted offsets to original mesh vertices
before skeletal skinning. Its cache is tied to both the source modifier and
geometry instance, preventing weights from leaking to a different appearance.
Reset returns neutral geometry. State is authoring-only: it is serialized into
`.bgemod`, but not into BF builds. GLB exports manual target weights as mesh defaults and recorded keys as a separate
user-authored facial animation clip. Export restores the exact previous
preview state even if animation sampling changes the selected appearance.

Real Sally tests compare the closed-eyelid preview with GLB throughout a
skeletal clip, including blended skin weights and atlas/UV corner duplicates.
Blender 5.2.2 retains default weights `[1,1]`, allows resetting/opening the eyes,
and restores the original pose when the weights are reapplied. The imported
maximum eyelid displacement is approximately 0.02401 units.

The available Anniversary component reader describes one-based progression
through ordered references, adjacent-group linear blending and a separate
weight multiplier (type-4 callback `1409D3670`, writer `1414846F0`). These remain
cross-version static findings; they do not resolve PC progression boundary/hole
behavior, channel producers, blink triggers or authored speech timelines.
No Anniversary timing model is enabled automatically in JadeView.

### Decoder and target export

The PC type-4 decoder now lives in `src/Features/Animation/Morphs/Formats/Pc/FaceMorphReader.cs`.
It preserves empty target slots, exact channel words, ordered duplicate group
references and `FFFFFFFF` holes. Payload header word 2 is the source vertex
count; decoding rejects a different mesh size and out-of-range indices,
non-finite deltas, truncated arrays and trailing bytes.

A real appearance-switching bug was found: `SceneActor.ApplyShape` replaced
geometry/material/palette but retained the previous model's modifiers. Sally's
active HD mesh (1242 vertices, `6200318D`) therefore inherited the ordinary-head
modifier (464 vertices, 886 sparse entries). Modifiers now follow the selected
source model. The HD payload has 3020 sparse entries. Both variants have 55
groups and six channels. Type 4 also appears on hand and other character assets;
it is a general vertex deformation modifier, not exclusively facial data.

Animated/skinned GLB export now includes neutral POSITION morph targets,
named `PC_Morph_00` through `PC_Morph_54` for Sally, with native slot indices
preserved. Deltas are transformed as vectors into the same mesh bind space as
exported positions and copied to every UV/atlas seam duplicate. Source channels
are retained in mesh extras, not interpreted as target weights. Export state
restoration also preserves the selected model's modifiers. Static/baked export
does not yet carry these targets; target normal deltas are not generated.

Tests compare every exported target delta at every triangle corner against the
decoded source and mesh bind transform, including atlas seams, alongside the
existing skeletal checks in three Sally and three military clips. Blender 5.2.2
imports all 55 Sally slots; setting group 1 to one moves the evaluated skinned
head by up to 0.02401 units, and resetting it restores the original vertices.
This proves shape-key conversion and deformation, not original facial timing.
An additional four-view Blender render with groups 1 and 2 both at weight one
shows closed eyelids on the active HD head. This visually validates their joint
blink effect; it does not establish the game's time envelope or scheduling.

The installed PC executable was rechecked with REA/Ghidra (SHA-256
`70AE232D58BAFE5CBF5C27712538AF91E8B13A98104AE1BF6AEA6DCF3506F5DB`).
The commented loader address `00487A40` is not a recognized procedure in this
static image. No new PC callback/timing semantics were obtained. The Anniversary
blink/lip-sync findings below remain cross-version evidence only. No original
game process was launched or attached during this continuation.

Next unresolved work: qualify PC channel interpolation and weight producers,
blink scheduling and dialogue-event timing. Until then there are no inferred
blink or speech clips in the normal export. In Blender, use the head mesh's
Object Data Properties > Shape Keys to inspect or keyframe the exported forms.
`tests/ExportVerification/blender_morph_check.py` verifies the real import.

## What the PC archive proves

The archive is `sally_clean.bf` (SHA-256
`84040591DC453A64D4EBA33E4CD339D325C23F39F3067118D33DD628B22BD1C5`). The
Sally head GAOs `CE00A4B0` (464 vertices) and `A90056F0` (1242 vertices) each
contain a type-4 modifier with 55 primary deformation groups and six channel
records. The data is present in the original PC archive, even though JadeView
currently keeps modifiers as raw bytes and does not apply them in the
viewport.

For the PC type-4 payload, each primary group is stored as a count, that many
32-bit vertex indices, then one float32 XYZ displacement per index. Thus one
entry occupies 16 bytes after the count. The ordinary head has 886 sparse
entries across non-empty groups 1, 2, 5, 6, 11, 31, 34, 37, 50, 51, 52 and 53.
Every checked index is in range, unique within its group, and every delta is
finite. Most maximum delta lengths are around 0.045.

Groups 1 and 2 are bilateral, compact deformations around the eyelids. Their
vertex pairs mirror across X, and their indices correspond exactly to the
named Anniversary groups `Morph_TeteHD_BlinkHaut` (upper eyelid) and
`Morph_TeteHD_BlinkBAS` (lower eyelid). PC channel records 0 and 1 reference
groups `[1,1]` and `[2,2]`. This makes the PC data very likely to represent
upper/lower blink targets, although the PC executable's callback has not been
verified.

## Cross-check against named REBGE data

The ordinary-head Anniversary preview contains sparse targets whose vertex
index lists match the PC groups for groups 1, 2, 5, 11, 31, 34, 37, 50, 51, 52
and 53. The named groups identify likely expressions: large eyes, brow frown,
light/bad smile, and A/O/I/scream mouth shapes. Most corresponding delta
components differ by at most about `6.5e-8`, consistent with float rounding.
Group 37 matches bit-for-bit. Two scalar components in some groups differ by
up to about `0.00553`, so the Anniversary payload is not a byte-for-byte
replacement for the PC payload. PC group 6 has the same index list as
Anniversary target 8; its identity remains uncertain because the intervening
neutral slots do not line up cleanly. The Anniversary preview also has an
extra target 39 whose indices and deltas nearly duplicate group 37, while PC
group 39 is empty. Treat neutral labels and these slots as unresolved.

The HD comparison is weaker: group counts and index arrays diverge for most
targets (for example, PC group 1 has 154 entries while the HD preview target 1
has 1223). Only groups 6, 7, 8, 37 and 46 have matching index lists. Do not
transfer the ordinary-head mapping to the HD head without further evidence.

The Anniversary type-4 records include 64-byte source labels; the PC records
use a compact layout without those labels. Matching topology and names across
these releases is useful evidence for the ordinary head, but does not prove
identical PC runtime behavior.

## How the data is likely used

Each primary group is a sparse vertex displacement target. At a conceptual
level a target weight `w` contributes `w * delta` to each affected base vertex;
multiple active targets add their contributions. This agrees with the
Anniversary static analysis and standard morph-target representation.

The six PC channel rows contain an entry count, two float words, and an ordered
list of group references. PC channel 0/1 currently reference the upper/lower
eyelid targets and have weight `1.0`, progression `0.0` on the ordinary head.
Channel 2 references group 10; channels 3 and 4 reference group 0; channel 5
references group 29. Some referenced groups are empty in the PC primary table,
so these rows cannot be interpreted as a complete list of named expressions.
The rows may be runtime channel slots or interpolation/control data, rather
than six independent morphs.

The decoded channel headers are:

| Head / channel | Entry count | Weight | Progression | First group reference |
| --- | ---: | ---: | ---: | ---: |
| CE00A4B0 / 0 | 2 | 1.0 | 0.0 | 1 |
| CE00A4B0 / 1 | 2 | 1.0 | 0.0 | 2 |
| CE00A4B0 / 2 | 24 | 0.0 | 0.0 | 10 |
| CE00A4B0 / 3 | 26 | 1.0 | 0.0 | 0 |
| CE00A4B0 / 4 | 25 | 1.0 | 0.0 | 0 |
| CE00A4B0 / 5 | 5 | 0.0 | 0.0 | 29 |
| A90056F0 / 0 | 2 | 0.0 | 0.0 | 1 |
| A90056F0 / 1 | 2 | 0.0 | 0.0 | 2 |
| A90056F0 / 2 | 29 | 0.0 | 0.0 | 9 |
| A90056F0 / 3 | 28 | 0.0 | 0.0 | 0 |
| A90056F0 / 4 | 50 | 0.0 | 0.0 | 0 |
| A90056F0 / 5 | 6 | 0.0 | 0.0 | 29 |

Only the first reference is shown for long rows; the rest contain repeated
references or `FFFFFFFF` holes. Preserve their original order and length when
decoding or exporting them.

The REBGE Anniversary static audit reports interpolation across ordered sparse
groups and callback-written channel weights/progression. Its Sally blink
callback advances a phase by `deltaTime * 15`, caps at approximately pi, and
uses a sine-like envelope to update upper/lower eyelid weights. This is
evidence for the Anniversary executable only; it is not proof of the 2003 PC
callback constants, phase, or timing.

## Earlier blocker: packed PC executable

The installed PC `BGE.exe` has a high-entropy `.text` section and enters
through a `.bind` loader. Ghidra's ordinary code at the callback addresses
listed in JadeView comments is not valid code for this executable. Therefore
those comments cannot establish the PC type-4 update routine. The available
static view reaches the loader but not the post-unpack morph callbacks.

JadeView currently has no type-4 decoder or morph renderer. It parses modifier
boundaries to preserve the resource structure, then stores the type and raw
payload in `SceneModifier`.

## Earlier archive-only conclusion

It is now established that the PC archive contains sparse face morph targets,
including strong upper/lower eyelid candidates, and that the ordinary-head
topology closely corresponds to named Anniversary targets. The data layout
and likely additive weighted deformation model are understood well enough to
prototype an offline preview, provided the uncertain group slots and PC/HD
differences remain visible.

The original PC playback curve, exact interpretation of the six channel
records, and callback timing remain unproven. Resolving those requires a
recoverable unpacked PC image/PDB or an authorized runtime memory observation.
No such evidence was available in this pass.

## Evidence files

- JadeView PC parsing: `src/Features/AssetBrowser/Formats/Pc/PcTextureLoader.cs`
  (`ModifierSize`, type 4) and `src/Features/SceneEditor/Formats/Pc/PcSceneLoader.cs`.
- REBGE Anniversary layout: `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/facial-morph-native-layout-20261012.md`.
- REBGE Anniversary blink and visage callbacks:
  `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/facial-blink-clip-20261012.md`.
- Sally ordinary and HD preview targets:
  `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/preview/sally-neutral-blink-20261012.glb` and
  `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/preview/sally-hd-neutral-blink-20261012.glb`.

The latter reports and preview files are in the separate REBGE checkout and
are not committed with JadeView.
