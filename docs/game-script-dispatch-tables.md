# World dispatch tables and script callbacks

This is a static source audit of the PC-oriented Iris engine model in `external/irisEng`. It describes how a loaded object's flags become world scheduler-table membership and how those tables are consumed. It is not a runtime capture from retail BGE. Jaded source and archive snapshots can corroborate structure, but do not establish the retail game's live table contents or callback outcomes.

## From world activation to EOT membership

Iris rebuilds a world's activated-object table by clearing it, iterating the world object table, clearing each object's runtime `flags2` active bit `0x1`, and calling `WOR_ActivateObjectIfFlagsAllow` only when the GAO identity flag `flags & 0x1` is clear (`external/irisEng/world.c:174-201`). The activation predicate then checks runtime `flags2`: ordinary time excludes already-active objects and objects intersecting `0x04400000` or `0x10`; special time instead requires `0x04000000` (`world.c:44-62`). Thus a serialized callback identity bit alone does not guarantee that an owner enters any scheduler table.

`EOT_SetOfEOT_Build` clears the derived tables and walks the **activated-object table**, using each activated entry's `objectFlags` snapshot as the identity mask (`external/irisEng/world.c:671-758`). The relevant masks are:

| Identity mask | Derived table | Source location |
| --- | --- | --- |
| `0x2` | animated objects | `world.c:691-698` |
| `0x4` | dynamic objects | `world.c:699-707` |
| `0x20` | AI callback owners | `world.c:708-716` |
| `0x100`, `0x200`, `0x400` | collision/map tables | `world.c:717-740` |
| `0x02000000` | EVE/event owners | `world.c:741-748` |
| `0x00400000` | hierarchy owners | `world.c:749-757` |

The activated table is populated with `gao->flags` as its table-entry flags (`world.c:44-62`), and the EOT builder copies those entry flags into each derived table. This is a table-build snapshot, not a fresh lookup of identity flags on every callback. Iris has explicit refresh/removal paths: `EOT_UpdateIdentityFlagsOfTable` updates table-entry flags from the live GAO (`world.c:479-495`); `EOT_RemoveObjectFromTableIfBadFlag` removes entries whose stored identity bit no longer matches the live object (`world.c:464-477`); identity-flag change handlers call these helpers for affected scheduler tables (for example `gameobject.c:2022-2025`, `2276-2282`, `2295-2307`). The main world loop also rebuilds activated/EOT tables when `tablesManagerAndEOTNeeded` is set (`external/irisEng/engine.c:137-150`). A static BF can provide the serialized identity mask, but not establish that later runtime edits or rebuilds did not change it.

The EOT builder sorts the AI callback table by each referenced object's `extended->AIPriority` (`world.c:521-669`, invocation at `world.c:760`). Therefore the AI callback scan order is not merely archive/object-table order. The displayed serialized GAO flag is useful eligibility evidence but must be kept distinct from the built scheduler-table snapshot and its priority order.

## Per-frame dispatch order and gates

`ENG_OneWorldEngineCall` performs scheduler passes in this order: EVE, animation, AI, hierarchy, then dynamics (`external/irisEng/engine.c:179-244`). The engine runs these for active universe worlds in `engine.c:560-582`; that outer active-world selection is separate from per-world object activation and EOT membership.

- **EVE:** `EVE_MainCall` iterates `eventGameObjectsTablePF`, then requires a valid entry, extended data and an EVE component, nonzero distance when `extraFlag & 4` is set, and runtime `flags2 & 0x00400000 == 0`; it sets the current EVE owner and evaluates its tracks (`external/irisEng/events.c:2683-2723`). Its membership source is the EOT identity mask `0x02000000`, not the presence of a decoded EVE asset by itself.
- **Animation:** `ANI_MainCall` follows EVE and consumes the EOT animated-object table; an example of the consumer loop and its actor/extra-flag checks is `external/irisEng/animation.c:2430-2465`. EVE evaluation itself also processes running tracks (`events.c:1996-2101`), but that does not substitute for world ANI scheduler membership.
- **AI:** `AI_MainCall` iterates `AICallbacksTablePF`, requires a valid table entry that has not been processed this world-call (`OBJ_b_HasBeenProcess(..., aiCallCount, 0x20)`), and applies the optional distance gate (`extraFlag & 4`) (`external/irisEng/ai_common.c:765-791`). It follows `basic->mtx->refGao2` links to an ancestor not yet processed and marks that resolved object processed (`783-792`). It then requires an `AIInstance`, performs a group-sector update under its own flags, sets current AI object context, and skips `AI_RunInstance` when runtime `flags2 & 0x00400000` is set (`793-805`).

These gates operate on different layers: GAO identity flags choose membership during EOT construction; object activation gates decide whether the source object reaches the activated table; EVE/AI then apply runtime-specific tests after membership. The current JadeView analyzers already distinguish most of these layers, but they do not preserve a concrete runtime EOT-table snapshot or AI priority ordering.

## A scheduler-wide AI early-exit edge

The scheduler has a consequential cross-owner control-flow dependency. `AI_RunInstance` decrements a callback's positive wait by `TIM_gf_dt`, runs callbacks whose remaining wait is zero, and interprets special return nodes (`external/irisEng/ai_common.c:711-762`). When it returns `0`, `AI_MainCall` executes `break` at `ai_common.c:803-804`, ending the **whole world AI callback-table scan for that call**, not just the current owner's callbacks. Since the table was priority-sorted earlier (`world.c:521-669`, `760`), an earlier-priority owner's callback can defer all later table entries for the current pass.

The return handling visible in source is bounded:

- `AI_RunInstance` returns `1` for missing/disabled instances and for special nodes `1` or `6` (`ai_common.c:716-723`, `752-755`); these end this instance scan while allowing `AI_MainCall` to continue to the next table entry.
- A special node `3` makes `AI_RunInstance` return `0` (`ai_common.c:756-757`), which stops the remaining world AI table scan because of the caller's `break`.
- Special node `5` decrements the callback index (`ai_common.c:758-759`); the loop increment then revisits the same index. This is a repeat/retry path, not evidence of a time-based delay by itself.
- Positive per-callback wait values are reduced by the frame delta; they are not called until the wait reaches zero (`ai_common.c:736-744`).

The evaluator's call wrapper maps jump-node markers `1`, `3`, `4`, and `5` to these integer outcomes (`external/irisEng/ai/ai_aifunctions.c:49-84`); `AI_EvalKeyword_ReturnTrack` yields marker `4` and `AI_EvalKeyword_Stop` yields marker `1` (`external/irisEng/ai/ai_keywords.c:91-107`). These facts support a future generic interpreter/model for callback outcomes. They do **not** show that an arbitrary archive callback returned one of these markers, nor establish the exact semantics of marker `6` in retail PC compiled callbacks. A callback body can be compiled/native or node-interpreted (`ai_common.c:688-700`), so this source model does not replace execution evidence.

This global early-exit can explain why a later AI owner may not get a callback in a particular world pass even when its owner-level membership and local gates look eligible. It does not directly explain a missing animation table entry: ANI ran earlier than AI in that pass. Any link from a yielding gameplay callback to a later animation change must be established from the actual callback body and state writes.

## Comparison with JadeView analyzers

- `CinematicEventSchedulerAnalyzer` reports serialized EVE identity bit `0x02000000`, the EOT activated-table identity snapshot, a linked SceneEvents resource, EVE's post-membership validity/distance/pending-inactive checks, and a fresh-load activation projection (`src/Features/Cinematics/CinematicEventSchedulerAnalyzer.cs`). The projection uses the common initial-activation forecaster, including the outer world-rebuild identity-bit-0 skip, and labels ordinary/special-time eligibility as conditional rather than observed. It does not prove the live EOT snapshot, runtime flag mutations, distance, or track execution.
- `CinematicScriptSchedulerAnalyzer` reports the AI callback table identity bit `0x20`, initial-state activation blockers, distance/hierarchy/AIInstance and pending-inactive gate (`src/Features/Cinematics/CinematicScriptSchedulerAnalyzer.cs:3-39`). JadeView now also reports per-callback wait/decrement and the world-scan `break` caused by status `3` as general rules. It does not resolve actual priority ordering, callback delays or return outcomes for a specific owner.
- `CinematicOwnerStartLifecycleAnalyzer` correctly labels EOT activation and EVE scheduling runtime-unknown, and warns that links do not prove table entry or track execution (`src/Features/Cinematics/CinematicOwnerStartLifecycleAnalyzer.cs:14-42,91-105`).

The EVE inspector now separates serialized identity, conditional initial activation and EOT table eligibility. This makes an important static distinction visible when investigating missing or idle owners without claiming runtime execution. It does not model cross-owner AI callback outcomes: a follow-on generic model can keep separate fields for serialized identity mask, activation eligibility, EOT snapshot membership, AIPriority/order, wait remaining, callback outcome (continue-instance / yield-instance / repeat / world-abort / unknown), and whether that outcome was statically resolved or runtime-observed. Do not mark every AI callback as delayed or missing; only attribute the scheduler-wide abort to an owner when its decoded control flow proves the status-3 return.

## Limits and next implementation seam

No retail runtime scheduler-table dump is present here. Archive owner data and decoded callbacks cannot prove actual activation, EOT build timing, effective priority after runtime mutation, callback wait values, compiled callback return markers, or the exact frame on which the AI table scan stops. Iris provides source behavior; Jaded is not interchangeable with retail PC; static script reachability is not runtime execution.

The smallest source-backed next JadeView change is a generic scheduler-outcome explanation in the Script Scheduler section, plus an optional callback-return analysis that recognizes only fully decoded status-producing paths. Keep table snapshot membership and live GAO flags separate, avoid converting unknown callback outcomes into delayed/skipped state, and do not change scene assembly or animation selection on this evidence alone.
