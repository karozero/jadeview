# BGE / Jade Engine script-system reverse audit

Status: source-driven static audit, 2026-10-23. No game was launched and no
script VM was executed. This document distinguishes observations in JadeView,
the available Iris/Jaded source trees, and the independent PC 2003 runtime
audit under `C:\Users\Dan\Desktop\rebge\docs\verified`.

## Shared game-script core (2026-10-24)

The `Features/GameScript` modules describe shared engine commands, not only
cinematic behavior. EVE playback and a future ordinary gameplay-script runtime
are separate consumers. The current core is not an executable AI VM or a
reconstruction of the game's scheduler.

`GameScriptObjectFlags` decodes object/group Invisible and Inactive setters and
`OBJ_FlagsControlSet`. `GameObjectRuntimeFlags.Apply` preserves the low status
word and gives set bits precedence over cleared bits. The cinematic adapter now
uses this same implementation. Group membership is supplied by the adapter;
hierarchy descendants are not implicitly group members. Object destruction
remains a separate lifecycle operation, not a flag-only implementation.

Source: Iris `ai_gameobjectfunctions.c:1253-1292,1319-1402` and
`ai_miscfunctions.c:14-76`. This shared slice covers the flags2 mutation only.
The native refresh of active/visible tables, inactive sound registration,
conditional hierarchy removal, sectors and callbacks are not executed here.
In particular, clearing ForceInactive does not establish scheduler eligibility;
visibility and activity remain distinct.

The ordered universe-state reducer and its unknown-state propagation are
documented in [game-script-world-universe.md](game-script-world-universe.md).

## Confidence and source boundaries

- **Serialized fact** means a relationship or value decoded from the BF data.
- **Source behavior** means a behavior visible in Iris/Jaded implementation.
  Iris is a reverse-engineered implementation and Jaded is a comparative Jade
  Engine source tree; neither alone proves that every retail BGE build follows
  that exact path.
- **PC 2003 runtime evidence** means a static observation from the captured
  BGE runtime image described in the verified documentation. It is stronger
  evidence about that artifact, but still does not prove that a particular
  callback or branch ran in a particular playthrough.
- **Preview inference** means JadeView schedules or evaluates source records
  without emulating the engine's live state. It must not be presented as a
  confirmed game event.

The verified bundle specifically warns that Jaded is comparative and that
Iris/iristool are unverified leads for the identified PC 2003 executable. Its
runtime audit is a separate static analysis of the installed PC release. See
`C:\Users\Dan\Desktop\rebge\docs\verified\SOURCES.md`,
`2003\scripting.md`, `2003\scheduling.md`, and
`2003\reports\runtime-script-function-map.md` for artifact identity and scope.

## Reconstructed system at a glance

```text
BF world and dependencies
  -> GAO object + brain/model + model OVA and instance OVA
  -> object links: Events / SceneEvents / actor actions / resource references
  -> EVE lists and key payloads (animation curves, morphs, AI-function trees)
  -> object/resource reference resolution and per-track runtime state
  -> two interacting update paths:
       world AI callback scheduler (owner lifecycle, gates, queues)
       EVE track scheduler (time, keys, nested calls, transforms)
  -> callbacks call engine operations or enqueue typed messages
  -> recipient brain consumes queued messages and changes object/game state
  -> scene evaluation renders the state, with visibility/material/audio/etc.
```

This explains the main source of missing actors and effects: a world can
serialize an object and its EVE data while the runtime callback that starts
that data is gated, uninitialized, delayed, interrupted, or never selected.
The stored scene graph alone is therefore not a complete playback schedule.

## Resource and owner graph

JadeView discovers cinematic content through a world and its resource
references, rather than treating a cinematic key as a self-contained movie.
Its loader indexes object records and their brain/model links, overlays
instance OVA values on model defaults, and reads the event resource keys
referenced by object links. The currently decoded model is in
`src/Features/Cinematics/CinematicScriptReader.cs:311-489`; the graph and
owner semantics are summarized in `docs/cinematic-script-system.md`.

The two event links have distinct roles in the inspected data:

- `Events` commonly points to an actor/action event resource and is relevant
  to ordinary actor animation selection.
- `SceneEvents` points to the script-driven EVE list used by `TRA*` calls and
  owner callbacks. It may belong to a controller or a prop, rather than the
  visible actor.

These labels are data conventions established by archive/source joins, not a
rule that every resource named `Events` or `SceneEvents` has identical use.
The EVE owner key, world key, event key, list link name, track index, target
GAO, bone selector, flags and decoded payload all matter. A numeric key by
itself is not enough to join two records; JadeView intentionally resolves in
world scope and retains ambiguity when multiple candidates remain.

## What an EVE track contains and how it runs

JadeView reads event resources as animation tracks and keeps `Calls` and
`Messages` alongside the source track (`CinematicScriptReader.cs:320-378`,
`CinematicEmbeddedEventTrack.cs:3-22`). An AI-function key is not just a
function name and timestamp. Iris loads a count-prefixed tree of typed AI
nodes, resolves references and enum/category IDs during event-list resolution,
then calls `AI_RunNode` when the key executes:

- Iris: `external/irisEng/events.c:168-215` sets the current object/owner
  context and invokes the interpreter; `792-835` loads and resolves the
  function-node payload.
- Jaded: `external/jaded/src/libraries/AIinterp/Sources/Events/EVEnt_aifunc.c:119-203`
  loads and resolves the equivalent tree; `216-280` runs it with the EVE
  track's target as current object, falling back to the owning GAO when no
  explicit target is present. A bone-addressed track can instead set the
  current object to a skeleton member.

That context is semantically important. Engine functions often act on
`CurrentUltra` / the current object, while message fields can separately
contain GAO and GAO2 references. The event's execution owner is therefore not
automatically the actor named inside a message.

The EVE update keeps per-track running, paused, done, current-key and local
time state. Jaded's `EVE_RunOneTrack` (`EVEplay.c:768` onward) advances time
using event durations measured in frames at 60 frames per second, executes
AI-function keys subject to their event flags, advances across keys and
handles loops. `EVE_ReinitTrack` and `EVE_SetRunningTrack` (`EVEplay.c:219-280`)
show that starting a stopped track resets its cursor; requesting run on an
already-running track does not perform that reset. Iris has corresponding
logic in `events.c:333-370`, `1689-1820`, and `2153-2190`. This is one reason
that flattening nested EVE keys to `start + key.Time` is not generally
equivalent to the engine: pause, stop, restart, loop, set-time, auto-stop and
conditional waits alter the mapping.

At load/resolve time Jaded walks event tracks, resolves reference operands,
and initializes each object's EVE instance (`EVEload.c:46-126`). This is
distinct from deciding whether a script owner is active in a level.

## Selector and argument dispatch

Literal EVE calls in JadeView are decoded from typed word-pair rows by
`CinematicLiteralCall.Decode` (`src/Features/Cinematics/Models/CinematicLiteralCall.cs:31-83`).
It recognizes the encoded context/reference, a function-selector row and an
end marker; immediate literals and a vector-constructor pattern are retained
as 32-bit arguments. The selector is mapped to a readable operation by
`CinematicEngineFunctionCatalog`, with many labels checked against Iris
function sources. This is a useful static index, not a complete interpreter.

The decoder does not run arbitrary selector trees, evaluate nonliteral
operands, execute branches, mutate a full world-state model, or reproduce all
typed stack and callback return semantics. A decoded `TRAPlay` or
`MSGSendAll` is evidence of a configured operation in the serialized track;
it is not proof that the track ran. The PC 2003 verified audit independently
found a typed bytecode interpreter, multiple dispatch namespaces and a native
callback path, while leaving several opcode semantics and the exact mapping
from parsed function records to scheduled slots unresolved. See the verified
`2003\scripting.md` and `2003\reports\runtime-bytecode-execution.md`.

## Owner lifecycle and cinematic scheduling

The shared `Cine_Events` callback family in Iris provides a concrete example
of how a cinematic owner becomes active. `Cine_Events_cine_init` evaluates
configured replay/save-state conditions and transfers to
`cine_tests_depart`; the latter evaluates configured start gates and
priority/interruption behavior before calling initialization and the event
start transition. `Cine_exec_InitialiseCine` applies configured input,
camera, board-panel and audio setup. `Cine_exec_GoToEvents` marks the owner
playing, sets up restrictions, starts the owner's EVE tracks with
`TRAPlayAll`, and transfers its callback to `cine_play_eventtrack`.
`cine_play_eventtrack` advances a duration/timecode, handles termination and
cinematic-mode messages, and stops the tracks before completion processing.
The relevant implementation is
`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4061-4395`,
`4397-5704`, `5706-5840`, and `5841-6087`.

This lifecycle makes an important distinction:

1. The BF can contain a valid `SceneEvents` list.
2. A GAO can link that list and have a source callback capable of starting it.
3. The owner may still be waiting on activity, saved flags, capabilities,
   priority, message state, an active cinematic, board-panel state or another
   gate.
4. Only after a runtime callback passes its gates does it start some or all
   tracks; later interruption/time conditions can stop them.

Iris `AI_ReinitInstance` copies configured callback slots into an instance and
resets its per-callback state (`ai_common.c:375-435`); `AI_RunInstance` walks
those callbacks (`711-762`), and `AI_MainCall` dispatches eligible object
instances in the world (`766-830`). This is a separate scheduler from
`EVE_RunOneTrack`. The PC 2003 verified runtime audit observes a scheduled
script context and callback/native-function path, but the exact identity
join from a parsed Sally record to the live scheduled slot remains open.

JadeView can statically inspect callback models, OVA gates, linked tracks,
source-reachable calls, flags and message candidates. Its current
script-owned preview controls are explicit because archive data cannot
reconstruct all live gates. A manually enabled owner preview means “preview
this serialized route from the chosen time,” not “the game confirmed this
owner ran.”

## Messages: delivery, dialogue and face motion

Messages are queued state, not direct method calls. Iris's
`AI_EvalFunc_MSGSend_C` copies into a destination GAO's bounded queue of at
most five messages (`external/irisEng/ai/ai_messagefunctions.c:3-20`);
`MSGGetByID` removes a matching queued message (`143-188`). Jaded implements
the same general queue-and-consume pattern in
`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c:88-121`
and `266-316`.

In Iris, `MSGSendAll` is not a global broadcast: it builds a typed message
from the stack and appends it to the current execution object's queue
(`ai_messagefunctions.c:193-225`). The serialized message's GAO/GAO2 fields
are data consumed by a receiver; they do not, on their own, identify the
queue that received it. The EVE execution context and the eventual callback
reader must be traced separately. Queue capacity, callback ordering and
whether a reader consumed the message are runtime facts.

Types 30 and 53 demonstrate why the receiver model matters:

- In the recovered `Cine_Textes` callback, type 30 selects text/audio table
  metadata. The receiver uses a message ID and `Int1` to select or advance a
  line, then configures shared dialogue UI/audio state. Iris source and the
  data joins are documented in `docs/cinematic-script-system.md` around the
  sections for `Cine_Textes_CiTxt` and `BF00218E`.
- Type 53 is consumed by several distinct callback families. The automatic
  lipsync callback reads `Int1` and `Vector2.Y` as mouth progression controls;
  a negative fractional command can request its compiled 32-form cycle.
  Companion and Sally callbacks interpret the same message type as different
  face/expression controls. The payload is therefore not a universal
  phoneme-coded viseme, and a type-30 dialogue line does not automatically
  generate type 53.
- On `FF005833 / BF00218E`, the archive has type-30 dialogue requests and
  separate type-53 controls whose contexts resolve to actors. Secundo's
  model has lipsync callbacks but its own linked event track lacks decoded
  type-53 rows; those facts do not identify a sender or prove the callback
  ran. This is a concrete example of why audio, dialogue selection, facial
  messages and actor callback activation must be joined as separate evidence.

The current working tree contains a preliminary route for nested facial
messages: `CinematicScriptReader` decodes channel messages;
`CinematicNestedEventTransformTracks.Schedule` maps those message times
through the channel playback clock; `CinematicPlayback` combines nested
messages with direct clip messages; and `CinematicPlayback.Facial.Events`
decodes type 53 for actor facial playback. This repairs the omission of
nested typed messages from the preview clock. It still does not execute the
owner callback, prove that its gate passed, enqueue/dequeue a runtime message,
reconstruct audio-to-phoneme timing, or infer lip motion for a dialogue line
that lacks an authored type-53 path. This is a current Changes implementation
and should be rechecked by the root agent's tests/review before it is treated
as final.

## Unresolved event-track route diagnostics

Literal cinematic and nested-EVE `TRAPlay` / `TRAPlayAll` calls that cannot be
joined to a unique `SceneEvents` list/channel are retained as route diagnostics.
The NLE displays these in an `Unresolved EVE routes` script lane at the source
call's mapped cinematic time, with source list/channel, target, operation and
resolution reason. Current reasons distinguish a missing list, cross-world
ambiguity, duplicate lists, an empty `TRAPlayAll` list, a missing `TRAPlay`
index, and an index absent from the resolved list. Synthetic tests cover root
and nested calls and verify that the NLE exposes the failure.

These entries describe static route resolution only. They do not prove that a
callback dispatched the call, that runtime gates passed, or that the resolved
track actually ran; successfully resolved routes remain subject to the existing
preview/runtime caveats. Dynamic target operands and arbitrary AI control flow
are not converted into guessed routes.

## EVE state transitions in the NLE

The reconstructed per-channel clock's state transitions are now exposed in an
`EVE state transitions` timeline lane. Entries identify owner, event list,
channel and preview route, and show the before/after running, pause, local-time
and run-index snapshot. `Start` is labeled as a preview-route start;
`AutoLoop`/`AutoStop` are attributed to the serialized channel flags; explicit
`TRA*` transitions are attributed to decoded commands. Every item states that
live callback dispatch is unverified. No-op lifecycle commands are already
visible in script-call lanes and are omitted from the transition lane unless
the modeled channel state changes.

This timeline visualizes JadeView's current reconstructed clock, not the retail
engine's live scheduler. It makes assumptions and state changes inspectable,
but callback order, wait conditions, runtime yields and cross-owner arbitration
remain outside the model.

## Asset activation and appearance

The source model supports multiple reasons that a visible object can be
absent, misplaced, in T-pose, or visually wrong:

- Its actor/prop record is not in the selected or paired world dependency
  closure, or its resource reference is unresolved/ambiguous.
- The object exists but a separate owner callback controls capability,
  custom bits, group-section visibility, hierarchy, destruction, shader or
  effect state.
- An actor's base `Events`/action clip is selected by gameplay state rather
  than the cinematic clip; a cinematic owner may switch actor mode, kit or
  action slot using messages and `ACT*` operations.
- A controller-owned `SceneEvents` list is not active because runtime gates
  have not been simulated.
- A camera, sound, procedural material, particle, video or light is updated
  from a separate subsystem/callback rather than from the visible mesh's
  animation keys.

Thus “load all GAOs and play the cinematic clip” cannot guarantee a complete
scene. The system needs world/resource dependency resolution plus an explicit
activation ledger, not scene-specific object-name rules. The active-state
ledger must distinguish `serialized`, `dependency-loaded`, `callback-linked`,
`gate-matched-from-known-data`, `manually previewed`, `runtime-unverified`,
and `confirmed by runtime evidence` states.

## JadeView coverage and remaining boundaries

JadeView already has substantial source-driven infrastructure: world-scoped
owner/event indexing, script callback reachability, OVA overlays, recognized
engine call descriptions, nested `TRAPlay`/`TRAPlayAll` traversal, bounded
cycle/depth checks, EVE playback clocks, selected owner previews, source-time
audio operation extraction, facial type-53 decoding, and synchronized actor
export. The implementation is spread across `CinematicScriptReader`,
`CinematicNestedEventTrackResolver`, `CinematicNestedEventTransformTracks`,
`CinematicScriptEventTrackCallResolver`, `CinematicPlayback`, and specialized
resolvers under `src/Features/Cinematics`.

Callback-start ledger entries now retain source-path-to-slot attribution when
the reachable call path begins at exactly one decoded serialized callback slot.
The owner-execution tooltip reports the slot index/function and the resolved
function path. If the slot table is unavailable, no slot name matches, or more
than one slot matches, that state is explicit. This binds static call-path
provenance to serialized slots; it still does not establish callback dispatch
or the runtime scheduler's slot index/order. A synthetic fixture verifies the
unique-slot and unavailable-table cases. A current static graph audit of
`FF005833 / BF00218E` also shows owner `BF00218D` (`CINE_APRES_MDISK_EVT02.gao`)
with `Cine_Events_cine_firstinit` in callback slot 2; its `GoToEvents` source
path begins at that same function. This confirms that the archive's serialized
slot and source-path shapes fit the resolver, but not that the retail runtime
dispatches slot 2 or executes the path during playback.

It is not a general script interpreter. The key limitations are:

1. Callback conditions and world scheduler state are only partially analyzed;
   live collision, save flags, object activity, message queues, priority,
   frame cadence and prior gameplay state are unavailable.
2. Static callback reachability does not prove an `.ofc` function was selected
   for an object instance or invoked. The supplied verified PC 2003 audit
   retains this exact identity/slot-selection boundary for its Sally sample.
3. Many engine selectors, nonliteral parameters, generic AI node branches,
   callback return/yield rules and state changes have no preview reducer.
4. Actor action/mode changes, props, visibility, particle/material effects,
   camera, audio and facial events do not yet share one complete deterministic
   state evaluation model. Some are handled by specialized paths only.
5. A static time join between an EVE key and an effect is not necessarily the
   runtime order: event duration, wait conditions, queue consumption,
   overlapping callbacks and scheduling cadence can change it.
6. Synchronized GLB export can only contain the state JadeView can evaluate;
   it cannot export hidden runtime-only state as if it were confirmed.

## Recommended general architecture

Build a data-driven preview pipeline with explicit stages and provenance:

1. **Archive graph:** index worlds, GAOs, model/instance brain records, OVA
   fields, `Events`/`SceneEvents` links and resource dependencies. Preserve
   source keys and world identity through all stages.
2. **Normalized event IR:** decode each track/key into typed transform,
   animation, morph, AI-call, message, label/wait and unknown-payload events.
   Retain raw bytes, tag/type metadata, source key, track target/context,
   selector, duration and decode confidence. Never discard unknown nodes.
3. **Binding and callback catalog:** resolve model callback slots, instance
   overrides, engine selectors, reference operands and receiver readers.
   Represent each edge as a typed link with `resolved`, `ambiguous`, or
   `unresolved` status and evidence source.
4. **Owner scheduler:** model the world AI lifecycle and cinematic owner
   transitions as a state machine with gate inputs. Unknown live inputs must
   remain unknown; user-selected preview overrides are separate inputs.
5. **EVE scheduler:** give each owner/channel independent cursor, running,
   pause, done, loop, wait and restart state. Derive nested clocks from the
   same event queue rather than flattening timestamps prematurely.
6. **Message bus:** schedule sends into explicit destination queues, apply
   queue capacity/order semantics, then invoke only receiver callbacks with
   a source-confirmed reader. Show dropped/queued/consumed/unknown status.
7. **State reducers:** route confirmed operations into small deterministic
   reducers for actor/action state, transforms/hierarchy, visibility,
   materials/effects, camera, audio, dialogue and face channels. Unknown
   operations remain inspectable without being guessed.
8. **Evaluation and export:** evaluate the graph from source time (including
   backward seeks) using snapshots/checkpoints plus deterministic replay.
   Preview and GLB export must consume the same evaluated state and carry a
   diagnostics/provenance report listing omitted or uncertain behavior.

No reducer should branch on a scene name, actor display name or specific GAO
key. Domain behavior should be selected through a source-backed callback or
selector descriptor and serialized data. A user-authored/manual activation
may target a particular owner, but it must be recorded as an override, not
mistaken for native execution.

## Next implementation order

1. **Nested typed-message clock mapping is implemented and verified.** Both
   type-30 and type-53 events follow nested channel start, pause/resume,
   stop/replay, and `AutoLoop`; duplicate routes are collapsed and a facial
   playback fixture seeks backward and forward around the same event.
   Regression coverage is in
   [`CinematicNestedEventTransformChecks.cs`](../tests/ExportVerification/CinematicNestedEventTransformChecks.cs)
   and
   [`CinematicFacialSignalChecks.cs`](../tests/ExportVerification/CinematicFacialSignalChecks.cs).
2. **Static callback-slot provenance, unresolved literal
   `TRAPlay`/`TRAPlayAll` diagnostics and modeled EVE state transitions are
   exposed.** The remaining work in this step is runtime callback scheduling
   and confidence-aware reconciliation of modeled versus observed state.
3. **Engine-level message queue semantics are implemented and verified.** The
   remaining work is to connect source-confirmed `MSGSend`/`MSGSendAll` events
   and receiver-specific reads to a timeline without guessing queue ownership,
   initial contents, callback order, or message consumption time.
4. Improve remaining callback-gate inputs for actor activity/capability,
   saved flags, priority, message state, cinematic arbitration, and scene
   object visibility/destruction. Each input needs a source citation and
   confidence level.
5. Connect actor state/mode/action selection and action-kit merge to the same
   event scheduler, then route world/prop/effect/material/camera/audio
   mutations through domain reducers.
6. Export evaluated world state and diagnostics together, so a GLB clearly
   distinguishes included source data from simulated activation and omitted
   runtime-only state.
7. Use the verified PC 2003 runtime-image evidence to validate critical Iris
   assumptions where the same routine is identifiable; keep Jaded-only
   behavior explicitly marked comparative until checked against PC retail.

## Source index

| Topic | Primary source locations |
| --- | --- |
| Event-track loading/reference resolution | `external/jaded/src/libraries/AIinterp/Sources/Events/EVEload.c:46-126`; `external/irisEng/events.c:792-835` |
| Event AI function payload and execution context | `external/jaded/src/libraries/AIinterp/Sources/Events/EVEnt_aifunc.c:119-280`; `external/irisEng/events.c:168-215` |
| EVE clocks and track lifecycle | `external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c:219-292,768-950`; `external/irisEng/events.c:333-370,1689-1820,2153-2190` |
| `TRA*` operations | `external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_tra.c:29-250`; `external/irisEng/ai/ai_eventtrackfunctions.c:5-156` |
| AI instance scheduler | `external/irisEng/ai_common.c:375-435,703-830`; PC 2003 static scheduler summary in `C:\Users\Dan\Desktop\rebge\docs\verified\2003\scheduling.md` |
| Message queues and receivers | `external/irisEng/ai/ai_messagefunctions.c:3-20,111-188,193-225`; `external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c:88-180,266-316` |
| Generic cinematic lifecycle | `external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4061-5704,5706-6087`; interpretation and limits in `docs/cinematic-script-system.md` |
| Serialized call decoding | `src/Features/Cinematics/Models/CinematicLiteralCall.cs:7-83`; selector labels in `CinematicEngineFunctionCatalog.cs` |
| Serialized message decode | `src/Features/Cinematics/Models/CinematicTimelineMessage.cs:6-68`; type-53 interpretation in `CinematicFacialEvent.cs:30-111` |
| Nested EVE schedule and preview routing | `src/Features/Cinematics/CinematicScriptReader.cs:311-489`; `src/Features/Cinematics/CinematicNestedEventTransformTracks.cs:11-160,176-237` |
| Playback consumers | `src/Features/Cinematics/Models/CinematicPlayback.cs:70-140,194-245`; `CinematicPlayback.Facial.Events.cs:10-51` |
| PC 2003 runtime boundaries | `C:\Users\Dan\Desktop\rebge\docs\verified\SOURCES.md`; `2003\scripting.md`; `2003\resources.md`; `2003\scheduling.md`; `2003\reports\runtime-bytecode-execution.md`; `2003\reports\runtime-script-function-map.md` |

## Type-53 receiver comparison and current playback routing

Focused source/code comparison, 2026-10-23. The Iris files below are useful
for reconstructing handler semantics, but the verified PC 2003 bundle does
not establish that these exact decompiled callbacks match the retail
executable. The PC 2003 evidence covers the captured interpreter/scheduler
and a Sally `.ofc` key map; it has not identified a retail type-53 handler.
Jaded's generic message API confirms queue operations, not these BGE-specific
type-53 meanings.

### Confirmed in the available Iris source

- **Automatic lip-sync controller:**
  [`ai_compiledscripts_lipsync_auto.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_lipsync_auto.c)
  `Cine_lipsynch_auto_Lips_init` reads `des_o_acteur`, defaults a null target
  to `AI_gpst_CurrentGameObject`, then assigns the morph and message callbacks
  (lines 25-37, 322-329). `Cine_lipsynch_auto_Lips_lecture_messages` removes
  type 53 from the current object's queue; a non-null message `Gao` replaces
  the configured target, while a null `Gao` leaves that target as-is
  (lines 489-525). It decodes `Vector2.Y` for the configured mouth channel,
  uses `Int1` as transition speed, and interprets the fractional remainder's
  sign as automatic-cycle control (lines 526-552). Its morph callback advances
  using `TIM_gf_dt` (lines 332-380 onward). This is receiver-specific control,
  not phoneme decoding.
- **Companion:**
  [`ai_compiledscripts_companion.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_companion.c)
  `companion_co_exec_reflex` reaches `companion_co_exec_get_message` through
  `AIExecute` (lines 7563 and 8800); that function consumes type 53 from the
  current object's queue (lines 4002, 5177-5253). It maps `Vector1.X`,
  `Vector1.Y`, and `Vector2.Y` into configured morph channels 0, 1, and 4;
  `Float2` supplies speeds for channels 0/1, `Int1` the speed for channel 4,
  and `Int2` becomes the immediate blink flag. The channel-4 fractional sign
  also selects mouth cycling. Its expression and blink callbacks apply
  channels to their configured animation target (blink's target is resolved
  from `o_blink_module_tete`, lines 12784-12820). This protocol differs from
  both the automatic lip-sync and Sally handlers.
- **Sally:**
  [`ai_compiledscripts_sally.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_sally.c)
  `Sally_sa_exec_message_read` consumes type 53 and stores it as
  `m_expression_message`; `Int2 == -1` clears cinematic-expression mode and
  the stored message, otherwise it enables that mode (lines 6589-6658).
  Sally's visage selector separately constructs local type-53 values for
  expression kits when cinematic mode is not active, then applies five vector
  components across morph channels (lines 10949-11190). Thus Sally's message
  acts as a mode/state input and expression data for a later callback; it is
  not equivalent to the companion or automatic-lip handler's direct decoder.
- **Common dispatch mechanics in Iris:** `AI_EvalFunc_MSGGetByID_C` removes
  the matching message from that object's queue
  (`external/irisEng/ai/ai_messagefunctions.c:143-188`). These callbacks read
  `AI_gpst_CurrentGameObject`, so owner/receiver context selects the queue.
  This confirms why decoding type 53 by ID alone is insufficient. It does not
  establish retail scheduling or prove a particular message reached a
  callback.

### Current JadeView behavior and remaining limits

The current preview gates type-53 interpretation on callback reachability from
the resolved execution owner, then selects separate AutomaticLipSync,
Companion, and Sally decoders:
[`CinematicPlayback.Facial.Events.cs`](../src/Features/Cinematics/Models/CinematicPlayback.Facial.Events.cs:35)
and
[`CinematicFacialEvent.cs`](../src/Features/Cinematics/Models/CinematicFacialEvent.cs:38).
The message inspector applies the same receiver-first distinction in
[`CinematicScriptMessageAnalyzer.cs`](../src/Features/Cinematics/CinematicScriptMessageAnalyzer.cs:75).
This is model-based routing rather than a scene-name exception, but callback
reachability remains capability evidence only, not proof of execution.

This routing gap has been addressed in the current Changes. For the
automatic-lip-sync handler, a resolvable message `Gao` overrides the configured
target; otherwise JadeView uses the owner's effective `des_o_acteur` reference
with instance-over-model precedence, and only uses the owner when the OVA field
is explicitly null or absent. Dynamic main-actor slots and unresolved
references remain unpreviewed. Companion and Sally keep execution-object
routing and do not inherit the auto-lipsync override. Callback classification
also fails closed when multiple receiver families are reachable. Regression
coverage checks target override/default/ambiguous cases and callback-specific
channel layouts. The behavior is model-backed, not runtime proof; cross-world
and special actor-slot routing remain unresolved.

Sally's `Int2 == -1` reset is also handled: the Iris receiver clears the stored
expression message in that branch, so preview decoding skips its vector fields
instead of treating them as fresh morph commands. The separate blink-state
field remains unimplemented.

## End-to-end trace: owner resource to cinematic state

The sources support the following chain. Each arrow has a different kind of
evidence; a static link is not proof that the runtime took that route.

| Stage | Evidence in sources / JadeView | What is and is not established |
| --- | --- | --- |
| **1. Load an owner and its resources** | JadeView indexes world-scoped GAOs, resolves brain/model and OVA references, then reads model callback slots, instance overrides, object references, and both `Events` and `SceneEvents` (`CinematicScriptReader.cs:311-379,426-489,494-588`). Jaded resolves EVE resource references and allocates per-instance parameters (`EVEload.c:46-132`). | The resource graph and stored references can be recovered. Resolution does not mean the owner is active or its linked resource is running. `SceneEvents` is used for GAO EVE operations in the inspected format; `Events` also participates in actor animation/action data and must not be conflated. |
| **2. Bind callbacks** | Iris copies callback slots into an AI instance and resets slot state (`ai_common.c:375-435`), then the world scheduler iterates eligible object instances and dispatches callbacks (`711-830`). JadeView joins callback function keys/names to owner models and computes source-call reachability (`CinematicScriptReader.cs:42-92`). | Callback slots/call paths describe possible behavior. A reachable nested function is not proof that a particular slot was selected, registered, or run. |
| **3. Pass owner gates and start the cinematic** | Iris `Cine_Events_cine_init`/`cine_tests_depart` check configured saved-state/start conditions and priority/interruption behavior (`ai_compiledscripts_global_managers.c:4061-5704`). `Cine_exec_GoToEvents` applies input/capability restrictions, sets playing/time state and calls `TRAPlayAll` (`5841-5898`). | A linked `SceneEvents` list can remain dormant until gates pass. JadeView owner previews explicitly simulate a chosen route; they do not reconstruct live flags, queue arbitration or prior gameplay state. |
| **4. Advance EVE tracks and nested calls** | Jaded loads/resolves typed event nodes (`EVEload.c:46-95`); `EVE_ReinitTrack` and `EVE_SetRunningTrack` define cursor/running reset behavior (`EVEplay.c:219-292`); `EVE_RunOneTrack` advances events, obeys AutoLoop/AutoStop and executes at event boundaries (`768-850,1027-1043`). `TRAPlay`, `TRAStop`, pause and all-track variants operate on the current object's EVE data (`AIfunctions_tra.c:29-180`). | Per-track clock, state and context are runtime data. EVE AI-function execution uses current track target or owner when no target is supplied; the message's GAO/GAO2 fields are separate operands. JadeView schedules supported literal nested play/pause/stop/replay and loop clocks in `CinematicNestedEventTransformTracks.cs:11-137,257-301`; conditions, arbitrary AI trees and engine cadence remain out of scope. |
| **5. Route and consume messages** | Iris `MSGSend` appends to the destination object's queue and caps it at five (`ai_messagefunctions.c:3-20,111-188`); `MSGGetByID` removes a matching message (`143-188`). Jaded also represents queue send/read operations (`AIfunctions_msg.c:88-180,266-316`). JadeView retains execution object/context separately from message GAO/GAO2 (`CinematicTimelineMessage.cs:6-55`). | Send destination, callback execution context, and payload object references are different edges. Static send-to-reader reachability cannot prove queue acceptance, order, or consumption. |
| **6. Interpret dialogue and face controls** | `Cine_Textes_CiTxt_reception_messages` scans queued type-30 messages, arbitrates priority, advances/selects a table slot, then copies text and voice metadata to `GST_AffichageTexte` (`ai_compiledscripts_global_managers.c:2964-3132`). Type 53 is receiver-specific: AutoLipSync, Companion and Sally callbacks have distinct field/target rules (see the preceding section). | Type 30 selects dialogue/audio metadata; it does not inherently emit type 53. Voice sound IDs are not WAVE keys until text-bank/TXL resolution. A temporal overlap between voice and morph messages does not prove lipsync was audio-generated. |
| **7. Update world state and end/cleanup** | The cinematic play callback advances elapsed time/timecode, sends actor/camera reset messages on termination, stops EVE tracks, updates active-cinematic state and calls completion logic (`Cine_Events_cine_play_eventtrack`, `5901-6048`). `Cine_Events_Cine_exec_EndOfEvents` changes saved flags and reinitializes tracks; sound-completion logic may restart or destroy the owner (`6089-6144,6051-6086`). Other script callbacks can activate/deactivate/destroy props, alter actor action/state, sound, camera or effects. | The cinematic container is one controller among many. A T-pose actor or absent prop can result from a missing dependency, inactive/gated owner, action-mode handoff, or an unsupported callback effect; the selected cinematic clip alone does not identify which cause applies. JadeView does not yet evaluate all these reducers through one authoritative state snapshot. |

### Cross-check against the verified PC 2003 evidence

The verified material is a static analysis of the captured PC 2003 runtime
image and a Sally `.ofc` resource closure, not a recovered retail source tree.
It confirms a scheduled script context and a callback/native-function path,
and the Sally key/pointer map is joined to the captured image. It explicitly
leaves the exact connection from a parsed resource record to the scheduled
slot unresolved; it also says the `.ofc` label cross-check comes from a
historical prerelease decompilation and does not prove identical 2003
semantics. Therefore the end-to-end Iris/Jaded callback details above are
**source-model evidence**, not retail-PC confirmation. The verified sources
do not currently validate the type-30 or type-53 handler implementation in
the identified PC release. See
[`SOURCES.md`](C:/Users/Dan/Desktop/rebge/docs/verified/SOURCES.md),
[`2003/scheduling.md`](C:/Users/Dan/Desktop/rebge/docs/verified/2003/scheduling.md),
[`2003/scripting.md`](C:/Users/Dan/Desktop/rebge/docs/verified/2003/scripting.md),
and
[`runtime-script-function-map.md`](C:/Users/Dan/Desktop/rebge/docs/verified/2003/reports/runtime-script-function-map.md).

### Shared owner execution ledger

JadeView now exposes an initial per-owner ledger in the cinematic **Execution**
tab (`CinematicNestedEventTransformTracks.cs` and
`EditorShell.Cinematics.ExecutionLedger.cs`). It unifies the preview routes
that were previously visible in separate controls: serialized `RunningInit`
channels, source-reachable callback `TRAPlay` / `TRAPlayAll` routes, explicit
cinematic calls, nested EVE calls, and manual owner previews. Each row keeps
its world/owner, event-list key, addressed channel indices, preview status,
preview-clock start where one is known, provenance, and a conservative runtime
gate result. The UI labels runtime gate state as **Unknown** and distinguishes
included, disabled, and available for manual preview.

Regression checks cover direct and nested call ownership/channel/time,
callback reachability, `RunningInit`, manual selection and delayed start,
user suppression, and the invariant that static routes do not assert a runtime
gate passed (`CinematicNestedEventTransformChecks.cs`). Each scheduled channel
now also retains an EVE preview clock with explicit `TRAPlay`, `TRAStop`,
`TRAPause`, `TRAUnPause`, `TRASetEnd`, `TRASetTime`, `TRAReinit`, `AutoLoop`,
and `AutoStop` transitions. The Execution table evaluates running/paused/
stopped state and local track time at the current cinematic playhead; tests
cover pause/resume, stop/replay, loop boundaries, auto-stop, and exact-time
seeks. These are reconstructed preview-clock transitions, not captured game
runtime state.

The ledger describes what this preview schedules from decoded evidence;
overlapping routes can address the same channel, so a row is not proof of the
unique cause of a game runtime activation. It still does not record failed or
unresolved calls, exact callback slot execution/timers, live scheduler and
frame delta, message queue acceptance/consumption, or resulting actor/object
state reducers.

The next extension is to make this ledger the single evaluated state consumed
by viewport, NLE, inspector, and export. Add explicit message send/receiver/
consume transitions and resulting actor/object state reducers, with each
transition carrying source evidence and confidence. Start conditions should
remain three-valued (`true` / `false` / `unknown`) until live save flags,
collision state, queue competition, scheduler timing, and callback dispatch
are actually reconstructed. Verify paired-world resolution, owner versus
track target, lifecycle transitions, gate true/false/unknown, a full message
queue, and seeking before/after message send before claiming broader runtime
parity.

### Iris runtime dispatch audit: callback owner, queues, and EVE state

This focused pass is based on the local Iris checkout at
`external/irisEng`, HEAD `45ef8bb74977288567259ca9da26a35578b9bcc1` (2026-10-19).
The configured origin is `https://github.com/ka87/irisEng`, but a GitHub fetch
could not be completed in this environment because the proxy was unavailable.
The observations below therefore describe this local revision only; they do
not claim it is the latest upstream Iris state. They are **Iris source-model
evidence**, not proof of retail PC 2003 behavior.

#### World scheduling and callback ownership

The frame loop calls `AI_MainCall(world)` as a normal AI update phase and later
calls `AI_ExecCallbackAll(world, 2)` in a separate callback phase
([`engine.c`](../external/irisEng/engine.c:205-219,274-293)). The first path
walks the world's `AICallbacksTablePF`, skips invalid/already-processed
objects, applies an `extraFlag`/distance gate, walks `refGao2` parents while
they have not been processed in this world call, sets the processed bit, and
then runs the resulting object's AI instance unless its status flags block it
([`ai_common.c`](../external/irisEng/ai_common.c:765-810)). This is not simply
“all scripts on all loaded objects run once”: table membership, per-frame
processed state, distance, hierarchy/owner redirection, object flags, and
instance/model enabled state affect reachability.

`AI_ReinitInstance` copies five model callback slots into the instance,
initializes each slot's jump cursor and timer, and clears its dynamic callback
list ([`ai_common.c`](../external/irisEng/ai_common.c:375-435)).
`AI_RunInstance` iterates the populated slot range, subtracts frame delta from
each slot timer, and runs the callback only when the timer is due. Return
codes can stop the instance pass, yield/return, or repeat the slot immediately
([`ai_common.c`](../external/irisEng/ai_common.c:711-763)). The `start` and
`end` members are calculated from occupied slot indices; they are not
cinematic start/end times.

There is also a distinct callback-registration path. `AICBAdd` appends a
function, callback ID, and a `gao` to the target AI instance's
`instanceAICBList` ([`ai_aifunctions.c`](../external/irisEng/ai/ai_aifunctions.c:409-455)).
`AI_ExecCallback` first checks object/instance status and dispatches the
model's callback slot, then dispatches matching registered entries. For a
registered entry it changes the current object/instance context to the entry's
stored `gao`, which may differ from the table object being visited; it restores
the outer context after dispatch
([`ai_common.c`](../external/irisEng/ai_common.c:812-897)).
`AI_ExecCallbackAll` invokes this for each valid object in the world callback
table ([`ai_common.c`](../external/irisEng/ai_common.c:899-915)). Thus the
owner used for discovery, the table object, and the effective callback GAO
must be represented as separate links. A static owner-to-function edge alone
cannot establish that the callback ran on the scene owner.

#### Message queue delivery is local, bounded, and distinct from payload

The names `MSGSendAll` and `MSGSendAllOwner` do **not** mean a world broadcast
in this Iris implementation. `MSGSend`, `MSGSendEx`, and `MSGSendAll` obtain
the destination queue from `AI_gpst_CurrentUltra`; `MSGSendAllOwner` instead
uses the global `MSG_gpo_Owner` registered by `MSGRegOwner`
([`ai_messagefunctions.c`](../external/irisEng/ai/ai_messagefunctions.c:3-20,22-40,111-188,193-225,356-410)).
The send-all form packs the richer message payload (message ID, object
references, vectors, floats, and integer fields), but still appends one
message to that one queue. The queue has capacity five and sends silently do
nothing once full. `MSGSendEx` also sets its message `gao` to the current
script object, while send-all operands independently supply `gao`/`gao2`.
Therefore queue recipient and `message.gao`/`message.gao2` are independent
edges, just as callback execution context is a third edge.

`MSGGet` pops the newest item from the queue; `MSGGetByID` searches by ID,
swaps the matching item with the last entry, and removes it; `MSGGetCount` and
message tests inspect the same queue
([`ai_messagefunctions.c`](../external/irisEng/ai/ai_messagefunctions.c:42-70,143-188,260-354)).
This means queue order is not a single FIFO contract: default reads are LIFO,
ID-specific reads remove from the middle, and the order of remaining messages
can change. A correct preview ledger must record attempted send, resolved
queue owner, accepted/dropped status, payload object refs, consumer/read mode,
and removal time separately. The current JadeView timeline inspector decodes
messages and receiver reachability, but does not yet emulate these queue
mutations or overflow.

JadeView's message-gate inspector now lists literal `MSGSendAllOwner` calls
whose serialized message ID and `Int1` satisfy a gate in a separate
“registered queue unresolved” category. It does not treat the EVE execution
object, `message.gao`, `message.gao2`, or the startup `GST_Sound_Manager`
registration as proof of the queue recipient: the UI now exposes the Iris
startup key (`7100F33F`) and source location as a baseline candidate, and
compares it with the selected gate's queue. `MSGRegOwner` can replace the
global pointer, and the archive view does not establish its live registration
order. JadeView also surfaces the latest earlier `MSGRegOwner` found in the
same serialized EVE channel as a potential registration candidate. This only
narrows the search: registrations in other channels, event scheduling, and
runtime execution order can still change the global pointer. Iris world teardown
clears the pointer (`world.c:2755`). This is a source-informed candidate only,
not a gate match or proof that the message was accepted/delivered. The startup
registration baseline is kept in the embedded
`script-global-message-queue.json` evidence catalog rather than a scene rule.
The argument decoder
uses ID at serialized argument 0 and `Int1` at 10 for the 12-argument send;
this follows Iris's reverse stack reads in `MSGSendAllOwner` and must remain
explicitly Iris-model-derived until confirmed against the retail PC executable.
JadeView centralizes these ID/`Int1` layouts and queue-destination kinds in
`CinematicEngineFunctionCatalog.TryReadMessageGateFields`; regression checks
cover `MSGSendEx`, `MSGSendAll`, `MSGSendAllOwner`, and malformed owner-send
payloads (`CinematicEngineFunctionCatalogChecks.cs`).

#### EVE control is per owner and its commands do not all mean the same thing

The `TRA*` public wrappers operate on the effective `CurrentUltra` object's
`extended->eve` data (`ai_eventtrackfunctions.c:5-156`). `TRAPlayAll` sets the
running bit for every track; `TRAStopAll` clears it. `TRAPause*` only toggles
the pause bit, while `TRAReinit*` resets cursor/time and restores the initial
running flag from the serialized track-list flags
([`events.c`](../external/irisEng/events.c:333-370,2153-2190)). Starting an
already-running track is a no-op; starting a stopped track resets its cursor
to event zero. Stopping only clears the running bit, so a later play restarts;
pause preserves the cursor. `TRAIsStopAll` checks running bits on channels
that have event data. This distinction matters for authored restarts and
loops: pause/resume, stop/play, and reinit/play are different state
transitions, not synonyms for a single timeline toggle.

`EVE_RunOneTrack` evaluates serialized event elements against `TIM_gf_dt`,
can execute an AI-function event, branches via labels, and handles track
AutoLoop/AutoStop flags; it can consume multiple event boundaries in one
frame (`events.c:1689-1820`). Track-local current time/key index, running,
paused, and “event already applied” state therefore affect the result.
JadeView's `CinematicTrackPlaybackClock` represents supported nested
start/pause/stop/replay/loop commands and now exposes before/after transition
records plus playhead snapshots. The owner ledger can show preview channel
state, but it still lacks scheduler tick/delta, callback slot timer/cursor,
and the retail EVE element cursor/flags used at each event.

#### Cinematic lifecycle and downstream state changes

The Iris `Cine_Events` controller is an ordinary script owner with an
instance-local state record. Its call chain is:

```text
Cine_Events_cine_firstinit
  -> Cine_Events_cine_init
  -> Cine_Events_cine_tests_depart
  -> Cine_Events_Cine_exec_InitialiseCine / Cine_Events_Cine_exec_GoToEvents
  -> Cine_Events_cine_play_eventtrack
  -> Cine_Events_Cine_exec_EndOfEvents
```

These edges appear in the compiled source and generated JadeView function
call index (`ai_compiledscripts_global_managers.c:4061-4395,4397-5840,5841-5898,5901-6048,6089-6144`; JadeView's
`script-function-calls.json` entries for the same functions). `GoToEvents`
sets `i_playing`, initializes duration state, calls `TRAPlayAll` on the
controller's current object, and switches its callback to the play-event
function. The latter decrements the timer and checks a custom activation bit;
termination can send message 47 to the main actor and message 60 to the camera,
stops all owner EVE tracks, invokes completion, updates the global active-cine
reference, and runs soundtrack fade logic. The messages are directed to
specific queues and are not generic broadcasts
([`ai_compiledscripts_global_managers.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:5841-5898,5901-6048)).
This confirms a key dependency: cinematic scene state depends on its controller
callback continuing to run and on downstream actor/camera/message handlers,
not only on the visual EVE channels.

The generic engine callbacks in this path have observable world-state effects.
For instance, Iris `OBJDestroy` removes/detaches an object only for one
authored flag case; otherwise it sets object status bits, requests world/EOT
and visibility rebuilding, destroys attached sound, and may detach matrix
hierarchy ([`ai_gameobjectfunctions.c`](../external/irisEng/ai/ai_gameobjectfunctions.c:155-224)).
So “hidden”, “deactivated”, and “destroyed” cannot safely be one preview
boolean. `ACTActionSet` calls `ACT_SetAction` and resets actor action time,
while `ACTActionFrequencySet` changes either EVE action-event frequency or
the actor animation frequency depending on engine state
([`ai_actionfunctions.c`](../external/irisEng/ai/ai_actionfunctions.c:67-121)).
Thus action selection can hand control between actor-state animation and an
EVE-driven animation. The cinematic callback also directly changes global
sound/capability state and communicates camera control through its own message
queue. Effects, material alpha, and visibility are often implemented in
asset-specific compiled callbacks; the generic scheduler proves only how
those callbacks can be reached, not that every callback has been identified
or translated into JadeView behavior.

#### What the current execution ledger needs next

The current record
([`CinematicOwnerExecutionLedger.cs`](../src/Features/Cinematics/Models/CinematicOwnerExecutionLedger.cs:3-34))
contains owner ID, event key, route, preview status, unknown runtime gate,
channel indices, optional preview start time, provenance, and limitation.
That is a useful route ledger, not an execution ledger yet. Based on the
source model above, add these independent, evidence-bearing fields/states
before describing it as runtime-like evaluation:

| State group | Minimum useful data | Why it is separate |
| --- | --- | --- |
| Callback scheduling | effective callback GAO; model-slot vs dynamic registration; callback ID/slot; current jump node; remaining delay; due/blocked/not reached; return/yield result | `AI_MainCall` and `AI_ExecCallback` use different routes and dynamic entries can execute in another GAO context. |
| World/frame gate | world key and AI call count; callback-table membership; already-processed bit; distance/extraFlag result; hierarchy redirection chain; relevant object/model enabled/status flags | These are upstream of callback execution and can prevent a statically reachable callback from running. |
| EVE channel lifecycle | list owner and effective target GAO; track index; serialized initial flags; running/paused/done bits; element index/local time; start/stop/reinit cause; loop/auto-stop; last update delta | EVE control commands mutate a stateful channel cursor, not just a fixed clip span. |
| Message lifecycle | queue owner; attempted/accepted/dropped; queue order/capacity; payload `gao`/`gao2`; message ID; reader mode; consumed/remaining; read timestamp | Iris separates delivery queue from payload refs; `MSGSendAll` is local and the queue is capped/unordered. |
| Resulting world state | actor current action/action time/frequency source; prop present/hidden/detached/destroyed; visibility rebuild pending; sound/effect/camera command and target; source reducer/callback | Script outputs include persistent state and owner-specific callbacks; unsupported outputs must remain unknown rather than be guessed. |

Use tri-state/unknown values for unobserved runtime gates and attach
provenance to each transition. Keep callback route discovery, an explicit
user preview override, and a decoded source command distinct. Do not mark an
object inactive merely because an inferred callback was not executed in the
preview.

#### Verified PC 2003 boundary and audit correction

The PC 2003 verified bundle remains separate evidence. It documents a native
callback/scheduler path in the captured executable and explicitly leaves the
mapping from a serialized resource to a particular live scheduled slot
unresolved; it does not validate Iris's five-slot scheduler details, dynamic
`AICBAdd` context, message queue capacity/order, the generic `Cine_Events`
implementation, or Iris callback behavior for material/effect activation.
No retail PC message-queue trace or callback trace was collected in this pass.
Consequently those details must remain Iris model behavior pending PC2003
runtime identification. The previous audit's wording that Iris provides
“the” callback execution chain should be read as **a reverse-engineered
source-model chain**, not a verified description of the retail executable.
The PC boundary sources are listed in the preceding verified-PC section and
in `C:\Users\Dan\Desktop\rebge\docs\verified\SOURCES.md`.

The next general JadeView step is now narrower than adding more owner rows:
implement a provenance-aware event-state reducer for one owner, with distinct
callback, EVE-channel, message-queue, and actor/object state records. Start
with accurately modeled `TRA*` lifecycle and bounded per-owner messages, then
connect message readers and action changes. Only after those generic reducers
work on multiple owners should the viewport/NLE/export derive visibility,
effects, camera, audio, and actor pose from the shared result. This targets
the missing actors, T-pose fallbacks, early effects, and face/audio timing
without scene-key special cases.

### Message-to-consumer state trace, with the current channel clocks

This follow-up uses the newly available JadeView channel-level state. Each
scheduled nested EVE channel now has a `CinematicOwnerChannelExecution` with
owner, event-list key, channel index, preview route, and a
`CinematicTrackPlaybackClock`; the clock exposes time-mapped snapshots and
before/after start, play, stop, pause, reinit, set-time, AutoLoop, and AutoStop
transitions (`CinematicNestedEventTransformTracks.cs:145-185`,
`CinematicOwnerExecutionLedger.cs:35-41`,
`CinematicTrackPlaybackClock.cs:1-80,90-151,180-208`). This provides a shared
timeline for deciding when a decoded channel event is eligible. It does not
itself bind each message to a queue, execute the recipient callback, or apply
the recipient's state mutation.

#### Concrete source traces

| Source event / sender | Recipient and handler evidence | State mutation that follows | Limit |
| --- | --- | --- | --- |
| `Cine_Events_cine_play_eventtrack` sends message 60 at termination to the resolved `_PJ_Camera.gao` queue, with `Int1=0`, `f2=-1` (`ai_compiledscripts_global_managers.c:5987-5995`). | `camera_cam_1_mainloop` reads its own queue and branches on type 60 (`ai_compiledscripts_camera.c:6297-6359,6547-6562`). Another camera callback reads type 60 and consumes focal fields (`ai_compiledscripts_camera.c:13261-13278`). | `Int1=0` exits cine mode and clears target/follow objects and offsets; `f2=-1` requests a cut. For `Int1=1/2`, the receiver uses `Gao`/`Gao2`, vectors, and focal/speed fields to set cinematic camera state (`ai_compiledscripts_camera.c:6301-6359,6547-6557`; second read site `8238-8299`). | Strong source-model proof of queue-target and receiver-state chain. It is not evidence that this branch runs in PC 2003 retail. JadeView's message analysis describes some of these fields but does not yet mutate a live camera-state reducer. |
| `Cine_Events_cine_play_eventtrack` sends message 47 to the resolved main-actor queue on configured cinematic exit (`global_managers.c:5967-5985`). | Sally's `Sally_sa_exec_message` handles its stored message 47 (`ai_compiledscripts_sally.c:6863-6911`). | On entry (`Int2 != 0`) it optionally calls `ACTActionSet` from `Int1`, saves callback track 2 and collision activation, disables collision, creates a type-53 expression message from vectors, and switches callback track 2 to the cinematic action. On exit it resets mouth/expression state and restores actor sight/callback flow (`6863-6940`). | The code proves both ends exist, but this inspection has not proven that the particular `AI_MainActor` recipient in every cinematic is Sally. Preserve exact resolved destination before joining these as one message instance. |
| A type-30 dialogue message is read by `Cine_Textes_CiTxt_reception_messages` from that owner's queue (`global_managers.c:3038-3061`). | The receiver scans/removes queued messages, chooses the highest-priority eligible type 30, then selects text-table index and sound metadata (`3062-3131`). | It writes dialogue text, associated sound ID, speaker/localization GAO, volume, and near/far fields into the display manager. Separate code subsequently requests and plays the sound (`global_managers.c:2100-2185`). | This proves a path from script message to voice configuration, not that the type-30 message creates lipsync. Facial type-53 messages have distinct receiver paths documented earlier in this audit. |
| Companion type 49 and 46 messages are read by `companion_co_exec_get_message` (`ai_compiledscripts_companion.c:4300-4409`). | Type 49 is removed with `MSGGetByID`; type 46 is stored as the companion cinematic message. | Type 49 updates shared companion status/action/world/position/sight state, can call `ACTActionSet`, then requests mode selection (`4315-4397`). Type 46 feeds the cinematic gaze/turn behavior documented by the separate companion callback. | This demonstrates deferred state: messages update global companion data and callback selection, not necessarily immediate pose/visibility. Exact runtime selection and delivery remain unobserved. |
| A type-53 message reaches a companion callback through the current-object queue (`companion.c:5177-5255`; reflex reaches message handler at `7563,8800`). | The handler decodes morph channels, speed, mouth-cycle flag, and blink state (see earlier type-53 comparison). | Changes target morph-controller state that later callbacks evaluate; it is not an audio/phoneme computation. | Source proves protocol shape only; it does not prove the callback was reached for a particular preview route. |

The `Cine_Events` message 60 path is the clearest end-to-end test case for a
generic state ledger: sender callback and send condition, destination queue,
serialized payload, receiver callback, branch predicate, and resulting camera
mode all have source anchors. The type-47 case is a useful caution: finding a
matching sender and handler is not enough to claim the same object received
the message. Queue-owner identity must be resolved per instance.

#### Object visibility, activation, and effects are not one generic message

The inspected Iris sources show at least three different mechanisms that
could look similar in JadeView:

- EVE/callback commands can directly call `OBJDestroy`; Iris may detach and
  remove the GAO or mark it with status bits, request table/visibility rebuild,
  destroy its sound, and detach its matrix, depending on flags
  (`ai_gameobjectfunctions.c:155-224`). This is a command effect, not an
  inferred receiver message.
- Message readers can mutate actor-specific state and callback selection.
  Sally's type 47 path changes collision activation and callback track;
  companion type 49 writes shared state and selects a mode
  (`ai_compiledscripts_sally.c:6863-6911`,
  `ai_compiledscripts_companion.c:4305-4397`).
- Sound/dialogue and camera state have manager/controller consumers, while
  many particle, alpha, and visibility behaviors are implemented by
  asset-specific BGE callbacks. The engine scheduler and generic message API
  do not reveal an all-purpose `activate effect` receiver or a universal
  visibility rule. Each effect needs its actual callback/receiver traced.

Consequently, JadeView should not model a missing actor/prop as merely
`visible=false`, and should not instantiate every linked effect. Distinguish
`not loaded`, `not scheduled`, `gate unresolved`, `explicitly destroyed`,
`hidden/deactivated by a classified callback`, and `runtime-only effect not
implemented`; only source-backed transitions may leave `unknown`.

#### Cross-check with Jaded and iristool

Jaded's message implementation confirms a per-GAO LIFO-style queue and
`MSG_DataSend` construction from destination, position object, ID, `Int1`, and
float payload (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c:88-158`).
Its queue is a linked list and `MSGGetByID` unlinks only the matching node
(`AIfunctions_msg.c:266-317`), unlike Iris's fixed array/swap-with-last
implementation. This is useful engine-family corroboration for separating
queue owner from message payload, but it is **not** evidence that Iris's
capacity, ordering, or callback IDs match PC 2003.

iristool supplies data-inspection evidence, not an independent runtime
trace. `Engine/Scripting.java` resolves OFC function/category IDs and script
references and applies model/instance variable resolution
(`external/iristool/src/iristool/Engine/Scripting.java:129-168,170-198,304-320,441-465`).
`EventViewer.java` lays out event-track keys and can dump each frame's
`OFCProgramCodeElem` values (`external/iristool/src/iristool/EventViewer.java:70-82,105-131`).
These sources support inspecting serialized EVE/OFC code and resolving IDs;
they do not show a world AI scheduler or execution of BGE message handlers.
No iristool consumer trace was found for camera 60, cinematic 47, dialogue
30, or facial 53.

#### Smallest general JadeView implementation slice

JadeView now retains a `CinematicScheduledMessage` for each preview-scheduled
nested EVE message. It records world, list owner, event key, channel index,
preview route, channel-local source time, mapped cinematic time, the channel
clock snapshot, execution-object key, payload, and a separate Iris queue-owner
candidate. The recognized 19-row payload ends in `MSGSendAll`; Iris EVE
dispatch sets `CurrentUltra` from the track GAO or list owner, and
`MSGSendAll` queues on that object (`events.c:168-222,1772`,
`ai/ai_messagefunctions.c:193-256`). JadeView now shows that candidate in the
NLE as an Iris-model inference, not a retail guarantee. Queue acceptance and
callback/consumer execution remain unknown
(`CinematicNestedEventTransformTracks.cs:183-199`,
`Models/CinematicScheduledMessage.cs`). The NLE message lane displays these
origins and EVE states while leaving raw payload `Gao`/`Gao2` separate. A
regression verifies that the event-list owner and `ExecutionObjectKey` remain
distinct and checks the queue candidate separately. This is provenance for
preview scheduling, **not** proof that a runtime gate passed, a queue had room,
or a callback consumed the message.

The next implementation slice is a bounded queue ledger with explicitly
unknown owner registration, initial occupancy, and consumer timing. It can
report conditional outcomes (for example, “would append if count < 5 under
Iris”) but must not assert accepted or dropped while those runtime states are
unknown. Keep message sender, queue candidate and `Gao`/`Gao2` payload
references as separate edges. After that, expose receiver callback branches
and add state reducers only for source-confirmed operations such as type-60
camera reset, leaving unsupported branches unknown.

This recommendation is a preview model based on Iris semantics. Before
claiming retail-PC equivalence, the verified PC 2003 evidence would need to
identify the actual message send/receive functions and their queue data
structure in the captured executable; the current verified bundle does not
do that.

### Global queue registration and actor-action EVE are separate routes

This pass resolves two ambiguities in the earlier queue model: **the event
list owner, the currently executing EVE target, and the global message-queue
owner are three different identities**; and an actor's action-kit EVE lists
are attached through the animation/action path rather than being the actor's
ordinary script-owned `extended->eve` component.

#### Global queue: registration, append, and read

Iris has a single process-global pointer `MSG_gpo_Owner`, declared in
`external/irisEng/gameobject.h:256`. The BGE global init script resolves
`GST_Sound_Manager.gao` (`ai_compiledscripts_global.c:292`) and registers it
through `AI_EvalFunc_MSGRegOwner_C` (`:356`). That C helper overwrites the
global pointer (`ai_messagefunctions.c:397-400`); the script-callable
`MSGRegOwner` instead captures `AI_gpst_CurrentUltra` as the new owner
(`:402-410`). No owner stack or owner registry is visible in these functions.
World cleanup clears the pointer (`external/irisEng/world.c:2755`); the source
does not establish that every world transition re-registers it before any
send.

`MSGSendAllOwner` is not a broadcast. It uses `MSG_gpo_Owner`, verifies that
the registered GAO has `extended->messages`, builds one `tMessage`, and
appends it if the fixed queue has fewer than five items
(`ai_messagefunctions.c:356-394`). In this function `Src.gao` is the current
script game object and `Src.gao2` is an explicit payload argument
(`:374-388`). By contrast, `MSGSendAll` uses `AI_gpst_CurrentUltra` as the
queue owner and takes both GAO fields from its arguments
(`ai_messagefunctions.c:193-231`). These are distinct selectors and should
not share a presumed recipient rule.

There is a concrete source-model consumer for the registered global owner:
`GST_Sound_Manager_gs_play_sound` checks its current object's queue, calls
`MSGGet_C_CURRENT`, copies the message, switches on `msgID`, and repeatedly
reads the next message (`ai_compiledscripts_global_managers.c:2381-2389,
2951-2953`). The global registration targets that same sound-manager GAO.
This supports the chain “BGE global init -> global owner pointer -> bounded
owner queue -> sound-manager message callback.” The exact callback scheduling
phase and whether every BGE build uses this sequence are not proven here.

In Iris, queue append is synchronous, but queue consumption is explicit: the
script asks for a message. `MSGGet_C` decrements `count` and returns that
slot; if empty it returns the slot with `msgID=0` (`ai_messagefunctions.c:34-45`).
Thus the backing array behaves as a LIFO stack for `MSGGet`; it is not a
FIFO event bus. `MSGSend`/`MSGSendAll` silently do nothing when full
(`:7-15,226-230`), and `MSGSendAllOwner` has the same five-entry cap
(`:389-393`). `OBJ_Reinit` clears a message count for objects with the
message flag (`external/irisEng/engine.c:74-80`). These are Iris facts, not
verified PC retail queue properties.

#### EVE component ownership and actor-action lists

The GAO loader reads a serialized event-list reference only when the EVE
identity bit is present, allocates `extended->eve`, and schedules the list
through `EVE_ul_CallbackLoadListTracks`
(`external/irisEng/gameobject.c:3095-3107`). During reference resolution,
`OBJ_ResolveAIRef` first resolves this ordinary EVE component
(`gameobject.c:2459-2460`). For actor GAOs it separately walks the action
kit and resolves every action element's `eventLists`; without an action kit,
it resolves the base animation's event list (`:2461-2510`). Action-kit
elements themselves load `eventLists` using the same EVE list loader and
load their associated shapes (`external/irisEng/actions.c:533-547`).

The ordinary owner EVE component is iterated by `EVE_MainCall`: it walks the
world EVE-object table, skips invalid entries, entries without `extended->eve`,
objects outside the distance condition, and objects with runtime flag
`0x400000` set; it then sets EVE owner/current data and calls
`EVE_b_RunAllTracks` (`external/irisEng/events.c:2683-2722`). This proves
that loading an event list does not by itself prove that its owner is active
or that its tracks execute. The activation table, runtime flags, distance,
and per-track state are additional gates.

Actor action EVE is a separate route. `ACT_SetAction` chooses an action-kit
action and its first action element, assigns that element's event list to
`animPtr[0]` using `ANI_SetTrackList`, applies its shape and action-frequency
state (`external/irisEng/actions.c:278-311`). `ANI_SetTrackList` initializes
the event data and marks the list/tracks active (`external/irisEng/animation.c:850-881`).
At action-element boundaries, `ANI_ChangeAction` selects the next element,
replaces its track list, and changes shape (`:884-945`). If EVE asks for an
action while `ENG_gb_EVERunning`, `ACT_SetAction` records
`eventRunningFrequency`, `elapsedDT`, and `currentActionTransition` and
returns for later handling (`actions.c:212-218`); the animation runner
consumes that deferred request and may invoke `ANI_ChangeAction`
(`animation.c:1366-1405`). This is a concrete mechanism for transitions
between EVE commands and actor animation state; it is not evidence that a
generic dialogue event automatically selects a correct actor action.

The outer Iris frame order is EVE, then actor animation, then AI callbacks:
`ENG_OneWorldEngineCall` brackets `EVE_MainCall` with
`ENG_gb_EVERunning=1`, calls `ANI_MainCall`, then sets
`ENG_gb_AIRunning` and calls `AI_MainCall`
(`external/irisEng/engine.c:184-215`). A message appended by EVE can therefore
exist before the AI phase in the same world-engine call, but the source pass
does not establish which registered script callback consumes it in that
frame or whether it is gated/deferred. Model delivery and consumption as
separate events.

#### EVE execution context: owner is not always the target

`EVE_MainCall` initializes `EVE_gpst_OwnerGAO` from the world EVE table.
`EVE_b_RunAllTracks` sets the active GAO to the owner, but for a track with a
resolved `track.gao` it switches the current GAO to that target before
calling `EVE_RunOneTrack` (`events.c:2019-2024,2047-2073`). For an AI-function
track, `EVE_Event_AIFunction_Play` then sets `AI_gpst_CurrentUltra` and
`AI_gpst_CurrentGameObject` to the resolved track GAO, or to the EVE owner
when there is no target (`events.c:168-224`). A bone-targeted command instead
resolves through the actor skeleton table and can execute with a skeleton
element object as current GAO (`:198-214`). Thus “script owner” is not a
safe substitute for “callback target” or “message queue recipient.”

`EVE_ResolveTrackRef` resolves embedded event references and AI function
track payloads (`events.c:2118-2139`); `EVE_ResolveListTrackRef` guards the
list with a resolved flag and walks each channel once (`:2141-2150`).
`EVE_ResolveRefs` allocates per-channel runtime track state, resolves refs,
reinitializes channel clocks, and optionally increments shared-list usage
(`:2192-2205`). The source therefore has both shared serialized list data
and owner-specific runtime channel state. The same serialized list can be
referenced by an owner component or an actor action element; those contexts
must not be collapsed into one channel clock.

#### What this changes for JadeView

The existing `CinematicScheduledMessage` already keeps world/list owner,
event, channel, route, source/timeline time, channel snapshot, payload,
queue-owner candidate, provenance, and delivery limit separate
(`src/Features/Cinematics/Models/CinematicScheduledMessage.cs:2-21`;
`CinematicNestedEventTransformTracks.cs:187-208`). Its current inferred
candidate is based on `ExecutionObjectKey` and is specifically justified by
the decoded 19-row `MSGSendAll` payload (`:199-208`). Do **not** apply that
rule to `MSGSendAllOwner`: its receiver is the registered global owner, and
the registration state may be missing from the preview. Keep the Iris
candidate conditional and show registration provenance separately from
per-track execution context.

The smallest general implementation seam is one source-provenance routing
record that can represent both send forms without pretending to run AI:

- `execution context`: EVE list owner, resolved track GAO/bone target, and
  selector (`MSGSendAll` versus `MSGSendAllOwner`);
- `queue resolution`: local target or registered-global-owner key, plus
  registration source/time and `Unknown` when unavailable;
- `queue result`: capacity/occupancy unknown, conditionally appendable under
  Iris's five-slot rule, and explicit consumed/remaining state only when a
  matching reader is actually scheduled;
- `reader edge`: receiver GAO/function and the source-backed branch/message
  predicate, distinct from the `gao`/`gao2` payload references;
- `actor animation edge`: action-kit key/action ID/action-element index,
  current action transition, list key, and its own clock, separate from the
  ordinary owner EVE component.

The first reducer should support just this generic append/read boundary and
the source-confirmed actor-action list link. It can show a queue route as
“resolved to owner,” “global owner unresolved,” “would append if capacity,”
or “reader not scheduled”; it should not execute arbitrary callbacks or
invent visibility/activation. This provides a reusable basis for later
script owners and actors without per-cinematic rules.

#### Evidence limits

These runtime details are supported by local Iris source at the exact paths
above. Jaded is a separate Jade Engine source comparison: its `MSG_DataSend`
uses a per-object message structure and its `MSGGetByID` removes a selected
node (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c:88-158,266-317`),
which reinforces the need to represent queue policy per engine implementation;
it does not establish Iris or PC BGE queue semantics. iristool's
`Engine/Scripting.java` and `EventViewer.java` decode/resolve script and EVE
data but do not emulate Iris's global owner or dispatch queues. No retail PC
2003 runtime capture in `docs/verified` identifies `MSG_gpo_Owner`, its
registration API, queue capacity/order, or the EVE -> ANI -> AI phase order.
Treat the above as Iris-source behavior and a hypothesis for JadeView preview
design, not as proven retail equivalence.

### Owner activation and per-channel run gates

The script/event audit needs several state machines, not a single “active”
boolean. Iris first chooses which world objects are activated, derives
specialized EOT tables from those objects' identity flags, then independently
advances ordinary owner EVE, actor animation/action EVE, and AI callbacks.
An archive hit for a GAO or event-list key establishes resource linkage only;
it does not establish that any of these runtime steps succeeded.

#### Source-confirmed transition chain

| Stage | Iris source behavior | Preview implication |
| --- | --- | --- |
| World membership | `WOR_World_ActivateObjects` iterates `objectTablePF`; invalid entries are skipped. It clears a pending `flags2` bit `1`, then calls activation only when identity `flags & 1` is clear (`external/irisEng/world.c:174-191`). Loaded objects are inserted into the object table and may be activated/visible immediately (`:193-202`). | World key + GAO key is not enough. Record whether the owner was in the world object table; if archive data cannot prove the loaded runtime table, use `Unknown`. |
| Activation eligibility | `WOR_ActivateObjectIfFlagsAllow` requires `flags2 & 1 == 0`. At normal special time it adds the GAO to `activatedObjectsTable4PF` only when `flags2 & 0x04400000 == 0` and `flags2 & 0x10 == 0`; at nonzero special time it only adds objects with `flags2 & 0x04000000` set (`world.c:44-62`). These raw flags are preserved here without assigning broader meanings. | Keep eligibility, table membership, and rendered visibility as distinct state. Special-time value is runtime context and often unknown from a BF snapshot. |
| EOT derivation | `EOT_SetOfEOT_Build` clears specialized tables and derives animation, dynamic, AI callback, collision, EVE, and hierarchy tables from each activated-table entry's copied identity flags (`world.c:671-767`). EVE membership specifically requires identity `flags & 0x02000000` (`:741-748`); AI callback membership requires `flags & 0x20` (`:708-716`). | An EVE-capable owner and an AI-script owner may have different table membership. Do not infer that `SceneEvents` starts merely because its list key resolves. |
| Owner resource creation | A GAO's EVE identity bit causes the loader to allocate `extended->eve` and queue its `SceneEvents` list load (`external/irisEng/gameobject.c:3095-3107`). `OBJ_ResolveAIRef` resolves the owner EVE and separately resolves actor action-kit event lists (`:2459-2510`). | Preserve owner component, event-list link, and actor action-list links separately. This stage proves loaded/resolved resource availability, not playback. |
| Owner AI callbacks | The AI table builder inserts `flags & 0x20` owners. `AI_MainCall` checks that entry, per-call process state, optional distance, `extended->AIInstance`, and raw `flags2` masks before `AI_RunInstance` (`external/irisEng/ai_common.c:765-809`). `AI_ReinitInstance` copies up to five model callback slots into instance callback slots, resets their jump nodes/timers, and clears dynamic callback registrations on reinitialization (`:375-420`). `AI_RunInstance` dispatches only present slots after their timer reaches zero (`:711-762`). | A model function name or callback reference is not proof the owner has a live callback slot. Track callback slot, delay, process-call gate, instance enable/disable, and owner gate independently. |
| EVE channel initialization | `EVE_ResolveRefs` resolves list payloads, allocates one `tAnimTracks` runtime record per channel, and calls `EVE_ReinitData` (`external/irisEng/events.c:2192-2205`). `EVE_ReinitTrack` resets cursor/time and clears runtime bits, setting running bit `1` only when the serialized channel list-element flags include bit `1` (`:2153-2189`). | Serialized `RunningInit` is an initial state input, not proof the owner is scheduled. Preserve the archive flag separately from the current channel running/paused/done state. |
| Script gates and explicit start | The generic Cine controller's `cine_tests_depart` evaluates configured requirements and saved special flags before initializing and starting the event tracks (`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4534-4585,5385-5436`). Its configured requirement bits include collision-zone exit, actor status/capability, and saved-flag exit gates (`:5392-5413`; interruption checks include zone target and detector refs at `:5459-5655`). `Cine_exec_GoToEvents` sets playing/time state, calls `TRAPlayAll` on the current owner and switches its AI callback (`:5841-5898`). `Cine_exec_InitialiseCine` tests phase-3/phase-2 save flags and can clear owner custom bits (`:5706-5787`). | This is one source-confirmed example of why a scene's controller may be dormant or conditional. Store gate definition, observed inputs, result, and provenance; leave the result unknown when gameplay flags, zone collisions, or callback execution are unavailable. |
| Per-frame owner EVE | `EVE_MainCall` walks the event-object EOT table; it requires a valid entry, `extended->eve`, passes a distance gate when `extraFlag & 4` is set, and requires `flags2 & 0x00400000` clear before advancing (`external/irisEng/events.c:2683-2722`). `EVE_b_RunAllTracks` executes only channels whose runtime running bit is set and paused bit is clear (`:1996-2102`). | Table scheduling and channel running are separate prerequisites. Do not use transform presence or owner-list linkage as a substitute for a running track. |
| Start/stop/reinit effects | `TRAPlayAll/TRAStopAll` operate on the target GAO's `extended->eve`; individual play resets cursor/time and clears pause/done state when transitioning from stopped, while stop clears running (`external/irisEng/ai/ai_eventtrackfunctions.c:5-35,85-118`; `events.c:333-361`). Pause/unpause and reinit update other parts of the runtime state (`ai_eventtrackfunctions.c:37-83,121-157`; `events.c:363-382`). | JadeView's channel clock models serialized commands and loop/stop behavior, but must keep command intent separate from whether the owner exists in EOT and whether the resulting runtime cursor was advanced. |

This chain is deliberately a partial-order model. In the world engine call,
EVE runs before `ANI_MainCall`, then `AI_MainCall` (`external/irisEng/engine.c:184-215`); later in the same outer call `AI_ExecCallbackAll(world, 2)` runs after dynamics/collision and a second hierarchy update (`:279-289`). This proves there are multiple callback phases, not that a particular zone or save-flag callback executes in a particular frame. The EVE owner pass, actor action runner, generic AI instance loop, and callback-ID pass are separate schedulers with different tables and gates.

#### Example: a Cine owner is not a scene-wide “play” switch

The source-level flow for the common `Cine_Events` model is:

1. The GAO must be loaded, have its script instance and `SceneEvents` resolved,
   and pass object activation before the specialized EVE/AI tables can include
   it. Its serialized identity bits independently request EVE and AI callback
   membership.
2. Its AI callback slot must be present and due. `cine_firstinit` can wait on
   `f_first_delay`; `cine_init` and `cine_tests_depart` apply per-owner start
   gates. The save-flag tests can be positive or inverted by the serialized
   `>= 10000` convention; collision-zone and actor tests require live resolved
   GAOs and collision/activity state (`global_managers.c:4029-4059,
   4061-4396,4534-4585,5392-5436`).
3. Only after the gate path chooses `Cine_exec_GoToEvents` does that callback
   call `TRAPlayAll` on the controller's own `extended->eve`. The controller's
   event list can then call nested channels or message actors; the actor's
   action-kit EVE list remains independently selected by its action/animation
   path.
4. Each resulting owner channel must still be running, unpaused and eligible
   in the EVE world table. Nested calls can add further target-availability,
   link-resolution, or cycle/depth limits.

This explains several otherwise ambiguous outcomes: a correctly indexed
actor can be absent because it was not loaded/activated; a loaded cinematic
owner can be gated; an owner can run while one channel is stopped; a channel
can run but target a GAO absent from the loaded world; and an actor can be
present with an action kit but lack the action selected by its AI callback.
These are distinct diagnosis states, not reasons to add a scene-specific
default action or unconditional prop visibility.

#### Existing JadeView representation and gap

JadeView already reports raw serialized event-table identity flag,
`SceneEvents` key, and Iris `EVE_MainCall` raw flag/distance conditions in
`CinematicEventSchedulerAnalyzer.cs:5-23`; its diagnostic explicitly says
world activation/table membership/distance/actual execution are not proven.
`CinematicScriptBehaviorAnalyzer.cs:60-90` reports callback reachability,
the `Cine_Events` lifecycle, priority state and configured gate definitions.
`CinematicNestedEventTransformTracks.cs:47-72,74-120` can preview serialized
RunningInit channels and reachable literal `TRAPlay` starts, while its ledger
marks runtime gates unknown. `CinematicTrackPlaybackClock.cs:46-60,66-111`
and the later interval reducer model channel-local state and command
transitions. The execution ledger therefore captures *preview route and
timeline state*, not Iris's preceding world activation / owner callback / gate
state machine.

There are two important distinctions to preserve in the next change:

- `RunningInit` is channel initial state. It should not silently mean “owner
  passed world activation.” A manually previewed owner is an explicit user
  route, not evidence about game activation.
- `TRAPlayAll`/`TRAPlay` in this Iris source address `extended->eve` on the
  current target object. The actor's action-kit lists are activated through
  `ACT_SetAction`/`ANI_SetTrackList`, not by that owner EVE command. The
  scheduler must keep them in distinct state domains.

#### Reusable state-transition model and next implementation seam

For each world-scoped owner, represent a state vector instead of one boolean:

```text
resource: absent | key-known | loaded | references-resolved
world membership: unknown | in-object-table | invalid-or-removed
activation: unknown | blocked-by-flags/time | activated
EOT membership: EVE-table? AI-callback-table? animation-table? (each tri-state)
AI instance: absent | model-resolved | enabled | disabled
callback slots: slot -> function, jump node, remaining delay, last dispatch phase
gate state: per gate definition -> unresolved | passed | failed + input evidence
owner EVE: event-list key, initialized, runtime running/pause state
channels: serialized flags + cursor/time/running/pause/done + control provenance
actor action: action-kit key, action ID, element ID, action-list key, own clock
effects: emitted state edges/messages only after their source operation executes
```

The next system-wide slice should be an **owner activation ledger**, separate
from the current cinematic execution ledger. Populate facts available from
the archive (`SerializedGaoFlags`, script/model/ref keys, serialized channel
flags, configured Cine gates) and annotate each runtime gate as unknown unless
there is a user-selected preview assumption or source-backed simulation. A
pure reducer can then evaluate Iris's structural predicates and report e.g.
“would enter EVE table if world activation succeeds,” “channel initialized
stopped,” “Cine start gate unresolved,” and “explicit preview override.”
After this state is visible, implement only a narrow deterministic transition
subset: owner table predicates and `TRAPlay/Stop/Pause/Reinit` using the
resolved owner EVE component; keep AI callback execution, save-state values,
zone collisions, arbitrary script branches, and actor action selection
unknown until their actual inputs are modeled. This avoids scene-specific
activation assumptions and gives missing actors, T-pose, dormant channels,
and conditional effects one auditable diagnostic path.

The archive can identify serialized references and flag values; it cannot
recover live `activatedObjectsTable4PF`, EOT snapshots, current `flags2`,
special-time state, callback delay/jump-node state, save bits, collision
results, or prior message traffic. The PC 2003 verified bundle also does not
identify these Iris raw flag predicates in the retail executable. Keep the
model's runtime conclusions explicitly Iris-only until those retail runtime
symbols/fields are mapped.

The next slice now carries the conditional activation forecast one source step
further: the shared GameScript forecaster projects a GAO's serialized state,
checks normal-time and special-time Iris activation separately, and maps an
eligible object's identity flags to the EOT tables in which it would be
indexed. This makes the separate EVE, AI callback, actor-animation, dynamic,
collision, and hierarchy routes visible without conflating them. See
[`game-script-eot.md`](game-script-eot.md). This remains a serialized-state
forecast; live activated/EOT table membership and runtime dispatch are unknown.


## Callback dispatch and observable state writes (Iris source)

This section pins down the callback invocation sites and the state that generic
script functions actually mutate. It is an Iris-engine source model. The checked
PC 2003 verified material does not independently confirm these internal callback
IDs, raw flag masks, queue semantics, or effect parameter IDs in the retail
executable; do not label these as retail-proven behavior.

### There is no single “script callback” phase

The Iris frame path calls `EVE_MainCall(world)`, then `ANI_MainCall(world)`,
then sets the AI-running guard and calls `AI_MainCall(world)`
(`external/irisEng/engine.c:184-215`). After dynamics/collision and the next
hierarchy pass it separately calls `AI_ExecCallbackAll(world, 2)`
(`engine.c:279-289`). These are distinct phase entry points. `ANI_MainCall`
walks the animated-object table, gates on activation/flags, and invokes
`ANI_OneCall`; when the animation list is newly installed it evaluates/resets its
tracks instead (`external/irisEng/animation.c:2414-2475`). That is the actor
animation/action path, not a generic call to every AI callback slot.

The AI instance loop is another scheduler: `AI_MainCall` traverses its callback
table and applies process-call, optional distance, instance, and raw `flags2`
gates; `AI_RunInstance` walks callback slots and calls only a present slot whose
remaining delay has elapsed (`external/irisEng/ai_common.c:711-809`).
`AI_ExecCallbackAll(world,id)` is a world-table dispatch by callback ID and
`AI_ExecCallback` invokes the model's instance function for that ID plus
matching dynamically registered callback entries when the instance and raw
flags2 gates allow (`ai_common.c:812-897`).

There are also render-context callbacks. Geometry rendering invokes callback
ID 0 before drawing and ID 1 after drawing, with `AI_C_Callback` set around the
call; it uses the rendered GAO's AI instance or resolves the animation reference
owner when needed (`external/irisEng/graphicsobjects/geo.c:771-779,888-893`).
String-object rendering has analogous ID 0/1 calls
(`graphicsobjects/geostr.c:2893-2895,3142-3144`). Other ID 3 invocations occur
in animation and world traversal (`animation.c:2391,2646`; `world.c:2465`).
Therefore a render callback can be draw-pass scoped and may run more than once
per frame or not at all when its object is not rendered. It must not be treated
as a timeline event or as equivalent to the regular AI slot scheduler.

### Messages: consume, inspect, and global owner are different operations

The regular per-GAO message queue is a fixed five-entry array. `MSGSend` appends
when capacity remains; overflow is silently ignored
(`external/irisEng/ai/ai_messagefunctions.c:3-15`). `MSGGet` decrements the
count and returns the previous last element, so repeated calls consume in
LIFO order (`:34-45`). `MSGGetByID` searches for a matching ID, swaps that entry
with the final live entry, decrements count, and returns the now-consumed slot
(`:143-168`). In contrast, `MSGRead(index)` returns a pointer into the live
array without changing count, and `MSGReadByID` returns the first matching live
entry without consuming it (`:269-332`). `MSGClear` drops the queue by setting
count to zero (`:70-83`). These differences matter when reconstructing a
receiver: a read/test is not proof the message was acknowledged or removed.

`MSGSendAll` in this Iris implementation appends to the selected GAO's local
queue; the name does not establish a broadcast (`ai_messagefunctions.c:193-231`).
The separate `MSGRegOwner`/`MSGSendAllOwner` route stores one global owner
pointer and queues to that owner's message array (`:356-410`). A global manager
then explicitly polls/consumes its own queue; for example the Iris sound manager
uses `MSGGet_C_CURRENT`, switches on `msgID`, and drains messages in its handler
(`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:2381-2389,
2951-2953`). This proves one owner -> callback -> local queue consumer chain in
Iris, not that every EVE message is broadcast or that every recipient is active.

### Object lifetime/visibility and GFX effects are separate writes

`OBJDestroy` has two materially different branches. If identity flag `0x20000`
is present, it removes/detaches the object from world, snap and sound systems,
then removes the GAO. Otherwise it sets raw `flags2` bits `0x200000` and
`0x400000`, requests world-table and visibility rebuild, destroys the sound
instance, and may detach/free hierarchy state
(`external/irisEng/ai/ai_gameobjectfunctions.c:155-211`). World activation is a
separate gate: `WOR_ActivateObjectIfFlagsAllow` adds to the activated table
only under its `flags2` and special-time predicates (`external/irisEng/world.c:44-62`).
Visibility admission is separately implemented by
`WOR_MakeObjectVisibleIfFlagsAllow`: it skips an already-visible object, treats
`flags2 & 0x200000` specially by setting distance/flag state, otherwise adds it
to `visibleGameObjectsPF` and may add light/BV memberships (`world.c:153-172`).
These functions prove that script/object state can change world membership and
render eligibility; they do not establish a direct renderer “visible” toggle
for every game script.

The generic `GFXAdd`/`GFXAddSorted` helpers allocate effect entries into the
world's unsorted or sorted effect lists; `GFXDel` marks a selected effect entry
for death, and `GFXLifeTimeSet` writes its lifetime
(`external/irisEng/ai/ai_gfxfunctions.c:49-85,97-132`). `GFXFlagSet` and
`GFXMaterialSet` mutate that effect entry; material assignment is accepted only
when the target GAO has raw identity flag `0x4000` and a usable visual material,
with type-4 multimaterials indexed modulo their child count (`ai_gfxfunctions.c:436-513`).
Typed integer/float/vector setters dispatch through the effect type's parameter
map to its private data (`ai_gfxfunctions.c:198-435`; `external/irisEng/gfx.c:308-331,1117-1265`).
The Iris `Cine_Events` fade initializer provides a concrete compiled-script
example: it deletes/adds a sorted GFX entry, sets effect flags/material, then
writes integer/float parameters (`ai_compiledscripts_global_managers.c:4150-4171`).
Thus an effect's appearance can be driven by script-created GFX state without
changing the source GAO's ordinary material; the parameter meaning remains
specific to the GFX type/parameter table and is not safely inferred from the
numeric argument alone.

### System-wide modeling implication and evidence boundary

The preview ledger should distinguish dispatch provenance (`EVE frame pass`,
`ANI/action pass`, `AI slot`, `AI callback ID`, `render callback`) from the
message/effect/object operation it causes. For each message edge, retain sender,
recipient queue owner, message ID and payload, enqueue time, queue capacity/drop
result, receiver callback/phase, and whether the operation was a consuming get
or non-consuming read. For object state, record activation/visible/EOT membership
as separate facts and record a source operation (for example `OBJDestroy` or
`GFXAdd` + parameter writes), rather than emitting an inferred “show” or
“effect active” event from an archive reference alone. This is the next common
implementation seam; it needs no cinematic-specific asset-name rule.

The verified PC 2003 bundle and local BF index can establish resource keys,
serialized flags, EVE links, and stored message/event payloads where decoded.
They do not provide the live Iris tables, callback invocation trace, current
queue contents, owner context at dispatch, gameplay gate inputs, effect-list
allocation, or a retail-PC mapping for the Iris callback IDs and GFX parameter
layout. Those runtime conclusions remain unknown for the retail game until
separately evidenced.

## Closed static paths: callback output to receiver/state

The following chains are closed at source level; “closed” means the code path
from a named Iris scheduler/callback to its direct queue or state writes is
visible, not that the corresponding retail-PC runtime branch was observed.

### Regular AI callback execution and message delivery

`EOT_SetOfEOT_Build` derives `AICallbacksTablePF` from activated objects whose
copied identity flags include `0x20` (`external/irisEng/world.c:671-767`).
`AI_MainCall` checks table validity, per-AI-call processing, optional distance,
walks to the processable hierarchy owner, sets the process bit, then runs its
AI instance unless raw disable bits gate it (`external/irisEng/ai_common.c:765-809`).
`AI_RunInstance` invokes only callback slots copied into that instance, after
slot delay expires, and preserves jump-node/wait behavior (`ai_common.c:711-762;
AI_ReinitInstance:375-420`). Inside that execution context, `AI_EvalFunc_MSGSend`
uses `AI_EvalFunc_MSGSend_C` to append the supplied message to the selected
GAO's local five-entry queue (`external/irisEng/ai/ai_messagefunctions.c:3-31`).
The send helper itself does not invoke the recipient or wake its callback; it
only appends if capacity remains. Delivery is therefore two operations: sender
callback writes queue state, and a later recipient script callback must execute
and read/consume that queue. Iris source confirms the receiver pattern, while
actual recipient activation and callback timing require runtime inputs.

A concrete generic dialogue chain is present in the Iris BGE scripts:
`action_reaction_ar_exec_send_info` constructs message ID 30 with text index,
duration, and sends it to the resolved Sally text GAO (`external/irisEng/ai/ai_bge/ai_compiledscripts_action_reaction.c:205-241`).
The compiled `Cine_Textes_CiTxt_reception_messages` receiver first checks its
current owner's queue, drains it with `MSGGet_C_CURRENT`, chooses the highest
priority type-30 message, then updates the display text, line index, associated
sound ID, speaker/localization, and volume fields
(`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:3038-3100+`).
This demonstrates message-to-dialogue/sound state through an explicit compiled
script receiver. The source files here do not prove which serialized callback
slot invokes `action_reaction_ar_exec_send_info` in a particular BF, nor that
the resolved text GAO was activated or its receiver callback ran in a given
scene/time. For the shared sound
manager, its separate message consumer path also drains its queue and branches
on message ID (`global_managers.c:2381-2389,2951-2953`).

Message consumption semantics are exact and asymmetric: `MSGGet` removes the
last queue item (LIFO), `MSGGetByID` removes a match by swapping it with the
last item, while `MSGRead` and `MSGReadByID` only return pointers into the live
array (`ai_messagefunctions.c:34-45,143-168,269-332`). The receiver's drain loop
can therefore empty the queue before choosing one dialogue message; a preview
that selects a type-30 candidate must still record which earlier entries were
consumed/discarded and must not model `MSGReadByID` as acknowledgment.
`MSGClear` is a distinct explicit queue reset (`:70-83`).

**Implemented engine primitive:** `GameScriptMessageQueue` now reproduces the
per-object queue operations after they are supplied as explicit inputs. It
models Iris PC's five-entry array, silent overflow drop, newest-item pop,
first-match-by-ID removal with swap-to-newest, non-destructive indexed/ID reads,
`Int1` mask tests, and clear. A separate Jaded profile uses its 1000-entry
linked-list capacity and preserves the remaining order when removing by ID
(`external/jaded/src/libraries/AIinterp/Sources/AImsg.h:23` and
`AIfunctions_msg.c:88-113,247-305`). This divergence is now tested rather than
folded into one assumed Jade Engine queue. `GameScriptMessageQueueChecks`
passes in the full ExportVerification suite. The primitive is not yet wired to
cinematic event playback: queue creation state, prior gameplay sends, owner
registration order, receiver callback dispatch and queue consumption time
must be represented before it can safely report an accepted message or drive
actor/audio state.

The NLE's Messages lane now adds one source-graph hop for scheduled Iris EVE
messages: the queue candidate is resolved to a unique owner in the same world,
then matching message readers reachable from that owner's callback table are
listed as possible consumers. Ambiguous or unindexed owners remain explicit.
This improves send-to-reader inspection without treating static callback
reachability as delivery or changing facial, dialogue, camera, or actor preview.

### Cinematic owner callback to timed event playback

The Iris `Cine_Events` route is also source-closed: its generic owner callback
evaluates configured departure requirements and conditions; after they pass,
it initializes the cinematic and calls `Cine_Events_Cine_exec_GoToEvents`.
That function sets `i_playing` and elapsed/duration state, calls `TRAPlayAll` on
the current owner, and switches the AI callback track to the cinematic event
runner (`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4534-4585,5385-5436,5841-5898`).
The owner EVE timeline then runs in the EVE scheduler; its `AIFunction` events
can execute AI nodes in the selected target/owner context
(`external/irisEng/events.c:168-224,1996-2102,2683-2722`). This closes the
owner gate -> owner EVE start -> event function execution chain in Iris. It does
not close arbitrary referenced callback edges to actual world objects unless
the event's target/link and its current loaded/active owner are resolved.

### Direct effect state writes

A concrete `Cine_Events_cine_init` branch checks configured fade/saved-flag and
priority/timing state, removes an existing effect, adds a sorted effect entry,
sets effect flags, assigns a selected material, then writes type-specific
parameters (`global_managers.c:4061-4169`; the effect helper implementations are
`ai_gfxfunctions.c:75-158,204-270,302-432,436-513`). The generic helpers route
IDs below 1024 to the sorted-after-AE list and IDs at/above 1024 to the ordinary
world GFX list (`ai_gfxfunctions.c:97-158,204-257,302-348,384-432`). `GFXFlagSet`
updates the element's flags; `GFXMaterialSet` changes its material pointer only
when the target visual GAO passes its raw identity/material checks; typed
setters map parameter IDs into effect-private data (`external/irisEng/gfx.c:308-331,1117-1265`).
This is a confirmed callback-to-effect-state write path, but a visible pixel
result still depends on later graphics passes, camera, resources, and effect
renderer state. It cannot be reduced to “set GAO alpha” or a generic material
assignment without knowing the effect type and parameter map.

### Object removal and visibility closure

For explicit object destruction, the `OBJDestroy` AI function delegates to
`AI_EvalFunc_OBJDestroy_C`. The hard-removal branch calls `WOR_DelObj`, detaches
world/snap/sound references and removes the GAO. The soft branch sets lifecycle
raw bits, schedules world-table and visibility reconstruction, tears down its
sound instance and may release hierarchy matrix state
(`external/irisEng/ai/ai_gameobjectfunctions.c:155-211`). World activation,
visibility-list admission and EOT-list construction are separate functions
(`external/irisEng/world.c:44-62,153-172,671-767`). A valid object key or a
message referencing that object cannot imply any of those runtime transitions;
only an executed function and its current guard/target context can.

### Global queue vs ordinary recipient queue

The ordinary queue is per-GAO and send calls use the currently selected GAO
context. A separate `MSGRegOwner` stores the one global queue owner, and
`MSGSendAllOwner` appends to that owner's message array; the function name does
not imply iterating over all objects (`external/irisEng/ai/ai_messagefunctions.c:193-231,356-410`).
Treat these as distinct addressing modes in the ledger: `recipient=explicit GAO`,
`recipient=current/ultra GAO`, or `recipient=registered global owner`. Preserve
null/reference tokens and mark unresolved GAOs rather than guessing which mode
was intended from a numeric script argument.

### Closed source paths and remaining unknowns

Source-closed in this audit: (1) world activation -> EOT table derivation ->
EVE/AI/animation table membership; (2) outer EVE/ANI/AI and late callback-ID
phase order; (3) AI callback slot gates/delay/jump execution; (4) local and
registered-owner message enqueue/read/consume behavior; (5) the Iris example
sender message 30 -> Cine_Textes drain/priority/text/sound state; (6) the generic
Cine_Events departure gate -> `TRAPlayAll` -> owner EVE event execution; (7) the
Cine fade branch -> GFX allocation/flags/material/parameter writes; and (8)
`OBJDestroy` hard/soft behavior plus separate activation/visibility/EOT lists.
All references are Iris source behavior and, where shown, the Iris game's
compiled scripts. They establish a reusable candidate architecture, not retail
parity.

Still unknown for PC 2003: whether its retail executable has the same callback
IDs/order and raw masks; whether all Iris model callback slots/functions map to
the same retail code; live script-instance/owner-table membership and callback
delay/jump state; per-scene save flags, collision-zone results and current
active actors; actual message queue contents/order and overflow events; whether
a given sender/target ran; effect type-to-parameter semantics in the retail
renderer; and final raster output/alpha/blend/light behavior. `external/jaded`
and `external/iristool` are corroborating archive/editor artifacts only where
their own serialized structures agree; they do not close these retail runtime
unknowns. The verified PC bundle is not a dynamic trace and supplies no
independent callback/queue/effect execution evidence.

### Coverage matrix and questions that remain open

| Static subsystem | Source-closed result | Evidence | Still not proven |
|---|---|---|---|
| World activation and EOT tables | Activated-object predicates derive separate animation, AI, EVE, collision and hierarchy tables. | `world.c:44-62,671-767` | Retail PC flag equivalence; live table membership for a given BF. |
| Frame/callback scheduling | Outer EVE → ANI → AI sequence, late callback-ID 2 pass, slot delay/jump handling, and render-context IDs 0/1 are separate call paths. | `engine.c:184-215,279-289`; `ai_common.c:711-897`; `animation.c:2414-2475`; `geo.c:771-779,888-893`; `geostr.c:2893-2895,3142-3144` | Which exact function slot/ID is reached for a particular retail scene and frame. |
| EVE lifecycle and nested actions | Owner event tracks have run/pause and explicit TRA control; event AIFunction dispatch uses target/owner context. | `events.c:168-224,333-382,1996-2102,2683-2722` | Retail callback parity; all runtime links and owner/target presence. |
| Message queues | Fixed five-message local queues; LIFO `MSGGet`; consuming `MSGGetByID`; non-consuming `MSGRead*`; one registered global owner queue. | `ai_messagefunctions.c:3-15,34-45,143-168,193-231,269-332,356-410` | Live recipient, actual enqueue/drop, ordering and callback polling in retail. |
| Dialogue/message consumer | Type-30 sender and Cine_Textes receiver show message selection feeding text and associated sound fields. | `action_reaction.c:205-241`; `global_managers.c:2964-3100+` | Per-BF sender-to-slot linkage, receiver activation, final voice-bank lookup/playback timing. |
| Object state | Hard/soft destroy, activation admission, visibility admission, and EOT removal/rebuild are distinct operations. | `ai_gameobjectfunctions.c:155-211`; `world.c:44-62,92-123,153-172,671-767` | Retail raw-bit mapping and runtime visibility/collision/save-state outcome. |
| GFX/effect writes | GFX entries can be added/deleted/aged, assigned flags/material and typed parameters; Cine fade is a concrete caller. | `ai_gfxfunctions.c:49-158,204-270,302-513`; `gfx.c:308-331,1117-1265`; `global_managers.c:4061-4169` | Retail effect-ID/parameter mapping, shader implementation, blend/alpha/light and resulting pixels. |

The source-only investigation cannot answer these remaining questions without a
retail PC execution trace or stronger PC-specific binary/source mapping:

- Does the 2003 PC executable use the same AI callback IDs, scheduler order,
  identity/`flags2` masks, queue size/order, or GFX parameter dispatch as Iris?
- For a selected cinematic container, which owner GAOs and script instances are
  actually loaded, activated, linked, and entered into each world table?
- Which save flags, zone/collision results, actor-state gates, action selections,
  and callback delay/jump-node states are live at each timestamp?
- Which queued messages were actually delivered, dropped, consumed or merely
  inspected, and which recipient callback consumed them?
- Which GFX element type/parameter IDs correspond to the visible effect in the
  retail renderer, and how do the retail render passes combine its material,
  alpha, lighting, camera and ordering?
- Which missing cinematic participants are absent because of resource loading,
  script gating, conditional creation, or later removal?

The static implementation boundary is therefore clear: JadeView can safely
show serialized references, Iris-derived candidate state transitions, and
source-confirmed operations with provenance. It cannot label a conditional
actor/effect as actually active, reconstruct arbitrary script branches, or
claim retail rendering parity without the missing per-scene runtime facts.

## Serialized AI functions and action-kit installation path

### AI instance, model, and callback-function resolution

For an Iris GAO whose serialized identity flags contain `0x20`, the object
reader consumes an AI-instance key and schedules `AI_ul_CallbackLoadInstance`
(`external/irisEng/gameobject.c:3092-3099`). That instance record contains two
references: an AI model and instance-variable data
(`external/irisEng/ai_common.c:1387-1407`). The model reader walks typed key
pairs: `.ova` resolves the model's variable/callback block and `.ofc` resolves
function objects (`ai_common.c:1325-1384`). The `.ova` reader decodes variable
metadata/defaults, then reads exactly five callback function keys into slots
0–4, followed by an optional counted array of instance-function keys
(`ai_common.c:1124-1322`). Thus slot identity is positional in the serialized
model data; a function's script name is not what selects its slot.

Each `.ofc` loader reads the function's archive key. It scans the static
`AI2C_gat_fctdefs` key-to-C-function table and assigns the matching native
function pointer, if present (`ai_common.c:1023-1109`; registry declaration and
entries in `external/irisEng/ai/ai_compiledscripts.c:112-2639`). For example,
key `0x350091C2` maps to `Cine_Events_cine_init` (`ai_compiledscripts.c:832`).
When no registry match exists, the loader retains the serialized AINode body
and logs “not compiled in”; when there is a native match, it can execute the
C function path. Some table entries map to `sc_dummy`, which simply returns
success without executing script behavior (`ai_compiledscripts.c:36-38` and
examples at `:133,167,212`). When `AI_CompiledAIEnabled` is true, a mapped
`sc_dummy` takes precedence over any interpreted nodes; this is a meaningful
limitation of this Iris registry, not proof the same functions are empty in
retail. `AI_pst_RunFunction` chooses the native pointer only when
`AI_CompiledAIEnabled` is true; otherwise it interprets the node graph
(`ai_common.c:688-700`). So a missing native mapping is not evidence that the
script has no behavior: it can still have an interpretable node body. Conversely,
a registry entry does not prove that its function is installed in any particular
owner slot.

During instance loading, `AI_ResolveInstanceRef` resolves model function refs
and variable refs and combines instance-variable data with model defaults by
matching names; `AI_ReinitInstance` then copies the five model callback pointers
into the live instance slots, initializes jump nodes/timers, sets active slot
bounds, and clears dynamic registrations on non-universe instances
(`ai_common.c:498-559,561-679,375-420`). `AI_RunInstance` later dispatches
those live slots by slot index after the delay reaches zero; `AITrackChange` and
its current-slot variants can replace a live slot, reset its jump node, and
adjust slot bounds (`ai_common.c:711-762`; `external/irisEng/ai/ai_aifunctions.c:113-275`).
`AITrackStop` clears the selected slot (`ai_aifunctions.c:161-198`). The
slot-to-function pointer chain is therefore:

```text
GAO AI-instance ref -> instance model ref -> .ova callback key at slot N
-> .ofc function object -> native registry pointer OR AINode body
-> live instanceCallBacks[N] -> scheduler / AITrack replacement
```

### Dynamic callbacks and context owner

`AICBAdd` is a separate runtime registration mechanism, not another serialized
callback slot. It appends an element containing callback ID, function pointer,
and a GAO context to the target AI instance's `instanceAICBList`;
`AICBDel` matches all three values and nulls the function pointer
(`external/irisEng/ai/ai_aifunctions.c:409-495`). During `AI_ExecCallback(owner,id)`,
Iris first invokes the model's `instanceFuncs[id]` when present, then scans the
dynamic list for entries with the same ID. For each match, it changes the
current GAO and AI instance to the registered context GAO before running the
function (`external/irisEng/ai_common.c:812-897`). The table owner that receives
the callback dispatch and the script context GAO can therefore differ. The
usual non-universe `AI_ReinitInstance` clears this list, so dynamic registrations
are runtime state and are not reconstructible from the `.ova` callback array.

The compiled Crochax code supplies a concrete registration example: it resolves
function keys such as `0x720026B3`, then registers `exec_do_snake` with callback
ID 2 and an explicit context GAO (`external/irisEng/ai/ai_bge/ai_compiledscripts_actor_crochax.c:343-405`).
The registry maps that function key to its C implementation in
`ai_compiledscripts.c` (the full key/function table is at `:112-2639`). This
proves the key → compiled function → dynamic callback registration route in
Iris. It does not establish which serialized actor instance or retail scene
runs the registration branch.

### Action-kit and actor-animation EVE route

The GAO object reader's actor-object branch reads animation, shape, group, and
action-kit references; the action-kit key is resolved with
`ACT_ul_ActionKitCallback` (`external/irisEng/gameobject.c:2980-3015`). The action
kit loader reads action keys into indexed entries. Each `ACT_ul_ActionCallback`
record decodes action elements with an EVE event-list key, transition data,
shape key, per-element advancement threshold, frequency, and flags; it resolves
event-list keys through `EVE_ul_CallbackLoadListTracks` and shapes through the
shape loader (`external/irisEng/actions.c:457-602`). The action kit is therefore
a table of actions, and each action is itself a sequence of action elements;
its element EVE list is not the cinematic owner's `extended->eve` list.

`ACT_SetAction(gao, actionId, ...)` indexes that kit, chooses the action, and
installs the first action element's event list via `ANI_SetTrackList`, along
with its shape, action-item index, and frequency (`actions.c:146-409`).
`ANI_SetTrackList` calls `EVE_InitData`, marks the list elements/tracks with
runtime flags, and primes the first event's flags (`external/irisEng/animation.c:850-878`).
`ANI_ChangeAction` advances the action's element and installs the next element's
EVE list and shape (`animation.c:884-942`). The actor animation loop later
runs `ANI_OneCall` / `ANI_PlayGameObjectAnimation`; it evaluates the current
actor's event data with animation-specific filtering where applicable
(`animation.c:1482-1704`). `ANI_MainCall` drives actors from the animated-object
EOT table, not from the cinematic owner's event table (`animation.c:2414-2475`).

When `ACT_SetAction` is requested while `ENG_gb_EVERunning`, it stores a pending
action transition and elapsed-time state instead of replacing the action list
immediately (`actions.c:146-220`). The animation/event runner then applies that
pending transition and restores/sets event time (`animation.c:1370-1450`). This
is a concrete synchronization seam between an EVE-triggered action switch and
the actor action-kit path; the two systems remain separate clocks/list owners.

A direct one-off animation install also exists in `ANI_pst_Load`: it loads a
supplied EVE track-list key when no action kit is attached, but sets the actor's
`animPtr[0]` to null when an action kit exists (`animation.c:1936-2017`). This
means a GAO having an action kit does not by itself select a meaningful action
from a scene snapshot. Iris's object reinitialization explicitly calls
`ACT_SetAction(gao, 0, 0)` for actor objects with a kit (`engine.c:70-87`), but
that is a reinitialization path and does not prove that every retail cinematic
load invokes it at the same time or preserves the same action state.

### Mapping boundary and JadeView consequence

The current local Iris source definition and header agree on an active declared extent of 2,530 for `AI2C_gat_fctdefs` (an observed source value, not an immutable engine constant),
but it is a source build's registry, not a universal mapping from every archive
`.ofc` key to a retail PC function. Some entries point to `sc_dummy`; function
keys absent from the table fall back to serialized nodes only if those nodes
are available, while a stubbed table hit may suppress that fallback in compiled
mode. The repository does not provide a complete retail-PC function
registry or a live trace proving registry lookup outcome for every resource.
The `.ova` reader proves the five positional callback keys and optional
instance-function array layout in this Iris implementation; it does not by
itself reveal which slot is active after arbitrary script branch changes.

JadeView can represent this without special cases as typed edges:
`GAO -> AI instance -> AI model -> OVA -> callback slot -> OFC key -> compiled
registry entry or node body`; then separately record `slot state` and
`dynamic callback(id,function,context GAO)` events. For action kits, represent
`GAO -> action-kit -> action[index] -> element[index] -> EVE list/shape`, plus
current action/item, transition and actor-event clock. A mere model key or
action-kit key should remain “available definition,” never “running callback/
action.” The Iris PC-2002/pre-release code confirms this candidate architecture;
retail 2003 callback table parity, activation timing, selected action and event
clock remain unverified.

## Callback mapping status: names are not implementations

The current archive-to-inspector path preserves a callback's numeric key and
resolved name, but does not preserve what kind of source evidence produced that
name. `PcScriptCallbackSlotReader.Read` resolves each nonzero OVA slot through a
`uint -> string` table (`src/Features/Cinematics/Formats/Pc/PcScriptCallbackSlotReader.cs:9-33`;
`CinematicScriptReader.cs:599-605`). `CinematicScriptCallbackSlot` consequently
contains only `Index`, `FunctionKey`, and nullable `FunctionName`
(`src/Features/Cinematics/Models/CinematicScriptOwner.cs:69`). The embedded
name table is generated by matching registry rows, not by validating their
function bodies (`tools/Generate-CinematicScriptFunctionNames.ps1:10-20`).

This distinction matters for `sc_dummy`: Iris defines it as a placeholder that
returns `1000` (`external/irisEng/ai/ai_compiledscripts.c:36-38`) and maps many
keys to it. When Iris compiled dispatch is enabled, `AI_pst_RunFunction` calls
the registry pointer instead of interpreting the node body
(`external/irisEng/ai_common.c:688-700`). Thus a name-table hit can be a mapped
stub in that Iris configuration; it is not proof of a recovered behavior, and
does not establish retail PC behavior.

JadeView already labels the exact `sc_dummy` symbol as a source stub in
`CinematicScriptReader.DescribeCallbackName` and the cinematic inspector
(`src/Features/Cinematics/CinematicScriptReader.cs:33-36`;
`src/Features/SceneEditor/Cinematics/EditorShell.Cinematics.Timeline.cs:417-449`).
The inventory also warns that configured callbacks can be skipped by live
scheduler gates (`EditorShell.Cinematics.ScriptInventory.cs:148-169`). Static
callback paths start from `owner.CallbackNames` and are filtered through model
functions (`CinematicScriptReader.cs:56-84`), while `HasCinematicFunctionReference`
also accepts model references without slot assignment
(`CinematicScriptBehaviorAnalyzer.cs:883-885`); the UI correctly describes
those as source references, not execution (`EditorShell.Cinematics.Timeline.cs:68-79`).
These are useful caveats, but the distinction is prose-only: the typed owner
model and exported graph records do not carry stub/implementation status.

The status loss reaches each audit surface:

- Cinematic clip graph exports raw `CallbackSlots` and `ModelFunctionNames`
  (`tests/ExportVerification/CinematicScriptGraphAudit.cs:61-77, 133-167`).
- World graph does the same (`tests/ExportVerification/WorldScriptGraphAudit.cs:45-83`).
- Archive indexing counts a known function name as mapped and emits only the
  name/key pair; `sc_dummy` therefore counts as mapping coverage
  (`tests/ExportVerification/ArchiveCinematicScriptIndex.cs:241-253, 280-286,
  595-602, 510-546`).

### Minimal typed model and patch order

Add a source-resolution record beside `CinematicScriptCallbackSlot`, without
changing the meaning of the slot itself:

```text
CallbackFunctionBinding {
  FunctionKey, Symbol,
  RegistryStatus: Unmapped | RegistryMapped,
  SourceBodyStatus: Unknown | Stub | ImplementationFound,
  SourceFile?, SourceLine?
}
```

Attach it as nullable evidence to each decoded slot, and keep these independent
facts separate in graph output: (1) serialized slot assignment, (2) registry
key/name mapping, (3) source-body classification, (4) source-call-graph
reachability from an assigned root, and (5) runtime dispatch/activation, which
remains `Unknown` for archive-only inspection. Classify `sc_dummy` from its
source definition rather than special-casing archive keys; do not label a
registry mapping `Implemented`. Rename or annotate archive counters as
`RegistryMapped*`, and export the typed status for both clip and world graphs.
Keep `ModelFunctionNames` a separate list of model references; never merge it
into serialized slot assignment. Analyzer findings originating only from a
model reference should retain a `ReferenceOnly` evidence label, while source
paths from assigned slots remain `SourceReachable`, not `RuntimeReachable`.

This is an implementation-ready seam for the next registry pass; no production
code is changed in this audit update. `sc_dummy` is confirmed as a stub only in
the supplied Iris source. A complete retail PC registry/body classification,
the exact compiled-vs-interpreted retail mode, and runtime reachability still
require independent evidence.

## OFC AINode interpreter: execution model and preview seam

### What Iris confirms

An Iris `tAINode` is exactly eight bytes: `int32 val`, `int16 val2`, byte
`flags`, byte `category` (`external/irisEng/ai.h:7-13`). The loader reads those
fields in that order (`loading.c:227-239`); the `.ofc` function loader reads
`stackOffsetNum`, the node-body byte size, and then eight-byte nodes, plus
extra nodes/string constants/local metadata where present
(`ai_common.c:1023-1109`). It looks up the enclosing function resource key in
the compiled registry, so this **function key → native implementation** mapping
is distinct from a node's **category/function selector**.

Before interpretation, `AI_ResolveEnums` builds ID-to-table-index mappings for
categories, keywords, engine functions, types and fields
(`ai_common.c:1459-1473`). Model resolution converts node category and `val2`
through that map and resolves object references in category-1 operands
(`ai_common.c:501-530`). After conversion, the category table dispatches a node
to the corresponding type, keyword, engine-function, field, variable, text or
event-parameter evaluator (`ai/ai_categories.c:3-143`). Category “Function” calls
the `AI_gast_Functions` table by its converted selector (`ai_categories.c:14-17`);
for example selector IDs `0x1784` and `0x1785` map to `AIExecute` and
`AITrackChange` (`ai/ai_functions.c:45-53`). These are engine APIs, not the
compiled-script registry.

The interpreter repeatedly dispatches nodes until a terminator/control result
is returned (`AI_pst_RunFunction`, `ai_common.c:688-700`). Confirmed control
keywords include relative jump and conditional jumps (`ai/ai_keywords.c:198-268`),
`Meta` setting a jump-node pointer (`:156-160`), `Return`, `ReturnTrack`, and
`Stop` clearing the current jump node and returning distinct sentinels
(`:91-107`). `AIExecute` pops a function reference, runs its OFC/native function
with the caller's stack-frame offset, and propagates control sentinels
(`ai/ai_aifunctions.c:49-110`). `AITrackChange` replaces a live callback slot,
resets its node cursor, and adjusts active slot bounds (`:113-159`); the world
scheduler invokes active callbacks only after per-slot callback timers permit it
(`ai_common.c:711-762`).

The value stack is typed: scalar/vector/object pushes allocate 32-byte global
stack cells and carry a parallel type record; the user stack instead advances
by the registered type size (`ai_common.c:17-109, 111-131`). `Push`, `Pop`,
`PopSP`, and `PopNow` move typed values between those stacks
(`ai/ai_keywords.c:109-153`). `DuringInit` stores a negative per-track timer;
`During` adds the current frame delta, reports whether the timer remains below
zero, and clears it after expiry (`ai_keywords.c:162-195`). This is source
evidence for timed/branching callback code, not a standalone wall-clock wait
command.

`AI_pst_RunFunction` prefers a native pointer when Iris compiled-AI mode is on;
otherwise it interprets the retained node cursor (`ai_common.c:688-700`). The
loader allocates node storage only when no compiled pointer was found; for a
compiled entry it may traverse/discard nodes or skip their decoding in its
speed/read mode (`ai_common.c:1067-1084`). Therefore “node bytes exist in the
resource” does not mean the interpreter body is retained or is the active
execution path. A static viewer must preserve node availability separately
from registry status and runtime mode.

### JadeView today and generic executor direction

JadeView has a typed **EVE cinematic message** decoder and an engine selector
catalog for EVE literal calls; it does not parse or execute AI OFC AINode bodies.
The selector catalog resolves names/descriptions for event calls
(`src/Features/Cinematics/CinematicEngineFunctionCatalog.cs:7-60`; resource
`Data/engine-function-selectors.json`), which is not a substitute for the Iris
`AI_gast_Functions` evaluator table or the key-to-native registry.

An implementation should proceed in bounded layers:

1. Decode each OFC function into raw node records, resource key, node availability,
   stack-frame size, string constants, and source-resolution status. Preserve
   raw category/selector IDs alongside any normalized IDs; Iris applies enum
   remapping before execution.
2. Add a symbolic disassembler/CFG first: category + opcode, operand/reference
   resolution, branch targets, terminators and unsupported nodes. Validate every
   target and stack effect against available bounds; avoid executing malformed or
   unknown records.
3. Build a deterministic per-owner VM state: callback-slot function/cursor,
   timed wait state, local/global/user typed stacks, current track and nested
   call frames. Model `AIExecute`, track changes and control sentinels separately
   from EVE playback time.
4. Route side effects through explicit preview adapters that emit timestamped
   state deltas (message delivery, transform/action, visibility, audio/effect,
   camera/world changes). Unsupported APIs remain visible as unresolved events;
   do not mutate the archive-backed source world or claim that an inferred branch
   ran. Keep source-reachable, statically evaluable, preview-executed, and retail
   runtime-observed evidence as separate statuses.

This direction is generic and can drive a script/effect preview without
scene-specific rules. The current code has only source indexes and EVE playback;
OFC decoding, CFG/stack validation, execution, and effect adapters remain to be
implemented. Iris establishes this interpreter structure for its pre-release
engine, not byte-for-byte PC 2003 semantics. The retail executable's exact node
layout/remapping, opcode table, registry coverage, compiled-vs-interpreted mode,
and scheduler ordering remain to be validated against PC-specific assets or
runtime evidence.

## Retail PC OFC storage probe: `sally_clean.bf`

This section uses the installed archive at
`E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf`
(2,673,827,836 bytes; archive timestamp 2026-10-19). No game process was started.
The archive's BIG index resolves container `FF005833` to `ff005833.bin`; JadeView's
`Archive.Read` decodes its stored 1,167,356 bytes into a 2,226,331-byte FF stream.
This reading path is implemented by `src/Features/Archive/Archive.cs:32-89`
(BIG index and entry metadata), `:92-96` (`Read`), and `:109-141` (bounded
raw/LZO decode).
The PC loader completes that stream with 3,040 records, including 73 `s` script
models and 660 `r` records. These are observations from the installed retail
archive, not an inference from Iris.

### Confirmed owner → model → OFC-key → raw-record chain

The current JadeView path is consistent with the decoded retail bytes:

1. A serialized owner brain is kind `q`; `PcSceneLoader.ReadResource` enqueues
   its first DWORD as kind `s` and second as kind `t`
   (`src/Features/SceneEditor/Formats/Pc/PcSceneLoader.Resources.cs:18-21`).
   `PcScriptModelResolver.TryRead` reads those two DWORDs as model key and
   instance key, then accepts the resolved kind-`s` record
   (`src/Features/Cinematics/PcScriptModelResolver.cs:32-47`).
   In the archive bytes, actor `BF00218D` (`CINE_APRES_MDISK_EVT02.gao`) points
   to brain `BF002190` (`q`), whose 8-byte payload is
   `C39100358F2100BF`: model `350091C3`, instance `BF00218F`.
2. In `FF005833`, shared model key `350091C3` is a decoded kind-`s` record at
   **decoded stream payload offset 1,512,170**, size 96. Its bytes are twelve
   `(uint32 key, 4-byte extension)` pairs; the `.ofc` entries include keys
   `350091C2`, `350091C8`, `350091C9`, `350091CA`, `350095FA`, `3500B021`,
   `3500B724`, `3500B726`, `3500B728`, `3500B84D`, and `26004C6C`.
3. JadeView's generic `ReadScriptModel` parses exactly this 8-byte row shape,
   enqueueing `.ova` as kind `t` and `.ofc` as kind `r`
   (`src/Features/SceneEditor/Formats/Pc/PcSceneLoader.ResourceLists.cs:12-21`).
   Each `r` then becomes a retained record with key/kind/payload offset/size and
   frame range. `ReadFramedRecord` validates its 32-bit length and invokes the
   kind handler (`PcSceneLoader.Frames.cs:43-62`); `r` is currently a framed
   leaf with no payload decoder (`PcSceneLoader.Resources.cs:69-74`). The record
   shape is defined in `src/Features/SceneEditor/Models/PcSceneLoader.Models.cs:5-27`.

Two concrete retail payload candidates are therefore available for the next
format probe, rather than merely model/function-name matches:

| Function key from model `350091C3` | kind-`r` payload in decoded `FF005833` | First three little-endian DWORDs | Interpretation status |
|---|---:|---|---|
| `350091C2` (`Cine_Events_cine_init` in the source-name catalog) | offset `1,535,598`, length `240`; outer frame length at `1,535,594` is `240` | `0x0000000C`, `0x00000AD8`, `0x000000E4` | Raw bytes verified; matches compact compiled candidate with 228 trailing local-string-data bytes. |
| `350091C9` (`Cine_Events_cine_play_eventtrack` in the source-name catalog) | offset `1,535,858`, length `12`; outer frame length at `1,535,854` is `12` | `0x00000054`, `0x000004B8`, `0x00000000` | Raw bytes verified; matches compact compiled candidate with zero trailing local-string-data bytes. |

The `s` rows establish that these keys are `.ofc` references, and the matching
kind-`r` records establish the actual retained resource payloads. The names in
parentheses come from JadeView's source-name catalog, not from these raw payloads.
Offsets above are inside the **decoded FF005833 stream**, not physical offsets in
the BIG archive; the BIG entry is compressed. The two samples demonstrate that
current JadeView can retain bytes needed for a disassembler. At this point the
compact compiled interpretation is supported by Iris and iristool source, while
retail runtime mode remains unverified.

### Compact compiled path: the samples fit Iris, conditionally

The initial naïve byte-count check above is not sufficient: Iris has a compiled,
speed-mode branch that deliberately skips serialized main-node bytes. In
`AI_ul_CallbackLoadFunction`, Iris reads `stackOffsetNum`, resolves the current
function key through `AI2C_gat_fctdefs`, and reads `nodeSizeSum`; it reads the
node array only when the key has no native `funcPtr` **or** speed mode/read mode
does not select the fast path (`external/irisEng/ai_common.c:1045-1084`). The
compiled key registry contains both sample keys (`external/irisEng/ai/ai_compiledscripts.c:832-834`).
After the omitted main-node body, the loader reads `extraAiNodeSizeSum` with
read mode 2, then reads `binarizedLocalDataSize` with read mode 1 and consumes
that many bytes as local string data (`ai_common.c:1086-1100`). `LOA_ReadLong`
shows that read mode 2 consumes **zero bytes** when both
`LOA_gb_SpeedMode` and `BIG_gi_ReadMode == 2` are true, while mode 1 consumes the
DWORD normally (`external/irisEng/loading.c:52-70`). This changes the interpretation
of the third DWORD in these compact resources:

| Key | First two DWORDs under Iris | Third DWORD in compiled fast path | Exact compact-size check |
|---|---|---|---|
| `350091C2` | stack offset `12`; omitted main-node byte count `2776` (divisible by 8) | `228` local-string bytes | `12-byte prefix + 228 = 240-byte resource` |
| `350091C9` | stack offset `84`; omitted main-node byte count `1208` (divisible by 8) | `0` local-string bytes | `12-byte prefix + 0 = 12-byte resource` |

Thus both real PC blocks exactly fit Iris's *compiled fast-path representation*;
the prior claim that they disprove Iris compatibility is withdrawn. The current
best explanation is that these `.ofc` records retain stack/node counts and
binarized local-string data while the compiled engine implementation supplies
behavior and the serialized node body is omitted. This is supported by the
source branch, the exact PC size arithmetic, and Iris's key-to-native registry.
It is still a **conditional Iris model**, not proof that PC 2003 uses the same
mode selection or registry. If the function lacks a native pointer, or the
runtime takes the non-fast/interpreter path, the declared node bytes are absent
from these resources and cannot be decoded from them as AINodes.

The flag test is a bit test, not an enum equality: read mode 1 and read mode 4
both have bit 2 clear and therefore read a DWORD even in speed/read-mode 2; mode
2 has bit 2 set and may suppress the read. In this function loader the stack
offset, node size, and local-string size use mode 1, while the extra-node size
uses mode 2 (`ai_common.c:1050,1067,1086,1094`). The subsequent optional local
node count also uses mode 2, but that whole local-data-node section is guarded
by `!LOA_gb_SpeedMode` (`ai_common.c:1101-1109`). The compact-path arithmetic
therefore is exact: mode-2 extra-size read returns zero without advancing the
cursor, then mode-1 local-string size reads the third DWORD. For C2 it is 228,
consuming the remaining 228 bytes; for C9 it is 0, ending at byte 12. Iris's
speed loader selects read mode 2 for buffered file reads
(`external/irisEng/loading.c:525-535`), and `LOA_FetchFile` fetches the bounded
resource payload (`:549-561`). The mode-4 use is visible in the model loader:
`AI_ul_CallbackLoadModel` first counts 8-byte key/type rows using mode 4, then
walks the same rows with mode 1 and resolves `.ofc` entries to
`AI_ul_CallbackLoadFunction` (`external/irisEng/ai_common.c:1341-1384`). That
matches the row shape in JadeView's retail `s` record; mode 4 is deliberately
not the skip mode.

The independent `iristool` reader corroborates the compact/binarized distinction,
but its trailing-field label is less precise than Iris's consumer:
`BFScriptingFunctionCodeChunk.parse` reads the file length and two DWORDs
(`randomAccessPoolOffsetter`, program-code length), then skips program-code
parsing when `insideBinFile` is true (`external/iristool/src/BFDataFileLib/BFChunkParsers/Map/BFScriptingFunctionCodeChunk.java:52-101`).
It skips the second program-code section in that mode, but still reads a
`descLen` and the described bytes when nonzero (`:103-141`). For the actual
retail samples, this parser path consumes C2's third DWORD as `descLen=228` and
then consumes exactly 228 bytes; C9's third DWORD is `descLen=0`. The cursor
therefore lands exactly at each declared resource end without an alignment
fallback. Iris itself names that field `binarizedLocalDataSize` and stores the
consumed bytes in `tAIFunction.localStringConstsData` (`external/irisEng/ai_common.c:1094-1100`,
`external/irisEng/ai.h:57-66`). Interpreted local-string nodes resolve against
this buffer (`ai_common.c:503-520`), and native callbacks also use offset-based
strings from it (for example, `external/irisEng/ai/ai_bge/ai_compiledscripts_e3.c:152-200`).
Thus `iristool`'s `desc` identifier is a parser label, not evidence these bytes
are a human-readable description. JadeView preserves them opaquely as
`LocalConstantBytes` in its
compact candidate model; it infers neither a text encoding nor function
semantics from their contents. `BFChunkParseClass.initParse` reads the explicit chunk length in that
case (`external/iristool/src/BFDataFileLib/BFChunkParseClass.java:46-64`), and
`BFDataFileClass` derives `insideBinFile` from its data type
(`external/iristool/src/BFDataFileLib/BFDataFileClass.java:304`). This proves
that the tool intentionally does not expose serialized nodes for binarized OFC,
and independently confirms the 228/0 trailing-byte length interpretation; it
does not prove retail runtime mode or compiled dispatch for the PC executable.

Keep both payload hypotheses in the generic inspector: **compact compiled**
(`stackOffset`, `nodeSizeSum`, optional/local-data length and bytes, with no
serialized main nodes) and **full interpreted OFC** (node bytes present, length
and 8-byte alignment validated). Select neither based only on a function name;
the first is structurally supported for these samples, while runtime mode and
retail registry coverage remain unknown. A compact parse should report
`NativeBodyCandidate`/`NodeBodyOmitted`, not `RuntimeExecuted`.

### Implementation-ready next seam

Add a passive retail resource inspection layer before any VM: represent a script
function as `(owner key, brain key, model key, instance key, function key,
extension, source container, record kind, decoded-stream offset, payload size,
outer-frame size, payload hash, raw bytes)`. Reuse the already retained
`PcSceneLoader.Record` plus its origin byte array; do not reconstruct identities
from function names. Add a bounded candidate-header probe with independent
results for compact compiled payloads and full interpreted nodes. For the
compact candidate, verify `12 + localDataSize == payloadSize`, preserve node
byte count without reading nodes, and expose the local-data bytes. For the full
candidate, validate node-size divisibility, all optional sections, and exact
final cursor before emitting nodes. Report source registry status, node-body
availability, and runtime dispatch as separate fields. This preserves a generic
path toward an OFC inspector without promoting a source name to proof of execution.

The next research requirement is to verify the compact compiled-path hypothesis
against the PC 2003 executable or another retail-specific parser, then establish
whether any retail resource retains full interpreted nodes. Current JadeView
provides the resource graph and raw payloads; Iris and iristool explain a
plausible compact encoding and its parser treatment, but do not prove retail
runtime mode, registry coverage, or execution. No OFC VM should execute until
those PC-specific dispatch and selector semantics are independently established.

## Retail payload audit and callback-to-state path

### Actual FF005833 / BF00218E compact-resource audit

The latest archive frame audit is recorded in
`artifacts/light-component-integration/script-payloads.log`. For the loaded
`FF005833` scope reached from cinematic/world `BF00218E`, it reports
`resources=661` and `CompactMetadataCandidate=661`; no resource in this audit
failed the bounded compact-layout checks. This is a result of JadeView's static
archive decoder over retained kind-`r` payloads, not an execution trace. In
particular, it does not imply that the retail executable registered or invoked
all 661 function keys.

The earlier direct byte checks remain useful representative fixtures:

| OFC key | Payload length | Header values | Structural check |
|---|---:|---|---|
| `350091C2` | 240 | stack count 12, declared node bytes 2776, local-constant-data length 228 | 12-byte prefix + 228 bytes exactly fills resource |
| `350091C9` | 12 | stack count 84, declared node bytes 1208, local-constant-data length 0 | header exactly fills resource |

The C2/C9 header values and exact length arithmetic are raw retail archive
evidence. Their fit with Iris's speed-mode compiled loader remains conditional
cross-source interpretation; no node body is present, and the audit does not
upgrade a source-name match into proof of callback execution.

### How a compact function key reaches source-call evidence

The current static association is a sequence of separate evidence steps:

1. A kind-`s` PC script model stores 8-byte `(key, extension)` rows;
   `PcSceneLoader.ReadScriptModel` enqueues `.ova` as kind `t` and `.ofc` as
   kind `r` (`src/Features/SceneEditor/Formats/Pc/PcSceneLoader.ResourceLists.cs:12-21`).
   The compact OFC reader validates only bounded metadata and preserves the
   declared node byte count (`src/Features/Cinematics/Formats/Pc/PcCompiledScriptFunctionReader.cs:12-65`).
2. The script model's OVA block supplies five callback function keys after
   variable storage (`src/Features/Cinematics/Formats/Pc/PcScriptCallbackSlotReader.cs:7-33`).
   `CinematicScriptReader.Load` retains the script model's source-name catalog,
   callback slots, and model-scoped OFC references on each owner
   (`src/Features/Cinematics/CinematicScriptReader.cs:428-457`). The compiled
   resource list is resolved from the model's explicit kind-`r` references in
   the model's own load-world scope; it is not a global key-only join.
3. Callback function keys are mapped to names using the embedded key/name
   registry (`CinematicScriptReader.cs:601-608`). Reachability starts only at
   names actually assigned in callback slots and follows indexed call/dispatch
   edges only when the target also belongs to that model's catalog
   (`CinematicScriptReader.cs:53-82,179-185`).
4. `ReachableEngineCalls` joins those static function paths to the embedded
   source-call index and returns API, source arguments, branch conditions, file,
   line, and callback path (`CinematicScriptReader.cs:207-218`; loading at
   `:634-641`). Those are source AST facts associated with a callback name/key;
   they are not decoded from the compact OFC bytes and do not identify a time
   when the source function ran.

Thus the compact payload contributes structural metadata while callback slots
and the source catalog provide identity and static source implementation.
These must remain distinct: resource parsed, slot assigned, source function
mapped, source call reachable, and runtime call executed are different states.

### Key-to-native callback path (Iris source model)

See [Native callback resolution](game-script-native-callbacks.md) for the source-backed key-to-slot and dispatch chain.

### General activation/deactivation behavior: visibility and inactive flags

The most reusable state-changing path for actors, effects, lights, and other
GAOs is `OBJ_FlagInvisibleSet` / `OBJ_FlagInactiveSet`. Iris registers them as
selectors `0x1FD8` and `0x1FD9` (`external/irisEng/ai/ai_functions.c:255-256`);
JadeView's selector catalog uses the same values
(`src/Features/Cinematics/Data/engine-function-selectors.json:228-229`). The
interpreter wrappers take one integer argument and dispatch against the current
game-object context (`external/irisEng/ai/ai_gameobjectfunctions.c:1340-1350,
1393-1403`). The shared helpers then:

- Set/clear `flags2` bit `0x00200000` for invisible and `0x00400000` for
  inactive (`ai_gameobjectfunctions.c:1319-1338,1353-1391`).
- Mark world visibility for refresh in both cases. Inactive additionally marks
  tables/EOT and spatial refresh, and can detach hierarchy/matrix state and
  register inactive audio (`:1373-1390`). This is more than a draw toggle.
- Leave the authored resource/record in place. It is a runtime state transition,
  not archive deletion. Iris's `OBJDestroy` is a distinct selector/helper and
  needs separate semantics before it is folded into this same model.

JadeView already has a deterministic preview reducer for literal EVE commands:
`CinematicObjectState` consumes source-clip and nested-event literal calls
(`src/Features/Cinematics/Models/CinematicObjectState.cs:27-40`), decodes the
two object selectors and group variants (`:128-141`), and applies control flags
to scene objects as the playhead advances (`:48-78`). `CinematicPlayback`
builds `ScriptStateCalls` from the serialized clip calls plus nested event
commands (`src/Features/Cinematics/Models/CinematicPlayback.cs:88-89`). This
correctly handles a serialized EVE literal with known time and target.

What this path does **not** cover is a source call found inside a callback's
compiled implementation. Those calls are visible in the static
`ReachableEngineCalls` inventory, but they are not `CinematicLiteralCall`
records in an EVE track and carry no playback timestamp. Feeding all such
calls into `CinematicObjectState` at time zero would invent execution and could
hide actors/effects or deactivate lights incorrectly. The current timeline flag
analyzer only derives timed state from serialized `ARRGlobalIntFlagSet/Clear`
literal writes (`src/Features/Cinematics/CinematicTimelineFlagAnalyzer.cs:16-39,
84-93`); it does not schedule callback source statements from branch or lifecycle
evidence.

### Callback state-effect candidates in the owner inspector

The first generic callback state-effect evidence slice is implemented in
`CinematicScriptCallbackStateEffectAnalyzer.cs` and displayed under
**Callback state-effect candidates** in the owner inspector. It projects
reachable `OBJ_FlagInvisibleSet` and `OBJ_FlagInactiveSet` source calls with the
owner key, raw source arguments, enclosing branch predicates, function path,
source line, and `SourceCallCandidate` status. Iris's wrappers take the current
game-object context implicitly, so the owner is shown as the candidate target;
the panel does not resolve arbitrary OVA aliases or constant-fold the flag
argument. It does not schedule or apply visibility/activity changes.

This lets a reviewer see which loaded actors/effects/lights have reachable code
that can hide or deactivate the owner while avoiding fabricated timing. A later
promotion to a timed state event requires a traced callback-dispatch edge and a
deterministic trigger/lifecycle timestamp. The projection is generic across
models and scenes and does not depend on object names. A synthetic check verifies
that it retains source arguments, branch conditions and unknown execution
status while ignoring unrelated APIs.

**Limits:** all 661 compact layouts passing is strong archive-structure evidence
for the audited FF005833/BF00218E load scope, not evidence of runtime dispatch.
Iris provides source-level flag semantics, but the target is an earlier reverse-
engine implementation and may not exactly match retail PC 2003. The indexed
source call graph records static paths and branch conditions; without the
serialized node bodies or retail callback/runtime trace, callback call order,
branch outcomes, actual target resolution, and execution time remain unknown.

### FF001FBE / 35009F74: missing Militaire and pearl are not missing resources

I re-ran the static audits against the installed `sally_clean.bf` (no game
launch) with `--cinematic-script-graph-audit ... FF001FBE 35009F74`,
`--cinematic-state-audit ... FF001FBE`, and focused `--cinematic-frame-audit`
at `t=0` for `_PJ_Militaire.gao` and `OBJ_Perle.gao`. The generated outputs were
in `%TEMP%\JadeView-mili-script-audit` and `%TEMP%\JadeView-mili-state-audit`.
Evidence separates three different failure modes:

The exact archive ownership is: Militaire key `7100596B`, source world
`_basic_militaire`, brain `7100596D`, model `35001FF8`, actor `Events=7100B192`
and no `SceneEvents`; pearl mesh `3D003D82`, source world
`01_04_entrepot_liberation_mili`, brain `3D003D81`, model `49021E51`, and no
EVE links; pickup proxy `3D003D85`, same gameplay world, brain `3D003D87`,
model `3D001911`, and no EVE links. Their source callbacks resolve respectively
to `companion_co_init`, `cine_perletransformation_init`, and
`perle_perle_init`. Separately, the clip-linked EVE owner is
`CINE_PIEGE_PORTE_LOBO_EVENTS.gao` (`35009F6F`, `SceneEvents=35009F74`); the
startup owner is `CINE_INIT_MILI_EVENTS.gao` (`35009652`,
`SceneEvents=35009655`). Thus the actor model, gameplay pearl instances, clip
event, startup event and each object's own callback are distinct owners/links.

| Question | Archive/source evidence | Result |
| --- | --- | --- |
| Is the resource absent? | Clip participant keys include `_PJ_Militaire.gao` (`7100596B`), `OBJ_Perle.gao` (`3D003D82`) and `OBJ_PerlePrendre.gao` (`3D003D85`). Loaded worlds include `01_04_entrepot_liberation_mili`; state audit resolves those rows there, while the Militaire model is in `_basic_militaire`. Focused frame audits load/render the Militaire and pearl geometry. | No. The BF dependency/world graph and JadeView's focused renderer resolve them. This proves resource residency/renderability only, not correct in-game placement or clip-camera visibility. |
| Is a pearl-related object authored hidden? | The decoded starting `ControlFlags` are `OBJ_Perle=0x02000000`, `OBJ_PerlePrendre=0x62600000`; `_PJ_Militaire` is `0x02000080`. `SceneLoader` maps serialized state bits `0x20000000/0x40000000` to JadeView invisible/inactive masks (`src/Features/SceneEditor/SceneLoader.cs:539-542`). Playback reset copies these initial flags (`CinematicObjectState.cs:88-95`), and the renderer hides objects whose effective preview flags include `Invisible` (`SceneObject.cs:30-31`, `SceneRenderer.cs:110-111`). | The pearl mesh `OBJ_Perle` and Militaire are not authored with those hidden/inactive masks; the separate pickup proxy `OBJ_PerlePrendre` is. If “the pearl is missing” refers to the pickup proxy rather than `OBJ_Perle`, its absent draw is explained directly by source-authored state, not by a missing asset or an unrun hide command. The pearl mesh's position may still be changed by its callback. |
| Is Militaire's requested animation available from the selected clip alone? | `35009F74` has `ACTActionFrameSet(201,1)` at `0s` and `ACTActionFrameSet(201,282)` at `7.817s`; the graph marks both unresolved because slot 201 is missing from the base effective action table. Separate owner `CINE_INIT_MILI_EVENTS.gao` (`35009652`), list `35009655`, requests `ACTKitMerge(3500965A,200,230)` and then message 49 with status 3/action 201 to Militaire. The kit contains slot 201 (`35009636`). | Yes: the action is supplied by a sibling startup `Cine_Events` owner, not by the selected clip. Its event is gated and is not implied to have run just because the clip was selected. |
| Is the pearl's runtime placement a static clip transform? | `OBJ_PerlePrendre.gao` maps to `perle_perle_init`, while `OBJ_Perle.gao` maps to `cine_perletransformation_init`; neither participant maps to its own EVE/SceneEvents list in this scope. Iris's `perle_perle_init` computes special flag `des_Perle_ID + 2900`, reads the global saved-flag bitset, checks the linked Crochax death flag, chooses the nearest of up to 10 Crochax drop points, then writes the pearl position to that point plus `0.7` on Z (`external/irisEng/ai/ai_bge/ai_compiledscripts_perle.c:2581-2714`). | The placement/availability is callback-derived and depends on runtime instance variables, save data and Crochax state. JadeView does not evaluate this native callback, so a static world row cannot establish the same runtime pearl location/state as the game. |
| What does the visual pearl initializer require? | `cine_perletransformation_init` reads ANI channels 0/1/2 as pearl/halo/morph, validates that all three objects resolve, then invokes the params callback and switches to the wait callback (`ai_compiledscripts_perle.c:140-188`). | This is another callback lifecycle, not a direct command in clip `35009F74`; no conclusion that it executed follows from its name being present. |

The Militaire chain is confirmed by the clip's unresolved action links and the
separate startup EVE's serialized merge/message pair. Iris's `Cine_Events`
source explains why selection is insufficient: `Cine_Events_cine_firstinit`
enters init (`ai_compiledscripts_global_managers.c:4029-4055`),
`cine_tests_depart` evaluates configured tests/priority and may return while
waiting (`:5392-5436,5438-5456`), and only the `GoToEvents` branch sets
`i_playing` and calls `TRAPlayAll` on the owner (`:5841-5898`). The archive
records possible gates, not their runtime outcomes. The observed animation seam
is therefore missing **owner lifecycle evaluation**, not a scene-name-specific
asset mapping: clip playback currently does not by itself guarantee the
independently owned startup channel has been scheduled.

For the pearl, the bounded source conclusion is narrower: the archive contains
both pearl objects and their script callback identities; the mesh is not
source-authored with JadeView's invisible/inactive masks, while the pickup
proxy is. Iris's callback contains saved-state-dependent positioning and
conditional effects. We have not proven which branch the retail PC game takes
for this save, whether runtime conditions move the pearl outside the camera,
or that this callback is the sole reason for a reported visual absence. The
callback owner's references and OVA values must be available before any
offline placement can be computed.

**System-wide next implementation seam (not another inventory):** add an
owner-lifecycle overlay to the cinematic evaluator. Compose the selected clip
with same-world `Cine_Events` owners as separate deterministic tracks; evaluate
only serialized gates whose inputs are actually known, expose unresolved gate
inputs as unknown, then apply known `ACTKitMerge` before resolving later action
requests. In parallel, define a typed, source-backed callback-effect adapter
for initializers that write object state (first candidate: perle placement),
with explicit required-input checks for save flags, actor references and
instance variables. If any input is unavailable, keep the state unresolved
instead of guessing a position/visibility. This generic lifecycle/effect
contract addresses both independent animation setup and scripted prop state;
it avoids Militaire/pearl special cases keyed by scene or object name.

**Boundary:** these checks are static against retail BF data and JadeView's
current offline assembly; Iris provides a source-derived model of the engine,
not proof that each callback/branch executes identically in retail BGE 2003.
The selected clip's raw track, the sibling event-owner data, world instance
state and callback source are now distinguished; a retail runtime trace is
still needed to establish the active saved-flag branch and actual visibility.

### Why the sibling `CINE_INIT_MILI_EVENTS` channel is not auto-scheduled

This is a confirmed **missing runtime input / independent owner route**, not a
lost serialized edge in the cinematic evaluator. In the actual `FF001FBE /
35009F74` archive graph, the clip has no decoded `TRAPlay*` call targeting
owner `35009652` or list `35009655`. Its Mili animation requests are direct
`ACTActionFrameSet` calls. The sibling owner instead has callback slot 2 set to
`Cine_Events_cine_firstinit`, its own `SceneEvents=35009655`, and channel 2
flags `0x14` (no `RunningInit` bit `0x01`). Its source-derived `Cine_Events`
configuration enables the actor-activity test and saved special flag 2007
(mask `0x104`). Those inputs belong to the live world/save, not to clip
35009F74's serialized timeline. Static graph analysis cannot determine whether
the runtime owner was activated or those gates passed.

The current scheduling code reflects that boundary: callback-discovered starts
are auto-scheduled only when a source-reachable owner resolves `TRAPlay*` to
the **selected clip's event key** (`src/Features/Cinematics/CinematicNestedEventTransformTracks.cs:17-31,77-96`).
`FindPreviewableSceneEventOwners` separately discovers lists in the cinematic
or paired gameplay world, but keeps them as `ManualPreview` candidates unless
the owner is explicitly selected (`:273-284,106-123`). `RunningInit` channels
are scheduled from their flag only when they are not under explicit owner
preview control (`:49-74`). Thus no generic edge connects this selected clip
to its sibling EVE; inventing one or auto-playing every sibling list would
bypass actual start conditions and play unrelated conditional events.

**How to preview the Militaire setup now:** in the `Cinematic Timeline`, open
`Script-owned previews` → `Script-owned EVE preview`, find
`CINE_INIT_MILI_EVENTS.gao · 35009652`, and enable its `Preview` checkbox at
the clip start (or use an offered gate-aligned start button when one is shown).
The owner preview is deliberately an explicit override: this list merges the
missing action-kit range and sends the action-201 companion message before the
clip's later action request. The adjacent gate text explains that runtime
conditions are not evaluated. Synthetic check
`VerifyRunningInitHonorsOwnerSelection` in
`tests/ExportVerification/CinematicNestedEventTransformChecks.cs:339-376`
asserts the general contract: an independently owned list is available but
unscheduled by default, and explicit preview schedules it without claiming
the runtime gate passed.

No code change is justified for automatic scheduling until a source edge or
the relevant live owner/save/actor state is available. The safe generic next
step is to consume a captured runtime owner-activation/gate result (when
available) as a typed start event; absent that evidence, retain the current
manual preview route and `RuntimeGate=Unknown` rather than treating sibling
presence as permission to run it.

### Source detail for the `Cine_Events` actor-activity gate

The typed inspector keeps the `0x004` gate unknown, and its required-input
labels now identify the exact missing predicate inputs: target runtime
`flags2` bit 0 (activated-world state) and `des_i_activity_test1`. Iris provides
that narrower predicate: when the gate bit is enabled,
`Cine_Events_cine_tests_depart` resolves `des_o_actor_status1` (including main
actor slots 1/2), reads the low bit of that object's `flags2`, and compares it
to `des_i_activity_test1`; a mismatch leaves `i_ready=1` and yields (`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4990-5012`).
The tested value is therefore the object's runtime **activated-world** bit
`flags2 & 1`, not proof that its script callback is currently on the AI
callback scheduler.

That bit has a lifecycle separate from the BF record. `OBJ_GameObject_Create`
initializes runtime `flags2` from `0x7B9C0080`, the serialized state word,
collider state and explicit translations of the high serialized bits; this
initialization does not itself set bit 0 (`external/irisEng/gameobject.c:2843-2872`).
`WOR_World_AddLoadedObject` adds an object to the world table and invokes
`WOR_ActivateObjectIfFlagsAllow` unless identity `flags & 1` blocks the call.
That activation routine inserts eligible objects into
`activatedObjectsTable4PF` and sets `flags2 & 1`; eligibility depends on
`ENG_gf_SpecialTime` and runtime flag masks. `WOR_World_ActivateObjects` also
clears/rebuilds the activated table and bit, while `WORUnPause` can set the bit
again (`external/irisEng/world.c:44-62,174-200`; `external/irisEng/ai/ai_worldfunctions.c:186-209`).
This confirms why JadeView's loaded-object list, serialized state word, and
cinematic actor playback are not interchangeable with the runtime activity
predicate.

For the concrete `FF001FBE / 35009F74` candidate, the retained archive index
records owner `35009652`'s `des_o_actor_status1` reference as key `71003F5A`,
with two `_PJ_Sally.gao` candidates across loaded worlds (`artifacts/goal-audit/archive-gameplay-script-index.json:391109-391125`).
The owner's derived gate mask is `0x104`; its only decoded saved-flag
condition is “2007 set” (`:1322540-1322555`). The status reference is thus to
Sally, not Militaire. The archive does not record Sally's live `flags2 & 1`,
the effective `des_i_activity_test1` comparison at callback time, the active
world-table membership, or prior `WORPause`/`WORUnPause` changes. This is
consistent with the typed evaluator leaving both status and the saved flag
unknown; it does not establish why this sibling owner did or did not run in a
particular retail playthrough.

**Implemented bounded diagnostic:** `CinematicStartGateEvaluator` now reports
the exact `flags2 & 1 == des_i_activity_test1` predicate and keeps the gate
status `Unknown` because JadeView does not observe the live bit
(`src/Features/Cinematics/CinematicStartGateEvaluator.cs:72-103`). When the
referenced actor resolves uniquely in the current world, it separately
reconstructs Iris's *initial* `flags2` projection from the retained raw state
word, applies the outer `WOR_World_ActivateObjects` identity-bit-0 exclusion,
evaluates both `ENG_gf_SpecialTime == 0` and `!= 0` branches of the inner
`flags2` activation predicate, and then attaches EOT membership from GAO
identity flags (`src/Features/GameScript/GameScriptInitialActivationForecaster.cs`).
The identity-bit skip belongs to the world-rebuild wrapper, not to
`WOR_ActivateObjectIfFlagsAllow` itself. The state projection mirrors the
assignments in `OBJ_GameObject_Create`; its following
`OBJ_UpdateStatusFlagRTL` call only changes flag bit `0x8`, not the activity bit
`0x1` (`external/irisEng/gameobject.c:47-64,2843-2867`). The result is typed as a
conditional forecast in
`src/Features/GameScript/Models/GameScriptInitialActivationForecast.cs`; its separate status is
never used to mark the callback gate satisfied or schedule an owner. The
timeline inspector renders the forecast below (not as) the gate result
(`src/Features/SceneEditor/Cinematics/EditorShell.Cinematics.Timeline.cs:155-164`).

The generic check `VerifyActorActivityGateRemainsUnknown` verifies that a
normal activity gate remains unknown and that raw state is not mistaken for
runtime `flags2`: serialized `0x04000000` is removed by the loader projection,
so it forecasts normal-time eligible and special-time blocked; serialized
`0x40000000` is retained and also sets runtime bit `0x00400000`, producing
`0x40400000`, which blocks the
normal-time path (`tests/ExportVerification/CinematicStartGateEvaluatorChecks.cs:57-92`).
This diagnostic is intentionally bounded: it assumes a freshly loaded object
and no later runtime flag mutations. It models the identity-flag skip and
reports the currently reconstructed initial bit 0; it does not know the
actual world mode, object load/activation history, or later pause/unpause
effects. The UI text keeps those limits explicit.

This distinction is material: `OBJ_GameObject_Create` masks the serialized
state word with `0x7B9C0080`, which does **not** preserve raw bit
`0x04000000`; raw `0x20000000` and `0x40000000` additionally set runtime bits
`0x00200000` and `0x00400000` respectively, while their original bits survive
the mask. The loader then ORs the raw `0x0000FF00`
range (`external/irisEng/gameobject.c:2852-2866`). The inspector therefore
prints both `serialized state` and reconstructed `runtime flags2` instead of
collapsing them. In the reconstructed initial snapshot, the special-time
activation bit `0x04000000` is absent; any later setting of it remains runtime
state outside this forecast.

### Source-backed custom-bit gate preview

The `0x010` `Cine_Events` test no longer reports every custom-bit predicate as
unavailable. Iris reads `des_o_actor_cbit1` (resolving actor slots 1/2), gets
`flags2 & 0xFF00`, then compares `(bits & des_i_cbit_test1)` with
`des_i_cbit_etat1 * des_i_cbit_test1`; mismatch yields (`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4965-4987`). The engine getter and writers confirm the field and operation semantics: `OBJ_CustomBitsGet_C` masks `flags2` to bits 8–15 (`external/irisEng/ai/ai_gameobjectfunctions.c:1456-1471`); `OBJ_CustomBitsSet_C` adds the first argument and clears the second within that mask (`:1429-1453`); `OBJ_CustomBitsNot_C` toggles masked bits (`:1495-1513`). On initial GAO creation, Iris ORs the serialized low `0xFF00` bits into runtime `flags2` (`external/irisEng/gameobject.c:2852-2866`).

`CinematicStartGateEvaluator.EvaluateCustomBitGate` resolves the configured
target uniquely in the current preview world, requires the selected clip's
call timeline to be available, starts from serialized
`flags2` bits 8–15, then replays source-known `OBJ_CustomBitsSet` and
`OBJ_CustomBitsNot` calls addressed to that target through the playhead
(`src/Features/Cinematics/CinematicStartGateEvaluator.CustomBits.cs:5-104`). It uses
the existing literal-call timeline and selector catalog
(`src/Features/Cinematics/Models/CinematicLiteralCall.cs:7-22`;
`src/Features/Cinematics/CinematicEngineFunctionCatalog.cs:8-14`). Multiple
custom-bit mutations for the same target and timestamp produce `Unknown`,
because the current call model does not preserve a proven engine execution
order for those cross-track writes. Missing call history also remains
`Unknown`. Otherwise the result is a preview-state
comparison and lists external gameplay/unlisted-script mutations as a limit;
it does not claim the retail callback would pass. Generic coverage checks
source bits, set/clear operations, seeking before a write, ambiguous same-time
writes, and missing timeline history (`tests/ExportVerification/CinematicStartGateEvaluatorChecks.cs:95-131`).

The custom-bit snapshot is intentionally local to this gate evaluator. It does
not yet feed render/material state or update `SceneObject`'s persistent
cinematic preview fields. No equivalent live custom-bit capture exists for
retail PC in the available artifacts, so real callback execution remains
unknown even when the serialized preview predicate is Satisfied or Rejected.

This is reusable for any `Cine_Events` owner and provides a source
explanation without scene-name matching. It is not an automatic start gate.
Only a future observed or explicitly supplied live activity-state value,
together with saved-flag and other required gate inputs, could authorize a
deterministic preview start. Keep the current manual preview choice separate.

**Evidence limit:** these lifecycle semantics are confirmed in the local Iris
source model. The checked Jaded/iristool/archive artifacts do not provide a
retail PC 2003 capture of the target's `flags2`, active-world table, or callback
result; the forecast therefore cannot claim retail runtime parity.

### Absolute position setters in serialized EVE

Iris and Jaded define two distinct literal vector calls that can be evaluated
as time-stamped world-space position changes. Jaded registers `OBJ_PosSet` as
function 8102 and `OBJ_PosSet_Teleport` as 8187, both with a vector argument
(`external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h:265-266`).
The Iris counterparts decode three floats, call `OBJ_SetAbsolutePosition`,
preserve orientation/scale, and recompute local position when the target is
hierarchy-bound (`external/irisEng/ai/ai_gameobjectfunctions.c:933-963`). Jaded
has the same absolute-position operation; the ordinary setter also refreshes
flash animation matrices and marks DYN collision optimization state, while
the teleport form has an additional teleport hook
(`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_obj.c:2210-2310`).
These setters change position once; they do not encode interpolated travel or
velocity. The selector alias `0x1FFB` was incorrectly cataloged as the regular
setter and is now named `OBJSetPos_Teleport`.

JadeView now decodes serialized clip calls `0x1FA6` and `0x1FFB` into
position-only transform tracks (`src/Features/Cinematics/CinematicScriptTransformTracks.cs`),
which use the same sampled object/actor transform and GLB animation pipeline
as the existing scale/zoom calls. Its reader accepts optional additional
`(time, call)` inputs so callers can supply already-scheduled nested event
calls without conflating them with direct clip events; source clip calls are
ordered first and equal-time updates remain stable, with the last update for
one target/component winning. `tests/ExportVerification/CinematicScaleChecks.cs`
covers independent position/scale channels, stable equal-time nested updates,
non-finite time rejection, backward seek, and exported translation keys. This
models the visible pose result only: dynamic collision revalidation, teleport
side effects, flash matrix updates, and runtime `DYN` integration are not
simulated. Cinematic playback, GLB object sampling and body-root initialization
now explicitly supply their already-scheduled nested event calls. This does
not schedule new owners or execute source-AI functions; those remain outside
this literal-call path.

This is not sufficient to reconstruct Sally's mode-6 vertical movement in
`FF005520 / 3500A927`. Iris's Sally gameplay action code sets dynamic speed,
then attaches the actor to a ray-hit wall with `OBJ_HierarchySet` and later
changes/clears that attachment (`external/irisEng/ai/ai_bge/ai_compiledscripts_sally.c:13983-14036,14087-14091`).
`OBJ_HierarchySet` computes a child-local transform and adds the object to the
world hierarchy update table; `OBJ_HierarchyMainCall` continuously resolves
the parent chain (`external/irisEng/ai/ai_gameobjectfunctions.c:2832-2920`;
`external/irisEng/gameobject.c:29-44,4545-4587`). The camera/root-motion audit
also found that this clip's mode-6 root translation is suppressed while the
source ANI routes movement to AI/DYN (`artifacts/movement-3500A927/FF005520-3500A927-71003F5A-root-trajectory.json`).
The remaining generic system seam is a preview-time scripted-parent state
plus its live parent-relative transform evaluation, kept separate from the
currently decoded static hierarchy-local matrix. It should only activate for
serialized `OBJ_HierarchySet/Reset` calls with uniquely resolved endpoints;
AI ray-hit results and dynamic speed integration remain unknown without
runtime/collision inputs.

### Literal hierarchy commands: binding transitions versus evaluated pose

The hierarchy selectors and operand roles are source-confirmed. Iris registers
`0x1FDC` as `AI_EvalFunc_OBJ_HierarchySet` and `0x1FDD` as
`AI_EvalFunc_OBJ_HierarchyReset` (`external/irisEng/ai/ai_functions.c:259-260`).
Jaded exposes the matching functions and signatures: Set receives the current
game object plus one parent game object, while Reset receives only the current
game object (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h:321-322`).
The VM wrappers use the current execution object as the child and pop the
parent for Set (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_obj.c:5720-5729`);
Iris does the same (`external/irisEng/ai/ai_gameobjectfunctions.c:2916-2931`).
Thus a decoded literal call means child = `call.Target`, parent = the sole
GameObject argument at index 0; Reset's target is the child and takes no
arguments. The target/object-reference extractor already classifies argument
0 for `OBJ_HierarchySet` (`src/Features/Cinematics/CinematicEngineFunctionCatalog.cs:43-48`).

Set is not a local-offset assignment. Both script wrappers reset the current
binding before calling the C helper; the helper rejects a null parent or
self-parent, and for a valid parent sets the hierarchy identity flag and
father pointer, then computes a new local matrix from the child's current
absolute matrix and the inverse of the parent's current absolute matrix
(`external/irisEng/ai/ai_gameobjectfunctions.c:2832-2931`;
`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_obj.c:5591-5729`).
Therefore a null/self Set has the wrapper's effective result of detached, even
though the C helper itself rejects that parent.
With the engine's matrix convention, local = childWorld * inverse(parentWorld).
This preserves the child's visible world pose at the instant of attachment.
Reset clears the active hierarchy identity flag and removes the child from the
world's hierarchy update table. It does not clear the stored father pointer in
these functions; the active-binding flag/table, rather than a null pointer, is
the effective state. The engine also maintains collision old-matrices and
flash/animation hierarchy side effects; those are not transform-only state
(`irisEng:2832-2910`, `jaded:5591-5700`).

After attachment, the world hierarchy pass resolves parent chains and computes
child global = child local * parent global on subsequent frames
(`external/irisEng/gameobject.c:29-44,4534-4587`;
`external/jaded/src/libraries/ENGine/Sources/OBJects/OBJorient.c:550-612`;
`external/jaded/src/libraries/ENGine/Sources/OBJects/OBJmain.c:1073-1113`).
The serialized scene's `ParentId` and `SerializedHierarchyLocalMatrix` are
currently loaded separately (`src/Features/SceneEditor/SceneLoader.cs:543-544`)
and used by `CinematicHierarchyPose`; they are not a timeline of script
reparent operations. The literal script transform decoder now emits bounded
hierarchy-binding transitions. Static hierarchy resolution still cannot stand
in for AI-selected runtime parents or update-order side effects.

The state model keeps command decoding, parent binding, and pose evaluation
separate:

```text
CinematicScriptHierarchyCommand(time, childKey, SetParent | Reset, parentKey?)
CinematicScriptHierarchyState(parentKey?, localMatrix?, frozenWorldMatrix?, status)
Decode(source literal calls + explicitly scheduled nested calls)
Reduce(initial binding snapshot, commands, playhead) -> per-child binding state
EvaluatePose(state, base pose, unique parent pose at command time) -> world pose
```

The decoder accepts only Set with exactly one GameObject reference and Reset
with zero arguments; malformed arity remains undecoded. A null or self parent
maps to detached because the script wrapper runs Reset before calling the C
helper. Missing/duplicate endpoints, cycles,
non-finite matrices, and non-invertible parents must remain typed unknown or
invalid states rather than silently clearing the parent. The relation reducer
in `CinematicScriptHierarchyBinding.cs` remains a separate diagnostic model
for endpoint resolution. The preview pose evaluator is implemented in
`CinematicScriptHierarchyPoseTimeline.cs` and is fed samples from
`CinematicPlayback.ScriptHierarchy.Pose.cs`. It samples affected actor and
object tracks at each literal command time, replays commands in source order,
and applies render-only world-matrix overrides. On Set it derives
`childWorld * inverse(parentWorld)` so attachment is continuous; later samples
resolve the child through the parent's current pose. On Reset it derives a
detached offset from the sampled base pose, preserving continuity while an
object's own animation continues. Serialized hierarchy-local matrices remain
unchanged. Singular/non-finite matrices and unresolved endpoints do not produce
a bound pose; the child remains detached at its sampled pose and receives an
explicit status.

This preview is limited to literal calls with unique GAO endpoints. Its
event-time snapshots include decoded actor/object transform samplers, but do
not execute AI callbacks, resolve collision raycasts, integrate DYN speed,
recreate collision-history side effects, or emulate all runtime update phases.
Serialized parent locals are included only when the matrix is available and the
child is not an actor or an independently animated object track; unknown
animated-local semantics remain outside the evaluator. The render-only override
is not yet a time-varying glTF hierarchy export, and this implementation does
not claim native retail-PC parity.

This model is sound only for literal calls with fixed, uniquely resolved
endpoints. Sally's compiled action obtains a father from a collision ray query
and changes dynamic speed before setting/clearing the hierarchy
(`external/irisEng/ai/ai_bge/ai_compiledscripts_sally.c:13983-14036,14087-14091`);
those runtime-selected endpoints and motion inputs are not available to the
static call decoder. Jaded/Iris source proves engine behavior, not exact retail
PC2003 runtime ordering or that every retail event uses the same callback and
object-lifetime path. Therefore the first implementation should expose a
source-linked binding preview with explicit provenance and unknowns; it must
not claim that it reproduces AI raycasts, DYN, collision, flash matrices, or
all retail runtime behavior.

#### Current JadeView integration boundary

The event-time preview path now samples unique literal child/parent endpoints,
then replays prior script bindings and relevant serialized parent relations at
each command before applying the current playhead pose. It keeps source-local
matrices intact and clears render overrides when playback deactivates. The
Execution tab still exposes endpoint resolution separately and now reports the
pose evaluator's status, so a valid relation is not confused with an evaluated
pose. Automatic AI-selected parents (including Sally's wall raycast) remain
outside this preview. GLB export also remains static for arbitrary object
reparenting: `SceneExport.ActorHierarchy` builds glTF node parenting from actor
skeleton membership while object nodes use the static object path
(`src/Features/Export/SceneExport.ActorHierarchy.cs:7-28`,
`src/Features/Export/SceneExport.Nodes.cs:8-52`).

Representative archive check: `sally_clean.bf`, container `FF005833`,
cinematic `BF00218E` decoded zero literal `OBJ_HierarchySet/Reset` calls. The
new path therefore does not explain missing fan/drone state in that scene; its
asset visibility/activation must be traced through EVE/AI owner gates and
world activation separately. The frame audit completed at 2.1 s and produced
23 visible objects, but this is a preview audit rather than native-game parity.

#### ANI_ModeSet and root-motion mode requests

The registered selector is `0x32D3` (`src/Features/Cinematics/Data/engine-function-selectors.json:697`).
Jaded declares `ANI_ModeSet(GameObject*, track, mode)` as function 13011
(`external/jaded/src/libraries/AIinterp/Sources/Functions/AIdeffct.h:910`).
Its evaluator pops `mode` and then `track`, and its C helper clears the
play-mode mask and writes `mode << 1`
(`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_ani.c:683-705`).
`ANIstruct.h:106-113` defines the mask `0x07`, mode 0 as speed from animation,
mode 1 as update frequency, and mode 3 as speed from dynamics; value 2 is not
named in the available source. The same header sets the simultaneous animation
track capacity to four. The runtime also checks that the requested track exists
on the object; four is only a capacity bound, not proof that a particular
object has track 0–3 loaded.

The Iris implementation agrees on the selector and stack argument order, but
its setter clears the low nibble (`flags &= 0xFFF0`) before writing the mode
(`external/irisEng/ai/ai_animationfunctions.c:226-247`). Jaded clears only
`ANI_C_AnimFlag_PlayModeMask` (`0x07`). Since bit `0x08` is the `Developped`
flag in Jaded, the two source implementations differ in whether setting a mode
clears that flag. Retail PC behavior for this bit is not verified.

Jaded action selection reloads the mode bits from each selected action item's
`uc_Flag` (`external/jaded/src/libraries/ENGine/Sources/ACTions/ACTcompute.c:943-959`);
the action player also resets flags when advancing action items
(`external/jaded/src/libraries/ENGine/Sources/ANImation/ANIplay.c:360-370`).
ModeSet is therefore a transient per-animation-track mutation, superseded by
the next action-item transition. In mode 3, the animation player restores the
pre-animation MagicBox position and adjusts flash state
(`ANIplay.c:1335-1357`); dynamics/AI is responsible for the object's movement.
The source supports suppressing animation-derived translation in that mode,
but it does not provide the DYN trajectory needed to reconstruct the resulting
world position.

JadeView reads the action-item flag byte at `CinematicActionLibraryReader.cs:96`
and classifies `(Flags & 0x07) == 0x06` as dynamics translation in
`CinematicAction.cs:74-75`; `CinematicAction.Motion.cs:67-80` removes root
translation for that case. `CinematicPlayback.cs:92` retains source and nested
literal calls, while body timelines currently receive nested commands at
`CinematicPlayback.cs:233`. The new `CinematicAnimationModeTimeline` and
collapsed Execution-tab panel record decoded `0x32D3` calls, reconstructed body
action starts, and gameplay handoffs. They expose mode 2 as encoded but unnamed,
validate argument arity, report whether a track index is inside the four-track
capacity, and flag calls after a gameplay handoff. They do not claim the call
executed or changed the effective mode. Action-item boundaries within a body
segment are not enumerated by this diagnostic, and same-time ordering is
marked unverified.

No preview or GLB motion was changed. Connecting these requests to root curves
would erase animation motion when mode 3 is requested but cannot add the
missing AI/DYN displacement; applying such a partial result would make
character paths less reliable. The separate panel is diagnostic evidence only
until DYN/AI movement and the retail PC mode-setter bit behavior are known.
