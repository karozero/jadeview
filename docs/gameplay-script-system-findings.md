# Gameplay script system findings

This document preserves archive/source findings that belong to future general
gameplay, world-state, actor, effects, and dialogue features. The evidence comes
from the installed PC `sally_clean.bf`, the archive reports under
`artifacts/export-verification/`, and reconstructed IrisEngine/Jaded source.
Source reconstructions explain a mechanism but do not by themselves prove that
the retail PC build executes the same branch.

## Confirmed system facts

### Callback state-effect candidates

The callback inspector reports source-reachable calls that may affect object
visibility, activity, capability, position, or hierarchy. It currently covers
`OBJ_FlagInvisibleSet`, `OBJ_FlagInactiveSet`, `OBJDestroy`, `OBJ_CapaSet`,
`OBJMove`, `OBJSetPos`, `OBJSetLocalPos`, `OBJReinit`,
`OBJ_HierarchySet`, and `OBJ_HierarchyReset`. The Iris implementations define
their effects: for example, `OBJ_CapaSet_C` applies
`capa = (capa & ~remove) | set`, `OBJMove_C` applies an offset (scaled by event
duration while EVE is running), and hierarchy operations update the parent
binding and derived transform.

Receiver binding is deliberately conservative. A `CURRENT` API variant or an
explicit `AI_gpst_CurrentGameObject` first argument resolves to the callback
owner; a direct pointer resolves only when it uniquely matches a source object
reference. Other operands, such as the parent passed to a hierarchy operation,
are not mistaken for the receiver. Ambiguous or branch-dependent receivers
remain unresolved. Each row retains source arguments, callback path, branch
conditions, receiver evidence, and is labelled `SourceCallCandidate`: the
inspector does not execute callbacks, infer dispatch times, or apply these
effects to playback.

This limit applies to code-reachable AI callback candidates. Separately,
literal `OBJ_HierarchySet`/`OBJ_HierarchyReset` commands serialized directly in
a cinematic or linked EVE timeline are evaluated by cinematic preview and are
sampled into GLB transforms for the active clip. That timeline path does not
execute a script callback or prove that the callback was dispatched in-game.
The world-scoped script graph audit emits the same candidate records for every
brain owner in the selected world, including ordinary gameplay worlds; the
cinematic clip audit additionally narrows owners to those reachable from that
clip. The gameplay world `FF005833 / D90057CC` (`00_02_home_decryptage`) has 33
script owners and 268 callback state-effect candidates, 171 with a uniquely
resolved receiver. This demonstrates archive coverage outside a cinematic; it
still does not establish which callbacks were dispatched in a retail run.

The current audit of installed `sally_clean.bf`, container `FF0045A2`, clip
`3500B8EA` emitted 1,926 candidate rows; 982 receiver targets resolve and 944
remain unresolved. Of the resolved receivers, 564 use a `CURRENT` API variant,
94 bind a unique source pointer, and 324 explicitly pass
`AI_gpst_CurrentGameObject`. The rows include 649 `OBJ_CapaSet` calls (472
resolved), 265 `OBJDestroy` calls (189 resolved), and 444 position/reinit/
hierarchy calls (193 resolved). Branch-dependent alternatives are not
collapsed to a single target. These are source-reachable calls, not runtime
executions: branches and callback scheduling remain unevaluated.

Iris `OBJDestroy_C` has two distinct outcomes: ordinary archive objects are
marked invisible and inactive, while generated objects may be detached from
world, sound, and collision registries and removed. The callback candidate
records this distinction but leaves the target unresolved unless source context
proves it; the separate literal EVE object-state timeline continues to handle
serialized `OBJDestroy` on its known target. Source: `external/irisEng/ai/ai_gameobjectfunctions.c`
(`AI_EvalFunc_OBJDestroy_C`, selector `0x1FB1`).
Serialized literal `OBJ_CapaSet` calls in cinematic/EVE tracks are handled by
the separate deterministic object-state timeline, which is seekable and resets
to archive capability values. This distinction prevents reachable AI code from
being mistaken for an executed timeline command. Source: `external/irisEng/ai/ai_gameobjectfunctions.c`
(`AI_EvalFunc_OBJ_CapaSet_C` / `_CURRENT`) and `external/irisEng/ai/ai_functions.c`
(selector `0x1FC5`).

The selected object's Script Inspector now also shows **Potential message
routes** across the owners in the loaded document. A route is listed only when
the callback graph has a statically resolved `MSGSend` recipient and message ID,
that recipient resolves to one script owner, and a reachable callback on that
owner contains a matching current-object queue reader. The row retains the send
callback path, source line, arguments, and branch guards. This works for ordinary
gameplay-world owners as well as cinematic owners; it is a static link, not proof
of callback dispatch, branch success, queue delivery, or consumption. Unknown or
ambiguous sends remain absent from this compact resolved-route view and should
be inspected in the source-call evidence instead. The same inspector has a
collapsed **Runtime scheduling evidence** section with the Iris world-AI and
EVE-dispatch checks plus source calls through the owner's OVA-linked objects;
the ordinary **Game activation** panel separately shows the initial-load flag
projection. These are independent layers: initial eligibility does not prove
that the object joined a scheduler table or that its callback ran.

### All-world brain-owner census

The new `--archive-gameplay-script-index` pass indexes every GAO brain owner in
every parsed world instead of filtering to world names containing `cine`. On
the installed `sally_clean.bf`, the report found 15,950 owners across 91
containers and 196 distinct world keys (988 container/world labels). The
existing cinematic-only subset is 739 owners; the remaining 15,211 are in
other worlds. All owners resolve to a brain and script model; 15,935 have at
least one callback key matched to the recovered IrisEngine registry. This
model-reference catalog is distinct from the callback keys stored in each
owner's five OVA slots: auditing those effective slots finds 16,204 nonzero
configured keys, of which 12 slots on 12 owners have no name in the bundled
Iris table. The 12 slots contain eight distinct raw keys:
`C1015F8D` (3 owners), `26004CED` (2), `620032AA` (2), `26004CD6`,
`3D001B6A`, `72006324`, `720068F7`, and `9E007752` (one owner each).
Eleven of the 12 owners are among the 15 whose model has no catalogued
function reference; the remaining key is `720068F7` on `INV_PresMateria.gao`,
whose model does have mapped references. All 15,950 owners have a structurally
decoded callback-slot block. These counts describe serialized configuration,
not callback execution.

The OVA merge decodes all 15,950 owners. Iris matches instance fields to model
fields by case-insensitive name; when array lengths differ, it copies the shorter
instance prefix into model storage and leaves the remaining model values intact.
This rule was confirmed in both `external/irisEng/ai_common.c` and Jaded's
`AIload.c`. It resolves the earlier audit failure for `WP_ailogo.gao`
(`FF00631B`): its `v_LanguePos` field has 12 model entries and 6 instance entries,
so the instance's six values replace the prefix and the six-value model tail is
retained. The all-world report stores compact owner rows and omits the full
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

The JadeView inspector now decodes this model family from callback names and
effective model/instance OVA values. Iris's `AI_EvalFunc_ARRBit4Test_C` ignores
zero slots and ANDs every configured predicate: a positive value up to 10000
requires that flag set, and a value above 10000 requires the flag
`value - 10000` clear. On a match, `D4sf_go` and `D4sf_wait` set
`SF_result_a_1`, clear `SF_result_a_0`, and enable capability `0x100`. In the
polling path, `des_i_only_vrai=0` makes a failed check reverse the result flags
and disable the capability; a nonzero value preserves the prior state. The
wait callback has no corresponding false branch. `f_every=-1` selects the
wait callback, registers the owner in `GST_Global.ao_optim_dialogue`, and sets
control flags `0x600000`; the shared global loop clears those flags on its
60-second maintenance pass. Other positive `f_every` values use the polling
callback in seconds. These are configured semantics only: live save flags,
script activation, and callback scheduling remain unavailable.

Production validation used `FF005833 / D0002E24` (`_basic_dialogues_00_home`)
from the installed archive. `WP_detect_dial_SF128.gao` overrides the model
defaults with `SF_1=2667`, `SF_2=10120`, `SF_3=10930`, result flag `128`, and
`f_every=-1`; JadeView reports the derived condition `2667 set, 120 clear,
930 clear`, the success effect, and the wait-mode registration. This is a
data-driven sample, not a hardcoded owner rule.

The related `Detect_o_SuperSpecialFlags` model (`7100C170`) generalizes this
condition to three groups of five raw flag indices, each with a parallel
required-state array. The recovered ET callback ANDs nonzero entries within
each group and accepts if any active group passes; the OU callback ORs entries
within each group and accepts only if every group passes. A zero in slot 0 is
the source-level group activation gate: ET treats that group as failing, while
OU treats it as passing, regardless of later slots. Zero entries inside an
active group are ignored by `ARRBitAnd_C` / `ARRBitOr_C`. Unlike the encoded
single/four-flag detector fields, these group entries are direct bit indices;
their paired state is zero for clear and nonzero for set. Polling callbacks
update result flags in both directions and toggle capability `0x100`; the
optimized dialogue callbacks apply the success branch only. `f_every=-1`
registers the owner in `GST_Global.ao_optim_dialogue`, while other values use
the timed test callback.

The archive-backed world audit validates this behavior on
`FF005833 / D0002E24` (`_basic_dialogues_00_home`). It finds 33 script owners,
including `WP_active_SF_4108.gao` using model `7100C170`: its ET groups require
flag 50 and flag 53 to be set, it polls at 0.5 seconds, and sets result flag
4108 on success. The inspector reports these from model OVA and callback data;
the current save state and whether the callback ran remain unknown. Source
references are `external/irisEng/ai/ai_bge/ai_compiledscripts_detectors.c`
(the five `Detect_o_SuperSpecialFlags_ssf_*` callbacks),
`external/irisEng/ai/ai_arrfunctions.c` (`ARRBitAnd_C` / `ARRBitOr_C`), and
`external/irisEng/ai/ai_compiledscripts.h` (`tScriptStruct_Detect_o_SuperSpecialFlags`).

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

The flag graph also reads literal set/clear/test commands in linked event
tracks. Iris/Jaded implement `ARRGlobalIntFlagSet` and
`ARRGlobalIntFlagClear` as writes to the global flag bitset, and
`ARRGlobalIntFlagTest` as a read that returns the bit's current Boolean value.
The event graph now treats Test as a reader, so writes from callback-derived
OVA semantics can link to event-track predicates and vice versa. In
`FF005833 / BF00218E`, the
`CINE_APRES_MDISK_EVT02.gao` event track sets flag 2166 at 44.783 seconds. In
the same world, `Cine_init_cableventilo_fixe.gao` and
`Cine_init_ecran_explik_invntr.gao` configure that flag as start gate 1 and
start gate 2 respectively, and link their own `SceneEvents` records. This
connects an event command to generic coordinator gates and their payloads;
whether those owners were scheduled in the retail run is not established by
the archive.

A world audit of `FF005833 / 00_02_home_decryptage` also found
`ES_MDisk_0_2.gao`'s `SceneEvents=C1009BF9` testing flag 2165 at time 0. The
same loaded graph links that test to the completion writer configured on
`CINE_APRES_MDISK_EVT02.gao`. This confirms that ordinary gameplay event lists
can participate in the same saved-flag graph as cinematic event tracks and
callback OVA fields; it does not establish which script scheduled the event or
the runtime order. Source confirmation is in
`external/irisEng/ai/ai_arrfunctions.c` (`ARRGlobalIntFlagTest_C` reads the
global bitset and pushes the Boolean result) and `ai_functions.c` selector
`0x2918`.

The source-backed flag scan now also covers direct Iris global-flag APIs in
functions reachable from each owner's configured callbacks. In
`FF0045A9 / 00_03_dehors_maison`, both `WP_Activate_ZONE_Attente.gao`
(`10004B56`) and `WP_Activate_ZONE_Suivi.gao` (`10004B5C`) use model
`3500A7A2`, callback `Activateur_avec_SF_Acs_init`, and instance flag
`des_i_SpFlag=2098`. For each owner the inspector finds the init read in
`Activateur_avec_SF_Acs_init` at `ai_compiledscripts_activateur_avec_sf.c:27`
and a second read in the timed loop at line 59. The loop's source condition
includes both the elapsed-time check and the flag comparison. The model OVA
sets `des_i_SpFlag_etat=1`, so JadeView can report that this source comparison
requires flag 2098 to be set; it still cannot know the current save value.
This is a generic model/callback/OVA join, not a rule for waypoint names. It
records configured source predicates; it does not prove that either callback
was scheduled or that the comparison passed.

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

The cinematic callback preview now applies `COLSetActivationSet` to the
resolved object's collision-instance activation mask when all of these inputs
are known: a unique callback receiver, a uniquely recovered callback window,
supported branch predicates, literal set/clear masks, and a resolved PC
collision instance. The inspector labels this as a cinematic script preview
and leaves the serialized source mask intact. It does not simulate collision
tests, zone overlap, or the resulting gameplay. Callback mutations whose
receiver is explicit in source remove that resolved receiver argument before
applying `OBJ_CapaSet`, `OBJDrawMaskSet`, and collision-mask set/clear
operands; implicit CURRENT calls retain their complete operand list. Map
activation is still a separate API and is not projected by this callback
path.

The `FF0045A2 / 3500B8EA` archive graph contains 83 clip-scoped script owners
and 655 serialized calls. Its reachable `PNJ_Spectre_sp_init` source includes
`COLSetActivationSet(AI_gpst_CurrentGameObject, -1, 0)` with an empty API
variant label. This confirms why operand selection must follow the resolved
receiver evidence, not a hard-coded variant name. The audit exposes 1,377
reachable state-effect call sites in the scoped owner set; these are candidates,
not evidence that every callback starts during playback.

A fresh `FF0045A2` collision-resource audit resolves 70 scene objects, including
53 collision instances, 47 maps, and 17 sets. `_PJ_Camera.gao` (`71003F56`) and
`_PJ_Sally.gao` (`71003F5A`) both have resolved collision instances with source
activation mask `0xFFFF`. However, the current bounded playback probe still
reports `CollisionActivationMask: null` for every sampled object. The Iris
source callback `camera_cam_1_init` contains
`COLSetActivationSet(AI_gpst_CurrentGameObject, 0, 1)`, which would clear bit 0
from that starting mask, but no callback execution is inferred from the static
candidate alone. This narrows the remaining gap: callback source and target
resources resolve, while startup callback admission/dispatch is not yet modeled
as a deterministic clip-time event. The fresh `FF0045A2 / 3500B8EA` graph
audit confirms the participant row for `_PJ_Camera.gao` (`71003F56`): identity
flags `0x0009FE20`, serialized state flags `0x030000AB`, and callback slot 1
`350008E4` (`camera_cam_1_init`). This is a stronger link than a source-index
candidate by itself. Iris initializes that slot's remaining timer to zero,
and the callback immediately calls
`AITrackCurChangeNow(camera_cam_1_mainloop)` after changing collision state
(`ai_common.c:375-420, 711-760`; `ai_compiledscripts_camera.c:7867-7905`). A
successful first AI dispatch therefore clears bit 0 and replaces the current
callback with the main loop in the same AI-instance pass. Its projected initial
`flags2` (`0x03000080`) passes the ordinary-time activation masks, while the
special-time activation path lacks required bit `0x04000000`; actual mode,
world-table admission, hierarchy/distance gates, and allocated live instance
remain unobserved. Keep the collision mask unknown until those owner-level
runtime gates are reconstructed; do not apply the source call at clip time 0
merely because its serialized callback slot is configured.

IrisEngine's PC implementation provides the retail-side details for cinematic
event preview: `OBJ_CapaSet` applies add/remove masks to `uw_Capacities`, while
`OBJ_CapaNot` XORs its argument into that field. The serialized capacity field
is 16-bit even though the wrapper arguments are read as signed 16-bit values;
preview therefore truncates both operations to the low 16 bits and restores the
initial mask on reverse seek. This models literal event commands only. It does
not execute callback branches, collision tests, or saved-flag gates that might
decide whether a command runs.

### Spatial trigger zones are stateful script owners

The recovered `Detect_o_Zone` brain is a concrete generic trigger mechanism.
Its initialization selects either a bounding-volume overlap test or a
ZDE-to-pivot collision test based on identity flag `0x400`, and can bind the
test to an explicit actor reference (small values 1 and 2 resolve through the
engine's main-actor slots). If the actor pointer is null, Iris's BV predicate
returns false and the ZDE predicate also rejects the null object; source does
not support assuming an implicit main-actor fallback.
On enter it changes an object capability and can set, clear, or invert
configured saved flags. On exit it reverses some of those effects; an optional
timer later changes the flags again. This means a trigger is not just a static
collider: it has enter/exit edges, selected collision semantics, optional actor
filters, per-owner state, and timed follow-up actions.

Keep this brain separate from the cinematic `Cine_Events` start predicates.
`Detect_o_Zone`'s Iris callbacks write capability bit `0x100` and saved flags
as state that other scripts may consume; the capability helper only mutates
the target's capability word and does not add that target to an AI/EVE world
scheduler. `Cine_Events` independently tests its own configured zone and
detector references, then can move its own callback state toward starting EVE
tracks. Sharing BV/ZDE collision primitives does not establish a call or
activation edge between the two script owners. Iris contains the BGE-specific
callback bodies (`ai_compiledscripts_detectors.c:1166-1365` and
`ai_compiledscripts_global_managers.c:4617-4931`); Jaded provides only the
generic collision and AI mechanisms, so those BGE field meanings must not be
inferred from Jaded alone.

#### Admission and message transport are separate paths

In Iris, `Detect_o_Zone_doz_init` is the admission point for the zone poller:
it resolves the two detector functions, clears the owner's `0x100` capability,
resolves the configured actor (including the two main-actor aliases), and only
replaces the current AI track with the BV or ZDE detector when that actor is
non-null (`external/irisEng/ai/ai_bge/ai_compiledscripts_detectors.c:1166-1201`).
Iris's compiled callback table maps model/function keys `0x35008312`,
`0x35008313`, and `0x35008314` to the zone init, BV detector, and ZDE
detector respectively (`external/irisEng/ai/ai_compiledscripts.c:2477-2479`).
`AI_ReinitInstance` seeds each instance's callback slots from its AI model's
callback slots (`external/irisEng/ai_common.c:385-420`); a BF owner therefore
needs the relevant model callback binding before this zone-specific init can
run. The selected detector reads its per-owner state from
`CurrentInstance->instanceVarDataCurrent` and polls collision each execution.
`AITrackCurChange_C` writes the function and its resume node into the current
instance/current-track slot (`external/irisEng/ai/ai_aifunctions.c:200-212`);
`AI_RunInstance` skips missing/disabled models, decrements the slot timer, and
runs admitted callbacks whose timer has expired
(`external/irisEng/ai_common.c:711-745`). The detector's enter/exit and timer
branches mutate capability and shared saved flags directly; the recovered
zone callbacks do not enqueue a generic message.

Iris messages are a distinct, explicitly consumed transport. `MSGSend_C`
copies a message into the chosen recipient GAO's bounded five-entry queue, and
the bytecode wrapper chooses the current `Ultra` object as recipient
(`external/irisEng/ai/ai_messagefunctions.c:3-31`). `MSGGet_C_CURRENT` pops
the newest queued message (`:34-50`); `MSGGetByID_C` searches by ID, removes a
match, and returns an empty-ID sentinel when absent (`:143-173`). A concrete
owner callback demonstrates the lifecycle: `action_reaction_ar_init` admits
`ar_check`, then `ar_check` consumes a message, branches on IDs 10/37, and may
send a modified message to its configured activator or transfer target
(`external/irisEng/ai/ai_bge/ai_compiledscripts_action_reaction.c:82-83,
86-168`). Queueing alone does not dispatch that recipient's callback; its own
AI instance and track must run and consume the message.

Jaded exposes a different, source-version-specific trigger path. Serialized
`TYPE_TRIGGER` owner variables contain an engine key, a procedure reference,
name, and an embedded `AI_tdst_Message` payload
(`external/jaded/src/libraries/AIinterp/Sources/AIstruct.h:360-370`). During
variable-reference resolution, the loader walks those trigger variables and
calls `AI_ResolveTrigger` (`external/jaded/src/libraries/AIinterp/Sources/AIload.c:523-557`). In the non-dynamic resolver,
the trigger key file and normalized trigger name are matched against the
registered AI2C trigger table first; otherwise the referenced file's procedure
list is scanned for a matching `@` procedure (`external/jaded/src/libraries/AIinterp/Sources/AI2C.cpp:457-531`). The loader
also reads each serialized trigger's procedure/name, sender, GAO arguments, vectors, integers,
and ID (`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1749-1777`). The `call_trigger` keyword then validates the
procedure pointer, pushes the trigger's stored message as a `TYPE_MESSAGE`
argument, and immediately invokes `AI_EvalFunc_AIExecute_C`
(`external/jaded/src/libraries/AIinterp/Sources/Keywords/AIkeywords.c:1153-1205`; keyword ID 2109 is registered in
`external/jaded/src/libraries/AIinterp/Sources/Keywords/AIdefkey.h:37`). This is a direct procedure call, not a spatial
zone test and not a queue insertion. Separate Jaded message APIs use a
per-object queue: `MSGSend_C` appends to the supplied object's queue and
`MSGGet_C` removes the newest item (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c:88-128,
175-220`; capacity is 1000 in `external/jaded/src/libraries/AIinterp/Sources/AImsg.h:23`).

Do not merge these abstractions in JadeView: Iris's BGE `Detect_o_Zone` is a
polling callback with collision and owner state; Jaded's `call_trigger` is a
serialized procedure reference plus payload invoked synchronously by AI bytecode;
ordinary messages are separately queued in both source trees, with different
capacity and wrapper/context details. Jaded is the Rayman 4-era source tree,
so its trigger layout and keyword behavior are comparison evidence, not proof
that retail BGE PC uses the same serialized trigger path. The repository does
not yet establish which retail BGE AI owner/callback load hooks admit each
zone model, whether a BF instance has its callback active before sector
visibility, or what runtime conditions gate `call_trigger` data on retail PC.

For the next implementation slice, make the inspector show the distinction
without simulating it: per owner, expose a resolved zone callback admission
record (owner/model, configured actor resolution, selected BV/ZDE branch,
current-track replacement evidence) separately from message edges (sender,
recipient, message ID/payload fields, and consumer callback candidates). Mark
runtime overlap, queue contents, visibility admission, and callback execution
as unknown until a source-backed runtime gate or observed trace resolves them.

JadeView's shared script-owner analyzer now reports that static behavior from
the callback catalog and effective OVA values. It describes the BV/ZDE
selector from the owner's serialized identity flags (bit `0x400` set selects
ZDE pivot 2; clear selects BV overlap), configured actor reference, positive saved-flag operations, and timer
behavior while explicitly leaving collision occurrence, live save flags, and
timer progress unresolved. A production-data check in `05_01_ilot_plaques`
(`FF005EFB` / `D9005EE0`) found 24 owners of model `35008316`; for example,
the military and Peyj ultra-tag owners share the model defaults but override
their actor reference and flag 2777 in instance OVA. This verifies the
analysis is shared by model family and driven by each owner's data rather than
names.

The trigger report now evaluates the initial selector per GAO: the three
`Detect_o_Zone_doz_init` owners found in this world have serialized identity
flags `0x000020A0`, so their initial route is BV overlap. A synthetic check with
the `0x400` bit set verifies the alternate ZDE-pivot route. The inference is
limited to saved identity flags; script writes before initialization could
change the runtime route.

The cinematic script index also finds `Detect_o_Zone` owners in cinematic
archives, while separate recovered `Zone_ChangeStatus` and
`Zone_OccupationTerrain` callbacks operate on actor/status state. This supports
a broad gameplay-tools roadmap: inspect trigger bounds and collision sets,
show configured participants and state transitions, and eventually simulate
them with a source-time/event queue. It does not justify executing arbitrary
game scripts in the editor.

### A serialized zone-to-gameplay chain through saved flag 1007

The installed retail archive contains an unusually traceable cross-world
example. In cinematic world `01_04_entrepot_liberation_mili_cine`
(`3500B8D5`), `CINE_Ouverture_Porte_2_3.gao` (`10000473`, model
`35008316`) has an unambiguous typed OVA binding for saved flag 1007, actor
reference 1, and the BV-overlap route. Iris's zone callback sets flag 1007 on
entry and clears it on exit when the configured timer is zero; `invert` here
is asymmetric set/clear behavior, not a toggle. The zone links
`SceneEvents=10000474`; EVE track 10 targets it and changes the owner's
inactive state and capability bit `0x100` at 0.00, 0.93, 4.40, and 19.80 s.
Those EVE commands do not themselves write flag 1007.

The archive-wide source/OVA join finds exactly two gameplay owners of the same
flag in `01_04_entrepot_liberation_mili` (`E6001FA7`).
`OBJ_porte_prisomili01.gao` (`1600272A`) reads the effective flag during
`porte_po_init` and selects its open path when set; its open/close callbacks
write/clear the configured flag. `WP_Kick_Porte.gao` (`10000D58`) reads it
during initialization and its threshold action can write it while changing
collision activation. This establishes a configured data-and-source chain
between the cinematic zone and gameplay consumers, but not a runtime causal
trace: it does not establish EVE dispatch order, whether zone callbacks run
while the owner is inactive, the live contents of actor slot 1, actual BV
collision, or which writer last changed the saved flag. The gameplay door's
configured child-activation values are not exposed by the current index, so
no specific spawned/hidden child is inferred here.

The all-container transition audit found no reachable transition API callsite
in those three owners. That does not prove there is no transition: another
owner, an indirect engine dispatch, or an opaque OFC body may cause one. The
archive establishes configured links and values; Iris establishes callback
semantics; neither establishes execution in a play session. See the
[reproducible flag-1007 report](gameplay-saved-flag-1007-chain.md) for exact
commands, source locations, event payloads, and audit scope.

The same archive shows a second reusable pattern in gameplay world
`01_02_entrepot_salle_fusibles` (`FF006BFC`): persisted object state can feed a
generic event gate. `OBJ_Fusible_2.gao` and `OBJ_Fusible_3.gao` share the
`get_object_go` model but instance-override their `des_SF_active_on_destroy`
and `des_SF_destroy_on_load` values to flags 2012 and 2013 respectively.
`get_object_go_get_i_bit` reads the configured load flag, while the present and
absent callbacks test and update the configured active-on-destroy bit
(`ai_compiledscripts_objects.c:406,1456-1478,1892-1914`). The neighboring
`EVT_cable.gao` (`C10011E9`, model `350091C3`) overrides
`des_i_specialFlag_test2=2013`; the `Cine_Events` test callback consumes this
flag as one part of its start predicate. This is a data-backed link from a
generic pickup's persistence state to a separately authored event owner, not
proof that pickup execution started the EVE track in a particular session:
the event also has capability/test-mask conditions, and the current report
does not recover live dispatch timing. In this same world,
`OBJ_porte1.gao` writes and reads flag 1003 through the generic door callbacks,
while `SND_AmbianceC_01_02.gao` reads 1003 in its sound play/stop callbacks.
That is evidence that persistent flags can coordinate object and audio
behavior; the join does not by itself establish which interaction changed the
flag at runtime. Together these examples support a general flag-centric
inspection view that groups writers, readers, and event/audio gates by the
effective instance value, while keeping runtime execution explicitly unknown.

A separate cross-container example illustrates why that index must be
archive-wide. The installed BF index returns four unique owner instances that
access flag 2035. In `FF001814 / 01_06_entrepot_defile_cercueils`,
`CINE_ENLEVEMENT_PEYJ_01_EVENTS.gao` (`35009FD6`) has SceneEvents track 8
setting global flag 2035 at 1.75 s. In `FF00791B / 01_00_entrepot_sas_entree`,
`EVT_TEMP_Porte_Fermer_SF2035aVRAI.gao` (`20007993`) overrides
`des_i_SPFlag_associe_vrai=2035`; its `Cine_Events_Cine_exec_EndOfEvents`
callback therefore sets 2035 on completion. The same owner has
`des_i_ripley=2`, however, and the source's startup/replay test for the
associated flag is guarded by `!des_i_ripley`; that particular 2035 read is
disabled for this configured instance. Its separate phase gate is configured
with `des_i_specialFlag_test1=2007` and `des_i_specialFlag_test2=3853`.
`Cine_Events_cine_tests_depart` checks these when capability `0x100` is
absent: test1 requires its flag set, while test2 requires its flag clear
(`ai_compiledscripts_global_managers.c:4175-4215,4331-4333,4557-4607`).
`HLP_mili_porte_fermee.gao` reads flag 2035 with a set-required condition, and
the linked `CINE_OUVRE_SAS_EVENTS.gao` track 12 clears 2035 on
`GST_Global.gao` (`710047BC`) at 42.05 s. The other container's track 8 also
targets `GST_Global.gao` and sets 2035 at 1.75 s. These are confirmed archive
write/read/configuration edges across container/world records, but not proof
they execute as one linear sequence. Neither does this establish a direct
door-state edge: the same index contains no 2035-reading door owner in
`FF001814`, and `LD_OBJ_porte_code.gao` in `FF00791B` uses flag 2040, while
the 2035 reader there is the helper text owner. The shared number alone does
not establish that the earlier event caused later door behavior; the archive
contains no runtime trace establishing which event ran first or the saved
flag value at the helper's test. The system-level finding remains that
saved-flag identity is global and must be joined across containers, while
effective per-instance guards must be applied before calling an owner a live
reader.

Reproduce the serialized EVE payload and the 01_00 owner/track joins with:

```powershell
dotnet run --project tests/ExportVerification -- --event-track-payload-audit "<path-to-sally_clean.bf>" FF001814 35009FD6 35009FD9 8
dotnet run --project tests/ExportVerification -- --world-script-graph-audit "<path-to-sally_clean.bf>" FF00791B 3500A7C6
```

### Jaded sector callbacks are not established for the PC Iris runtime

Jaded has explicit sector-driven lifecycle callbacks in
`external/jaded/src/libraries/ENGine/Sources/WORld/WORmain.c`. During
`WOR_ActivateObjectIfFlagsAllow`, `WOR_SectoObjActive` controls membership in
the active-object table. When the sector test becomes false and the owner was
not already marked sector-inactive, Jaded marks it inactive and dispatches
callback ID 6 (`SectoActOn`). When the test becomes true for a previously
sector-inactive owner, it may call `ENG_ReinitOneObject` when both
sector-inactive and sector-reinit control flags are set, dispatches ID 7
(`SectoActOff`), clears the inactive state, and adds the object to the active
table. The callbacks are edge-triggered by the stored sector state; they are
not dispatched on every table rebuild.

Visibility is a separate pass. `WOR_MakeObjectVisibleIfFlagsAllow` evaluates
`WOR_SectoObjVisible`; when the test becomes false and the owner was not
already sector-invisible, it dispatches ID 8 (`SectoVisOn`) before marking it
sector-invisible and culled. When the test becomes true for a previously
sector-invisible owner, it dispatches ID 9 (`SectoVisOff`) before clearing
that state and adding the object to the visible table. The IDs
are named in `external/jaded/src/libraries/AIinterp/Sources/AIstruct.h:50-59`.
Thus Jaded's activation callback pair and visibility callback pair have
different state, tables, and dispatch moments.

The bundled Iris source defines the same callback names in metadata, but the
Iris dispatch-site inventory contains no direct call sites for IDs 6–9. Its
`WOR_World_ActivateObjects` rebuilds activation from `flags2` and its
`WOR_MakeObjectVisibleIfFlagsAllow` applies visibility flags without Jaded's
sector callbacks (`external/irisEng/world.c:44-62,153-190`). In this
source tree, Jaded therefore documents a possible Jade Engine mechanism, not a
confirmed retail BGE PC mechanism. JadeView should not simulate these callbacks
or infer sector-triggered object visibility for PC archives unless retail
evidence establishes that path.

An archive-wide join strengthens that boundary: all 15,950 script owners in
the installed BF resolved, but none has a nonzero model engine-hook assignment
for IDs 3–9; IDs 6–9 are absent from the configured callback census entirely.
The complete counts and generation command are in
[`game-script-engine-callbacks.md`](game-script-engine-callbacks.md). This
rules out only those configured callback IDs in the audited archive, not other
sector logic or callback-free activation paths.

This difference is relevant when diagnosing an object that is present in the
archive but appears only after entering a region: sector-gated loading or
activation is an architectural hypothesis, not a proven explanation for any
reported BGE actor. The current BF preview has no live sector-membership trace,
and the Iris source currently provides no direct ID 6–9 dispatch path to join
to an owner assignment. The next evidence needed is a retail runtime trace or
an Iris/retail call path that demonstrates sector state updates and callback
dispatch; asset presence and Jaded's implementation alone are insufficient.

The related `Detect_o_Capa` family is another generic activation gate. Iris
source shows `Detect_o_Capa_doc_init` selecting an all/any true/false test from
`des_i_type_test`, checking `des_i_capa_test` against up to ten referenced
objects, and changing the detector owner's capability `0x100`. A production
example is `HLP_peyj_coupe_grille_DETECT.gao` (`D9007848`) in
`FF005EFB / D9005EE0`: its model sets mask `0x100`, while instance OVA changes
the mode to `1` and links camera/event owners in serialized array slots 1 and
2. JadeView summarizes the source mode and linked references in the cinematic
script inspector. The PC OVA reader skips the serialized dimension header,
preserves zero-based array slots, and overlays instance values onto the model
defaults using the engine's case-insensitive variable-name match and
prefix-copy rule (`min(instanceCount, modelCount)`). For the production owner,
the two non-null effective slots are `[0]` (atelier event) and `[1]` (Medusa
event); the dimension header is no longer misreported as a slot. The overlay
is a static serialized snapshot; it does not resolve
runtime main-actor slots, target capability state, or callback execution.

The one-callback `Detect_o_BV_CapaSet` family is a separate collision-driven
capability writer. Its Iris struct contains `o_perso`, `o_perso_capa`,
`i_capa`, `des_i_test_si_actif`, `des_i_met_SF`, and `des_capa_a_faux`. The
callback checks a BV-pivot collision between its owner and `o_perso`; when the
optional active-state guard is enabled, it skips the test while the actor has
control mask `0x400000`. A collision adds `i_capa` to `o_perso_capa`, or clears
it when `des_capa_a_faux` is set. A positive `des_i_met_SF` is also set on
collision. If the capability target resolves null, the source falls back to
the collision actor. The callback does not contain an exit reset, so JadeView
does not imply that the capability or saved flag is cleared when the actor
leaves the volume.

The production instance `LD_OBJ_gyro_Active_BV_Active_detect.gao` (`0200718C`)
in `FF0057FD / 00_01_home_sas_hangar` uses the shared model `32000064`: model
OVA points `o_perso` at Sally and configures capability `0x100`, while instance
OVA overrides the actor with `_PJ_Enterprise.gao`, targets the detector itself
for the capability write, and enables the `0x400000` guard. Its saved-flag
field is zero. The new inspector summary resolves those model/instance values
and the saved-flag graph treats a positive `des_i_met_SF` as a conditional
writer. The preview still cannot evaluate collision geometry, current control
flags, capability state, or callback execution.

The same `32000064` model appears in `01_00_entrepot_sas_entree`
(`FF00791B / 200078D7`) as `ZON_Inactive_Peyj_Fight.gao` (`9E0077A6`). Its
instance OVA overrides both actor and capability target with
`_PJ_Peyj_le_petit_cochon.gao`, changes the capability mask to `1`, and enables
the same actor-control guard. This second world confirms the inspector follows
the common model while honoring per-instance fields, rather than relying on
the asset name or the hangar setup.

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

The high-frequency `helper_text_hlptxt` model (`350074D6`, 515 owners in the
installed archive) is a gameplay hint/dialogue path separate from the
`Cine_Textes` cinematic receiver. Iris source shows it builds a condition mask
from configured actor, message, visibility, capability, custom-bit, saved-flag,
and destructible-state fields, then checks runtime readiness before selecting
a text slot. `helper_text_hlptxt_exec_ask_display` submits message 30 to the
shared text/audio manager, carrying the text-bank reference, sound modifier,
speaker-position reference, text-box value, priority, volume and optional
near/far distances. A zero volume selects the source fallback 0.8; a zero far
distance means the callback does not write the distance pair. With
`des_i_auto_delay=0`, it starts from the sound duration and falls back to
per-line duration or a text-length estimate; otherwise it uses the latter
fallback path. Game-speed scaling is applied before the manager receives the
message. These are callback semantics and serialized line data, not proof that
an owner passed its gates or that the game played the sound.

JadeView now decodes these line arrays in addition to the cinematic dialogue
tables. This required accepting the source-defined type difference for
`des_ai_encart`: `Cine_Textes` stores it as float, while `tScriptStruct_HelperText`
stores the corresponding per-line text-box value as integer. The parser keeps
the raw speaker reference and explicit duration instead of resolving small
reference values to actors or presenting zero duration as the final runtime
length. Archive validation in `FF000352 / 01_09_entrepot_boss` found
`HLP_Arrivee_HH.gao` with two lines (`3500A31F:9/10`, sound modifier 3 on the
first line) and `HLP_CestParLa.gao` with one (`D0002E11:1`, sound modifier 2).
The inspector derives their message gate and line metadata from callback/model
and instance OVA data; current readiness, actual localized text and runtime
sound duration remain unknown. Relevant source is
`external/irisEng/ai/ai_bge/ai_compiledscripts_helper_text.c` and the
`tScriptStruct_HelperText` declaration in
`external/irisEng/ai/ai_compiledscripts.h`. The saved-flag graph also records
encoded `des_i_specialFlag_test1/2` reads and conditional completion writes to
`des_i_SPFlag_associe_vrai/faux`, so helper-text gates can be linked to event
tracks or other script owners when the same global flags are serialized.
The cinematic world audit also links `HLP_Arrivee_HH.gao`'s configured read of
flag 2051 to `CINE_ARRIVEE_HH_EVENTS.gao`'s `ARRGlobalIntFlagSet` commands at
5.20 s and 9.383 s in `SceneEvents=3500A264`. This is a static
producer/consumer edge: the graph does not claim either track was scheduled or
that the helper displayed its lines.

The callback graph follows indirect `fctr_` function-reference variables as
well as direct `fct_` references. This completes the source-visible helper-text
path from `helper_text_hlptxt_init` to `helper_text_hlptxt_tests_depart`, then
through the `AITrackCurChangeNow` transition to `helper_text_hlptxt_tests_wait`
and its `AIExecute(exec_ask_display)` call. JadeView can therefore show the
configured path even when the GAO assigns only the init callback. In the retail
sample `FF001FBE / 01_04_entrepot_liberation_mili_cine`,
`TXT_bullrushfoireux.gao` has that init-only setup, while its message gate
references `OBJ_porte_prisomili01.gao` and message 37 with Int1 mask `0x7`.
The serialized message with Int1=4 is issued by `GST_Global.gao`, so it does
not match the helper's queue target. These are source/configuration links;
they do not prove that the helper was scheduled, that a branch passed, or that
the message reached the queue in game execution.

Indirect function references also retain their concrete Iris dispatch site:
the wait-callback transition resolves function key `7100E7B3` in
`ai_compiledscripts_helper_text.c:1175` and is issued at line 2227. The graph
checks that the resolved key maps back to the target callback name before it
adds this edge. This lets a reviewer follow the resolver assignment and engine
call instead of relying on the generated function-name edge alone. The
callback-path graph was rebuilt
against three other retail clips: `BF00218E` (28/31 owners with reachable
callback paths), `3500B8EA` (74/83), and `BF003BD1` (12/14). These counts are
coverage of configured/source-reachable callbacks, not proof of live execution.

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
6. **Scripted effects and materials:** continue adding callback-derived
   summaries for recurring effect models; `SFX_HaloSimple` now exposes its
   native GFX setup, material reference, owner inactivity and capability-driven
   color branches. Runtime GFX state, alpha/visibility changes and global
   lighting still need resolution through the shared event graph rather than
   scene- or asset-name rules.
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

### Cinematics reference reusable actor brains across worlds

`FF005833` loads 12 worlds for the Mdisk sequence, including `_basic_peyj`,
`_basic_sally&camera`, and the gameplay world `00_02_home_decryptage`. The
selected cinematic clip directly binds only Secundo as an actor rig, while its
OVA references and event targets also address Peyj, Sally, the two Mdisk
drones, and separate gameplay/dialogue controllers. Following those serialized
keys across loaded worlds finds five additional script owners, including
Peyj's `companion_co` brain (`35001FF8`) and its serialized `Events` key
(`35009544`). The actor skeleton table separately resolves to 114 action slots;
the first slot references clip `35009544`. Sally's base record exposes 215
action slots. JadeView now lists an actor's action slots with clip and shape
keys when its referenced script owner is selected. This inventories available
actions; it does not evaluate transitions or identify which slot is active at a
playhead. This is a generic cross-world graph relation: actor/script records
can live in reusable base worlds while a cinematic world only sends them
messages or targets their GAO keys. JadeView's cinematic inspector now shows
those linked owners and callback slots, while retaining the source-world label.
The link does not prove that the actor brain was active or that its regular
action table was selected during playback. The separate serialized `Events`
field is decoded as a separate linked event resource, and the inspector now
marks action slots that contain the same clip key. This is a serialized key
match only: it does not establish that the slot was selected or explain how a
brain callback selects it. `SceneEvents` remains the distinct EVE list used
for `TRA*` indexing. Resolving runtime action selection is still needed for
T-pose and scripted actor-motion cases.

## Evidence limits and next audit

An all-world pass over the installed `sally_clean.bf` now indexes 91 containers,
196 worlds, 15,950 brain-bearing owners, and 435 script models. Every brain
owner resolved to a script model, and every owner had its five serialized
callback slots decoded. 15,935 owners map to at least one Iris function; 15
have no mapped callback function. Across the archive, 16,204 callback slots
are configured; 12 slots across 8 distinct keys have no function name in the
bundled Iris table. No brain records or script models were missing, and the
owner-variable snapshot pass reported no variable-layout errors. The raw
unmapped keys and their owner/container examples remain in the generated
`archive-gameplay-script-index.json` audit at
`artifacts/gameplay-script-census-current/`. This is strong coverage for the
serialized owner/model join, not a census of functions that execute at runtime;
the 12 unmapped slots remain unresolved rather than being guessed from nearby
keys or asset names. The eight keys are `C1015F8D`, `26004CED`, `620032AA`,
`26004CD6`, `3D001B6A`, `72006324`, `720068F7`, and `9E007752`; their examples
span elevator/funicular, pearl, cinematic-effect, inventory, and NPC owners.

The all-world index now joins callback/OVA flag predicates and source API flag
accesses with literal set/clear/test commands decoded from each owner's linked
`Events` and `SceneEvents` EVE records. The current `sally_clean.bf` pass covers
all 15,950 brain owners and emits 4,873 owners with 31,805 statically
recognized callback/source flag accesses, plus 248 timed EVE accesses from 83
owners, grouped into 1,404 flag keys. Source `ARRBitTest/Set/Clear` calls are
included only when their buffer argument resolves to `Flag_speciaux_sauve`,
avoiding unrelated bitsets such as planet-hour state. The index keeps
container, world, owner, model, callback, OVA field, event key, channel,
target and time with each access. Potential cross-owner edges are counted in
full and at most 64 example links per flag are serialized. The generated audit
is at `artifacts/archive-script-bitflags-final-current/archive-gameplay-script-index.json`.

For example, `FF005833 / 00_02_home_decryptage_cine`, saved flag `2166` now
connects the `CINE_APRES_MDISK_EVT02.gao` `SceneEvents` set at 44.783 seconds
in `BF00218E` to the replay-suppression predicates on
`CINE_EXPLIK_INVENTAIRE_EVENT.gao` and the gameplay-world
`EVT_apres_matos.gao`. The link records configured state compatibility; it
does not establish execution order or whether the live predicate passed.

This remains a bounded static scan, not a complete flag census: the indexed
accessors are the current callback analyzer patterns, direct
`ARRGlobalIntFlag*` calls, `ARRBitTest/Set/Clear`, bounded inclusive
`ARRBitSetRange/ARRBitClearRange` calls, and literal EVE commands whose saved
storage/endpoints resolve. Range calls must target `Flag_speciaux_sauve`, use
positive ordered indices, and fit the 4488-byte Iris-resolved buffer. Unknown
storage aliases, dynamic or invalid ranges, and other encoded state APIs remain
unresolved. The current recovered callback corpus has no direct range-call
sites; the decoder is guarded by synthetic regression fixtures. It also does
not execute callbacks or prove runtime order. No generic callback evaluator or
user-facing gameplay-script preview has been implemented.

The archive-wide OVA object-reference join is now world-aware. It first resolves
within the same BF container and world, then accepts a unique owner in the same
world, and finally accepts a globally unique owner key. Conflicting candidates
are emitted separately as unresolved instead of creating a false cross-world
edge. The refreshed `sally_clean.bf` report resolves 13,399 script-owner
references (11,241 same-container/world and 2,158 unique archive-wide); 1,715
references remain ambiguous across worlds. Conflicting object names for a key
inside one world are left unlabeled. The audit is
`artifacts/script-owner-reference-audit-current/archive-gameplay-script-index.json`.
A source symbol scan finds `ARRBitSet`/`ARRBitTest` calls in 73 recovered
`ai_compiledscripts_*.c` files, so global-flag behavior is distributed across
many script families rather than isolated to the cinematic coordinator. This
is a source-file count, not a count of unique flags, owners, or executed paths.

The in-app resource index now applies the same ambiguity rule to typed resource
links: a key resolves only when its preferred-world, shared-world, or unique
archive-scope candidate is singular. Duplicate records in the preferred world
or shared scope are no longer silently collapsed to the last record. Event
link diagnostics include the ambiguity cause. This protects `Events` and
`SceneEvents` inspection, script-model references, and actor resource joins
from order-dependent selection. Regression checks cover local, shared, unique
external, cross-world ambiguous, and same-world duplicate keys. This is a
reference-resolution safeguard; it does not establish that an EVE command ran.
The archive-index resolver now rejects conflicting preferred payloads and
permits shared copies only when their bytes match. A fresh scan of all 91
`sally_clean.bf` containers retained 15,950 / 15,950 brain-owner links and 435
script models; 16,204 callback slots were configured and 12 remained unmapped.
The audit is `artifacts/resource-index-audit-v64/archive-gameplay-script-index.json`.

The 12 unmapped callback slots reduce to eight distinct function keys. The
updated audit records candidate named resources for every key; all eight have
no named record in the scanned BF archive, and a case-insensitive search found
none in the supplied Iris source tree. Jaded's earlier loader confirms the
architecture: `AI_ul_CallbackLoadFunction` sets the C function name only when
the loaded function key matches `AI2C_gat_fctdefs` (`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1331-1349`).
That snapshot's `AI2C_fctdefs.h` is empty, so it cannot recover retail names.
These IDs remain raw and their behavior remains unknown; the Jaded snapshot is
not evidence of retail BGE semantics. The detailed archive output is
`artifacts/unmapped-callback-resource-audit-v65/archive-gameplay-script-index.json`.

Iris confirms that the five callback references belong to the resolved script
model: `AI_ul_CallbackLoadVars` reads them into `tAIVars.callBacks` at
`external/irisEng/ai_common.c:1292-1297`, and instance reinitialization copies
the model's callback functions into per-instance runtime slots at lines
`389-404`. The instance OVA is separately overlaid onto model variable data by
matching variable names at lines `586-627`; it does not replace the model's
callback list. JadeView labels these as **Script-model callback slots** to
distinguish them from the instance's OVA variable overrides. This proves the
ownership rule, not that a configured callback ran.

The same source also makes the initial slot span computable without runtime
simulation. `AI_ReinitInstance` sets `end` to the lowest configured callback
slot and `start` to the highest (`ai_common.c:387-397`); `AI_RunInstance` loops
from `end` through `start` inclusively and invokes only a non-null callback
whose timer is nonpositive (`ai_common.c:729-747`). JadeView now derives and
shows that inclusive span from the decoded model slots, including unknown
nonzero keys; unconfigured gaps are noted as skipped. No configured slots
produce an empty span. This describes the initial model-defined pass only;
world-table membership, parent redirection, object state and live timers still
control whether any callback executes.

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
- [`ai_compiledscripts.h`](../external/irisEng/ai/ai_compiledscripts.h)
- [`ai_sectorfunctions.c`](../external/irisEng/ai/ai_sectorfunctions.c)
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

### Sector activators and reference-counted visibility

The cinematic script inspector now recognizes `Activator_act_init`, `loop`,
`afterrec`, and `cutter` by their callback catalog entries and reports only
behavior derived from the matching Iris implementations and the owner's
effective model/instance OVA values. Initialization can register the owner as a
render sector (`des_i_register`) and mark a referenced group as sector-managed
(`des_o_cacher_groupe_de`). The activation callbacks test the configured
`des_o_detect` with the engine's BV-pivot collision function; `afterrec` also
tests the current viewport object when the configured test does not collide.
Its `des_i_auto_hide`, `i_forceshow`, global no-sector state, and capability-1
branch influence the script's activity counter.

The native `GRPSectInvisibleSet` implementation increments the object's
`if_this_not0_wont_render_gao` counter for argument 1, decrements it for 0,
and clears it for -1. It applies that operation to sector members and, for
sector-managed groups, group members too; the renderer suppresses an object
while the resulting counter is nonzero. This makes visibility shared and
reference-counted, so a single Boolean per object would be incorrect. The
activator loop's apparent raw slots are now resolved against the same
`tScriptStruct_Activator` declaration: DWORD slot +130 is byte offset 520
(`i_cptact`), and +131 is byte offset 524 (`i_forceshow`). JadeView applies
those meanings only when the referenced object resolves in the same world to
an owner with the `Activator_act_afterrec` callback. For other targets it
reports the reference without claiming a field meaning. The inspector does not
simulate collision, global runtime state, capabilities, or callback scheduling.

Retail source-backed audits confirm this model is reused across worlds: the
intro world in `FF0045A2` contains `SCT_Bas.gao` and `SCT_Haut.gao`; the
warehouse-entry world in `FF00791B` contains `SCT_1.gao` and `SCT_2.gao`; and
`05_01_ilot_plaques` in `FF005EFB` contains linked activators such as
`SCT_1.gao`, `SCT_3.gao`, and `SCT_Deb.gao`. These owners use model
`5E000487`; in the latter world, `SCT_1.gao` references 11 activators in its
remove-sector list, and several owners have populated show-sector lists (for
example, `SCT_DebH.gao` references `SCT_Deb.gao`). The callback's +130/+131
writes therefore resolve to the target sector owners' `i_cptact` and
`i_forceshow`, respectively. These are examples of a reusable sector system,
not scene-specific rules. Audits were written under
`artifacts/activator-audit/`.

### Bounding-volume activation and deactivation

The shared `BV_Active_Desactive` script model (`7100F56F`) maps five callbacks:
`BV_Active_Desactive_BV_In` (`7100F56D`), `..._BV_Out` (`7100F571`),
`..._BV_CBits_Out` (`7100F73B`), `..._BV_CBits_In` (`7100F73C`), and
`..._BV_AD` (`7100F741`). The archive index finds 127 owners in 27 containers.
The analyzer derives behavior from callback names and effective model/instance
OVA values; it does not match scene names or specific GAO names.

Iris source confirms `BV_AD` selects the callback route from `i_detection_type`:
zero chooses the configured-actor route, nonzero chooses the custom-bit route.
The actor route tests `acteur_a_detecter` against `o_BV_a_qui`, using
`COLBVObjectObject_C` when `i_bv_pivot=0` and `COLBVPivotCollide_C` otherwise.
When `o_BV_a_qui` is null, `BV_AD` assigns the current GAO as the volume before
dispatching the selected callback.
The custom-bit route calls `COLBVGetFirstOverlap_C(volume, acteur_avec_CBit, 0, 5)`.
Despite its name, `acteur_avec_CBit` is a numeric required-flags mask, not a GAO
reference. `OBJ_b_test_flag` case 5 checks `flags2`; a candidate passes when any
bit in the required mask is present, with no forbidden-bit mask. A zero required
mask cannot pass that check. This distinction matters when an instance stores
`acteur_avec_CBit=256`.

The paired In/Out branches apply opposite values to the target inactive flag,
derived from `active_dans_bv`. Up to three `object_a_activerN` references can be
configured; each `ia_reinitN` can reinitialize that target's AI variables before
the state write. `ne_fait_pas_l_activation` suppresses inactive-flag writes.
`rend_invisible_aussi` requests separate invisible-flag writes and updates group
visibility for targets carrying identity bit `0x800000`. `ne_fait_qu_activer`
controls whether the callback switches to its paired track, and
`se_desactive_apres` can make the volume owner inactive after the state change.

Production audit: `FF00056C / 0200056B` (`03_07_egouts_abattoir_interieur`)
contains `F_BV_Active_Gardes.gao`, which references Sally, the volume itself,
and two guard targets. Its effective instance values select pivot detection,
`active_dans_bv=1`, one-way activation, and deactivate-after; the initial callback
uses the actor route. Its alternate custom-bit path requires `flags2 & 0x100`.
`S4_LD_BV_Active_Gardes.gao` in the same world reinitializes two referenced guard
AI instances. A second audit of `FF000FC8 / 83000FC7` (`11_04_tresor_alpha_01`)
found seven instances, including turret pairs and per-floor guard activation.
Those records select both callback routes depending on their OVA values. These are
serialized links and configured branches, not proof that an owner was scheduled
or collision occurred.

Sources: `external/irisEng/ai/ai_bge/ai_compiledscripts_bv_active_desactive.c`,
`external/irisEng/ai/ai_compiledscripts.h` (`tScriptStruct_BVActiveDesactive`),
`external/irisEng/ai/ai_colfunctions.c` (`COLBVGetFirstOverlap_C`), and
`external/irisEng/gameobject.c` (`OBJ_b_test_flag`). JadeView now presents these
static findings in the existing script inspector. It does not evaluate live
collision lists, flags2, callback scheduling, or target activity.

### World transitions and companion event routing

#### World changes are a request protocol, not a single loader call

**Confirmed in the supplied PC Iris source:** the world API has separate
operations with materially different effects. `WORUniverseAdd(key)` loads one
world and adds it to the universe; `WORActive(slot)` changes the active-world
handle to an already loaded world; `WORDesactive(slot)` clears that world-table
active bit; and `WORLoadList(key)` deletes the existing universe worlds and
replaces them from a world-list resource
(`external/irisEng/ai/ai_worldfunctions.c:24-63,100-120`; the function IDs are
registered in `ai/ai_functions.c:169-178`). These are confirmed Iris API
semantics, not proof that retail BGE's ordinary door/exit scripts call
`WORLoadList` directly.

The compiled BGE gameplay scripts instead expose a shared-state handoff. The
ground-map exit path writes `load_world=1`, a target map number, and an entrance
number to the shared `univers.ova` variables, then yields for a delay; the
space exit path writes the same request fields from its destination object
(`external/irisEng/ai/ai_bge/ai_compiledscripts_es_au_sol.c:5416-5421`;
`ai_compiledscripts_es_ciel.c:283-286`). The Mdisk return branch also writes
`load_world`, its selected map ID, and entrance before stopping its current AI
track (`ai_compiledscripts_es_au_sol.c:5498-5508`). Those are script-side
requests, not themselves evidence that a world has loaded.

The global map manager resolves gameplay map numbers through its
`WORLD_Key_Map_ID` / `WORLD_Key_Map_Key` table, writes the corresponding world
key, and chooses either its special-load or editor/setup path. It also copies
the target map/entrance into the E3 transition fields on its initialization
branch (`external/irisEng/ai/ai_bge/ai_compiledscripts_global_mapmanager.c`,
`map_manager_mm_exec_TM`, especially the `WORLD_Key_Map` resolution and
`AIExecute(SpecialLoad/Editeur)` tail). Destination entry selection is a
separate step: `ES_au_sol_esas_enter_the_map` matches each authored entrance's
`des_numero_de_l_entree` against the shared `world_entrance_ID`
(`ai_compiledscripts_es_au_sol.c:195-205,984`). Therefore a map ID, BF world
key, and entrance ID are three different values and should not be collapsed
into one “scene key.”

`WORReinit` is a distinct engine-wide reset request: Iris sets
`ENG_gb_NeedToReinit`, and the next outer engine frame reinitializes the AI
universe and each loaded world before clearing the request
(`ai/ai_worldfunctions.c:142-150`; `external/irisEng/engine.c:655-668`). The
source shows it resets runtime state; it does not by itself identify or load a
destination world. This distinction matters when following scripts that set
the world request fields and later invoke `WORReinit`.

**Confirmed chain boundary:** source code proves the script writes, map-ID to
archive-key lookup, entrance matching, and the separate Iris world APIs. The
examined transition functions do not establish the exact runtime owner or
engine callback that consumes `load_world` and performs the retail transition;
the `WORLoadList` body is not directly called by the cited exit routines. A
static BF can expose configured destination values and object references, but
cannot show the live request value, which global-manager callback ran, load
success, arrival actor placement, or subsequent world activation. Treat any
claim that this complete handoff is already reconstructed as **unverified**.

#### Source-wide consumer search: the request flag still has no proven reader

The source search separates the three shared fields instead of treating them
as one transition call. In the supplied compiled Iris BGE scripts,
`univers.ova` `load_world` occurrences resolve the field at offset 0 and write
`1` (request) or, in the reset path, `0`; the source-wide search found no
conditional read of this field. The request producers include the ground and
sky exit scripts, E3/load-map setup scripts, global message handling, and the
Mdisk return path. For example, `Global_glob_reflex` writes `load_world=1`,
`world_ID=message.Int1`, and `world_entrance_ID=rounded(message.f2)` for its
message cases 12/13 (`ai_compiledscripts_global.c`, `Global_glob_reflex`;
the buffer offsets are 0, 4, and 8).

Reproducible census: `rg -n "load_world" external/irisEng` returns 58
matches across 10 source files; these include 15 buffer-resolution lines, 21
write-to-1 lines, and one write-to-0 reset line. A comparison/branch search
over those matches returns no read-like use. A separate search for direct
`WORLoadList`, `WOR_pst_Universe_AddWorldList`, `WORUniverseAdd`, or `WORActive`
callsites under `external/irisEng/ai/ai_bge/ai_compiledscripts_*.c` returns
zero matches. These are source-text census results, not runtime dispatch
coverage.

The nearest apparent downstream consumer is
`map_manager_mm_exec_TM` (`ai_compiledscripts_global_mapmanager.c`). Its init
path copies `world_ID` and `world_entrance_ID` into the E3 handoff fields
(offsets 18208 and 18212), and its transition tail maps `world_ID` to a BF
world key through `WORLD_Key_Map_ID/Key`, chooses `SpecialLoad` or `Editeur`,
and may replace the entrance with the current authored entrance. However, the
function resolves and reads `i_special_init`, `i_engine_mode`, `world_ID`, and
`world_entrance_ID`; it does not resolve or read `load_world`. No source
callsite from the exit writers into this map-manager function was recovered.
Thus this is a confirmed consumer of the destination fields in its own
execution path, but not a confirmed consumer of the request latch or proof
that it runs because one of those exits set the latch.

The API route is also distinct in the recovered source. Iris registers
`WORUniverseAdd`, `WORActive`, and `WORLoadList` in
`ai/ai_functions.c:169-178`; the wrappers in `ai/ai_worldfunctions.c:24-63`
and `:100-120` call `WOR_pst_Universe_AddWorld` or
`WOR_pst_Universe_AddWorldList` directly. Iris's `WORLoadList` implementation
does not inspect `univers.ova`; `world.c:2705` performs the world-list load
(including deletion of current universe worlds in the shown implementation).
The generated BGE script corpus has no direct callsite to these three APIs,
according to a search of `ai/ai_bge/ai_compiledscripts_*.c`. `WORReinit` is
another independent path: its wrapper sets `ENG_gb_NeedToReinit`, and the
engine frame loop calls `AI_ReinitUniverse` and `ENG_ReinitOneWorld`; it does
not select a world from the shared request fields
(`ai/ai_worldfunctions.c:142-150`, `engine.c:655-668`).

**Exact edge map supported by source:**

```text
exit / E3 / message / Mdisk script
  └─ writes univers.ova.{load_world, world_ID, world_entrance_ID}
       ├─ no matching load_world reader found in supplied Iris source
       ├─ map_manager_mm_exec_TM reads world_ID + entrance on its own path
       │    └─ map number → BF world key → SpecialLoad or Editeur
       └─ WORLoadList(key) → WOR_pst_Universe_AddWorldList(key)
            [separate API path; no script callsite linked to the writers]

WORReinit() → ENG_gb_NeedToReinit → AI_ReinitUniverse / reinit loaded worlds
             [runtime reset; no destination selection]
```

This negative result is scoped to the supplied Iris C/source corpus and its
recovered compiled-script callsites. It does not rule out an offset-based read
with no `load_world` symbol, a native executable path omitted from Iris, or a
runtime callback whose call edge is not represented in these files. A live
trace or retail PC executable analysis is still needed to close that edge.

**Jaded corroboration only:** Jaded has analogous world/universe and
transition-facing APIs, but it is a separate desktop/engine lineage. Its
behavior can guide where to look for a consumer; it cannot fill the missing
retail PC runtime edge without a PC Iris callsite or live trace.

In Jaded, `AI_EvalFunc_WORLoadList_C` likewise receives a world key directly
and calls `WOR_pst_Universe_AddWorldList`; before that it resolves Jaded's
fixed-world preload requests (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_wor.c:560-630`).
The engine implementation is in `WORuniverse.c:188`. A source-wide search of
the Jaded paths found no `univers.ova`/`load_world`/`world_entrance_ID`
consumer, so this corroborates the existence of a direct world-list API but
does not establish that the Iris shared-field protocol reaches it.

### Message IDs are receiver-local, not a global event enum

The Iris sources contain incompatible handlers for numeric message `49`. The
companion brain consumes `Int1`/`Int2` as a status/action handoff; the Crochax
cinematic receiver consumes `f2` as a scale; and the Secundo SFX receiver uses
`f2`/`f1` as light/alpha factors. The same integer therefore has no reliable
meaning without the resolved recipient and its active AI callback. Script
inspection must preserve the sender, target, target owner/model, and callback
reader together. JadeView's **Potential message routes** already requires a
statically resolved target and a matching queue reader before presenting a
route; unknown targets remain raw evidence instead of being assigned a global
message label. Cinematic timeline descriptions apply the same rule to type 49:
a reachable companion receiver exposes status/action fields, Crochax exposes its
scale use, and Secundo SFX exposes the light/alpha fields. Other receivers get
only generic reader evidence when available. These descriptions identify
callback semantics but do not claim that the message was delivered or the
callback executed.

One relevant gameplay-to-pose path is in
`external/irisEng/ai/ai_bge/ai_compiledscripts_companion.c`:
`companion_co_exec_get_message` receives message `49`, and for `Int1 == 4`
stores `Int2` in `Perso_ID_action[des_i_friend]` and `i_attente_action`, then
re-enters `companion_co_init_select_mode`. The selector calls `ACTActionSet`
for a valid stored action. `Zone_ChangeStatus` and elevator/enterprise
companion-management code provide source-level writers for such messages, but
their presence does not prove a given scene sent one or that its gate passed.
The start-resource inventory describes `Perso_ID_action` as a 12-element
integer array at `univers.ova` offset 884; it does not expose the value held in
that array at a particular cinematic entry.
The retail `univers.ova` payload (`71003FF2`) has a zero-length serialized
default-value buffer. This is distinct from an invalid variable offset: the
descriptors describe runtime slots, but this model file does not provide their
initial bytes. Iris/Jaded then read the model's callback references after that
buffer. The startup audit now reports `NoSerializedDefaults` for this case
instead of marking every slot out of bounds. It does not infer that these slots
start at zero or identify their live values; callback execution and other
runtime initialization still need to be traced.
For Peyj in `FF00791B / BF003BD1`, the archive has only a root-transform clip
track and no linked action request; the owner graph finds the `_basic_peyj`
companion brain, but no serialized event evidence identifies the active action
at clip start. This is a useful generic handoff hypothesis, not a confirmed
runtime explanation for that specific T-pose.

The shared `ES_au_sol` model (`71003F6E`) appears on 359 owners across 91
containers in the all-world retail script index. Its `play_events` callback
contains a generic state branch: at jump node 10 it tests capability `0x100`
and identity flag `0x02000000`; when both pass it freezes player input and
issues `TRAPlay` for owner `SceneEvents` indices 3, 4, and 5. Otherwise it
unfreezes input and changes callback tracks 1 and 2 to the Reflex and Mdisk
handlers. At jump node 8, the Mdisk travel path can run a configured status
change, write `i_objet_mdisk_map` and `des_vers_entree_numero` into shared
world-load state, and stop the current AI track. These are source control-flow
branches. JadeView shows the callback and the serialized owner/event links but
does not know the live jump node, identity/capability state, or whether an AI
runtime dispatched either branch.

The source is `external/irisEng/ai/ai_bge/ai_compiledscripts_es_au_sol.c`
(`ES_au_sol_esas_play_events`); `ai_eventtrackfunctions.c` and
`gameobject.c` establish that `TRAPlay` addresses the target's runtime EVE list
loaded from `SceneEvents`. The world-script audit now includes embedded EVE
indices alongside linked event commands, so a reviewer can verify whether a
requested index exists in a specific serialized owner list. For example,
`FF005833 / D90057CC` (`00_02_home_decryptage`) contains
`ES_MDisk_0_2.gao` (`D9005D6E`, model `71003F6E`, `SceneEvents=C1009BF9`).
The callback's indexed `TRAPlay` commands describe possible event routing, not
proof that these tracks were active during the Mdisk scene.

The per-channel archive audit now includes decoded calls and typed messages,
and resolves event/message references to names with world scope. In
`C1009BF9`, channel 3 at time 0 issues `TRAPlay(6)` on `ES_MDisk_0_2.gao` and
tests saved flag 2165. The nested channel 6 targets
`OBJ_00_02_ordi_mange_cd.gao` and contains four keys over three seconds, with
no recognized literal commands. Channel 5 toggles `TXT_lecteur.gao` invisible
at time 0 and visible at 2.333 seconds, and carries messages whose EVE track
execution object resolves to `_PJ_Camera.gao`; the type-55 payload additionally
references the Mdisk controller and its `_WP` object (`Int1=0`, `Int2=-569`).
The type-55 payload does not establish that Sally is the recipient: Jaded's
`EVE_Event_AIFunction_Play` sets the current object to the EVE track target,
and Iris `MSGSendAll` stores the message in that object's extended message
list. The camera main loop drains that list and applies type 60; it has no
type-55 branch. Sally's separate script does have a type-55 handler, but its
semantics cannot be transferred to this camera-targeted message. In the same
EVE list, type-60 messages on `_PJ_Camera.gao` with `Int1=1` / `Int1=0` match
the recovered camera start/reset branches. For modes 1/2 the callback reads
`Gao` and `Gao2` as destination and look-target objects, `Vector1` / `Vector2`
as camera/target offsets, `Float1` as focal value, and `Float2` as transition
speed (zero requests a cut); mode 0 clears cinematic camera state. Other
`Int1` values enter a separate camera/effect branch and are not given this
description. JadeView labels the object as the EVE execution target and keeps
the type-55 payload raw. Source: `external/jaded/src/libraries/AIinterp/Sources/Events/EVEnt_aifunc.c`,
`external/irisEng/ai/ai_messagefunctions.c`, and
`external/irisEng/ai/ai_bge/ai_compiledscripts_camera.c` / `ai_compiledscripts_sally.c`.
This trace reveals a nested event chain and named object dependencies in the
retail data; it still does not prove the outer `ES_au_sol` branch was reached.

### Static script inspection when cinematic playback is unavailable

The Cinematic Timeline inspector no longer requires a linked shared animation
clip to expose the selected world's script data. The world-only scope starts
from script owners in that world, then follows serialized OVA object
references, decoded `Events` / `SceneEvents` tracks and their uniquely
resolved cross-world targets. This reuses the same world-aware key resolver as
the clip scope; ambiguous external keys remain unresolved.

When no shared clip is available, the inspector can still show serialized OVA
values, model callback slots and their Iris-derived scan span, world scheduler
flags, static trigger findings, event-track literal calls and typed messages,
callback-reachable SceneEvents operations, action-kit merges, linked-object
calls, and source call paths. This is archive/source inspection only. It does
not evaluate the game VM, live callback timers, save-state flags, world
activation, message dispatch, or whether an actor is visibly active at a given
time. A missing playable clip therefore no longer hides static evidence, but
the inspector does not claim to reconstruct runtime behavior from that
evidence alone.

The `00_02_home_decryptage_cine` production audit (`FF005833` container,
`BF000127` world, `BF00218E` clip) verifies this cross-system path. Owner
`CINE_APRES_MDISK_EVT02.gao` (`BF00218D`) links `SceneEvents` to `BF00218E`,
the 44.78-second cinematic record. Its decoded timeline has 131 literal
commands. At 0 seconds it hides `PNJ_Drone_Mdisk_R.gao` and
`PNJ_Drone_Mdisk_V.gao`; at 30.35 seconds it shows both, with another show
command at 44.7 seconds. The R variant resolves to the `_basic` script owner
in `00_02_home_decryptage`, brain `7200512B`, model `720050F1`, and its
configured `Drone_Mdisk_Drone_Mdisk_init` callback. Iris source reachability
from that callback includes wait, movement, plug, dialog-check and visual
helper functions. This establishes both a timeline visibility route and a
separate gameplay-style AI implementation; it does not establish which route
caused any observed pose at runtime.

The same timeline enables XMEC type 10 on `DEC_00_02_moteurV.gao` at 41.53
seconds. At 43.22 seconds it adds control flags `0x600000` to
`DEC_00_02_helicesV.gao` and removes those flags from `Cine_Bone04_ventilo.gao`;
particle and halo capability changes bracket the event. These decoded calls
provide a source-backed timeline explanation for the rotor/ventilation
sequence without assigning undocumented meaning to the raw control flags. The
audit is `artifacts/callback-span-audit-v68/FF005833-BF00218E-script-graph.json`.

### Engine-side object → brain → callback load path

Iris confirms that a GAO stores separate typed references for its appearance,
brain instance, event list, and sound bank. In `gameobject.c`, the `0x20`
extension flag reads an AI-instance key and queues `AI_ul_CallbackLoadInstance`;
the `0x02000000` flag reads an EVE-list key and queues
`EVE_ul_CallbackLoadListTracks`. `PcSceneLoader.ReadObjectExtensions` mirrors
these two serialized links as the brain (`q`) and `SceneEvents` (`i`) records.
The base `.gao` animation `Events` key is a different field parsed in
`PcSceneLoader.ReadBaseObject`.

The AI instance loader reads two more typed keys: an AI model and optional
instance OVA. The model loader reads `(key, tag)` pairs, where `.ova` supplies
model defaults and `.ofc` supplies functions. `AI_ResolveInstanceRef` resolves
model references and initializes instance data; instance OVA values are merged
over same-named model values by `AI_ResolveCarDecHiHiEditor`. The function
loader first tries to bind a resource key to Iris's compiled-function registry;
when no compiled implementation matches, the serialized AINode body remains
available for interpretation. This is why an OVA reference, a callback model,
and a compiled function are separate edges in the asset graph, rather than one
monolithic script blob.

Runtime dispatch is also context-sensitive. `AI_ExecCallback` selects the
configured model function at the callback index and then invokes matching
`AICBAdd` registrations. Source call sites show callbacks 0 and 1 in geometry
render paths, callback 3 during animation processing, and callback 2 from the
world engine loop. Separately, `AI_Exec` calls `AI_RunInstance` for eligible
objects during the world AI pass. That ordinary brain execution is distinct
from indexed callback dispatch. Thus a script can have visible, animation-time,
or per-frame brain behavior that does not run merely because JadeView loaded the
object and its brain.
Previewing a callback should therefore expose its dispatch source and input
context; it should not call every configured callback as though all four were
scene-start hooks.

These findings give a reusable join for future asset-loading and script preview
work: `GAO typed link → resolved AI instance → model defaults/functions →
instance overrides → callback dispatch site → engine calls and referenced GAOs`.
The remaining gap is runtime ordering and predicates: static OVA references do
not establish that a render, world, or animation callback was dispatched for
the current frame, nor that its conditions passed. Relevant sources:
`external/irisEng/gameobject.c`, `external/irisEng/ai_common.c`,
`external/irisEng/graphicsobjects/geo.c`, and
`external/irisEng/animation.c`.

### World-loading and activation commands

The cinematic script inspector now describes the world-management calls whose
PC Iris implementations are available. `WORUniverseAdd(key)` loads and
initializes a world, resolves and activates its objects, and returns its
universe slot. `WORActive(slot)` and `WORDesactive(slot)` change which already
loaded world is active. `WORGetObject(worldSlot, objectSlot)` resolves an
object-table entry, while `WORUniverseDestroy(slot, flags)` deletes a loaded
world; PC Iris consumes the second integer but does not use it.
`WORRefreshVisible(slot)` and `WORRefreshVisibleOfActor()` rebuild visibility
tables for a world or the current execution object's world.

`WORLoadList(key)` has substantially different scope: the PC Iris
`WOR_pst_Universe_AddWorldList` implementation deletes existing universe
worlds, clears global graphics, audio, and texture state, reads the requested
world-list key, merges its member worlds, resolves AI, activates objects, and
attaches the result as the current world. It is a world transition/replacement,
not a request to add all listed GAOs to the currently assembled cinematic.
Jaded's desktop source has an additional fixed-world resolution step before
the same load-list routine, so its behavior should not be copied into the PC
Iris preview without data proving the target version uses it. `WORLoaded` in
PC Iris is a stub returning constant 1, not a reliable loaded-world predicate.

JadeView reports these calls as static script findings. It does not execute
world replacement, activate external worlds, or infer that every referenced
world belongs in the selected cinematic. The source behavior explains a
general asset-loading path, while the caller's trigger and runtime timing still
need to be established from its script graph and game state.

An archive-backed example distinguishes visibility refresh from world loading:
the `FF001FBE` / `35009F74` graph has four `WORRefreshVisible(0)` calls in
`camera_speciale_cs_wait`, `camera_speciale_cs_mode_inventaire`, and
`camera_speciale_cs_mode_look`. The graph records camera capability tests on
some paths before the refresh. The sampled `BF00218E`, `BF003BD1`, and
`3500B8EA` graphs have no `WORLoadList`, `WORUniverseAdd`, or
`WORUniverseDestroy` calls. That absence is limited to those decoded script
graphs; it does not rule out a world transition initiated by another owner or
external game state.

The source-reference inventory is now also joined against the cinematic's
assembled object set in `--cinematic-script-graph-audit`. On `3500B8EA`, the
reachable owner graph produced 337 owner/key reference rows; 192 reference rows
point to keys already included through scene, track, or serialized OVA links.
The remaining rows include gameplay managers, inventory/UI and weapon helpers.
They are useful evidence, but a callback being source-reachable does not show
that it runs during this cinematic. JadeView therefore reports these as
possible script dependencies and does not automatically add them to the scene.
This avoids pulling gameplay-only objects into cinematic previews before the
callback dispatch and runtime gate are established.

The \`3500B8EA\` graph confirms this distinction for the generic cinematic
manager. \`CINE_BIGINTRO_EVENTS.gao\` resolves \`INV_MatFade.gao\` in
\`Cine_Events_cine_init\`, then passes that resolved pointer through a local
assignment chain to \`GFXMaterialSet\`. That call is nested under the
\`i_sf_fade\` gate and an additional saved-flag/fade-priority/time condition.
The owner's serialized/model \`i_sf_fade\` value is \`0\`, so the fade material
reference is available to the script but this instance's source data does not
request that fade branch. The script-graph report now connects the object key,
source API call, branch predicates, and OVA values used by those predicates.
This is stronger than an unqualified reference list, but the preview still
does not evaluate global saved flags, clock state, or actual callback dispatch.

### Script object references are lookups, not GAO load requests

PC Iris resolves object references with `AI_ul_RealResolveOneRefObject` →
`AI_ul_RealResolveOneRef` (`external/irisEng/ai_common.c:456-472`). The latter
looks up the key in the BF index and then asks the LOA address table for the
already loaded object; it does not enqueue the GAO's loader. By contrast,
loading an AI instance, its model, callback functions, and variable blocks uses
explicit `LOA_MakeFileRef` requests (`ai_common.c:1296-1406,1447-1452`). World
assembly and world-list loading determine which game objects are resident
before these script references are resolved. This confirms why reachable
source references are useful diagnostics but cannot safely be treated as an
automatic list of scene additions. JadeView now labels that distinction in
the callback inspector and resolves reference names against actor roots and
their component meshes as well as standalone world objects.

There is an important separate edge: while parsing an EVE/`SceneEvents` list,
`EVE_ul_CallbackLoadListTracks` explicitly queues each non-null object target
(the record's animation-track form is handled separately) through
`LOA_MakeFileRef(..., OBJ_ul_GameObjectCallback, ...)`
(`external/irisEng/events.c:2585-2670`). The later
`EVE_ResolveListTrackRef` resolves the loaded target pointers, and playback
temporarily makes each target the current event object. Thus an event-list
target can cause its GAO resource to load even when no AI OVA field references
it. That is a resource-load edge, but it is not by itself proof that the GAO is
attached to the owning world's object table, activated, visible, or running AI;
those are separate world/activation relationships. JadeView's SceneEvents
target resolver should preserve this distinction: show an unresolved target as
a possible typed dependency, and add it to a preview world only when world
membership or a tested attachment path is established.
Jaded's editor-side EVE loader makes the same typed object-reference request
(`external/jaded/src/libraries/AIinterp/Sources/Events/EVEload.c:541-576`),
corroborating the asset-link interpretation without proving retail-PC world
attachment behavior.

### Runtime effects are registered separately from asset loading

The Iris PC AI bindings distinguish world residency from effect registration.
`PAG_AddGenerator` resolves an already available effect-object pointer and asks
the current object's PAG component to create a runtime generator
(`ai_pagfunctions.c:5-15`); it returns a generator ID and does not load a GAO.
`PAG_SetGeneratorFreq` only changes an existing generator by ID. `GFXAdd`
registers an object in the current world's GFX list through `GFX_i_Add`
(`ai_gfxfunctions.c:50-57`), while `CreateExplosion` constructs a transient
GFX effect from the current object's geometry/material
(`ai_gfxfunctions.c:562-583`). These calls can make an already loaded asset or
transient effect render, but they do not add arbitrary archive resources to
the world's object table.

`GFXAddWorld` is unsafe to interpret as a loading operation: the available
Iris PC implementation is a TODO stub, only toggles a presentation flag when
a display exists, and returns `-1` (`ai_gfxfunctions.c:17-31`). The function
name alone is not evidence of a world or asset load. For previews, classify
calls by their implementation and required pre-existing objects; only
`WORUniverseAdd` / `WORLoadList` provide confirmed world-loading paths in the
examined PC source. Runtime PAG generator creation is not yet simulated by the
cinematic preview, which currently handles the source-confirmed
`PAG_SetGeneratorFreq` behavior for the camera-following storm controller. A
lexical census of the supplied Iris BGE compiled-script modules finds no direct
`PAG_AddGenerator` call sites and 26 `PAG_SetGeneratorFreq` call sites using
generator ID `1`. These are source call-site counts, not runtime invocation
counts; they support the static-generator path used by current scenes but do
not prove that dynamic generator creation is impossible elsewhere.

### Special AI actor slots are not archive keys

The type-40 object resolver has one exception before the BF/LOA lookup:
keys `1` and `2` are returned unchanged. Its consumers convert those values to
`AI_MainActor[key - 1]` (`ai_common.c:252,1496,1522,1583-1594`). They therefore
refer to the two runtime actor slots, not GAOs with archive keys `00000001` or
`00000002`. Script-reference labels and source-dependency joins now exclude
those two values. The rule is deliberately scoped to AI object references;
other serialized fields and synthetic timeline fixtures can use small numeric
keys with different meanings. The source-call inspector applies the same rule
when joining OVA object fields to individual API call sites.

### The inventory-screen video controller is state-driven

The `BF00218E` script graph identifies `Cine_ecran_explik_invent.gao` as a
procedural Bink material using `mpegplayer_init`; its instance links
`des_o_Interrupteur` to `C100AEF9` and has `des_i_SubMatLevel=1`. Iris's retail
function (`ai/ai_bge/ai_compiledscripts_misc.c`, `mpegplayer_init`) resolves
that already-loaded object, tests capability `0x100`, and also checks the
configured saved-flag gate. If neither condition is true, it stops the current
AI track and hides the screen owner. Otherwise it enables the video material
state, makes the object visible, and switches its AI track to
`mpegplayer_loop`. The loop is dispatched only after the source AI node changes
the current track; merely finding the function in the model does not start it.

The cinematic has a direct `OBJ_CapaSet(2, 0)` event for the screen owner at
44.783 s, but the game script first evaluates the external interrupter's
capability and save flag. In the ordinary `mpegplayer_init` branch, source node
15 overwrites the serialized `i_Play` value from interrupter capability `0x100`
before it selects the video material; a positive `des_i_sf_on_off` can also
start playback. JadeView now derives the initial video state from the switch
only when the ordinary branch is identifiable and the relevant save-flag
controls are zero. Otherwise it retains the existing preview fallback and
labels that state as estimated. The preview does not claim to know the global
save flags or whether the native AI callback was dispatched. This separates three things that can look like one
missing-asset bug: the screen GAO/material may be loaded, its Bink source may
be attached, and the script may still keep it hidden or on a non-video material
state. For the ordinary init path with a unique loaded switch, zero special
save-flag controls, and no sound-status gate, JadeView's initial owner visibility
matches the source-derived state. It then follows the same start/stop/toggle
state used by the procedural video preview, and the result is sampled by
cinematic GLB export. When a nonzero `des_i_SND_modifier`, save-flag branch,
unresolved switch, or ambiguous owner prevents an exact initial answer, both
the video and owner use the documented playback fallback and the UI labels it
estimated. This keeps the screen from being rendered independently of its
video state without implying that the runtime gate is known. This remains a
bounded reconstruction of the `mpegplayer` family, not execution of the AI VM
or proof that a runtime callback ran. The report is
`artifacts/callback-driven-eve-audits/FF005833-BF00218E-script-graph.json`;
the retail cinematic export audit now verifies the monitor's GLB scale is zero
at clip time 0 and changes to one at the serialized 44.783333 s toggle. This
proves the owner-visibility channel is present in the exported animation, while
the still-image limitation for the Bink material remains.
the finding is specific evidence for a reusable rule: conditional appearance
requires both the owner GAO and the runtime script state, not a blanket
dependency expansion.

### World residency is built from the world object group

The retail Iris load path gives a concrete order for deciding which scene
assets can exist before scripts run. `WOR_ul_World_LoadCallback` reads the
`.wow` world record and queues its object-group resource through
`LOA_MakeFileRef(..., WOR_ul_GameObjectGroupCallback, ...)`. The group callback
reads object keys and queues each GAO through `OBJ_ul_GameObjectCallback`.
During GAO parsing, typed fields queue their own resources, including visual
geometry/material, actor action kits, AI instances, `Events`/`SceneEvents`,
sound banks, lights, and other linked data. This is the loader's dependency
graph; an AI model's OVA object reference is not another edge in that loading
graph.

After resource resolution, `WOR_pst_World_Load` calls
`WOR_World_JustAfterLoadObject` for the loaded objects. That routine
reinitializes each GAO, resolves collision/sound/animation references, attaches
it to the world, and optionally resolves its AI instance. `WORUniverseAdd`
then initializes the world, activates eligible objects, and builds the AI EOT
table. This separates four states that a preview should keep distinct:
archive resource exists, world group makes the GAO resident, world activation
allows it to participate, and its AI callbacks are actually dispatched.

Consequently, a missing actor or effect should be debugged in this order:
check whether its GAO key is in a loaded world's object group; check whether
its typed GAO links (geometry, animation, script, sound, etc.) resolve; inspect
initial visibility/capability/activity state; then inspect script gates and
dispatch. Automatically adding every source-referenced GAO would skip these
steps and can pull in unrelated gameplay helpers. The source supports
world-level loading (`WORUniverseAdd`) and world-list replacement
(`WORLoadList`); it does not establish a generic script API that loads an
arbitrary GAO into an existing world by key. This last point is a boundary of
the examined bindings, not proof that no private engine path exists.

Source references: `external/irisEng/world.c`
(`WOR_ul_World_LoadCallback`, `WOR_ul_GameObjectGroupCallback`,
`WOR_pst_World_Load`, `WOR_World_JustAfterLoadObject`,
`WOR_pst_Universe_AddWorld`), `external/irisEng/gameobject.c`
(`OBJ_ul_GameObjectCallback` typed references), and
`external/irisEng/ai/ai_worldfunctions.c` (`WORUniverseAdd`, `WORLoadList`).
Jaded corroborates the broad engine design but its desktop-specific world
transition behavior should not replace the retail Iris call path.

### Nested native calls and sound-completion handoff

The call-graph generator now balances nested C parentheses instead of treating
the first semicolon as the end of an API call. The previous regex swallowed
`AITrackCurChangeNow` when it appeared inside the condition containing nested
`SNDGetStatusModifier` calls. It also now attaches resolved function keys to
both `fct_` and `fctr_` locals. The regenerated index covers 681 functions and
1,720 dispatch edges; verification checks the resolved sound-completion edge.

The recovered `Cine_Events_cine_play_eventtrack` chain ends by executing
`Cine_Events_Cine_exec_EndOfEvents` (function key `3500B726`). That function
queries the three configured music, ambience, and voice sound modifiers. If
any raw modifier status has bit `0x4`, it replaces the current AI track with
`Cine_Events_cine_test_fin_sons` (key `3500B021`). That callback checks the
same three statuses on subsequent AI updates. Once none has bit `0x4`, it
sets the three stored fade-time values to `1.0`, calls `SND_i_TrackSeti` for
IDs `0x3F2`, `0x3FC`, and `0x410` when any configured modifier ID is valid, and
then either returns to track 2 through `f_track_init` when `des_i_ripley == 1`
or destroys the cinematic script owner. If the initial status check has no
`0x4` bit, `EndOfEvents` instead flushes the configured sound streams and
continues its immediate end path.

This is evidence that the cinematic's audio tail is controlled by live sound
modifier state, not only by the EVE duration or the audio cue list. JadeView can
now expose the source transition and key, but does not emulate the native sound
modifier state machine or its track operations. Keep `0x4` as a raw PC Iris
predicate in diagnostics: Jaded's desktop `SNDGetStatusModifier_C` returns a
different state-mask value, so its implementation does not establish the
meaning of that bit for the PC game build.

### `01_04_entrepot_liberation_mili_cine` residency check

An archive-backed script-graph audit of container `FF001FBE`, clip
`35009F74`, resolves the loaded scene worlds `01_Carte3D_RdC`,
`01_04_entrepot_liberation_mili_cine`, and `01_04_entrepot_liberation_mili`.
The assembled participant roster already contains `_PJ_Militaire.gao`
(`7100596B`), `OBJ_Perle.gao` (`3D003D82`), and
`OBJ_PerlePrendre.gao` (`3D003D85`). The reachable script-owner graph reports
no `WOR*` world-management calls for this clip. In this case, their absence in
a rendered frame is not evidence that the script failed to load those GAOs;
the archive and scene assembler resolve them as participants. The next checks
should target runtime activation/visibility, the selected actor pose, and
material state. `_PJ_Militaire.gao` has the generic `companion_co_init`
gameplay brain, but static callback reachability alone does not prove that
brain's gameplay actions execute in a cinematic preview. Audit output:
`artifacts/export-verification/FF001FBE-35009F74-script-graph.json`.

### Camera-following PAG effects: `SFX_tempete.gao`

The intro audit (`FF0045A2`, main clip `3500B8EA`) resolves owner
`SFX_tempete.gao` (`3D002F6C`) to model `3D002F6F` and callback
`SFX_colpag_cam_scp_loop`. The owner has no serialized `Events` or
`SceneEvents` link. Its instance variables instead reference
`DEC_3_9_brumephare.gao` (`3D002DDF`), `Part_Fight.gao` (`3D002DCB`), and
`Part_Fight1.gao` (`3D002DE3`). Thus the two `Part_Fight` objects are runtime
effect carriers selected by the owner's script data, not evidence of many
standalone animation scenes.

The PC Iris callback checks capability mask `1` on its current owner. When the
bit is set, it clears forced-invisible on both PAG objects and sets generator
ID `1` to 50 and 20 particles per second. It periodically chooses a 1–4 second
delay, toggles a mode bit, and repositions/aims the two effects from the live
`_PJ_Camera.gao` position, sight, horizon, and banking vectors. When the bit is
clear, it sets both generator frequencies to zero. Jade Engine's shared source
confirms that `OBJ_CapaTest` is a bit test of the object's extended capacities,
`OBJ_FlagInvisibleSet` controls the force-invisible flag, and the PAG frequency
setter updates a generator by ID (`ai_compiledscripts_sfx.c:9093–9550`,
`AIfunctions_obj.c:2680–2720,2970–2980,5152–5159`,
`PAGstruct.c:2025–2040`).

This explains why loading the carriers and decoding their geometry is
insufficient: the callback owns visibility, emission, and camera-relative
placement, and—when the owner is activated and admitted to the EOT callback
table—it runs as an AI update rather than being sampled from a standalone `i`
clip. The static archive cannot establish that this owner was actually
scheduled during a particular game run. JadeView now has a callback-specific
preview adapter. It binds the callback by name, resolves both effect objects
from the owner's OVA fields, applies the owner's capability changes from the
cinematic timeline, and updates force visibility, generator rates, transforms,
and orientation on seek/playback. The source callback references the global
`_PJ_Camera.gao` key (`71003F56`), while the cinematic route for that receiver
can select a distinct camera object. JadeView applies the effect placement from
the active route's evaluated render pose, including route changes, so the
assembled cinematic world does not need a duplicate global camera object.
The mode's next random delay is not recoverable from a timeline alone, so the
adapter uses the 2.5-second mean of Iris's `MATHRandFloat(1, 4)` interval. The
in-game PRNG sequence, EOT scheduling, and whether the callback was activated
remain unverified; this is a deterministic preview approximation, not full AI
execution. The adapter does not treat an OVA object reference as an activation
or resource-load request. The generic actor-motion fallback separately
requires a serialized owner `Events` or root cinematic `SceneEvents` link, so
an unlinked `i` resource cannot become a motion merely because one of its
tracks targets the same GAO. Other compiled callbacks still need their own
source/API evidence before JadeView can preview them. The implementation is in
`CinematicCameraPagControllerReader.cs` and `CinematicCameraPagTimeline.cs`;
`CinematicCameraPagChecks` covers owner binding, capability gating, active
preview-camera pose changes, repeatable backward seeking, missing-camera
behavior, and allocation-free warmed evaluation. This camera substitution is a
preview adaptation: the in-game `_PJ_Camera` and the selected cinematic route
can diverge in native execution.

A real archive frame audit of `FF0045A2 / 3500B8EA` confirms the owner gate is
off at 33 s (`cap=0`, both PAG rates are zero) and on at 54 s (`cap=1`, rates
50/20; both OVA references resolve and the effects receive the camera-relative
pose). Iris's serialized timeline activates `SFX_tempete.gao` at 53 s by
clearing control flags `0x00600000` and setting capability `1`; it repeats
those commands at 117.35 s. Therefore the star visible in the 33 s frame is the
separate cinematic flash controller, not these camera-following PAG carriers.
The PAG adapter does not explain or fix the still-open 33 s spiral/lightning
effect. The frame audit now prints callback owner, gate, references, effect
visibility, frequency, and preview position to keep these systems distinct.

### Script execution is a runtime relationship

Jade Engine keeps a world's AI-bearing objects in `st_EOT.st_AI`.
`AI_MainCall` (`AIengine.c:1077–1180`) walks that table, skips objects already
processed through hierarchy, optimized-culled objects, and objects with
`ForceInactive` or `SectoInactive`, then advances each remaining AI instance.
This means an object reference in a script model is not by itself proof that
the referenced object is loaded, active, or being updated.

There is a second, dynamic relationship for engine callbacks.
`AI_EvalFunc_AICBAdd_C` (`AIfunctions_ai.c:535–580`) appends a
function/type/context tuple to a destination object's AI instance;
`AI_EvalFunc_AICBDel_C` (`AIfunctions_ai.c:607–635`) removes the matching tuple
by nulling its function. `AI_ExecCallback` (`AIengine.c:1350–1420`)
checks the engine state, inactive/pause flags, AI instance/model validity, and
then runs only entries whose callback type matches the requested phase.
`AI_ExecCallbackAll` enumerates the world's AI EOT and requests the same phase
for each object (`AIengine.c:1200–1234`). The callback's stored context object
can differ from the destination whose instance owns the callback table.
The phase IDs are lifecycle points, not generic gameplay triggers: before and
after display, after record, after blend, destroy, short-way, and sector
activation/visibility on/off. Jaded routes them from rendering, animation,
world-sector, and destruction code (`STRrender.c`, `ANIrender.c`, `ENGcall.c`,
`WORmain.c`, `AIfunctions_obj.c`, and `AIfunctions_way.c`).

Iris's BGE source makes the ordinary script scheduler more concrete and shows
why these two mechanisms must not be conflated. An OVA's five callback keys
become AI tracks 0–4 (`external/irisEng/ai_common.c:1124-1325`). On instance
reset, `AI_ReinitInstance` copies those five model functions into mutable
instance slots and initializes their jump cursors and timers
(`ai_common.c:375-430`). `AI_MainCall` visits world AI-table entries and,
after its per-frame processing, distance and parent-hierarchy checks, advances
the instance unless runtime flag `flags2 & 0x400000` blocks it
(`ai_common.c:765-804`). `AI_RunInstance` skips a disabled model, visits the
active span from `end` through `start`, subtracts frame delta from each track's
wait timer, and runs due tracks while retaining their jump position
(`ai_common.c:711-762`). Thus a track key is a default/update function, not a
one-shot event timestamp.

`AITrackChange` replaces one mutable track function and expands the active span;
`AITrackStop` clears it and contracts the span (`external/irisEng/ai/ai_aifunctions.c:113-186`).
By contrast, `AI_ExecCallback(owner, phase)` invokes the model's indexed
instance function for that phase and then any dynamically registered entries
with the matching phase id (`ai_common.c:812-895`). `AICBAdd` stores a
function, phase id, and context object on a destination object's AI instance;
the context may differ from that destination
(`external/irisEng/ai/ai_aifunctions.c:409-494`). The engine calls AI updates
in the regular frame loop after EVE and animation passes
(`external/irisEng/engine.c:185-214`), while lifecycle callbacks are requested
from engine/render/animation paths at their specific phases.

Callback phase IDs also do not identify a single dispatch site. Retail Iris
ID 2 is a world-table pass after dynamics, collision, records and hierarchy
(`engine.c:226-288`); ID 3 has separate ANI pre-render, ANI initialization and
world-render sites (`animation.c:2391,2646`; `world.c:2465`); IDs 0/1 occur
before/after geometry and string rendering (`graphicsobjects/geo.c:772-778,
888-893`; `geostr.c:2894,3143`). Direct `AI_ExecCallback` requires a live AI
instance/model, both blocking `flags2` bits clear, and at least one fixed or
dynamic callback; it runs a matching fixed function before registered entries
(`ai_common.c:812-897`). Callback-all additionally requires the engine running
and walks valid entries from the world's AI-callback table (`:899-915`). The
caller has extra gates: for example, ANI pre-render requires a visible object
with a skeleton group, while geometry callbacks may redirect a bone GAO to
its animation initialization/reference GAO (`animation.c:2396-2410`;
`graphicsobjects/geo.c:766-780,879-895`). JadeView now exposes these shared
dispatcher checks, site-specific caller conditions and target-routing rules
beside each indexed call. They are source descriptions, not evaluated state.
Jaded corroborates the callback architecture, but its direct gate uses its own
ForceInactive/InPause controls (`AIengine.c:1372-1377`) and must not replace
the Iris retail-PC `flags2` checks.

Practical consequence for JadeView: replaying an EVE/SceneEvents timeline and
then evaluating the default five AI tracks as though they were clip tracks
would be incorrect. A faithful preview needs separate state for (1) instance
track functions, cursors, timers and track changes, and (2) registered engine
callbacks keyed by owner, phase and context, plus engine eligibility state.
Static BF data can seed some defaults and explicit event calls, but cannot
reveal which runtime gates passed, which tracks were changed before the clip,
or which callbacks were registered by branches. A preview must label these as
declared defaults, event-derived changes, or unknown runtime state.

JadeView now has a bounded serialized mutation scan for `AITrackChange`,
`AITrackStop`, `AITrackCurChange`, `AITrackCurChangeNow`, and
`AITrackCurStop`. It reports literal track indices and uniquely model-linked
function references in the global-startup function inspector and archive audit.
Dynamic operands, non-pointerref targets, out-of-range indices, and unlinked
functions remain explicit unresolved candidates. The scan is not a VM
execution trace: it does not resolve branches, execute `AIExecute`, or decide
whether a track mutation ran. This gives a source-backed way to inspect
possible track handoffs without inventing the actor's live track state.
In the installed retail `sally_clean.bf`, the archive audit finds one literal
current-track handoff candidate in `univ_init.ofc` (`71003FF1`): node 1387
references `univ_go.ofc` (`71004002`) through `AITrackCurChangeNow`. This
matches the source operation's role in replacing the current track and
continuing its scheduler immediately, but the archive alone does not prove
that the initialization branch ran in the user's session.

The cinematic Script Owners view now applies the same source-backed analysis
to each owner's reachable model callbacks. In `FF005833 / BF00218E`, it found
511 possible track mutations across 45 owners. Across 332 current-track calls,
the indexed graph yields one possible slot for 330 and multiple possible slots
for two. These slot numbers come from serialized callback roots and may-reach
edges (`AIExecute` preserves context; literal `AITrackChange` switches to its
destination slot); they do not reconstruct the live AI track. Dynamic/direct
engine-callback routes remain unknown, and the displayed function path is not
an execution trace. See `docs/cinematic-script-system.md` for the UI and its
runtime limitations.

A lexical census of Iris BGE's compiled C sources finds 312
`AI_EvalFunc_AICBAdd_C` call sites in 51 script modules: 176 register
before-display (type 0), 21 after-display (type 1), 93 after-record (type 2),
and 22 after-blend (type 3). This count records source call sites, including
mutually exclusive branches; it is not a count of registrations that occur in
a particular scene or save. It gives JadeView a tractable first set of
source-backed callback phases to map to the script call graph.

JadeView's script-owner inspector can therefore report serialized script model
functions and object links, while an “active now” answer needs additional
runtime state: EOT membership, current sector/inactive flags, and dynamically
registered callback tuples. Those are not fully recoverable from a static BF
archive. The cinematic script inspector now exposes source-reachable
`AICBAdd`/`AICBDel` call candidates with their raw context/destination/function
operands, uniquely resolved function keys, literal callback IDs, branch
alternatives, and indexed Iris dispatch sites. Uncertain assignments remain
visible as alternatives instead of being collapsed to one target. These are
possible mutations of the callback list, not a reconstruction of the live
list: branches, initialization/reinitialization, owner eligibility and actual
dispatch remain unresolved. Jaded's AI interpreter source is the architecture
cross-check; Iris remains the retail-PC behavior reference.

### Event-track owners also require world dispatch eligibility

The retail Iris frame loop calls `EVE_MainCall`, then `ANI_MainCall`, then
`AI_MainCall` for a world (`external/irisEng/engine.c:185-214`). These are
separate passes: an EVE track can update transforms or issue timeline commands
before the regular animation and AI passes, but the presence of a decoded EVE
list does not mean that its owner is being ticked.

`WOR` builds `eventGameObjectsTablePF` from activated objects whose runtime
table flags include `0x02000000` (`world.c:727-748`). On each event pass,
`EVE_MainCall` iterates only that table and requires a valid object, an attached
EVE component, no pending-inactive runtime bit `0x00400000`, and—when the
owner's extra flag `0x4` is set—a nonzero distance before it calls
`EVE_b_RunAllTracks` (`events.c:2683-2720`). Thus there are at least three
separate facts to inspect: the EVE resource was decoded, its owner qualified
for the world's event table, and the runtime pass actually advanced its
tracks. The latter two depend partly on runtime world/visibility state and
cannot be inferred from the clip's duration or target list alone.

Jaded's later desktop implementation uses the same per-world event-table
dispatch shape, but its gate is expressed through its own control/LOD helpers
(`external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c:1600-1647`).
Use it as architectural corroboration; keep Iris's flags and predicates for
the retail-PC preview. This also explains why a script owner may be correctly
listed in JadeView yet its attached event track or effect does not run. JadeView's
owner lifecycle now adds a fresh-load EVE dispatch projection: it combines the
serialized EVE-table identity bit with both modeled initial world-activation
modes and reports a candidate, a clear table-request bit, or blocked initial
activation. The phase remains `runtime unknown`; the projection does not confirm
that the owner entered the rebuilt table, passed EVE's live distance/inactive
gates, or advanced a track. A live game trace is still required for
`runtime-confirmed` status.

### Global universe AI startup and tick gate

The retail archive's start key (`71003FF9`) resolves to a global AI instance,
model `univers.ova` (`71003FF2`), and seven linked `.ofc` functions. The model
OVA declares 140 variables; `Perso_ID_action` is a 12-element integer array at
offset 884. Its serialized initializer buffer is zero bytes long. That means
the archive contains the variable schema but no initial byte values for those
slots; it does not establish that the values are zero. JadeView's startup audit
now reports `NoSerializedDefaults` rather than incorrectly treating the
descriptors as out-of-bounds reads.

Iris's loader reads five per-track callback keys immediately after that
defaults buffer. The audit now preserves those slots separately from the
model's ordinary `.ofc` link list. In this archive the slots decode as
`[0, 0x0C, 0x08, 0, 0]`; neither nonzero value resolves to a BIG entry. They
therefore remain unresolved callback references. The seven named `.ofc` links
in the model are not substituted by slot order: doing so would conflate two
different serialized tables and invent a callback binding.
The JadeView OVA inspector now displays these five slots separately, with
empty, uniquely resolved, missing, and ambiguous BIG-key states.
The low keys remain a source/archive mismatch: Iris passes each nonzero slot
to `LOA_MakeFileRef` with the required-reference flag, and `LOA_Resolve`
performs a BIG-key lookup (`loading.c:257-277, 336-356`); the retail archive
has no entries keyed `0x0C` or `0x08`. Jaded's desktop editor adds one
important qualification: when built with `ACTIVE_EDITORS`,
`BIG_ul_SearchKeyToPos` first calls `LOA_ul_ExFileKey` (`BIGkey.c:292-312`).
The editor can enable that redirect with `/exfilekey[file]`; the text file
contains `oldkey newkey` pairs (`EDIapp.cpp:797-817`, `LOAdefs.c:2139-2196`).
The non-editor/runtime branch searches the archive key table directly. No such
redirect file has been identified for this retail archive, so the two slots
remain unresolved in JadeView; they are not evidence that the ordinary model
`.ofc` list should be substituted.

The loader path is explicit in Iris: `WOR_Universe_Open` calls
`AI_pst_LoadInstance`, which resolves the AI model and its OVA/OFC references,
initializes the instance's callback slots, and allocates the save buffer for
the archive start key (`world.c:2511-2518`, `ai_common.c:1441-1454`). The engine
frame loop calls `AI_RunInstance` for that universe instance only while
`AIUnivIsRunning` is true (`engine.c:528-535`). `AI_RunInstance` walks its
scheduled callback tracks, decrements per-track wait timers, and resumes a
callback only when its timer reaches zero (`ai_common.c:711-754`). The loaded
script body `univ_init.ofc` also contains literal assignments to shared state,
including `i_engine_mode`, `i_INV_OptionsFlags`, and `world_ID`; those are
serialized instructions, not proof that a particular startup branch executed.

This makes the startup inventory useful for identifying the data and code
available to the global AI, but not for reconstructing save-dependent values
or claiming that an initialization callback ran. A future JadeView runtime
would need an explicit initial-state source and callback scheduler state before
it could safely emulate these global effects during cinematic preview.

For the confirmed distinction between BF OVA defaults and packed saved values,
see [Retail PC AI save-state inputs](gameplay-save-state-format.md). The save
layout is source-derived but has not yet been validated against a matching
retail slot file.

### Dynamic callback-list lifetime and repeatable EVE side effects

The dynamic `AICBAdd`/`AICBDel` list is runtime state, not a serialized BF
snapshot. Iris and Jaded both clear registrations when an AI instance is
reinitialized; duplicated AI instances start with an empty dynamic list, and
destruction frees it. BF inspection can therefore enumerate candidate Add/Del
sites and their statically resolvable operands, but cannot report the current
registration count without a runtime trace.

An EVE AI-function wait can call its node repeatedly before the key advances.
The wait path returns before the `EventDone` marker, so the node's DoOnce guard
does not suppress a retry. Since Add appends without duplicate suppression and
Del removes one exact registration at a time, a registration inside such a
node can be duplicated on each retry. This is a source-proven mechanism, not
proof that a particular BF script contains this pattern. JadeView should model
the callback collection as an ordered multiset and mark wait-reachable Add
sites as potentially repeatable; it should not promote candidate sites to
confirmed active registrations without resolved runtime branches and dispatch.

Evidence: Iris `events.c:1768-1785`, `ai_common.c:375-420, 947-963`;
Jaded `EVEplay.c:891-933`, `AIfunctions_ai.c:551-579, 607-635`,
`AIengine.c:1444-1497, 1705-1728`.

### AI callback return values also control scheduler flow

An AI function's result is also a command to the scheduler. Jaded names the
relevant sentinels in `AIengine.h:173-178`: `StopInstance=1`, `StopAI=3`,
`CurChangeNow=5`, and `Destroy=6`. In `AI_RunInstance` (`AIengine.c:1012-1053`)
the per-track wait timer is reduced by the frame delta before a due function
runs. Afterward, `StopInstance` and `Destroy` end this instance pass;
`StopAI` returns false, which makes the world-level AI table loop stop at that
owner (`AIengine.c:1165-1171`). `CurChangeNow` decrements the loop index before
the normal increment, so the replacement callback on that same track runs
immediately in the same instance pass. That is a same-frame handoff, not a
next-frame transition.

Retail Iris has matching numeric-result handling in `ai_common.c:729-760` and
the same consequence for `StopAI` in `AI_MainCall` (`ai_common.c:803-805`).
Its `AITrackCurChangeNow` replaces the current function and jump cursor, then
returns 5 (`ai_aifunctions.c:252-280`); Jaded does the corresponding update
and returns `AI_CR_CurChangeNow` (`AIfunctions_ai.c:290-329`). Iris also
continues normally when the requested function is already current, avoiding a
spurious repeat (`ai_aifunctions.c:252-255`). This explains why two
source-reachable callbacks can execute back-to-back before another AI owner is
visited, and why a stop result can affect unrelated later owners in the same
world pass.

JadeView can show the static replacement path, but its preview does not
execute the per-instance result protocol or model how a result changes the
remaining world-table iteration. A generic runtime model needs the callback
result, per-track timer/cursor, track order, and world-pass continuation as
separate state. The source establishes these semantics for Iris and is
corroborated by Jaded; a static BF path still cannot establish that a retail
callback returned a particular result in a given session.
