# Engine callback assignments

Script-model OVAs contain two different callback tables. JadeView already reads
the five track callbacks used by `AI_RunInstance`. Iris reads an optional second
table after those five keys: a count followed by function keys indexed by engine
callback ID (`external/irisEng/ai_common.c:1292-1318`). This second table is
now decoded separately as `EngineCallbackSlots` and shown under **Callbacks and
call paths** in the cinematic script inspector.

`AI_ExecCallback` dispatches the model's function at the requested ID when the
owner has an AI instance and model, the owner passes runtime flags checks, and
that ID is in range (`ai_common.c:812-871`). It also executes
matching dynamically registered instance callbacks. An OVA assignment alone
does not prove that the object was instantiated, reached the corresponding
engine phase, or passed those checks.

Jaded names the callback IDs in `external/jaded/src/libraries/AIinterp/Sources/AIstruct.h:50-59`:

| ID | Jaded callback name | Evidence in bundled Iris source |
|---:|---|---|
| 0 | Before display | Direct dispatch in `graphicsobjects/geostr.c:2894` and `graphicsobjects/geo.c:772,778`. |
| 1 | After display | Direct dispatch in `graphicsobjects/geostr.c:3143` and `graphicsobjects/geo.c:888,893`. |
| 2 | After record | World-frame dispatch in `engine.c:288`, after the record stage. |
| 3 | After blend | Animation dispatch in `animation.c:2391,2646`. |
| 4 | When destroyed | Name is present in Jaded; no direct dispatch call site was located in the bundled Iris source. |
| 5 | Short-way event | Name is present in Jaded; no direct dispatch call site was located in the bundled Iris source. |
| 6–9 | Sector active/inactive and visible/invisible | Names are present in Jaded; no direct dispatch call site was located in the bundled Iris source. |

The inspector labels IDs 0–3 as having an Iris dispatch call site. IDs 4–9
retain the Jaded names but are explicitly marked as lacking a direct call site
in this Iris tree. Unknown IDs are preserved as numeric IDs rather than
rejected: the source loader accepts a serialized count and `AI_ExecCallback`
checks the requested ID against that count.

The IDs 0–3 call sites have distinct caller gates; they are not one generic
per-frame callback. All use `AI_ExecCallback`, which additionally requires an
extended object with an AI instance, a model, clear runtime `flags2` masks
`0x400000` and `0x4000000`, and either a configured fixed slot or dynamic
callback registration (`ai_common.c:812-871`). A matching fixed model slot is
called before matching dynamic registrations.

| ID | Iris call sites and caller-specific gates |
|---:|---|
| 0 | `GEO_Render` (`geo.c:761-780`) before geometry processing, only in the material/draw-list branch; `STR_Render` (`geostr.c:2879-2895`) when the string render counter differs and string material exists. A bone GAO can route to its ANI reference GAO if that referenced object has AI; otherwise the visual/string GAO is the target. |
| 1 | `GEO_Render` (`geo.c:794,879-895`) after processing, only on the valid-vertices path; `STR_Render` (`geostr.c:3134-3144`) after rendering when the GAO has an extended component and AI instance. Geometry targets follow the same possible bone-to-ANI-reference routing as ID 0. |
| 2 | One `AI_ExecCallbackAll(world, 2)` pass in `ENG_OneWorldEngineCall` (`engine.c:226-288`), after dynamics, collision, records, and hierarchy. It iterates the current world's AI callback EOT table and skips invalid entries before the shared callback gate. |
| 3 | `ANI_BeforeRender` (`animation.c:2390-2410`) requires a valid visible object, `flags2 & 0x4 == 0`, GAO flag `0x2`, and a skeleton group; `ANI_Render` (`animation.c:2631-2648`) requires `animPtr[0]` and that the actor was not initialized in `ANI_MainCall`; `WOR_Render_All_GO` (`world.c:2419-2465`) requires a valid visible world object with `flags2 & 0x4 == 0` and display flag `0x80000` clear. |

These are source-derived gates. They establish when Iris can attempt each hook,
not whether the corresponding object passes runtime state checks in the retail
game. The scene-specific join below adds archive assignments and EVE target
links, but does not remove those runtime conditions.

### Per-object lighting in display callbacks

Iris display hooks can change lighting for one draw without changing the
material or its alpha. In `geo.c`, callback ID 0 runs before the object's
geometry is rendered and ID 1 runs after it (`geo.c:761-780, 888-895`). A
dynamic callback registered through `AICBAdd` executes with the registering
owner as its current object (`ai_aifunctions.c:409-435`,
`ai_common.c:861-875`). The before hook can save the display context's RLI
factor/destination color and set temporary values; the after hook restores
them. Iris applies the RLI factor and color while preparing the object's
lighting (`geo.c:526-543`).

For example, the lighthouse shield registers `sbp_bdisplay`/`sbp_adisplay` on
its linked dome. The first hook sets the dome's RLI factor from the controller
field `f_transparency_coef` and sets its RLI destination color to white; the
second restores the previous values (`ai_compiledscripts_sfx.c:7846-7986,
8643-8650, 8795-8815`). This can look like a fade, but it is not a material
alpha animation. JadeView applies a bounded, draw-scoped preview for uniquely
paired Before/After registrations when callback paths, registration gates, and
RLI inputs can be resolved. The lighthouse controller's dynamic
`f_transparency_coef` path is evaluated by replaying ordered assignments and
preserving OVA fields across callback transitions. Ambiguous paths, unresolved
branches, and unsupported input expressions remain unapplied. This is still a
preview of a bounded source-derived path, not proof that the hook ran for a
particular retail frame.

The bundled RLI value-flow index is generated with the optional
`-ValueFlowOutputPath` argument to
`tools/Generate-CinematicScriptOvaFieldAccessIndex.ps1`. It retains ordered
source assignments and branch conditions for callback-local variables and OVA
fields in RLI-related functions. JadeView loads these source facts by reachable
callback path and evaluates them when callback windows, branches, and dynamic
inputs resolve; it does not claim that every assignment executed in the game.

These engine hooks are not the five AI tracks. `AI_RunInstance` advances the
track callback span and its per-track wait timers (`ai_common.c:711-760`),
whereas `AI_ExecCallback` invokes an indexed model hook at display, animation,
world, or other engine call sites. The viewer inventories hook assignments and
previews the bounded RLI case described above; it does not execute arbitrary
hook code or emulate the full engine phase.
That is why a callback assigned to an effect or actor can explain a possible
activation path without proving the effect should be visible at a given frame.

The parser accepts the source layout's optional tail and rejects a truncated
count/key list. Synthetic regression coverage checks no-tail, populated,
empty-entry, and truncated cases. The labels are source metadata, not a claim of
retail-version parity; Jaded is corroborating source and Iris is the primary
pre-release implementation used by this project.

The production `--script-model-variable-audit` was run on `sally_clean.bf`,
container `FF0045A2`. In that container it decoded 248 script owners; 51 had
nonempty engine-hook assignments (83 assignments total): 33 ID 0 hooks, 31 ID
1 hooks, and 19 ID 2 hooks. All 83 function keys mapped to names. No ID 3 or
IDs 4–9 assignment appeared in this sample. This confirms the optional table is
present and useful in real archive data, beyond synthetic parser fixtures. It
still reports configured functions only; EOT participation, runtime flag
checks, and which display/frame hooks actually fired are not serialized by
this audit.

The all-world `--archive-gameplay-script-index` now joins the engine-hook tail
to the model's explicitly typed `.ova` references and retains that OVA key as
provenance. If more than one referenced OVA supplies a nonempty callback tail,
the rows remain ambiguous candidates instead of being chosen by resource
iteration order. On the installed `sally_clean.bf`, all 15,950 brain owners
resolved, and the census found 4,179 nonzero engine-hook assignments across
3,060 owners. Every assignment had a unique model-OVA binding: ID 0 had 1,165,
ID 1 had 1,048, and ID 2 had 1,966 (these counts sum to all 4,179
assignments). The separate Iris source-site inventory confirms direct dispatch
call sites for IDs 0–3; ID 3 has a call site but no configured assignment in
this archive. No assignment was found for IDs 4–9, and the Iris source has no
direct call site for those IDs. These are configured assignments in this
retail BF archive, not runtime dispatch coverage. In particular, the archive
provides no configured-hook support for applying Jaded's sector callback path
to BGE, but this does not rule out other sector-related behavior or
callback-free activation logic in retail BGE.

### Engine hooks attached to SceneEvents targets

The per-container `--script-model-variable-audit` can be joined with a
world-scoped script graph to connect these systems without assuming that a
scene name implies script behavior. For `FF005833 / BF00218E`
(`00_02_home_decryptage_cine`), the audit resolves 73 models and reports no
multiple-OVA bindings. Joining serialized SceneEvents track targets to the
owners' OVA-bound hooks shows these concrete relationships:

| SceneEvents owner / event | Target | Configured engine hook | Serialized EVE activity |
|---|---|---|---|
| `CINE_PEYJ_REPARLECTEUR_EVENTS.gao` / `BF00012B` | `Cine_SFX_ligth_boom.gao` (`BF00024C`) | ID 2 `SFX_Halo_Dynamique_SFXHD_afterrec` | `OBJ_CapaSet` toggles capability `0x100` around 16.03, 16.10, and 21.50 s. |
| same | `Cine_SFX_Eclair.gao` (`BF000162`) | ID 2 `SFX_Lightning_SFX_Lightning_afterrec` | `OBJ_CapaSet` toggles `0x100` around 14.08–21.50 s. |
| same | `Cine_SFX_Etincelles01.gao` (`BF00015D`) | ID 2 `SFX_Etincelles_SFX_Etincelles_afterrec` | `OBJ_CapaSet` toggles `0x100` repeatedly around 14.37–17.17 s. |
| `CINE_EXPLIK_INVENTAIRE_EVENT.gao` / `BF00026A` | `Cine_AppareilPhoto_explikinventaire.gao` (`BF000A37`) | IDs 0/1 are empty in its model OVA | Track 13 changes control flags; tracks 14–15 animate the same target. |
| same | `OBJ_Interrupteur_ordinateur.gao` (`C100AEF9`) | ID 0 `Switch_s1_exec_set_constante`; ID 1 `Switch_s1_exec_reset_constante` | The EVE has a track targeting the switch. |
| `CINE_REACTION_MDISK_0002.gao` / `3500ADA0` | `Cine_Mini_Disk.gao` (`3500B798`) | ID 2 `snap_oriente_on_bone_soob_snap` | Track 13 targets the disk. |
| `CINE_APRES_MDISK_EVT02.gao` / `BF00218E` | `Cine_SFX_Light_Peyj.gao` (`BF002145`) | ID 2 `SFX_Halo_Dynamique_SFXHD_afterrec` | Track 17 targets the halo. |

This is evidence for two distinct mechanisms on the same scripted sequence:
EVE serializes time-indexed target commands (including capability changes),
while a model OVA serializes engine-phase hooks. For example, Iris dispatches
ID 2 once from the world-frame path in `ENG_OneWorldEngineCall`
(`external/irisEng/engine.c:288`), after dynamics/collision and hierarchy
processing. The lightning after-record function updates effect endpoints from
the current object and its configured destination
(`external/irisEng/ai/ai_bge/ai_compiledscripts_sfx.c:19550-19574`). An EVE
capability command and an ID 2 callback are therefore separate possible
contributors to an effect; seeing one does not prove the other fired. The
archive and source show a plausible, connected path, but runtime activation,
visibility, callback scheduling, and successful execution remain unverified.

The switch provides a similar distinction: its ID 0/1 assignments are present
in the model OVA, while the EVE graph proves only that the switch is a track
target. The recovered `Switch_s1_exec_set_constante` and reset implementations
contain saved-flag behavior (`external/irisEng/ai/ai_bge/ai_compiledscripts_switch.c:2073-2147`);
static track targeting does not prove that the timeline invokes those hooks.

Reproduce this targeted join with the following commands. The script graph
output is a generated local artifact; the model audit supplies the OVA-bound
callback assignments:

```powershell
dotnet run --project tests/ExportVerification -- --script-model-variable-audit "<path-to-sally_clean.bf>" FF005833
dotnet run --project tests/ExportVerification -- --cinematic-script-graph-audit "<path-to-sally_clean.bf>" FF005833 BF00218E
```

The checked run wrote
`artifacts/export-verification/FF005833-model-variable-references.json` and
`artifacts/export-verification/FF005833-BF00218E-script-graph.json`. These
reports are ignored local artifacts and are not committed.

Regenerate the census with:

```powershell
dotnet run --project tests/ExportVerification -- --archive-gameplay-script-index "<path-to-sally_clean.bf>" "<repository-root>"
```

### Track callbacks and per-owner lifecycle

Iris and Jaded show that ordinary AI tracks and engine-phase hooks are separate
runtime mechanisms. A model OVA provides up to five initial function slots,
one for each AI track. On instance initialization, `AI_ReinitInstance` seeds
the live instance slots from the model and resets track execution state;
instance-bound OVA data and variables can affect the effective owner state.
`AI_RunInstance` then advances each active track using its callback cursor and
wait/timer state. Track-changing operations can replace or stop a slot, so the
model's initial function list is not a complete playback trace.

The track built-ins confirm that this is a small cooperative scheduler, not a
set of independent animation clips. `AITrackChange(track, function)` replaces
that track's callback and resets its jump node; `AITrackCurChange` does the
same for the executing track. `AITrackCurChangeNow` additionally returns a
special scheduler result so the new callback can be entered immediately.
`AITrackStop` clears the callback and cursor. `TIMEWait(seconds)` stores a
positive per-track delay and suspends at the next node; each AI tick subtracts
the frame delta before the function resumes. The `during`/`duringinit`
keywords use the same timer with a negative remaining value to keep a timed
block active across calls. These distinctions matter when previewing state:
the visible current callback alone cannot tell whether its node is waiting,
re-entering, or has just been replaced.

Engine hooks are dispatched separately by numeric hook ID and engine phase.
They are not automatically the next step after an ordinary track callback.
Names such as `firstinit`, `init`, `reflex`, and `afterrec` are function names,
not a guaranteed lifecycle ordering. An `afterrec`-named function may be an
ordinary track callback or an engine hook; determine this from the serialized
slot and its OVA provenance. An object can also need to be in the relevant
runtime callback table before the engine invokes a hook. Static hook
assignments therefore prove a configured path, not that the path ran for a
particular owner or frame.

Iris also supports runtime hook registration. Its `AI_EvalFunc_AICBAdd_C`
stores a hook ID, function and target GAO in the registering AI instance's
dynamic callback list; `AI_EvalFunc_AICBDel_C` removes a matching registration
by clearing its function pointer. During `AI_ExecCallback(owner, id)`, Iris
first runs the owner's model-OVA function for that ID, then walks the owner's
dynamic registrations for the same ID. A registered function executes with
its registered GAO as the active script object, while the owning AI instance
provides the registration list. This allows one actor/controller to arrange
for a later engine phase to update another object. The dynamic list is cleared
by `AI_ReinitInstance`, so it is runtime state, not recoverable from static OVA
assignments alone.

Dispatch also depends on the engine's callback-owner table: Iris builds that
table from objects carrying object flag `0x20`, sorts it with the EOT priority
routine, and the world-phase walker calls hooks for those entries. The direct
`AI_ExecCallback` path skips owners with `flags2 & 0x400000` or
`flags2 & 0x4000000`. These filters and callback registrations are additional
reasons a configured function name cannot prove execution. They also explain
how EVE capability/state changes may be necessary to make an object eligible
for a later script-driven visual update, though the exact mapping from each
serialized EVE opcode to runtime flags should still be traced separately.

Iris source points: model and instance function loading at
`external/irisEng/ai_common.c:1287-1320,1376-1407,1434-1455`, track seeding at
`AI_ReinitInstance` (`:375-447`), per-instance track execution at
`AI_RunInstance` (`:711-760`), owner enumeration and engine-hook dispatch at
`:765-897`, world callback-table construction at
`external/irisEng/world.c:680-760`, dynamic registration/removal at
`external/irisEng/ai/ai_aifunctions.c:405-472`, and instance-hook dispatch at
`external/irisEng/ai_common.c:812-897`. Jaded corroborates the shape in
`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1788-1799` and
`AIengine.c:964,1200,1350,1444-1474`; its engine's phase loop differs in
details, so do not transfer Jaded scheduling constants directly to retail
BGE.

Track manipulation is implemented in
`external/irisEng/ai/ai_aifunctions.c:113-285`; wait and time state in
`external/irisEng/ai/ai_timefunctions.c:8-21` and
`external/irisEng/ai/ai_keywords.c:155-197`.

For a generic JadeView runtime preview, keep per-owner state for the five
track slots (current callback/node, wait state, and active/stopped status)
separate from engine-hook registrations and their runtime gates. Initialize
from the resolved model OVA, then apply owner/instance overrides where the
archive defines them. A source-backed callback evaluator should return an
explicit outcome such as applied, gated, unknown, or unsupported. This lets
the viewer show what is known without inferring behavior from names or
executing arbitrary OFC code. Current inspectors expose configured slots,
hooks, static reachability, and EVE/message evidence, but do not yet simulate
this live lifecycle.

The serialized-OFC inspector also folds a bounded set of source-confirmed
integer expressions when both operands are known: arithmetic, comparisons, and
bitwise OR/AND/XOR. The same fold can resolve a constant-return predicate before
a conditional jump. Iris's operator implementations (`ai/ai_keywords.c:270-577,
690-1050`) are the primary evidence. Float coercion, arrays, variable reads,
multi-operator stack expressions, and branch-dependent assignments remain
unresolved; this is static constant propagation, not VM execution.

The script inspector also detects a narrow, data-flow-backed callback pattern:
`OBJ_GetChannelProg` on the callback's current object, assigned to one unique
local, then passed to `OBJ_SetChannelProg` for one uniquely resolved OVA or
source object reference. It retains source and target channels, the callback
path, source lines, and common branch guards. When a matching guarded
`OBJ_FlagInvisibleSet(target, 0)` exists, it reports that alongside the channel
handoff. This makes cross-object morph transfer visible without treating it as
executed playback. The analyzer rejects ambiguous targets, non-unique value
assignments, unrelated branch paths, and getters whose receiver is not the
callback's current object. Iris's channel getter/setter and invisible-flag
implementations are in `ai_gameobjectfunctions.c:1319-1337,2430-2472`.
The analyzer also examines source calls in all functions indexed for the
owner's model OVA. If a function has no recovered dispatch path from the
serialized callbacks, it is labeled as a configured model-function candidate;
membership in the model is not treated as proof that the function ran.

The call-graph generator now resolves `AITrack*` function-pointer operands by
the key supplied to Iris's `AI_ul_RealResolveOneRefFunction`, rather than
requiring a decompiler-generated variable name to repeat the caller and target
function names. Conflicting assignments to the same pointer variable are kept
unresolved. Regenerating the checked-in graph from the bundled Iris source
added 62 source-supported dispatch edges and removed none. This recovers the
shield sequence `apparition0 → apparition → apparition1 → abp_apparition2`,
which carries the linked dome's channel progression. These are source call
edges; runtime gates, timer state, and the exact callback tick remain separate.

The inspector now has a bounded callback-branch preview for source-confirmed
cross-object channel transfers. It evaluates simple Boolean predicates,
effective OVA integer fields, and `OBJ_CapaTest_C_CURRENT` using the selected
clip's preview capability state. It can also fold a prior literal `MSGSendEx`
or `MSGSendAll` only when its execution target resolves to the callback owner,
then apply source-indexed message-handler field writes whose guards are
supported. The result is explicitly three-valued: true, false, or unknown.
This is a diagnostic estimate at the selected playhead, not callback execution:
queue capacity and ordering, AI polling cadence, timer progression, and
unsupported predicates are not simulated. Saved-flag assumptions can be
forwarded to the bounded callback reducer, but do not establish that the owner
was admitted to the game's AI scheduler.

The callback branch inspector also exposes temporary set/clear assumptions for
literal `ARRBitTest` calls only when the source expression resolves to Iris's
`Flag_speciaux_sauve` buffer. Unspecified flags stay unknown, and unrelated
bitsets remain unsupported. It lists source-reachable state-effect calls behind
those gates (including visibility operations) and evaluates their source
predicates under the selected assumptions. When a callback's entry and exit
are source-resolved, those assumptions can drive its supported bounded preview
effects; they still do not execute arbitrary init code or prove that the owner
was admitted to the runtime scheduler. This supports examining both sides of
source-confirmed save-state gates without silently treating the BF archive as
a save file.

The message target distinction follows retail Iris. `MSGSendEx` and
`MSGSendAll` enqueue on the current execution object's message queue
(`external/irisEng/ai/ai_messagefunctions.c:120-138,200-225`); GAO/Gao2 fields
inside `MSGSendAll` are payload, not the queue recipient. `MSGSendAllOwner`
instead targets the runtime `MSG_gpo_Owner` registration
(`:356-388`), so the preview leaves it unresolved. This prevents a message
whose payload merely mentions an object from being mistaken for a message
delivered to that object. Iris dispatch order also keeps the EVE and AI passes
distinct (`external/irisEng/engine.c:183-215`), so an EVE message is not
evidence that its recipient callback ran in the same tick.

### Callback effects have a runtime interval

An AI callback is the currently selected function on an object's AI track, not
a permanent condition attached to that object's model. In retail Iris,
`AITrackCurChangeNow` immediately replaces the current function
(`external/irisEng/ai/ai_bge/ai_compiledscripts_sfx.c`, lighthouse shield
callbacks); Jaded's corresponding `AITrackCurChangeNow_C` changes the current
track root (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_ai.c`).
Therefore a source-recovered callback preview must identify both its entry
transition and the first resolved outgoing replacement. Its state/morph writes
can be sampled through that interval and then held, unless a later EVE event or
another known runtime owner changes the same state. Re-evaluating the old
callback's guards at every later playhead is incorrect: a still-true capability
does not imply that the function remains active after its track was replaced.

The bounded reducer also samples uniquely resolved `OBJ_CapaSet` callback
effects. Iris applies the two 16-bit masks as `(current & ~remove) | set` to the
selected receiver (`external/irisEng/ai/ai_gameobjectfunctions.c:1516-1539`).
JadeView applies only literal masks; dynamic operands remain unresolved. The
resulting capability state is available to later supported callback predicates,
and a later explicit EVE capability command takes precedence. This captures a
generic input to script branching without executing arbitrary AI code.

It also samples uniquely resolved `OBJDrawMaskSet` calls when the target's
serialized visual-object flag `0x4000` is resolved. Iris applies set/remove
masks to the visual GAO's `objDrawMask` only when a basic visual component is
present (`external/irisEng/ai/ai_gameobjectfunctions.c:67-87`). JadeView maps
the result to the same draw-mask state used by direct EVE commands, so
visibility and synchronized-scene export agree. If the receiver's visual flag
is absent or ambiguous, the source call remains inspector evidence and is not
applied. This is a draw-mask change, not material alpha.

This boundary is now used by JadeView's bounded callback reducer and verified
against `FF0045A2 / 3500B8EA`: the physical dome receives its controller channel
state during the shield-opening callback and keeps the completed progression
after the callback exits. The same reducer previews uniquely resolved
`OBJ_FlagInvisibleSet`, `OBJ_FlagInactiveSet`, and `OBJDestroy` effects for any
uniquely resolved scene target, without requiring that target to have a morph
channel. It skips callbacks whose entry or exit interval cannot be bounded from
the current source/timeline evidence. This is a reducer for bounded source
paths, not an AI scheduler. Callback cadence, unresolved timers, arbitrary
code, registered per-object display hooks, and unknown save flags remain
outside its execution model.

### Callback visibility in synchronized GLB export (2026-10-25)

Synchronized scene export now samples callback-driven visibility through the
same `CinematicPlayback.Seek` path used by the preview. Resolved callback
targets are included in the export even when they have no direct EVE transform
track, and their callback entry times are added as animation boundaries. This
lets uniquely linked `OBJ_FlagInvisibleSet`, `OBJ_FlagInactiveSet`,
`OBJDestroy`, and supported draw-mask effects carry into Blender instead of
being preview-only. The exporter restores the pre-export object and actor
preview state after sampling.

This exports JadeView's bounded callback reduction, not arbitrary AI execution.
Unknown runtime gates remain unapplied, and callback-internal timing is sampled
at the resolved callback entry boundary. For source routes that depend on live
save flags, collision results, queued messages, or unresolved timers, the GLB
cannot claim the retail game's chosen branch.

### Callback-driven local material alpha (2026-10-25)

The same bounded callback path now recognizes source-reachable
`SetLocalAlpha` calls. Iris validates the receiver's material/submaterial,
selects the requested texture layer, and stores alpha as a five-bit value:
`(alpha >> 3) << 11`, preserving the lower planar/reflection bits
(`external/irisEng/ai/ai_materialandcolorfunctions.c:7-35,395-405`). The
preview resolves the callback receiver, material selector, and layer before
applying the value to the live renderer state; frozen material snapshots carry
the same layer value into synchronized GLB export. An explicit later EVE
`SetLocalAlpha` write supersedes the callback sample for that material layer.

Literal and source-value-flow expressions are applied only when they resolve to
an integer alpha in `[0,255]` and the callback path and branch are known.
For callback-local expressions, the evaluator also consumes the embedded
engine-call index's reaching assignments when they are uniquely resolved;
branch-specific alternatives retain their own guards, while ambiguous
definitions are excluded rather than guessed.
The Iris aircraft water-fade callback demonstrates the boundary: the index now
retains both clamped `v3` branches feeding `v4`. For this callback, JadeView
also resolves the source-reachable `OBJGetPos_C_CURRENT(&a1)` output from the
owner's sampled scene pose, evaluates the numeric clamp guards, and computes
the same five-bit material alpha (`external/irisEng/ai/ai_gameobjectfunctions.c:835-879`,
`external/irisEng/ai/ai_bge/ai_compiledscripts_actor_cv_aircraft.c:2867-2882`).
This is a generic callback value-flow path rather than an aircraft-name rule:
it applies when the current-object position call and its output local are
uniquely source-resolved. It samples JadeView's evaluated object pose; it does
not reproduce native callback cadence or arbitrary hierarchy/reference
semantics that are absent from the preview pose. Ambiguous writes, unsupported
expressions, unbounded or unknown callback gates, and runtime display-hook
cadence remain unapplied. The preview updates the stored alpha field; it does
not infer missing alpha-blend flags or reconstruct arbitrary material mutation
code.

Callback branch predicates now evaluate bounded scalar arithmetic, numeric
comparisons, 32-bit bitwise masks, and shifts with C-like operator precedence.
This covers common source guards such as animation ratios and capability/flag
tests when their operands are resolved. `TIMEElapsed_C` is evaluated only when
the timer field was uniquely written from the source-confirmed elapsed main
clock on the preceding callback path; a serialized timestamp by itself has no
known clip-relative origin. Pointer dereferences, collision queries, unknown
engine calls, and unavailable runtime state remain `unknown`; the evaluator
does not guess those branches.
