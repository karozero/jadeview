# Global AI startup chain: independent source check

This note independently checks the local PC archive's global-AI references and
the source-level startup/handoff path. It describes static resource data and
the Iris/Jaded engine models; it is not a trace of a retail BGE process.

## Retail BF bytes checked

The local `E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf`
header selects start key `71003FF9`. Reading that key through JadeView's
`Archive` API gives `univers.oin`; its first two little-endian DWORDs are
`71003FF3` (`univers.omd`) and `71003FF8` (instance `univers.ova`). The model
key's serialized link list begins with `71003FF2` (`univers.ova`), followed by
the seven OFC keys `71003FF1`, `71004002`, `710046D6`, `7100C854`, `7A000AF7`,
`7100D95B`, and `26003350`. Their names in the archive are respectively
`univ_init`, `univ_go`, `univ_exec_get_world_key`, `univ_exec_new_game_init`,
`univ_exec_load_new_world`, `univ_exec_special_raz`, and
`univ_volume_manager`.

The raw `univ_init.ofc` starts with stack-offset count 8 and declared node-byte
size 11112, i.e. 1389 8-byte records. Parsing those bytes at offset 8 using the
Iris `tAINode` field order gives node 1386 `{ val=71004002, funcID=0x0027,
category=1 }`, node 1387 `{ funcID=0x1789, category=3 }`, and node 1388 category
31 (end). `univ_go.ofc` declares 82 records; nodes 62 and 63 carry the
`7A000AF7` function reference and `AIExecute` (`funcID=0x1784`), while node 80
is `WORActive` (`funcID=0x1F41`). `univ_exec_load_new_world.ofc` has five nodes
and node 2 calls `WORLoadList` (`funcID=0x1F46`). These are serialized static
instructions and link targets, not evidence that the runtime took those paths.

The record layout is source-backed by Iris `tAINode` (`external/irisEng/ai.h:7-14`),
`LOA_ReadAINode` (`external/irisEng/loading.c:227-239`), and IrisTool's OFC
parser (`external/iristool/src/Common/DataType/OFCProgramCodeElem.java:102-114`,
`.../BFScriptingFunctionCodeChunk.java:64-101`). The byte parse was performed
against exported raw BF entries; the archive resource has its full node body.
Do not treat this as the compact 12-byte OFC metadata candidate used by some
other PC resources.

## Runtime handoff and first world-selection path

Iris's first-frame reset path calls `AI_Reset()` and then
`WOR_Universe_Open(BIG_gst.hdr.startKey)` (`external/irisEng/engine.c:617-633`).
`WOR_Universe_Open` loads an AI instance from that key
(`external/irisEng/world.c:2511-2518`); `AI_pst_LoadInstance` resolves its
instance/model references and calls `AI_ReinitInstance` before returning it
(`external/irisEng/ai_common.c:1441-1456`). The loaded global instance is then
stored in `WOR_gst_Universe.univAIInstance`. Iris's frame loop runs this global
instance before iterating active world slots (`engine.c:528-534,540-578`).

`AI_ReinitInstance` establishes the callback range from the non-null model
callback slots, copies them into the live per-instance callback array, assigns
each jump node to that function's root, and zeroes its delay (`ai_common.c:375-420`).
The existing archive audit records only slot 2 populated, with
`univ_init.ofc`; the raw body's final reference then targets `univ_go.ofc`.
Iris's `AI_EvalFunc_AITrackCurChangeNow_C` switches the current callback and
jump node and returns result 5 (`ai/ai_aifunctions.c:252-265`). The scheduler
resets the jump node to the selected function root and decrements its track
index on result 5 (`ai_common.c:742-760`), so the newly selected function is
eligible immediately in the same `AI_RunInstance` pass. Therefore the source
model is **one initialization callback followed by a same-track handoff to
`univ_go`**, not a separate animation/event track. This immediate handoff is
confirmed in Jaded too: `AI_EvalFunc_AITrackCurChangeNow_C` returns
`AI_CR_CurChangeNow`/5 (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_ai.c:290-329`),
and `AI_RunInstance` decrements `i` to repeat the track (`AIengine.c:999-1053`).

The handoff is not an unconditional proof that `univ_go` loads a particular
scene. The global script has branch gates and obtains the world key through
`univ_exec_get_world_key`; it conditionally calls the new-game initializer and
the load helper, then reaches a `WORActive` call. `WORLoadList` returns a
runtime universe-slot index, which the script stores for the later activation.
The named selector implementations are registered in
`external/irisEng/ai/ai_functions.c:169-175`; Iris's wrappers implement
`WORActive` and `WORLoadList` in `external/irisEng/ai/ai_worldfunctions.c:43-55,100-119`.
The key lookup and branch inputs come from global OVA variables/arrays and
runtime state, so static script reachability does not identify the chosen map
or prove that either branch ran. The branch mechanism is the serialized
`JumpFalseSP` keyword: Iris consumes the condition and either advances one node
or jumps by the node's relative offset (`external/irisEng/ai/ai_keywords.c:239-250`).
The fixed OFC helper-function keys are static; the world key supplied to the
load helper is selected from runtime/global variables and is not a fixed
literal in this handoff.

## Iris/Jaded boundary and lifecycle nuance

Jaded's reset path likewise calls `AI_Reset` then `WOR_Universe_Open` using the
BF universe key (`external/jaded/src/libraries/ENGine/Sources/ENGloop.cpp:241-270`).
Its world engine invokes the universe AI before iterating loaded worlds
(`external/jaded/src/libraries/ENGine/Sources/ENGcall.c:560-569`). Its
`WOR_Universe_Open`/`AI_pst_LoadInstance` chain resolves and reinitializes the
instance (`WORinit.c:141-151`; `AIload.c:2082-2110`), consistent with Iris at
this boundary.

The reinitialization special case matters: both engines avoid resetting the
universe instance's callback-track array when reinitializing an already
installed global instance (Iris `ai_common.c:385-420`; Jaded `AIengine.c:1459-1497`).
Thus `AI_ReinitUniverse` is not equivalent to reopening the global AI from its
serialized `univ_init` callback. A fresh `WOR_Universe_Open` creates a new
instance and initializes its callback state; an in-place reinit preserves the
current callback/jump state while applying the universe-specific variable
reinit policy. This means “univ_init runs once” is valid only per freshly
loaded global-AI instance, not as a universal guarantee across resets/reloads.

Jaded's `WOR_LoadList` also has additional fix-world preloading and a
custom-level key-rewrite branch before it calls the shared list loader
(`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_wor.c:560-634`).
That is a real source divergence and should not be projected onto Iris or the
retail PC game's exact transition history.

## What remains unknown for retail playback

- Which branches `univ_go` takes depends on live/saved OVA variables, current
  world history, and engine timing; the BF does not encode their current runtime
  values.
- The exact world key, loaded-world table order, slot index, active flags, and
  current-world pointer cannot be reconstructed from this static chain alone.
- `ENG_gf_SpecialTime` is not implied by the presence of `univ_exec_special_raz`
  or `WORPlayAbsTime` in a function catalog. A dynamic callsite and its runtime
  branch/arguments must be established first.
- Iris and Jaded source are engine models, not verified retail-PC call traces.
  The archive provides the selected resource links and bytecode, but no record
  of the chosen callback dispatch or universe state at cinematic entry.

Safe JadeView boundary: expose this as a global-AI resource/callback graph with
static branch alternatives and explicit unknown runtime inputs. Do not infer a
loaded scene or mark `WORActive` satisfied until an execution trace supplies
the world key, returned slot, and active-state transition.
