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
capability and save flag. This separates three things that can look like one
missing-asset bug: the screen GAO/material may be loaded, its Bink source may
be attached, and the script may still keep it hidden or on a non-video material
state. JadeView previews the decoded track and procedural video descriptor,
but does not yet emulate the screen AI instance's live gate or Bink control
loop. The report is `artifacts/callback-driven-eve-audits/FF005833-BF00218E-script-graph.json`;
the finding is specific evidence for a reusable rule: conditional appearance
requires both the owner GAO and the runtime script state, not a blanket
dependency expansion.

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
