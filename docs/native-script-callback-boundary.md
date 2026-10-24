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

## Read-only loader closure measurement

The dependency tool now accepts `-FunctionName`; `/Gy` emits per-function COMDAT sections, so the report can isolate one section rather than mixing every function in `ai_common.c` or `loading.c`. Its categories distinguish unresolved engine imports, direct calls to functions defined in the same translation unit, and direct undefined data references. Example:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/NativeScriptBridge/measure-callback-imports.ps1 -SourcePath external/irisEng/ai_common.c -FunctionName AI_ul_CallbackLoadFunction
```

Selected section results (direct references only; counts are not transitive link closures):

| Function section | Unresolved function imports | Same-TU direct calls | Undefined data refs |
| --- | ---: | ---: | ---: |
| `AI_ul_CallbackLoadInstance` | 5 | 0 | 1 |
| `AI_ul_CallbackLoadModel` | 8 | 0 | 1 |
| `AI_ul_CallbackLoadVars` | 12 | 0 | 4 |
| `AI_ul_CallbackLoadFunction` | 12 | 0 | 3 |
| `AI_ResolveInstanceRef` | 0 | 2 | 0 |
| `AI_ReinitInstance` | 2 | 0 | 1 |
| `LOA_MakeFileRef` | 0 | 0 | 3 |
| `LOA_Resolve` | 4 | 3 | 5 |
| `BIG_pc_ReadFileTmp` | 2 | 3 | 2 |
| `WOR_World_ActivateObjects` | 2 | 1 | 0 |
| `EOT_SetOfEOT_Build` | 2 | 2 | 2 |
| `AI_RunInstance` | 0 | 1 | 10 |
| `AI_MainCall` | 4 | 1 | 3 |

For example, `AI_ResolveInstanceRef` has no unresolved imports in its own function section, but makes same-file calls to `AI_ResolveModelRef` and `AI_InstanceAfterLoading`. The tool reports those internal edges; it does not recursively resolve their imports, and an indirect call through `loaStackElem->funcPtr` is inherently data-driven. `ai_common.c` as a whole has 41 direct unresolved function names and 34 undefined data references, while `loading.c` as a whole has 26 and 30 respectively; these translation-unit totals are much broader and should not be mistaken for a necessary single-callback link set.

The loader mechanism is concrete in `external/irisEng/loading.c:336-370`: `LOA_Resolve` walks the pending reference ring, finds each key's BF position, looks up an already loaded address or invokes the queued resource callback, optionally records the result in the address cache according to reference flags, writes the pointer to the owner's destination, then releases the global read buffer. Missing keys with required flag bit 0 trigger `DBG_assert`. `LOA_MakeFileRef` only queues key/destination/callback/flags into global `LOA_gast_Stack` (`loading.c:314-333`). That makes a small read-only owner loader plausible, but it still requires real bigfile key lookup, byte reads and resource callbacks. `BIG_pc_ReadFileTmp` directly calls `LOA_FetchFile`, `BIG_fread`/`BIG_fseek`, and `BIG_p_RequestBuffer` (`external/irisEng/bigfiles.c:834-864`); its read-mode branches and global stream/buffer state are part of the contract.

The actor activation boundary is later: `WOR_World_ActivateObjects` iterates the world table and feeds eligible GAOs to `WOR_ActivateObjectIfFlagsAllow` (`external/irisEng/world.c:44-57,174-187`). EOT build then snapshots objects into the AI callback table from the activated-table `objectFlags` bit `0x20` (`world.c:671-760`). `AI_MainCall` reads that table and checks live owner state before calling `AI_RunInstance` (`external/irisEng/ai_common.c:711-810`). This stage is not read-only resource resolution: it depends on mutable world flags, current time, owner distances/process markers and AI globals. Do not add it to a loader-only helper.

## Concrete archive path

The owner fixture now preserves all six bounded resources needed to inspect this chain. For `FF001FBE`, `WP_Active_Inter.gao` (`10001E0C`) points to AIC `10001E0E`, which points to AIM `9E00152F`; the AIM's ordered entry 0 is model OVA `9E00152D` and entry 1 is OFC `9E00152E`, while instance OVA is `10001E0D`. Each resource is resolved with its BF offset, length and SHA-256 in `artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/manifest.json`. The OVA decodes the callback key into slot 2. A second owner, `toyolum.gao` (`8F0027AF`) in `FF006BFC`, uses the same model/OVA/OFC but has instance OVA `C100B3C4` with empty storage; its exact provenance is in `artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/manifest.json`.

These are evidence fixtures generated by JadeView's archive parser, not resources resolved by Iris' native loader. They verify that a native read-only bridge could use real bytes rather than fabricated examples; they do not verify Iris parser compatibility, pointer assignment at runtime, actor activation, or callback execution.

## What this changes about the bridge plan

The current `Activateur_avec_SF` command-capture harness proves that one original callback body can run against a deliberately small, controlled context. It emits object-change intents; it does not load a world or apply those intents. `activateur_act_ini` is a useful contrasting case because it confirms that object activation is not just an EVE keyframe: it depends on an AI owner, callback slot, actor references, current world collision results, flag writes, and subsequent EOT/table refresh.

The next viable seam is a **read-only native loader/dispatch trace** for one parsed owner: pass the fixture's real resource bytes through the Iris resolver and resource callbacks, report resource identities, ordered links, callback slot and registry hit, then stop before `AI_ReinitInstance`, world activation and callback execution. The bounded files and expected topology now exist for `WP_Active_Inter.gao` in `FF001FBE`. The section audit shows that loading is not a one-function stub: its direct references include memory allocation, string/vector/type readers, file-cache/stream state and global queue state; a real link still needs the transitive dependency graph. Implement only after that graph is explicit, and fail on any unprovided function or resource path. Do not fold EOT, world activation or collision into a supposedly read-only loader.

## Second retail owner cross-check

A fresh read-only audit of container `FF006BFC` resolves `toyolum.gao` (owner `8F0027AF`, world `20006BF7`) to the same model `9E00152F` and slot-2 callback `9E00152E`. It reports six model variables, zero decoded instance variables and zero resolved object references. Evidence: `artifacts/export-verification/FF006BFC-model-variable-references.json`. This confirms the family is shared across different named assets and containers; it does not supply a usable target or justify executing the callback. Zero decoded instance fields must retain their provenance rather than being interpreted as known zero-valued runtime inputs.
