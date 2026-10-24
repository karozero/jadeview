# Native callback boundary: actor activation

## Result

The second callback family examined is Iris' `activateur_act_ini` (compiled-script key `0x9E00152E`). It is directly associated in the parsed PC archive with owner `WP_Active_Inter.gao` (`GAO 10001E0C`, world `E6001FA7`, model `9E00152F`) in container `FF001FBE`. Its callback array has `0x9E00152E` in slot 2; the archive audit reports six model variables and five instance variables. This is concrete owner-to-callback metadata, not proof that the callback ran in the retail game. Evidence: `artifacts/export-verification/FF001FBE-model-variable-references.json:2694-2708`.

Iris registers `0x9E00152E` as `activateur_act_ini` in `external/irisEng/ai/ai_compiledscripts.c:1981`. The body in `external/irisEng/ai/ai_bge/ai_compiledscripts_activateur.c:3-53` reads the current instance variables, resolves actor references, tests collision/overlap, and requests changes to activation/visibility flags. The only safe conclusion for JadeView is that this is an activation callback with collision-dependent conditions; archive presence alone cannot tell whether the target should be visible at a particular cinematic time.

## Measured callback dependency boundary

`tools/NativeScriptBridge/measure-callback-imports.ps1` compiles the original Iris source as x86 and intersects COFF relocations with unresolved symbols. For `ai_compiledscripts_activateur.c`, the direct call imports are:

- `AI2C_CheckActorGao`
- `AI_EvalFunc_COLBVObjectObject_C`
- `AI_EvalFunc_COLBVGetAllOverlaps_C`
- `AI_EvalFunc_OBJ_FlagsControlSet_C`

The object also directly references `AI_gpst_CurrentInstance` and `AI_gpst_CurrentGameObject`. Reproduce with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/NativeScriptBridge/measure-callback-imports.ps1 -SourcePath external/irisEng/ai/ai_bge/ai_compiledscripts_activateur.c
```

This is a compile/relocation audit only: it does not link or invoke the callback. `AI2C_CheckActorGao` maps small actor-reference tokens through `AI_MainActor` and otherwise returns the supplied GAO (`external/irisEng/ai_common.c:1579-1585`). The overlap functions need collision state: `AI_EvalFunc_COLBVObjectObject_C` calls `COL_b_BVOverlap` (`external/irisEng/ai/ai_colfunctions.c:144-151`); `AI_EvalFunc_COLBVGetAllOverlaps_C` reads the actor's collision detection list and filters it using object flags (`ai_colfunctions.c:88-117`). These cannot be represented honestly by inert success stubs. The final flag write changes `flags2` and marks world tables, visibility, and spatial refresh as needing updates (`external/irisEng/ai/ai_gameobjectfunctions.c:1253-1276`). A synthetic harness without the real collision world would test invented activation conditions, so this family is intentionally not executed by the harness.

There is an additional source safety concern: `activateur_act_ini` declares only one local pointer `v10` and passes `&v10` as the overlap output buffer (`ai_compiledscripts_activateur.c:5-6,19`). Iris' `AI_EvalFunc_COLBVGetAllOverlaps_C` writes every matching pointer and then a terminating null (`ai_colfunctions.c:88-117`). If there is at least one qualifying overlap, that source-level buffer shape appears too small. This may be an Iris reconstruction/decompilation defect or a retail ABI discrepancy; it is **not** a basis to alter the source or emulate its behavior. Together with the real collision dependency, it is another reason not to invoke this callback in a standalone harness until the buffer and retail behavior are validated.

## Owner-to-dispatch path in Iris source

The source chain that a future bridge must preserve is:

1. A GAO record with AI-instance data queues `AI_ul_CallbackLoadInstance` (`external/irisEng/gameobject.c:3089-3094`). The instance loader queues its AIM model and instance OVA (`external/irisEng/ai_common.c:1387-1407`).
2. The model loader walks its typed references, queues its model OVA into `model->vars` and retains ordered OFC function references (`ai_common.c:1325-1384`). The OVA loader resolves the five callback keys into `callBacks[0..4]` (`ai_common.c:1124-1134,1289-1300`).
3. The OFC loader resolves the resource key against `AI2C_gat_fctdefs`; for a compiled match it stores `funcPtr`. It separately reads compact metadata and only materializes an interpreted node body on the non-compiled path or outside the speed/compiled read mode (`ai_common.c:1023-1106`).
4. `AI_ReinitInstance` copies the five model callback slots to the instance, initializes jump nodes and run timers (`ai_common.c:375-425`). `WOR_pst_Universe_AddWorld(..., resolveAI)` activates objects and builds EOT tables (`world.c:2243-2261`). `EOT_SetOfEOT_Build` snapshots activated-object flags into the AI callback table when bit `0x20` is present (`world.c:671-760`).
5. `AI_MainCall` applies further live scheduling/owner conditions and invokes `AI_RunInstance`; `AI_pst_RunFunction` calls `funcPtr` when compiled AI is enabled (`ai_common.c:688-700,711-810`).

The real archive audit confirms the owner, model and callback key at the serialization/metadata end of this chain. Iris source confirms how Iris would dispatch such a key. It does **not** establish the retail PC 2003 loader ABI or prove that this owner reaches the callback at a specific time. The parsed archive report does not include the resolved model-OVA key or current collision overlaps; those remain missing inputs for exact callback execution.

## What this changes about the bridge plan

The current `Activateur_avec_SF` command-capture harness proves that one original callback body can run against a deliberately small, controlled context. It emits object-change intents; it does not load a world or apply those intents. `activateur_act_ini` is a useful contrasting case because it confirms that object activation is not just an EVE keyframe: it depends on an AI owner, callback slot, actor references, current world collision results, flag writes, and subsequent EOT/table refresh.

The next viable seam is a **read-only native loader/dispatch trace** for one parsed owner: load/resolve its actual `.aic` → `.aim` → `.ova`/`.ofc` chain, report the callback slot and registry hit, then stop before executing callbacks. This has a deterministic expected result for `WP_Active_Inter.gao` from `FF001FBE`, does not need collision stubs, and would bridge the current gap between JadeView's archive metadata and the private source callback harness. Before implementing it, measure the native resource loader's link closure independently; do not pretend that compiling one callback has brought Iris' BF loader, world activation, EOT rebuild or collision system into the helper.
