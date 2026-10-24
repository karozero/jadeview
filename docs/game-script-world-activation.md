# Iris world pause and resume commands

JadeView now has a pure reducer for literal `WORPause` and `WORUnPause` commands. It accepts a resolved universe-world slot and an explicit runtime snapshot, then returns proposed `flags2` changes plus the table, visibility, and SnP refresh requests made by Iris. It does not mutate scene objects or claim that a cinematic callback executed.

## Source behavior

The command handlers are in `external/irisEng/ai/ai_worldfunctions.c`:

- `AI_EvalFunc_WORPause_C` reads the selected world's activated-object table. It matches objects whose GAO identity flags overlap the command mask, then sets runtime `flags2` bit `0x04000000` unless exclusion bit `0x08000000` is already set.
- `AI_EvalFunc_WORUnPause_C` scans the world's full object table. For valid object pointers it clears `0x04000000` only when GAO identity flags match; independently of that mask match, it sets active bit `0x1` when activation-block bit `0x00400000` is clear.
- Both handlers request table/EOT rebuild, visibility refresh, and SnP refresh after scanning.

A separate `GameScriptWorldActivationRebuildReducer` models the next world-engine pass: `engine.c` calls `WOR_World_ActivateObjects`, which clears the existing activated table, clears runtime `flags2` bit `0x1` on valid object-table entries, skips objects whose GAO identity flags contain bit `0x1`, and calls `WOR_ActivateObjectIfFlagsAllow`. Iris then rebuilds EOT from the newly activated table. Normal time rejects `flags2 & 0x04400000` and `flags2 & 0x10`; special time instead requires `flags2 & 0x04000000`. If `ENG_gf_SpecialTime` is unknown, the reducer reports an object as resolved only when both modes produce the same membership.

The command's first integer is a universe-world slot, not a GAO key. Its second integer is a mask against identity flags (`SceneObject.Flags`). The reducer takes current runtime `flags2` and activated-table membership as explicit inputs because those are not equivalent to serialized GAO state. A missing world snapshot, malformed command, or slot mismatch returns `Unknown` with no object changes.

## Scope and limits

The command reducer reports immediate per-object flag intents and refresh requests; the separate rebuild reducer calculates new flags2/activity membership when time mode is supplied (or leaves mode-dependent results unknown). Neither reducer updates cinematic draw visibility or schedules AI callbacks. JadeView's archive-derived preview does not contain a live Iris universe slot table, runtime `flags2`, activated-table membership, or special-time state, so the current cinematic path cannot safely turn an archive command into concrete object activity. Jaded's later engine source uses a different sector/control-flag activation model; do not apply these Iris reducers to Jaded semantics or treat either source tree as proof of retail PC parity.

Synthetic regression checks cover activated-table filtering for pause, full object-table traversal for unpause, both exclusion/block bits, refresh requests, malformed inputs, unknown world resolution, normal/special-time rebuilds, identity-bit exclusion, invalid table pointers, and mode-dependent unknown results. They prove the reducers' source translation, not runtime command dispatch.
