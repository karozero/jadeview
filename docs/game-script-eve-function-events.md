# EVE AI-function keys and conditional waits

Iris EVE tracks can contain AI-function keys (`flags2 & 0x7C0 == 0x40`). At the event boundary, `EVE_RunOneTrack` invokes the serialized AI node unless the engine is in `EVE_gb_PlayOnlyAnimTracks` mode. The event context is the track's GAO when present, otherwise the EVE owner; a valid bone target changes the context to that skeleton entry. See [`events.c`](../external/irisEng/events.c#L168-L224) and [`events.c`](../external/irisEng/events.c#L1730-L1795).

Two serialized key bits inspect the node's integer result:

- `0x0008`: while the node returns true, Iris resets the event cursor time to zero and returns without advancing the key. The same key is evaluated again on a later EVE update. Once it returns false, the track can advance.
- `0x0010`: the same hold-and-retry behavior applies while the result is false; the track advances once it returns true.

The `0x0008` branch has precedence because Iris implements the second condition as `else if`. These bits describe a polling/wait key, not a request to restart the entire EVE track. A wait holds the channel cursor at that key, so later events can drift from their serialized timestamps; JadeView warns about that and continues to preview the source timestamps. The key's AI node may query or mutate arbitrary game state, so archive data does not provide its result. JadeView does not execute the node or infer its result.

`EVE_gb_PlayOnlyAnimTracks` is a separate Iris guard. In the checked-in source, `WOR_World_JustAfterLoadObject` sets it around `ACT_SetAction` or `ANI_PlayGameObjectAnimation` while preparing an animation-only initial pose, then clears it ([`world.c`](../external/irisEng/world.c#L1026-L1055)). `EVE_RunOneTrack` checks the guard before invoking an AI-function key ([`events.c`](../external/irisEng/events.c#L1769-L1795)). The supplied source has no other assignment sites. This supports treating it as a scoped world-load guard, not as an authored cinematic playback mode; JadeView still does not observe a running game or execute the AI nodes.

Jaded's `EVE_Event_AIFunction_Play` reaches the same hold-and-retry behavior in `external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c:912-934`: it resets the event cursor time and returns without advancing while the selected wait condition remains true. Jaded's enum labels name the release condition, while the Iris flag bits are more safely described by the result value that holds the cursor. This corroborates the scheduler shape, but its later engine is not proof of the 2003 retail PC runtime. Regression coverage is in `CinematicEventFunctionControlFlowChecks`.

## Archive check

The archive census on `sally_clean.bf` scanned 91 FF containers and found six serialized EVE AI-function wait keys in three containers: five hold while false and one holds while true. In `FF005833 / D90057CC` (`00_02_home_decryptage`), `ES_MDisk_0_2.gao` (`C1009BF9`) has a `SceneEvents` link on owner `D9005D6E`; channel 3 has a time-zero key with flags `0x0049`, classified as **hold while true**. Its single decoded call is `ARRGlobalIntFlagTest(2165)`, so the wait can be described more specifically: it polls saved flag 2165, holding while set and advancing once cleared. The owner's configured callback is `ES_au_sol_esas_enter_the_map`. This demonstrates a real serialized global-state wait on an owner linked into the gameplay world. The save's live flag value and whether the channel started remain unknown.

The same census includes a cinematic example: container `FF001FBE`, world
`3500B8D5`, clip `3500A4D0` (`01_04_entrepot_liberation_mili_cine`) has
time-zero AI-function keys on channels 1 and 2 with flags `0x0051` (AI-function
event plus **hold while false**). The corresponding node result and live
runtime predicate are not established from the archive, so following events
on those channels remain conditional in real playback. JadeView currently
keeps their authored timestamps and warns about the possible drift; it does
not synthesize a wait duration or shift the other tracks.

### Wait keys can repeat side effects

The wait behavior interacts with dynamic AI callbacks: while a wait key holds
its cursor, Iris invokes that same AI node again on later EVE updates. If the
node performs `AICBAdd` before returning the value that keeps the wait active,
it can append the same registration more than once. Iris and Jaded store these
registrations as ordered entries rather than a set, and `AICBDel` removes one
exact matching entry at a time. This is a source-level consequence of the two
mechanisms; the archive alone does not prove that a particular game node
repeats an add this way. A faithful preview would need the saved/global state
that releases the wait and must preserve callback-list multiplicity.
