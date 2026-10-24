# Iris world pause and resume commands

JadeView now has a pure reducer for literal `WORPause` and `WORUnPause` commands. It accepts a resolved universe-world slot and an explicit runtime snapshot, then returns proposed `flags2` changes plus the table, visibility, and SnP refresh requests made by Iris. It does not mutate scene objects or claim that a cinematic callback executed.

## Source behavior

The command handlers are in `external/irisEng/ai/ai_worldfunctions.c`:

- `AI_EvalFunc_WORPause_C` reads the selected world's activated-object table. It matches objects whose GAO identity flags overlap the command mask, then sets runtime `flags2` bit `0x04000000` unless exclusion bit `0x08000000` is already set.
- `AI_EvalFunc_WORUnPause_C` scans the world's full object table. For valid object pointers it clears `0x04000000` only when GAO identity flags match; independently of that mask match, it sets active bit `0x1` when activation-block bit `0x00400000` is clear.
- Both handlers request table/EOT rebuild, visibility refresh, and SnP refresh after scanning.

A separate `GameScriptWorldActivationRebuildReducer` models the next world-engine pass: `engine.c` calls `WOR_World_ActivateObjects`, which clears the existing activated table, clears runtime `flags2` bit `0x1` on valid object-table entries, skips objects whose GAO identity flags contain bit `0x1`, and calls `WOR_ActivateObjectIfFlagsAllow`. Iris then rebuilds EOT from the newly activated table. Normal time rejects `flags2 & 0x04400000` and `flags2 & 0x10`; special time instead requires `flags2 & 0x04000000`. If `ENG_gf_SpecialTime` is unknown, the reducer reports an object as resolved only when both modes produce the same membership.

The command's first integer is a universe-world slot, not a GAO key. Its second integer is a mask against identity flags (`SceneObject.Flags`). The reducer takes current runtime `flags2` and activated-table membership as explicit inputs because those are not equivalent to serialized GAO state. A missing world snapshot, malformed command, or slot mismatch returns `Unknown` with no object changes.

## Deferred activation and animation scheduling

Retail Iris processes a pending `tablesManagerAndEOTNeeded` rebuild at the
start of `ENG_OneWorldEngineCall`: it rebuilds activated world objects and then
EOT tables (`external/irisEng/engine.c:137-150`). That call runs EVE afterwards
and ANI later in the same call (`engine.c:180-200`). `OBJ_FlagInactiveSet_C`
changes runtime `flags2` and sets the pending rebuild flag during EVE
(`external/irisEng/ai/ai_gameobjectfunctions.c:1353-1375`); it does not
immediately rebuild or remove the object from the current animated table.
`ANI_MainCall` iterates the current EOT animated-object table
(`external/irisEng/animation.c:2414-2482`). Therefore a direct inactive command
can still be followed by that call's ANI update. The changed membership takes
effect after the next world-engine call rebuilds the activated table and EOT.

JadeView's cinematic activation inspector predicts eligibility at that next
rebuild boundary. It does not claim the current ANI call is skipped, and it
does not reconstruct the runtime EOT table from archive data. Callback dispatch,
world slot, special-time mode, and current live table membership remain outside
this projection unless explicitly supplied.

## Scope and limits

The command reducer reports immediate per-object flag intents and refresh requests; the separate rebuild reducer calculates new flags2/activity membership when time mode is supplied (or leaves mode-dependent results unknown). Neither reducer updates cinematic draw visibility or schedules AI callbacks. JadeView's archive-derived preview does not contain a live Iris universe slot table, runtime `flags2`, activated-table membership, or special-time state, so the current cinematic path cannot safely turn an archive command into concrete object activity. Jaded's later engine source uses a different sector/control-flag activation model; do not apply these Iris reducers to Jaded semantics or treat either source tree as proof of retail PC parity.

Synthetic regression checks cover activated-table filtering for pause, full object-table traversal for unpause, both exclusion/block bits, refresh requests, malformed inputs, unknown world resolution, normal/special-time rebuilds, identity-bit exclusion, invalid table pointers, and mode-dependent unknown results. They prove the reducers' source translation, not runtime command dispatch.

During cinematic playback, the selected object's Game activation inspector also
shows its current source-timeline `flags2` projection and evaluates a hypothetical
next activation-table rebuild separately for normal and special time. It reports
the EOT tables selected by the object's identity flags if that rebuild admits
it. For example, an object can have the `actor animation` identity bit while
its current inactive state blocks membership. This diagnostic uses the same
Iris rebuild reducer as the tests; it does not execute callback branches or
claim that the native engine rebuilt the table at that playhead.

## Jaded comparison

Jaded corroborates the broad engine architecture—world object activation is
rebuilt before scheduler tables are used—but its activation rules are not a
drop-in source for the retail Iris reducer above. `WOR_World_ActivateObjects`
in Jaded evaluates sector membership and control flags, including
force-inactive and always-active handling, and can run sector enter/leave
callbacks or reinitialize objects (`external/jaded/src/libraries/ENGine/Sources/WORld/WORmain.c:47-105`).
It rebuilds active-object membership separately (`WORmain.c:166-200`). Its
`WORPause`/`WORUnPause` paths update Jaded control flags and defer activation
rebuilding (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_wor.c:897-940,1107-1134`).

Retail Iris instead changes runtime `flags2`, then requests the world-table,
EOT, visibility, and SnP rebuilds described above. JadeView therefore uses
Iris for retail BGE flag semantics and keeps Jaded's sector/control-flag model
as architecture evidence only. Neither tree alone proves that a particular
archive owner was dispatched or that a callback passed its live runtime gates.
