# Gameplay script system findings

This document preserves archive/source findings that belong to future general
gameplay, world-state, actor, effects, and dialogue features. The evidence comes
from the installed PC `sally_clean.bf`, the archive reports under
`artifacts/export-verification/`, and reconstructed IrisEngine/Jaded source.
Source reconstructions explain a mechanism but do not by themselves prove that
the retail PC build executes the same branch.

## Confirmed system facts

### All-world brain-owner census

The new `--archive-gameplay-script-index` pass indexes every GAO brain owner in
every parsed world instead of filtering to world names containing `cine`. On
the installed `sally_clean.bf`, the report found 15,950 owners across 91
containers and 196 distinct world keys (988 container/world labels). The
existing cinematic-only subset is 739 owners; the remaining 15,211 are in
other worlds. All owners resolve to a brain and script model; 15,935 have at
least one callback key matched to the recovered IrisEngine registry. The 15
unmapped owners cluster in eight models whose function references are absent
from that registry, not because their brain/model resource was missing.

The OVA merge decoded 15,949 owners; one owner (`WP_ailogo.gao`, `FF00631B`)
has an unequal-layout override for `v_LanguePos` and remains explicitly
unresolved. The all-world report stores compact owner rows and omits the full
per-owner OVA payload to keep the generated JSON manageable. It is a structural
index, not a callback execution trace.

Most revealingly, the shared `Cine_Events` model (`350091C3`) is not limited to
worlds named as cinematics. In the ordinary world
`01_02_entrepot_salle_fusibles` (`FF006BFC`), 32 owners use this model and all
32 have a `SceneEvents` resource; examples include cable, fuse-switch, and fan
interaction objects. This is strong evidence that the same event-track
coordinator runs gameplay interactions as well as cutscene tracks. JadeView
should treat `SceneEvents` as a general scripted-sequence facility and derive
its context from the owner, not from a cinematic-world label.

The high-frequency `Detect_o_4SpecialFlags` model (`7100E398`, 642 owners) is
another cross-world primitive. Its recovered callback checks up to four
configured saved flags (including inverted predicates), then can set/clear
result flags and change an object capability; one mode polls at an interval,
another registers with a shared dialogue/progress manager. Its owners occur
mostly in shared `_basic_dialogues_*` worlds, but some also appear in regular
level worlds. This is a general condition/gating system, not merely a subtitle
or cutscene feature.

Regenerate the local compact report with:

```powershell
dotnet run --project tests/ExportVerification -- --archive-gameplay-script-index "<path-to-sally_clean.bf>" "<repository-root>"
```

It writes `artifacts/export-verification/archive-gameplay-script-index.json`
(ignored by Git). The cinematic-only command remains available and retains its
full OVA snapshots for its smaller owner set.

### Global saved flags link gameplay and cinematic owners

The shared `Cine_Events` model (`350091C3`) computes its start-test bit mask at
runtime from OVA references and special-flag fields. A zero serialized
`i_tests_a_reussir` value is therefore not evidence that a coordinator starts
unconditionally. The `specialFlag_test1` and `specialFlag_test2` fields have
opposite default polarity; encoded values >=10000 invert the required state.
See [`cinematic-system-analysis.md`](cinematic-system-analysis.md) for the
callback-level state-machine details.

In `FF0045A2` (`00_03_dehors_maison_Intro_cine`):

- `CINE_BIGINTRO_EVENTS.gao` associates saved flag `0x0A5A` on completion.
- `CINE_BIGINTRO_INIT.gao` tests `0x0A5A` as set and `0x0A5C` as clear.
- IrisEngine's `Monstroplante_mon_init` reads the same flag bits 2650
  (`0x0A5A`) and 2652 (`0x0A5C`). Bit 2650 gates startup; bit 2652 selects the
  generation/fight phase.
- The same archive container has `MontroPrison2.gao`, whose brain uses the
  Monstroplante base script model and references `PNJ_Monstroplante.gao`.

This establishes a real bridge between a cinematic completion flag and a
gameplay actor's state machine. It is a strong candidate explanation for
conditional enemy/effect appearances that JadeView currently misses, including
the previously reported underground enemy appearance. Exact appearance timing
and runtime branch execution still require playback comparison or a trace.

The IrisEngine callback `Monstroplante_mon_init` reads saved flag 2650
(`0x0A5A`) before enabling its startup path, and later reads 2652 (`0x0A5C`)
to choose the generation/fight phase. The `MontroPrison2.gao` owner in this
container is bound to the Monstroplante script model and references
`PNJ_Monstroplante.gao`. This makes the flag bridge concrete at the configured
archive/source level: cinematic state and a gameplay actor brain use the same
global state. It does not establish the exact retail frame of an appearance or
prove which branch ran during a particular playthrough.

### Gameplay interactables and reaction state

IrisEngine's recovered `action_reaction_ar_action` callback illustrates a
second generic feature family. Its configured owner can check distance and a
collision predicate against an activator, request a contextual Sally action
through the board manager, and react after a configured kick count. At that
threshold it may set saved flags and disable collision capability; the precise
result depends on the owner's decoded variables. This is evidence that future
scene editing/preview will need object capabilities, collision predicates,
actor-action requests, and global-state changes—not just transforms and
visibility. This callback is an example mechanism, not a claim that all
interactables use this model.

Jaded's recovered engine implementation confirms the low-level meaning of the
operations: `OBJ_CapaSet` clears the requested capability bits and then sets
the requested bits; `COL_ColSetActivationSet` applies the same clear-then-set
pattern to a collision instance's activation mask; and
`COL_ZDE_ZDECollide` tests two specified ZDE indices. This makes capabilities
and collision activation distinct, inspectable state from render visibility.
Jaded is an earlier engine source tree, so this corroborates the API mechanics
but is not by itself proof that every retail BGE script follows the same path.

### Spatial trigger zones are stateful script owners

The recovered `Detect_o_Zone` brain is a concrete generic trigger mechanism.
Its initialization selects either a bounding-volume overlap test or a
ZDE-to-pivot collision test based on an object flag, and can bind the test to
an explicit actor (or use the main actor when no explicit actor is configured).
On enter it changes an object capability and can set, clear, or invert
configured saved flags. On exit it reverses some of those effects; an optional
timer later changes the flags again. This means a trigger is not just a static
collider: it has enter/exit edges, selected collision semantics, optional actor
filters, per-owner state, and timed follow-up actions.

The cinematic script index also finds `Detect_o_Zone` owners in cinematic
archives, while separate recovered `Zone_ChangeStatus` and
`Zone_OccupationTerrain` callbacks operate on actor/status state. This supports
a broad gameplay-tools roadmap: inspect trigger bounds and collision sets,
show configured participants and state transitions, and eventually simulate
them with a source-time/event queue. It does not justify executing arbitrary
game scripts in the editor.

### Script-owned event tracks exist outside `SceneEvents`

The same `FF0045A2` container contains `Cine_MonstroPlante_0003_intro.gao` with
ordinary `Events=BF00229A` and no `SceneEvents` reference. The clip has 58
tracks, a 2-second duration, and target-zero tracks marked running-init and
auto-loop. The owner brain is bound to the Monstroplante death script model;
its OVA references explosion and particle objects and contains timing/position
arrays. Reconstructed callbacks include before/after-display, loop, init, and
wait-for-death. This demonstrates that a cutscene world can include AI-owned
event playback and conditional procedural effects beyond the selected visible
cinematic track. Target-zero track ownership still needs event-by-event
validation; do not assign every zero target to this owner without decoding its
payload semantics.

### Dialogue, sound, and facial motion are separate coordinated paths

The shared `Cine_Textes` script consumes message 30 to select a dialogue row
and submit text/sound parameters to the subtitle/audio manager. A separate
facial handler consumes message 53 to drive mouth-form channels. Audio playback
alone cannot reconstruct who speaks or the face's source-clock timing. A future
dialogue feature needs speaker, message, sound, subtitle, and facial signal
links as one inspectable event graph.

## Future feature work to preserve

These are system-level features, not scene-specific fixes:

1. **Gameplay script/trigger preview:** evaluate a bounded subset of recovered
   brain callbacks and event conditions, beginning with saved-flag tests/writes,
   message gates, and collision predicates. Keep unresolved conditions visible.
2. **Conditional actors and spawning:** use flag/state transitions and object
   references to activate actors, props, and child effects at the correct time.
   Do not populate every object present in the archive as visible from frame 0.
3. **General event-track inspection/playback:** expose ordinary `Events` as
   well as `SceneEvents`; retain the owning GAO, callback model, track lifecycle
   flags, target resolution context, and up to five concurrent AI tracks per
   script instance.
4. **World-state preview:** model cross-owner state such as saved flags, actor
   capabilities, visibility/activity, and camera/input locks with deterministic
   seek/reset. These values affect gameplay scenes and exports as well as
   cinematics.
5. **Dialogue/lipsync timeline:** join message 30 dialogue rows and message 53
   face signals by actor and source time; preserve sound and subtitle metadata
   in exports.
6. **Scripted effects and materials:** resolve effect owners, parameters,
   alpha/visibility changes, and global lighting state through the same event
   graph rather than hardcoding a named scene or asset.
7. **Gameplay interactables and reactions:** discover owners by script model and
   callback signature; evaluate configured collision/distance gates, actor
   requests, capability changes, and saved-flag writes as reversible state
   transitions. Show render visibility, object capabilities, and collision
   activation as separate state channels.
8. **Spatial triggers:** inspect BV/ZDE predicates, actor filters, enter/exit
   state, timers, and their saved-flag/capability side effects.
9. **World-state inspection:** expose saved global flags and object capability /
   activation state with provenance (which owner/callback/event changed each
   value), so users can understand why an actor or effect is present. Distinguish
   existing-but-inactive objects from removed generated objects.

## Evidence limits and next audit

The current script index joins owners to model callbacks and merged OVA values,
but it does not execute callbacks or prove their runtime order. The local
`Cine_Events` flag join found compatible same-container writers for 49 of 194
special-flag predicates; other script models and event payloads can account for
additional readers/writers. The next audit should scan all recovered IrisEngine
callbacks for saved-flag reads/writes, map their model keys to owners in each
world, then join those owners to both `Events` and `SceneEvents` references.
Report ambiguous or external writers instead of inferring an order from names.
The first non-cinematic source examples above confirm the value of that wider
scan, but they are not yet an archive-wide gameplay-script census. No generic
callback evaluator or user-facing gameplay-script preview has been implemented.
A source symbol scan finds `ARRBitSet`/`ARRBitTest` calls in 73 recovered
`ai_compiledscripts_*.c` files, so global-flag behavior is distributed across
many script families rather than isolated to the cinematic coordinator. This
is a source-file count, not a count of unique flags, owners, or executed paths.

### Recovered script API footprint

A static symbol scan across 143 IrisEngine `ai_compiledscripts_*.c` files found
the following broad source footprint:

| Mechanism signature | Files containing a matching call |
| --- | ---: |
| Saved-flag `ARRBitSet` / `ARRBitTest` / clear / change | 73 |
| `AITrackCurChange` / `AITrackChange` state transitions | 74 |
| `OBJDestroy` lifecycle changes | 77 |
| `OBJ_CapaSet` capability changes | 58 |
| Collision activation changes | 31 |
| Collision predicate calls | 32 |
| Object active/inactive/identity flag operations | 46 |

These counts measure source files matching signatures, not unique script models,
owners, or executed paths. They show that a useful preview should prioritize a
small state-transition vocabulary shared by many scripts—track switching,
object lifecycle, capabilities, collision predicates/activation, and global
flags—before attempting full behavior emulation. Keep these categories
independent: destroying an object, disabling its capability, disabling a
collision set, and hiding its geometry are not interchangeable operations.

Jaded's AI instance layout explains why track switching must preserve
concurrency: each script instance has five independent AI tracks, each storing
its own current function, instruction node, and wait time. The source comments
that one function per track can execute in a frame. A preview that flattens
these into one current callback can lose side effects or leave actors/effects
stuck in their initial state. This is a strong engine-level model for future
script evaluation, though retail scheduling should still be checked against
runtime captures.

The same Jaded `OBJDestroy` implementation distinguishes ordinary objects
from generated objects. For non-generated objects it force-marks the object
invisible and inactive and requests world visibility/activation refresh; a
generated object can instead be detached from world, sound, collision, and
engine registries and removed. Therefore an editor's generic “destroyed” state
may need to preserve a loaded-but-inactive record versus actual spawned-object
removal, rather than simply deleting a row or toggling render visibility.

Useful source/report locations:

- [`ai_compiledscripts_global_managers.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c)
- [`ai_compiledscripts_monstroplante.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_monstroplante.c)
- [`ai_compiledscripts_action_reaction.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_action_reaction.c)
- [`ai_compiledscripts_detectors.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_detectors.c)
- [`ai_compiledscripts_global.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_global.c)
- [`ai_compiledscripts_detectors.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_detectors.c)
- [`ai_arrfunctions.c`](../external/irisEng/ai/ai_arrfunctions.c)
- [`AIfunctions_obj.c`](../external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_obj.c)
- [`AIfunctions_col.c`](../external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_col.c)
- [`AIfunctions_ai.c`](../external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_ai.c)
- [`AIstruct.h`](../external/jaded/src/libraries/AIinterp/Sources/AIstruct.h)
- [`archive-cinematic-script-index.json`](../artifacts/export-verification/archive-cinematic-script-index.json)
- [`archive-cinematic-body-coverage.json`](../artifacts/export-verification/archive-cinematic-body-coverage.json)
