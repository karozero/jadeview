# Jaded source map

Use this as an index when checking a JadeView behavior against the Jaded source.
Jaded is a later engine snapshot, not a verified 2002 build: its README says
the community-patched source is based on a copy from late May/early June 2006.
It may differ from the released BGE PC build. For retail-PC behavior, use
`external/irisEng` as the primary source;
use Jaded to find architecture, call paths, and hypotheses, then verify them
against Iris before changing JadeView semantics.

For a reverse-engineering question, give the Jaded reference agent the concrete
JadeView hypothesis or symptom. Ask for the call chain and exact source ranges,
then compare the retail Iris implementation and label archive facts separately
from runtime assumptions.

## Standing reference agent

`/root/jaded_reference` is the dedicated Jaded source specialist for this task.
Before making a reverse-engineering claim or changing JadeView behavior that
Jaded may clarify, consult this agent for source-backed call paths and exact
locations. Use its findings as architectural evidence, then cross-check retail
BGE behavior in `external/irisEng`; do not assume the pre-release Jaded branch
matches the 2003 PC game. If the specialist is unavailable, use this map and
inspect the cited source directly, marking any conclusions that remain
unverified.

## Subsystem entry points

| Area | Jaded source locations |
| --- | --- |
| Archive and resource lookup | `external/jaded/src/libraries/SDK/Sources/BIGfiles/BIGfat.c:63,135,372,434,682`; `BIGkey.c:292,358`; `BIGread.c:136,204,247`; `LOAding/LOAread.c:783,809,912` |
| World loading and object references | `external/jaded/src/libraries/ENGine/Sources/WORld/WORload.c:320,447,889,1922,2369`; `WORuniverse.c:188,629-633,715-821` |
| World activation and visibility | `external/jaded/src/libraries/ENGine/Sources/WORld/WORmain.c:47-96,166-191,282-350`; these are Jaded's gates and should not be assumed to equal retail `flags2`/distance/universe-slot rules |
| EOT scheduler tables | `external/jaded/src/libraries/ENGine/Sources/EOT/EOTmain.c:50-109,116-187`; animation table membership is derived from object identity flags and activated objects |
| EVE playback and event tracks | `external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c:219,246,374,768,1600`; `EVEload.c:102-132`; `Events/EVEconst.h:38-85` |
| TRA controls | `external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_tra.c:29-113,134-160,348` (`TRAPlayAll/StopAll/PauseAll/UnPauseAll`, per-owner play/stop/time) |
| AI owners and callback loading | `external/jaded/src/libraries/AIinterp/Sources/AIengine.c:964,1004,1074,1150-1170`; `AIload.c:2103`; `ENGine/Sources/OBJects/OBJinit.c:170-192`; function binding tables in `AIinterp/Sources/Functions/AIdeffct.h` |
| Animation and actions | `external/jaded/src/libraries/ENGine/Sources/ANImation/ANImain.c:701,755-787`; `ANIplay.c:1185+`; `ACTions/ACTcompute.c:580-700` |
| Camera | `external/jaded/src/libraries/GraphicDK/Sources/CAMera/CAMera.c:34,76,113,210,244,254,392-411`; camera object load/render in `CAMstruct.c:98,186-223`; world application in `ENGine/Sources/WORld/WORrender.c:297-336,2585+` |
| Audio banks and dialogue | `external/jaded/src/libraries/SouND/Sources/SNDbank.c:112-151,199-304,443-455,554-675`; dialogue track selection in `SNDtrack.c:307`; instance creation in `SND.c:781-825`; stream code is platform-specific, so do not infer PC retail from another backend |
| Materials, UV and alpha | `external/jaded/src/libraries/GraphicDK/Sources/MATerial/MATCompute.c:117,535,641,664,881`; draw paths in `MATDraw.c:112,177,580-654`; PC build variant in `MATerial/PCretail/MATCompute.c` |
| Lighting and world rendering | `external/jaded/src/libraries/GraphicDK/Sources/LIGHT/LIGHTcompute.c:2506,2860-2877,2970+`; `ENGine/Sources/WORld/WORrender.c:2585-2617,2886-2957,3090-3197`; platform-specific hardware paths exist |
| Modifiers, SPG, particles and effects | `external/jaded/src/libraries/ENGine/Sources/MoDiFier/MDFmodifier_GEN.c`; `MDFmodifier_SPG2.c:1536+`; `GraphicDK/Sources/PArticleGenerator/PAGstruct.c:1510,1913-1941`; `GraphicDK/Sources/GFX/GFX.c:421,539` and effect-specific files `GFXlightning.c`, `GFXflare.c`, `GFXsmoke.c`, `GFXfade.c`, `GFXwater.c` |

## EVE dispatch cross-check

The broad path is object/resource resolution, per-world activation, EOT
construction, active-world scheduling, then per-owner EVE checks. Jaded's
`WORmain.c:47-96` applies its sector/force-inactive controls;
`WORmain.c:166-191` rebuilds active-object membership; `EOTmain.c:116-187`
derives scheduler tables; and `EVEplay.c:1600-1650` walks event owners with
runtime eligibility checks. These gates are not interchangeable with retail
Iris's `flags2`, distance, and active-universe-slot conditions. See
[`cinematic-owner-start-lifecycle.md`](cinematic-owner-start-lifecycle.md) for
the retail-PC lifecycle and the static/runtime evidence boundary.

## AI callback track pass

Both checked implementations scan configured callback tracks in ascending
slot order. Jaded's `AI_RunInstance` loops from `uw_FirstTrack` through
`uw_LastTrack` (`AIengine.c:1004`); Iris PC initializes `end` to the lowest
configured slot and `start` to the highest (`ai_common.c:375-394`), then loops
from `end` through `start` inclusively (`ai_common.c:729-760`). Iris
`AITrackChange_C` expands those bounds when a callback installs a lower or
higher slot (`ai_aifunctions.c:113-139`). The active span skips empty callback
slots at execution time and each nonempty track runs only when its wait timer
has expired. The track order is not a Jaded-versus-Iris difference; use Iris
for retail semantics and Jaded as the corroborating architecture reference.
This static range still does not prove that an owner is in the world's AI
callback table or that its callback ran in a particular scene.

### AI track state lifecycle

The model's variable descriptor contains five serialized initial callback
function references. Jaded reads those five keys at
`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1788-1802`; its AI
instance resource reads only model and variable-descriptor references at
`:1999-2037`, and `AI_pst_LoadInstance` resolves references then calls
`AI_ReinitInstance` at `:2082-2103`. Iris's `AI_ul_CallbackLoadVars` reads five
initial callback keys at `external/irisEng/ai_common.c:1292-1297`; its
instance loader reads model and instance-vars references at `:1387-1408`,
then reinitializes the loaded instance at `:1441-1456`.

The five current function pointers, per-track resume nodes, wait timers, and
first/last active slot bounds are per-instance live state, initialized from
those model references rather than serialized in the instance resource:
Jaded fields are in `AIstruct.h:283-302`, reset/copy is
`AIengine.c:1444-1488`; Iris fields are in `external/irisEng/ai.h:97-109`,
reset/copy is `ai_common.c:375-413`. The five initial callback references are
serialized model configuration; a decoded callback slot is not proof that
the track remains installed or has executed after runtime mutations.

Jaded track commands are in
`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_ai.c`:
`AITrackChange_C` `:108-146` replaces a numbered slot when the function differs,
resets its resume node, expands the scheduled range, and initializes a
null-root C function's timer/sentinel; `AITrackStop_C` `:169-190` clears a
numbered slot and shrinks the range; `AITrackCurChange_C` `:209-224` replaces
the currently executing slot; `AITrackCurChangeNow_C` `:290-315` requests an
immediate same-slot restart when its target differs; `AITrackCurStop_C`
`:335-356` clears the current slot and adjusts bounds. Retail Iris has
corresponding implementations in `external/irisEng/ai/ai_aifunctions.c`:
numbered change/stop `:113-198`, current change/stop `:200-250`, and immediate
current change `:252-281`. Both versions use five slots and the same broad
distinction between scheduled replacement and immediate current-track
restart. Check Iris for retail semantics: its track-change index has no
visible runtime bounds guard in this function, while Jaded only clamps invalid
indices under `ACTIVE_EDITORS` (`:114-130`); do not rely on editor clamping as
game behavior.

Each `AI_RunInstance` walks scheduled slots in ascending order, subtracts the
frame delta from positive per-track timers, and runs only non-null functions
whose timer is due. Jaded: `AIengine.c:964-1066` (loop and timer at
`:999-1023`, return codes at `:1046-1049`); Iris:
`external/irisEng/ai_common.c:711-760` (loop/timer at `:729-744`, return codes
at `:752-759`). `TIMEWait` stores a resume node and per-track delay, Jaded
`Functions/AIfunctions_time.c:33-48`, Iris `ai/ai_timefunctions.c:4-22`.
The `AITrackCurChangeNow` return sentinel is 5 in both engines; the scheduler
decrements its slot index so that replacement can run immediately in that
pass. Jaded names sentinels in `AIengine.h:173-179`; Iris compares their
numeric values in `ai_common.c:752-759`. Ordinary current-track change does
not use this immediate-restart return path. These are scheduler mechanics,
not evidence that a callback's branch or wait command was reached.

`AI_MainCall` supplies another runtime gate before normal track scheduling.
Jaded iterates the world's AI EOT, skips holes/processed or culled owners,
resolves hierarchy, and checks AI presence plus ForceInactive/SectoInactive
and deferred flags (`AIengine.c:1074-1187`). Iris iterates the active-world
`AICallbacksTablePF`, with invalid/processed/distance/hierarchy checks and a
`flags2` gate before `AI_RunInstance` (`external/irisEng/ai_common.c:765-810`).
The gate differences are material: use the Iris path for retail-PC claims.

JadeView now has a per-owner **AI track provenance** view in the Script Owners
inspector (`docs/cinematic-script-system.md`, “AI track mutation candidates”).
It places source-call candidates below the five serialized initial callback
slots, shows literal slot/function operands, branch predicates, callback paths,
and source-confirmed operation descriptions, and labels transitions as
possible. For current-track mutations, it propagates possible slot provenance
from each configured callback slot through nested `AIExecute` and numbered
track-change edges. It unions all candidate slots; dynamic indices and direct
engine-callback paths remain unknown. This uses every source call-graph edge,
while the displayed callback path still keeps only the first route per
function. The current function, resume node, remaining timer, selected branch,
and whether `AI_MainCall` admitted the owner remain explicitly unknown. This
can expose missing or conditional animation handoffs without claiming that the
static inspector executed a script. These are possible source-level paths,
not proof of runtime reachability.

For any future inferred slot annotation, distinguish the caller's current
track from a function's destination slot. `AIExecute` calls a nested function
and restores the current function/resume-node context without changing
`AI_gul_CurrentTrack`: Jaded `Functions/AIfunctions_ai.c:35-66,73-101`, Iris
`external/irisEng/ai/ai_aifunctions.c:49-84,86-110`. Thus a nested function
reached from a known scheduled slot preserves that slot context. By contrast,
numbered `AITrackChange(slot, function)` installs the target in its explicit
slot; it does not change the currently executing slot. The direct engine
callback dispatchers (`AI_ExecCallback`) run model/dynamic functions while
setting the GAO and AI context, but do not assign `AI_gul_CurrentTrack`:
Jaded `AIengine.c:1350-1412`, Iris `external/irisEng/ai_common.c:812-897`.
Consequently paths through `AICBAdd` / callback dispatch lose any sound
current-slot provenance; a later `AITrackCurChange*` on such a path must be
reported with unknown slot.

`AITrackCurChange` changes the current slot's function but does not request
same-pass re-entry. `AITrackCurChangeNow` changes that same slot and propagates
sentinel 5 through `AIExecute`; the `AI_RunInstance` scheduler consumes it by
decrementing the slot loop index (Jaded `AIengine.c:1046-1049`, Iris
`ai_common.c:752-759`). A direct `AI_ExecCallback` ignores the return value of
`AI_pst_RunFunction`, so the immediate re-entry behavior is only established
when execution returns through `AI_RunInstance`; do not claim it for a direct
callback route. Iris uses internal sentinel 1000 for same-function no-op and
maps it to normal continuation (`ai_aifunctions.c:252-280`), while Jaded
returns 0 (`Functions/AIfunctions_ai.c:290-315`).

`CinematicScriptReader.NestedCallbackPaths` keeps one source path per function
for display (`src/Features/Cinematics/CinematicScriptReader.cs:56-96`), so
that label alone cannot prove a unique slot. The separate
`CinematicScriptAiTrackSlotProvenanceAnalyzer` starts from every configured
callback slot, preserves slot through `AIExecute` and current-track edges,
sets the target slot for literal numbered `AITrackChange`, and keeps unknown
states from direct callback dispatch or unresolved joins. It unions all
catalogued routes. Branch conditions, calls, stops, and mutations remain
candidates: this is a may-reach set over the indexed source graph, not runtime
track state or proof that the graph captures every retail call path.

## Evidence tiers and consultation checklist

Keep conclusions in three buckets:

1. **Jaded source fact:** what the checked 2006 Jaded snapshot implements. It is
   useful for engine architecture and candidate call paths, but is not a
   verified 2002 engine or retail BGE implementation.
2. **Retail-PC source fact:** behavior found in `external/irisEng`, the
   project's retail-PC reverse-engineered implementation. Prefer this over
   Jaded for 2003 PC behavior, while still identifying any uncertain or
   reconstructed parts of Iris.
3. **Unknown/runtime-dependent:** actual owner dispatch, current world slot,
   collision results, callback execution order at a particular frame, native
   platform behavior, and data-dependent script results. Static BF data may
   establish references, keys, transforms, serialized flags, and track/event
   contents; it cannot prove that a runtime gate passed.

For each JadeView question, use this short path:

1. State the exact observed symptom, scene/clip key, object GAO key/name, and
   expected result. Identify whether the question is file format, engine
   scheduling, or a specific runtime outcome.
2. Search the Jaded subsystem listed above for the function that reads,
   resolves, schedules, or renders the data; follow the call chain one level
   outward and inward. Record exact symbol and line range, and note whether
   the implementation is generic or platform-specific.
3. Cross-check retail behavior in Iris before proposing a JadeView behavior:
   use `external/irisEng/ai/ai_worldfunctions.c` for world-list APIs;
   `world.c` and `engine.c` for world membership and frame order;
   `events.c` plus `ai/ai_eventtrackfunctions.c` for EVE; `animation.c` and
   `actions.c` for animation/action; `cameraengine.c` and
   `graphicsobjects/camera.c` for camera; `sound.c`, `soundmodifier.c`, and
   `ai/ai_bge/ai_compiledscripts_sound.c` for PC audio/script glue;
   `gdi.c` and `materialanddraw.c` for UV/material dispatch;
   `lights.c` and `graphicsobjects/geo.c` for lighting/render path;
   `particlegenerator.c` and `gfx/` for effect updates/rendering.
4. Separate serialized facts from simulated state. Only project effects with
   explicit, time-addressable commands when JadeView can identify their target
   and ordering. Keep callback-conditioned changes as candidates/diagnostics
   unless the relevant retail callback and required state are understood.
5. Verify the claim against the relevant BF record/track and, when the claim
   concerns actual game behavior, a PC runtime observation. Do not use a Jaded
   implementation detail alone to assert BGE 2003 behavior.

For BGE-specific cinematic callbacks, inspect Iris
`external/irisEng/ai/ai_bge/ai_compiledscripts_*.c`; Jaded's generic AI function
wrappers are not evidence that it contains those retail game scripts.

## Action changes during EVE playback

Jaded's `ACTcompute.c:615-686` interprets action flags, avoids restarting an
already-playing action where appropriate, and defers `ACT_SetAction` requested
while EVE is running until the current frame's elapsed portion has been
rendered. This is an animation-frame scheduling rule, not a companion command
or mode policy. Iris separately confirms that the BGE companion message
callback requeues MSG 49 while `co_current_mode` is 9 or 17; that callback gate
is BGE-specific and is not inferred from Jaded.

## Dynamic AI callback registration

Jaded's generic mutable callback list is implemented by
`AIfunctions_ai.c:535-580` (`AI_EvalFunc_AICBAdd_C`) and
`:607-635` (`AI_EvalFunc_AICBDel_C`). A registration is stored on the
destination AI instance as `(type, function, source GAO/context)`; deletion
searches for all three values, nulls only the first matching function slot,
and leaves the list slot/count in place. Evaluators at `:582-600` and
`:637-655` use the current GAO as the stored/deletion context and take the
destination explicitly from the script arguments. This is generic Jaded
architecture, not proof that a particular retail BGE script reaches the call.

Retail-PC Iris has the same tuple behavior at
`external/irisEng/ai/ai_aifunctions.c:409-435,459-475`; its evaluators at
`:437-456,477-496` take the current GAO as context and the explicit argument
as destination. `AI_ExecCallback` at `external/irisEng/ai_common.c:812-897`
requires an AI instance/model and eligible flags, executes a fixed model
callback first, then scans live dynamic entries for a non-null function with
the requested ID (`:849-875`). Callback-all additionally requires the engine
running and current-world EOT membership (`:899-915`). Jaded's corresponding
dispatch is `AIengine.c:1350-1412`; its `AI_ExecCallbackAll` and AI-owner
iteration are at `:1200-1232` and `:1074-1187`. Use Iris for retail-PC gates.

Both implementations clear dynamic registrations when an AI instance is
reinitialized: Iris `external/irisEng/ai_common.c:375-420` and Jaded
`AIengine.c:1444-1497`. The script API reinitializes the current GAO's own AI
instance (`AIfunctions_ai.c:746-761`; Iris
`external/irisEng/ai/ai_aifunctions.c:533-546`), rather than taking the
callback's explicit destination argument. The universe instance is a special
case excluded from the normal reset block in both implementations (Iris
`:385-386`; Jaded `:1459-1461`). When dispatching a dynamic callback, Iris sets
the current GAO/AI context from the stored source GAO (`ai_common.c:861-875`),
so a reinit reached through that callback resets the context object's AI
instance. This does not establish that an arbitrary reachable reinit resets a
separate AICBAdd destination.

For static JadeView inspection, pair an Add and Del only when destination,
context GAO, callback ID, and function identity are all resolved to the same
values. A function name alone is insufficient. Even an exact static tuple is
only a possible removal: branches and runtime order determine whether it is
reached, and one Del removes only one matching registration. Report
`AIReinitVars` as a possible reset of its statically resolved current GAO's AI
instance; do not associate it with an Add destination unless receiver identity
is independently proven. Neither Jaded nor Iris source callsites alone prove
that registration, deletion, reinitialization, or dispatch occurred in a
particular BF scene.

## Per-frame EVE and callback handoff

Retail Iris's world tick runs `EVE_MainCall` first, then `ANI_MainCall`, then
the regular `AI_MainCall`: `external/irisEng/engine.c:184-215`. EVE iterates
the active world's event-object table, requires a live object with event data,
applies its cull/distance and inactive gates, then calls
`EVE_b_RunAllTracks` (`events.c:2683-2723`). EVE writes are therefore ordered
before the ordinary AI scheduler in this tick, but this does not prove a
particular script consumed a write: its callback-table membership, AI gate,
branch, and script state still matter. `AI_MainCall` traverses the separate
AI-callback EOT and applies processed/distance/hierarchy/AI-instance and
`flags2` gates before `AI_RunInstance` (`ai_common.c:765-810`).

Other engine callbacks use distinct phases and contexts. Retail Iris runs
callback ID 2 across the world's AI callback table after dynamics, collision,
records, and a hierarchy pass (`engine.c:226-288`; `ai_common.c:899-915`).
Animation code invokes ID 3 in `ANI_BeforeRender`, `ANI_Render`, and
`WOR_Render_All_GO` (`animation.c:2391,2646`; `world.c:2465`). Geometry/display
code invokes IDs 0 and 1 before/after geometry and string rendering
(`graphicsobjects/geo.c:772-778,888-893`; `geostr.c:2894,3143`). These are not
interchangeable with `AI_MainCall`: each direct callback has its own caller
timing, call-specific gate, and target. The shared Iris dispatcher requires
an extended object, AI instance and model; both runtime `flags2` bits
`0x00400000` and `0x04000000` must be clear, and there must be at least one
fixed callback or a live dynamic entry (`ai_common.c:812-897`). It executes a
matching fixed model function first, then scans the instance's live mutable
registration list for the requested ID (`:849-877`). Callback-all additionally
requires the engine to be running and walks valid objects in the world's AI
callback EOT (`:899-915`). Specific caller gates differ: ID 3 in
`ANI_BeforeRender` comes from visible objects with a skeleton group
(`animation.c:2396-2410`); `ANI_Render` dispatches only for a lead animation
that has not been initialized in `ANI_MainCall` (`:2631-2648`); and geometry
ID 0/1 may route a bone GAO to its animation initialization/reference GAO
(`geo.c:766-780,879-895`). AICBAdd/AICBDel mutate the dynamic list, not the
serialized five-slot `AI_RunInstance` schedule.

Jaded has the same broad EVE -> ANI -> AI ordering in
`external/jaded/src/libraries/ENGine/Sources/ENGcall.c:230-268`; its EVE owner
loop and `EVE_b_RunAllTracks` are at `AIinterp/Sources/Events/EVEplay.c:1600-1650`,
and its AI owner loop/direct callback dispatch are at `AIengine.c:1074-1187`
and `:1350-1425`. It additionally calls `AI_ExecCallbackAll` with
`AI_C_Callback_AfterRec` after REC (`ENGcall.c:358-382`). Use these as
architectural corroboration only; Iris is the retail-PC authority, and exact
callback IDs/phase gates must be checked there.

JadeView models reachable AICBAdd/AICBDel tuples and candidate same-tuple
removals in `CinematicScriptCallbackRegistrationAnalyzer`, reports literal-ID
source dispatch sites through `CinematicScriptReader`, and summarizes regular
Iris AI scheduler gates in `CinematicScriptSchedulerAnalyzer`. The generated
dispatch-site catalog records the enclosing Iris function, source context,
shared dispatcher eligibility, caller-specific gates, and target routing.
The registration and model-hook inspectors display these separately so a
world-table callback cannot be confused with a draw- or animation-time direct
call. They do not simulate execution or change cinematic preview. Keep them
candidate-level: static data cannot prove that a dynamic registration
survived, that the target was in the appropriate runtime EOT/table, that the
caller phase occurred, or that any branch/message/collision gate passed.
