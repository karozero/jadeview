# Cinematic system: current reconstruction

Updated 2026-10-20 from the PC `sally_clean.bf` archive census, JadeView's current
preview code, IrisEngine sources, and the Jaded Jade Engine source tree.

For reusable findings that apply to gameplay scripts and world state beyond
cutscenes, see [`gameplay-script-system-findings.md`](gameplay-script-system-findings.md).

## What a cutscene is made of

A cutscene is not one self-contained movie record. Its visible result is assembled
from several data and runtime layers:

1. **Scene and asset containers** provide worlds, GAOs, actor rigs, geometry,
   materials, textures, animation/action resources, and script resources. A
   cinematic world can depend on the regular gameplay world and shared resources.
2. **Animation/event tracks** contain timed transforms and opaque event payloads.
   A track may target a GAO, an actor root, or a script-owned target. Track data
   supplies timing; it does not necessarily contain the final action or effect.
3. **Actor brains and script models** connect an object to compiled functions,
   default variables, and instance variables. These callbacks can choose actions,
   change visibility/material channels, create effects, alter global state, or
   communicate with other actors.
4. **Engine managers** evaluate event tracks, animation/action blends, sound,
   lighting, and GFX. The final image/audio is the combined result of these
   managers and the current game state.

This explains why a character can be present in the outliner but remain in T-pose,
why a prop can exist but fail to break, and why a sky/effect can be visible at the
wrong time: object presence and clip playback do not execute the actor's whole
runtime brain.

## Root timeline tracks versus object event tracks

`Events` and `SceneEvents` are separate links on a GAO. The cinematic picker must
not promote every decoded `i` resource loaded under a cinematic world into a
playable scene: actor/prop `Events` are local event tracks, and dependency loading
can make many such short tracks appear alongside the actual sequence. For
`FF0045A2` (`00_03_dehors_maison_Intro_cine`), the previous broad rule exposed 155
clips; discovering root clips only through cinematic-world `SceneEvents` links
exposes 13, including the main `3500B8EA` sequence and its related script-owned
tracks. Object `Events` remain available to actor/event inspection and do not
become top-level cinematic playback choices. A few short `SceneEvents` are real
setup or effect tracks, so short duration alone is not used as a filter. This is
serialized-link classification; it does not claim that every retained script
track is active in the game at a given time.

The same distinction was checked against five retail containers: root
`SceneEvents` counts were 13 for `FF0045A2` (intro), 10 for `FF005833`
(decryptage), 21 for `FF006B96` (central elevator), 4 for `FF00791B` (sas
entry), and 13 for `FF001FBE` (military release). The script inspector still
decoded 15 ordinary `Events` links in the intro, including the Monstroplante's
two-second `BF00229A` track under its own owner. To repeat the inventory on
another container, run
`dotnet run --project tests/ExportVerification -- --cinematic-timeline-inventory <sally_clean.bf> <FF-container-key>`.

The fallback that populates an actor's motion list follows serialized links too:
the actor or one of its components must own the `Events` link, or the clip must
be a root cinematic `SceneEvents` track. It no longer scans every decoded `i`
record and accepts any short resource whose track happens to target that GAO.
This prevents unrelated effect/PAG resources from appearing as actor motions;
the exact `Part_Fight.gao` activation and particle phase still depend on its
script callback and are analyzed separately. The source-link unit check covers
owner `Events`, root `SceneEvents`, paired gameplay-world links, and rejection
of an unlinked record.

Actor clip discovery combines two legitimate sources: root `SceneEvents` in the
cinematic world and member `Events` linked by that actor's root/bone owners. The
former scan over every cinematic-world `i` record could attach unrelated short
tracks to a rig merely because their target key happened to overlap. The loader
now joins `i` records to those serialized owner links before adding actor clips;
actor-owned `Events` are resolved in their own source world so a paired gameplay
world is not discarded. In the
`3500B8EA` script graph, `Part_Fight.gao` is referenced by the
`SFX_colpag_cam_scp_loop` callback through its `des_o_pag1` / `des_o_pag2`
instance fields. That callback toggles the objects' visibility, changes PAG
generator frequency, and positions them using camera horizon/banking data. This
is evidence of callback-controlled effect objects, not a standalone cutscene or
proof that their local tracks should appear on every actor. The retail
`FF0045A2` actor-clip audit compared the former broad
scan with owner-linked candidates: 44 actor/clip assignments matched, all 44
were retained by root `SceneEvents` or actor-member `Events`, and no extra
unlinked clip was attached to an actor. The independent root timeline inventory
contains 13 sequences, rather than the 155 cinematic-world `i` records exposed
by the former global scan. Short root setup/event tracks remain valid and are
not filtered by duration. This audit does not assert that every retained track
actually ran in-game.

A fresh archive inventory confirms the intro still resolves to 13 root
`SceneEvents` and 15 ordinary object `Events`; none of the 13 root choices is
`Part_Fight` or `Part_Fight1`, and neither effect GAO owns an ordinary event
track in this container. The many short `i` records from the old broad scan
were dependency resources, not dozens of new cinematic scenes. If a build
shows that larger list, it is using the pre-filter discovery path or an object
event inspector rather than the root cutscene picker. The source-links fixture
guards this distinction.

The `SFX_colpag_cam_scp_loop` behavior is also previewed by a dedicated adapter
in JadeView. It follows the callback's OVA object references, then derives
visibility and particle rates from the cinematic capability timeline. The
engine callback resolves the gameplay-global `_PJ_Camera.gao` (`71003F56`),
but a cutscene preview can have a separate routed cinematic camera. JadeView
therefore applies camera-relative placement using the currently selected
cinematic camera's evaluated render pose, including routed position and target
changes; PAG carriers do not require the global gameplay-camera GAO to be
present in the assembled cinematic world. Its mode changes are deterministic:
the source callback uses a random 1–4 second delay, while preview uses the
2.5-second mean so seeking gives repeatable results. Mapping the gameplay-global
camera dependency to the routed cinematic preview camera is an approximation;
native runtime camera selection may differ when those cameras are not the same.
This does not execute the
game's EOT callback scheduler or PRNG and does not prove that the callback is
active in every scene containing its owner. Treat it as a tested approximation
for this callback, not as a general script interpreter.

## Evidence from the retail PC archive

The generated `artifacts/export-verification/archive-cinematic-census.json`
scanned 91 `FFxxxxxx` containers, including shared container `FF0084DF` and 90
other cinematic/scene containers. It found 284,113 parsed records and 4,365
animation records. JadeView decoded 4,355; 10 did not decode. Selector `8056`
appears 4,183 times across 56 containers.

These are counts of parsed occurrences, not counts of executed events. The census
does not run scripts, establish that an event reached its target, resolve every
cross-container dependency, or claim that all `FF` containers are cutscenes.
Selector frequency is a lead for broad coverage work, not proof of semantics.

The new research command
`--archive-cinematic-script-index <archive> <repository-root>` also scans the
archive's `*_cine` worlds and resolves root GAO → `q` brain → `s` script model
references, joining model function keys to IrisEngine's
`ai_compiledscripts.c` registry. The current pass found 38 cinematic containers,
739 brain owners and 116 distinct script models. All 739 script-model keys now
resolve for analysis: 671 locally, plus 68 through byte-identical copies found
in other cinematic containers. Of the resolved owners, 737 have at least one
function key in the Iris registry; two models have no mapped function key. This
demonstrates that the BF repeats shared script definitions across scene
containers. A cross-container match is safe for analyzing the model bytes,
but does not prove that the engine resolves its external defaults or runtime
instance data from another container.

The clearest cross-scene result is the `Cine_Events` script model `350091C3`:
176 owners in 35 cinematic containers. Its Iris callback map includes
`cine_tests_depart`, `cine_play_eventtrack`, `exec_GoToEvents`,
`exec_InitialiseCine`, and `exec_EndOfEvents`. The source shows that the manager
gates playback on special flags and scene conditions, initializes global state,
starts event tracks, tracks a cinematic timecode, and restores flags/state when
tracks stop. This is a shared orchestration layer, not a scene-specific effect.
It is a strong explanation for why previewing an isolated cinematic clip cannot
reconstruct all state transitions from the clip alone.

The shared `Cine_Textes` model `350091B0` appears on 26 owners in 25
containers, with callbacks for initialization and message reception. Dialogue
is therefore actor-script-driven across the archive; audio cues alone do not
describe all dialogue playback or facial triggers. An explosion SFX model
(`9E0031DA`) is reused by 28 owners in eight containers, showing another
cross-scene family of procedural effects.

IrisEngine's `Cine_Textes_CiTxt_reception_messages` consumes queued message 30,
chooses a dialogue row by priority and `Int1`, then reads aligned arrays for
subtitle text, sound ID, volume, near/far distance, and speaker location. It
passes that state to the shared `GST_AffichageTexte` manager. The separate
facial handler consumes message 53 and updates mouth-form channels. Thus the
source supports two coordinated but distinct paths: message 30 selects the
dialogue/audio row; message 53 drives the face. A correct lip-sync preview must
join them by actor and timeline rather than infer mouth motion from audio
amplitude. The runtime relationship and exact retail event delivery still need
to be confirmed from the BGE archive data.

The index is generated at
`artifacts/export-verification/archive-cinematic-script-index.json`. It reports
callback keys and owner/container counts; Iris labels are cross-version source
matches and do not establish callback execution. The exact retail activation
conditions still need to be read from each owner instance/default data and
validated against the PC archive.

## Event-track execution flags

The archive census now also tallies raw track/key control bits across parsed
animation records. For the 91 `FF` containers it counted 17,647 track bits
matching Jaded's `AutoLoop` mask, 14,987 `RunningInit`, 10,786 `AutoStop`, and
19,721 `DoOnce` event bits. The 38 containers containing `*_cine` worlds account
for 9,640, 8,153, 8,325, and 16,098 of those respective counts. These are
occurrences in serialized tracks, not counts of playback outcomes.

The control-flow markers that most directly block or redirect later events are
much rarer in this sample: six wait flags (`WaitFalse`/`WaitTrue`) and five
`SetTimeToLabel` markers in all 91 containers; no `GotoLabel` markers were
counted. Wait markers occur in three containers with cinematic worlds, and the
label markers occur in two others. This makes “the preview should run a general conditional VM”
an incomplete explanation for the archive-wide mismatches. Track lifecycle
(`RunningInit`, loop, stop, and one-shot state) is a broader candidate for
repeated or prematurely persistent behavior. The nested `SceneEvents` transform
preview now applies Jaded's `AutoLoop` and `AutoStop` flags to its shared
playback clock. `RunningInit` channels are also seeded at clip time zero for
owners present in the assembled world, matching Jaded's `EVE_ReinitData`
initialization. This still does not simulate per-event `DoOnce` behavior,
object-processing state, or callback gates.

The per-track sample narrows that hypothesis: among 1,661 flagged tracks whose
targets are named `_PJ_*`, all 1,661 carry the `AutoStop` bit and none carry
`AutoLoop`. Thus the recurring body motion reported in character scenes is not
explained by the serialized loop bit on those actor tracks. A better next lead
is the actor's action library and the brain callbacks that select/restart an
action; loop-enabled tracks are more likely to explain persistent camera,
environment, or effect animation. The per-container report now includes the
target, key count, duration, and Jaded-labeled lifecycle bits for tracks in
`*_cine` worlds, so these can be checked without guessing from a screenshot.

These labels come from the later Jaded definitions in
[`EVEconst.h`](../external/jaded/src/libraries/AIinterp/Sources/Events/EVEconst.h)
and event processing in
[`EVEplay.c`](../external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c).
Jaded shows a per-track current-event/time state, event-once state, event waits,
label jumps, and auto-loop/auto-stop behavior. JadeView currently samples
recognized source keys by clip time; it does not emulate that per-track state
machine or the callback's AI context. IrisEngine confirms the BGE-specific
script layers but does not provide an equivalent retail event-player source
here, so the bit interpretations remain a cross-version hypothesis until
verified against PC runtime behavior. The census now writes
`Totals.EventControlFlags` and an explicit provenance limit in
`archive-cinematic-census.json`.

The next systemic implementation step should validate track-loop semantics on
an effect/camera track and actor action-library scheduling on an actor before
changing preview behavior. Compare serialized flags/actions, the current
JadeView sample, and user-observed playback at the relevant transition. Wait
and label emulation should remain secondary unless new archive evidence or
runtime traces show those branches are materially responsible.

## What the source trees add

- **IrisEngine** exposes BGE-specific compiled-script reconstructions and the
  mapping from function keys to callback names in
  [`ai_compiledscripts.c`](../external/irisEng/ai/ai_compiledscripts.c). Its
  shared cinematic coordinator is in
  [`ai_compiledscripts_global_managers.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c),
  with the `Cine_Events` instance layout in
  [`ai_compiledscripts.h`](../external/irisEng/ai/ai_compiledscripts.h). Its
  event-track functions include play/stop/pause/reinitialize operations, while
  [`ai_compiledscripts_lipsync_auto.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_lipsync_auto.c)
  shows a script switching AI tracks and consuming message 53 to drive facial
  channels. The SFX callbacks also show that a script may own lifecycle,
  visibility, channel progress, alpha, audio, and global lighting together.
- **Jaded** provides a later Jade Engine implementation. Its
  [`EVEplay.c`](../external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c)
  and [`EVEstruct.h`](../external/jaded/src/libraries/AIinterp/Sources/Events/EVEstruct.h)
  are useful for understanding event-track playback and event dispatch. Its
  [`GFX.h`](../external/jaded/src/libraries/GraphicDK/Sources/GFX/GFX.h)
  defines 23 GFX families, including lightning, smoke, water, fade, diaphragm,
  sun, and ripple. That is a roadmap of distinct engine systems, not evidence
  that every family appears in a given BGE scene or uses identical retail-PC
  parameters.
- **IrisTool** is useful for independently inspecting assets, but its own scope
  excludes cinematic/script playback. It cannot supply the missing runtime
  semantics.

IrisEngine's game-specific source and the retail archive are the closest evidence
for BGE behavior. Jaded is a later engine snapshot and should be used to explain
architecture or generate hypotheses, then checked against PC archive data.

## Current JadeView coverage

The preview already has separate readers/evaluators for actor action libraries,
body actions, facial events, camera routing, audio cues, environment palettes,
screen fade, shutter, flash, lightning, shockwave, and some object-state events.
Scripted handlers are discovered by their serialized function references and
variable data, then evaluated on the preview timeline. This is the right pattern
for behavior that can be deterministically reconstructed.

The remaining gap is not simply “more animation files.” JadeView does not run the
game's general AI/script VM. Consequently it cannot yet reproduce arbitrary
conditional script branches, gameplay-to-cinematic handoffs, actor track
scheduling, spawned/dependent actors, all visibility/material callbacks, or all
23 Jaded GFX families. A guessed idle/action can hide T-pose but can also give a
wrong result; an unknown callback must be diagnosed rather than silently mapped
to a character-specific fallback.

### Body-action archive audit (2026-10-23)

`--archive-cinematic-body-coverage <archive>` compares cinematic actor/clip
bindings with decoded body-action requests and serialized action kits. The
latest installed-PC archive run scanned all 91 FF containers, found 38
cinematic worlds and 1,232 clips, and observed 441 skeletal actor/clip
bindings with 1,429 resolved body-action segments. 198 bindings also contain
direct bone tracks and 423 contain root tracks. Of the 145 bindings with no
resolved body-action segment, 117 have root-transform data and 28 have direct
bone tracks. Ten bindings contain at least one unresolved action request; seven
of these still resolve other body actions. These paths overlap: an action kit
can animate bones while the scene clip supplies actor placement.

An earlier audit used the legacy global action-kit map rather than the
world-scoped resolver used by playback. This undercounted body-action segments
(1,281 instead of 1,429) and reported five unresolved bindings instead of ten.
The current report uses the playback resolver and shows merge providers and the
number of populated slots in requested ranges. For `FF001FBE / 35009F74`,
_PJ_Militaire requests action 201 at 0 and 7.817 s, but its serialized base kit
has 100 slots and contains no slot 201; the clip has no successful kit merge
for that action. This explains why the static preview has no skeletal body
action there. It does not prove what action the game's live brain selected or
whether runtime state supplies another source. Other unresolved examples
include _PJ_Militaire in `3500A4C7`, `Cine_Boss` in `3500BA47`, PNJ_Faucheur in
`3500B047`, and Peyj in `BF0024B6`.

This matches the engine source: IrisEngine's `ACT_ul_ActionKitCallback` reads
action-kit slots and resolves referenced action records, while
`Merge_ActionKit_mak_init` calls the range-based `ACTKitMergeNoOver`; the
cinematic selectors `0x3398`/`0x3399` represent that merge family. It also
explains why a single cutscene clip cannot always be interpreted in isolation:
the missing Militaire calls occur in clips without an in-clip kit merge, while
other clips in the same container do merge the high action range. Whether an
earlier setup clip, actor brain, or another runtime path establishes the needed
kit state is not yet proven. Do not fix this by globally copying all high slots
or by actor-name fallback; next trace clip launch/setup and brain callbacks for
the affected actors, then verify the same state flow across unrelated scenes.

The audit also exposed and corrected a false diagnostic: opaque event payloads
were counted as body actions even when they were unrelated selectors. JadeView
now reports “no skeletal action resolved” only when it decoded a recognized
body-action request for that actor. The synthetic checks cover both an actual
missing body action and an unrelated opaque event.

The audit now groups serialized coverage mechanisms separately: 163 bindings
have both resolved action segments and direct bone tracks, 110 use resolved
action kits only, 111 are root-transform-only, 22 have direct bone tracks but
no action segment, and five contain unresolved body-action requests. These are
evidence categories, not inferred causes. In `BF003BD1`, for example, Peyj has
a root transform track but no decoded action request or direct bone track;
JadeView cannot derive his pose from this clip alone. His T-pose therefore
needs the actor brain/gameplay handoff path traced, rather than an arbitrary
idle fallback.

### Event tracks can be shared by multiple owners

The archive index found 30 of the 1,232 serialized cinematic event clips
referenced by more than one GAO in the same cinematic world. Every one of these
30 clips has at least one track whose target is `00000000`. The candidate owner
references are serialized in `archive-cinematic-body-coverage.json`; they
distinguish ordinary `Events` references from `SceneEvents` references. All 30
ambiguous cases in this scan are ordinary `Events` references.

After expanding body-action decoding to every owner context, a fresh scan of all
91 archive containers still finds no recognized body-action requests in those
30 shared clips. The change therefore provides correct target-zero routing for
the format without claiming it fixes the known T-pose cases, which need a
different gameplay/brain path.

This is not evidence that the clip should be copied into separate files. The
later Jaded source shows that event data is attached to a game object, while
runtime track state is allocated per object (`EVE_ResolveRefs` allocates a
`ListParam` for that object's event data). `EVT_PlayAll` receives a specific
game object and starts that object's event tracks. IrisEngine's BGE
`Cine_Events_Cine_exec_GoToEvents` likewise starts tracks on
`AI_gpst_CurrentGameObject`, which is the script owner. These references can
therefore share serialized track content while still running in separate
owner contexts and with separate playback state.

The initial playback implementation stored one cinematic clip per event key
and assigned its `CinematicOwnerKey` from the first matching record. That lost
the other owner contexts. An event payload with an explicit target is
independent of this fallback, but a payload that inherits target zero can be
attributed to the wrong GAO or omitted for the other owners. The audit records
inherited-target calls, not just track targets.
Among the 30 shared clips, one has a decoded inherited-target call: clip
`5E0065C6` in `00_03_dehors_maison_Intro_cine`, referenced by seven owner
objects. Its command is `OBJ_CapaSet` (`0x1FC5`), confirmed by the IrisEngine
function table; its arguments are add-mask `0` and remove-mask `3`. IrisEngine
and Jaded both apply `(capabilities & ~remove) | add`, so this command clears
bits 0 and 1 on each of the seven owners. The exact BGE meaning of those bits
is not established here. The remaining 29 shared clips
have no payload matching the current word-pair call decoder, so their
zero-target track header alone is not evidence of an owner-context bug.

JadeView now retains all serialized event owners on the shared clip instead of
only the first. Target-zero body actions, camera messages, facial events, and
decoded literal calls resolve against each owner's context; material and
screen-fade timelines use the same context resolution. The capability preview
applies `OBJ_CapaSet` using each target's initial mask and the verified
add/remove argument order. Seeking backward resets to those initial masks;
clearing the cinematic preview discards the temporary values. The serialized
source clip remains one record, while owner-addressed commands are evaluated
in their respective contexts. This previews decoded events; it does not execute
each owner's script brain or prove what the two capability bits do in retail
BGE.

## System-level next steps

### `Cine_Events` coordinator state machine (2026-10-20)

I compared the `Cine_Events` OVA layout in `ai_compiledscripts.h` with the
recovered callbacks in IrisEngine's `ai_compiledscripts_global_managers.c` and
the archive-wide owner index. This establishes a generic coordinator lifecycle
and narrows what a composed preview must model:

1. `cine_tests_depart` is the poll/eligibility stage. A bit mask in
   `i_tests_a_reussir` enables optional predicates: saved special flags
   (`0x80`, `0x100`), two collision/overlap tests (`0x01`, `0x02`), capability
   (`0x08`), custom bits (`0x10`), actor activity (`0x04`), queued message
   (`0x20`), visibility/on-screen (`0x40`), and destruction/design state
   (`0x200`). A failed enabled predicate leaves the owner in its wait state.
   The exit/interruption track has its own corresponding checks through
   `i_tests_a_reussir_exit`.
2. Once eligible, the coordinator arbitrates against the globally active
   cinematic actor and a board-panel busy bit. Unless
   `des_i_cine_independante` is set, it records itself as the active actor;
   `des_f_tempo` can hold the transition before initialization.
3. `Cine_exec_InitialiseCine` applies global/gameplay state from
   `des_i_cine_type` and other OVA fields, including input freeze, board UI,
   camera ejection, audio prefetch, and the optional global joy lock.
   `Cine_exec_GoToEvents` then calls `TRAPlayAll` on the coordinator owner.
   Therefore the playable event timeline belongs to that owner, not to the
   cinematic container as a whole.
4. `cine_play_eventtrack` advances that owner's `f_timecode` using
   `i_enLecture`, watches the duration/stop state, and calls the end callback
   after the owner tracks stop. Completion can send gameplay/camera messages,
   set/clear saved special flags (`des_i_SPFlag_associe_vrai/faux`), advance
   phase flags, restore input/UI/capabilities, reset tracks, and finish/fade
   audio. It can destroy the coordinator or return to its init track for a
   scripted retry.

The OVA fields `i_tests_a_reussir` and `i_tests_a_reussir_exit` are runtime
state, not the serialized trigger configuration: `cine_init` clears both and
rebuilds their masks from configured references and values. For example, a
non-null `des_o_actor_zone1` enables collision test `0x01`, a non-null
`des_o_actor_capa1` enables capability test `0x08`, and nonzero
`des_i_specialFlag_test1/2` enable flag tests `0x80/0x100`. The corresponding
exit mask is built from exit-zone/status/capability/flag references. Therefore
the zero OVA defaults do not mean “no conditions.” In this census 126/176
owners set `des_i_SPFlag_associe_vrai`, 104/176 and 90/176 set the two special
flag tests, and 38/176 are marked independent; the owner fields configure
trigger and completion logic that becomes active when the script initializes.
For flag values >=10000, the callbacks invert the tested bit, allowing a
condition to require the opposite state. The two predicate fields have
opposite base polarity: `des_i_specialFlag_test1` requires the bit set for
values below 10000 and clear for encoded values >=10000; `test2` requires it
clear below 10000 and set for encoded values >=10000. Do not infer polarity
from a key value alone. Before polling, a non-Ripley owner with already
satisfied `des_i_SPFlag_associe_vrai/faux` completion state destroys itself.
This is a one-shot/replay guard, separate from its start predicate. Phase-test
fields are nonzero on only five instances and phase-advance fields are zero in
the captured OVA defaults; shared flag writes from other owners still affect
the actual sequence.

For `00_03_dehors_maison_Intro_cine` (`FF0045A2`), the three coordinators are
separate owners with separate `SceneEvents` keys. `CINE_BIGINTRO_INIT.gao` is
independent (`des_i_cine_independante=1`) and configures `des_i_ripley=2`,
`des_i_specialFlag_test1=0x0A5A`, and `des_i_specialFlag_test2=0x0A5C`; init
turns those fields into required flag predicates. `CINE_BIGINTRO_EVENTS.gao`
points `des_o_actor_detec1` to `_PJ_Sally.gao`, uses type 15, and associates
completion flag `0x0A5A`; it has no actor-zone or other optional start predicate
configured, so its start is immediately eligible once the owner initializes
(subject to global cinematic priority). `CINE_BIGINTRO_Intro_EVENTS.gao` also
points at Sally and uses type 15, but additionally requires special flag
`0x0B34` **clear** and associates that same flag on completion, making it a
clear-to-set one-shot. The same container has a separate
`CINE_INCANTATION_ARRIVEE_PEYJ_EVENTS.gao` owner that completes with flag
`0x0A5C`; `CINE_BIGINTRO_INIT.gao` requires `0x0A5A` set and `0x0A5C` clear,
so one of its conditions is directly supplied by the main Big Intro event.
`Cine_reinit_trou.gao` also requires `0x0A5A` set. Elsewhere in the same
container, `CINE_MORT_MONSTRE` completion flag `0x0ADD` is required by two
Secundo owners; `CINE_SAC_SECUNDO_2_EVENTS` then writes `0x0ADE`, required by
`CINE_FAISCEAU_ASCENSION`, which writes `0x0AE3`, required by
`CINE_ARRIVEE_ALPHA_EVENT`. This is concrete evidence of a reusable
special-flag handoff system linking script-owned event tracks.

The same flag store is read outside the cinematic coordinator. IrisEngine's
`Monstroplante_mon_init` directly tests bits 2650 and 2652: bit 2650 gates its
startup, and bit 2652 selects its generation/fight phase; the companion
`Monstroplante_socle_mp_init` also uses 2652 to control base/text visibility
and timing. Both related owners exist in the Big Intro archive container:
`MontroPrison2.gao` is bound to the latter model and references
`PNJ_Monstroplante.gao`; `Cine_MonstroPlante_0003_intro.gao` is bound to the
Monstroplante death script model and owns ordinary `Events=BF00229A` (58
tracks, all target-zero, 2-second clip), plus references to explosion and
particle GAOs. Its callbacks include before/after-display, loop, init, and
wait-for-death. Thus the world demonstrably contains script-owned event data
outside the `SceneEvents` chain and conditional gameplay actors/effects. The
source indicates these callbacks participate in the Monstroplante lifecycle;
the exact in-game timestamp and which callback activates each visible effect
still need runtime or payload-level confirmation. This is why a faithful
preview must include relevant gameplay-script effects and conditional actor
activation, not only `SceneEvents` tracks.

The local `Cine_Events`-only join found a compatible same-container writer for
49 of 194 configured special-flag predicates; 145 have no matching writer in
that subset. These are candidate edges, not a complete flag graph: other
script models and event commands can also set/clear global flags. In
particular, the `0x0A5C` producer/start relationship and the initial trigger
for the first track still require joining non-`Cine_Events` callbacks and
event payloads. The evidence proves the container is not one timeline, while
the complete ordering must be reconstructed from the wider script graph.

For JadeView, this means a correct high-level “play cinematic” operation needs
to build an owner graph from each brain's `SceneEvents` reference, keep track
state local to the owner, and evaluate supported owner predicates/flag writes
in order. It must not concatenate every `SceneEvents` clip in a container.
The archive alone still cannot determine initial trigger order where it comes
from gameplay state, another script, or runtime collision/message activity.
Those cases need an explicit unresolved-trigger diagnostic until that state is
available or can be reconstructed from the surrounding script owners.

### Archive-wide script graph baseline (2026-10-20)

The refreshed `--archive-cinematic-script-index` pass resolves all 739 brain
owners across the 91 FF containers to script models; 737 owners have at least
one callback key matched to IrisEngine's registry. The archive contains 116
distinct script models and 2,515 mapped callback keys. The shared
`Cine_Events` model (`350091C3`) is attached to 176 owners in 35 containers,
which confirms the same coordinator is reused broadly rather than authored
per cutscene. The two unmapped owners and all function keys remain in the JSON
report for follow-up.

This index is still a reference graph, not an execution trace. It identifies
script model callbacks and now decodes merged OVA values, but it does not prove
which branch ran. IrisEngine maps function keys to recovered callback names;
that reconstructed source does not establish retail-PC runtime state by itself.
The next useful join is to match each owner's object-valued OVA fields and
event references against the callback's reads/writes, so we can distinguish
configured parameters from branches that actually execute.

The index now merges model-default OVA variables with each owner's instance
overrides. It resolves all 739 brain owners, including all 176 instances of the
shared `Cine_Events` model. A model-default OVA outside the owner's world scope
is accepted only when its same-container copies are byte-identical; ambiguous
or missing defaults remain an error instead of being guessed. In the decoded
coordinator owners, `des_i_cine_type` is 7 for 86, 15 for 21, 6 for 13, 3 for
4, 2 for 2, 4 for 1, and default 0 for 49. `i_enLecture` is 1 and
`f_first_delay` is 0 for all 176. IrisEngine's recovered coordinator uses type bit 1 to send
message 47 to the main actor and unfreeze controls when the event track ends,
bit 2 to send message 60 to the camera, and bit 4 to hide the board UI during
playback. This is direct evidence that a cinematic's end is a scripted handoff
back to gameplay/camera state; it is not simply the last animation key. It also
rules out a per-coordinator `i_enLecture` multiplier as the likely source of
the user-observed faster body motion in this sample. Body action frequency,
event-track speed, and per-actor scheduling still need separate evaluation.
The index also extracts type-40 GAO references from these OVAs and resolves
them against the cinematic container's object table where possible. 172 of the
176 `Cine_Events` instances contain at least one object reference; for example,
`CINE_BIGINTRO_EVENTS.gao` points `des_o_actor_detec1` at `_PJ_Sally.gao`.
Across all owners, 1,040 object-reference fields currently resolve to named
GAOs; reserved actor aliases and references outside the loaded scene remain
unresolved and are preserved as keys rather than guessed.

This is now joined to each owner record's `Events` and `SceneEvents` fields.
The `00_03_dehors_maison_Intro_cine` example has three different
`Cine_Events` instances, all using model `350091C3`: `CINE_BIGINTRO_INIT.gao`
owns `SceneEvents=3500BE9D` with type 0; `CINE_BIGINTRO_Intro_EVENTS.gao`
owns `SceneEvents=3500DEF4` with type 15; and `CINE_BIGINTRO_EVENTS.gao`
owns `SceneEvents=3500B8EA` with type 15. The latter two bind their detection
actor to `_PJ_Sally.gao`. This is strong evidence that the scene's visible
timeline clips are separate script-owned tracks with a shared coordinator, not
one monolithic file. JadeView currently exposes these tracks as separate
playback choices and does not execute the coordinator conditions that order
and hand off between them; a true “play cutscene” mode must compose them from
the owner/script graph rather than concatenate clips by name or archive order.

### Cross-feature flag and actor-state evidence (2026-10-20)

The same `FF0045A2` archive provides a concrete connection between cinematic
completion and gameplay actor state. `CINE_BIGINTRO_EVENTS.gao` associates
special flag `0x0A5A` on completion, while `CINE_BIGINTRO_INIT.gao` tests that
flag together with `0x0A5C`. IrisEngine's recovered `Monstroplante_mon_init`
callback reads those exact saved-flag bits: bit 2650 gates startup and bit 2652
selects whether the actor enters its generation/fight phase. The container also
contains a `MontroPrison2.gao` owner bound to the Monstroplante script model and
referencing `PNJ_Monstroplante.gao`. Together these establish a configured
cross-feature path from cinematic state to gameplay AI state; they do not prove
the exact frame at which an actor/effect appears in retail gameplay.

The recovered `action_reaction_ar_action` callback shows another reusable
gameplay mechanism: an object can validate distance and collision against an
activator, request a contextual Sally action, and after a configured kick count
write saved flags or disable its collision capability. This suggests a generic
interactable/reaction preview feature should model predicates, capability
changes, global-flag writes, and actor requests as one state transition, rather
than treating the object as static scene geometry.

Further evidence, source paths, and implementation opportunities for non-cinematic
features are collected in [`gameplay-script-system-findings.md`](gameplay-script-system-findings.md).

1. Extend the flag graph beyond `Cine_Events`: find writers/readers in the
   other script models and in event payloads, join callback models to actual
   owners in each world, then resolve remaining trigger/start paths and
   conditional actor activation. Keep unknown edges explicit instead of
   filling them from clip names or archive order.
2. Keep literal EVE `OBJ_CapaSet` state deterministic per target and share it
   with gate/material consumers; callback-derived capability changes now expose
   a receiver when its source context is unique, but still need invocation time,
   callback gates, and branch state before they can safely enter playback.
3. Classify preview gaps by behavior family: actor/action scheduling, object
   activation and spawning, material/alpha, lighting/global state, audio, camera,
   and procedural GFX. Prioritize families by archive occurrence and user-visible
   failure count.
4. Add generic, data-driven evaluators one behavior family at a time. Each must
   be identified by script/function signature and decoded variables, not by scene
   or character name, and must support deterministic seek/reset as well as play.
5. Validate against multiple unrelated containers and compare representative
   states, then export the same evaluated state to Blender. Keep unsupported
   branches visible in diagnostics rather than reporting native parity.

The immediate research target is the **owner → event-track context → script
functions/variables → affected objects** graph, followed by the broader
brain/script/clip join. This is the bridge between a global preview timeline
and the per-object event state and gameplay scripts that drive actions,
conditional effects, and transitions.
