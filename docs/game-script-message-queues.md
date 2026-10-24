# Game-object message queues

JadeView now has a small engine-specific queue reducer for source-confirmed message operations. It is a reusable state model for future cinematic/script playback; it does **not** claim that a static EVE event was dispatched or that an AI callback ran.

## Iris PC model

Iris stores up to five messages in each allocated game-object queue. `MSGSend`, `MSGSendEx`, `MSGSendAll`, and `MSGSendAllOwner` append only when the destination has an extended object and allocated message storage; when the queue is full they silently skip the append. `MSGGet` pops the newest entry. `MSGGetByID` searches from the oldest array index, removes the first match, and swaps the newest entry into the removed slot. `MSGRead` and `MSGReadByID` inspect without removing; `MSGTestInt1` checks whether `(mask & Int1) != 0`; `MSGGetCount` reports occupancy; `MSGClear` empties the queue.

## Jaded comparison

Jaded uses a linked per-object queue capped at 1000 messages (`AI_C_MaxMessages`). Sends append at the end and `MSGGet` also removes the newest entry. `MSGGetByID` removes the first matching entry while preserving the order of other messages, unlike Iris's swap-with-newest behavior. This is a source divergence: JadeView must select semantics by engine model and must not treat the later Jaded queue as proof of Iris/retail capacity.

## Scope

The reducer models queue data structure operations once they are supplied explicitly. It does not determine callback dispatch, queue-owner registration order, messages already present before cinematic start, receiver scheduling, or runtime changes made by unlisted scripts. Those must remain unknown until a deterministic event path or runtime capture supplies them.

The cinematic NLE message lane now follows a scheduled Iris EVE message's queue-owner candidate to a unique script owner in the same world, then lists source-reachable callbacks that read that message type. It labels these as possible readers; the queue may be absent/full, a callback may not be dispatched, and a message may be consumed before another reader sees it. This is a source graph edge, not simulated delivery.

Sources: [`external/irisEng/ai/ai_messagefunctions.c`](../external/irisEng/ai/ai_messagefunctions.c) (per-object capacity, send/get/read/test/clear semantics); [`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c`](../external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_msg.c) and [`external/jaded/src/libraries/AIinterp/Sources/AImsg.h`](../external/jaded/src/libraries/AIinterp/Sources/AImsg.h) (linked queue semantics and capacity). Regression coverage is in [`GameScriptMessageQueueChecks.cs`](../tests/ExportVerification/GameScriptMessageQueueChecks.cs).
