# Iris engine update-table membership

Iris first builds an activated-object table from runtime `flags2`. It then
builds separate EOT tables by testing each activated entry's GAO identity
`flags`. Passing world activation therefore does not mean an object participates
in every update path.

| GAO identity bit | EOT table | Relevant phase |
|---:|---|---|
| `0x00000002` | Animated objects | `ANI_MainCall` |
| `0x00000004` | Dynamic objects | Dynamics pass |
| `0x00000020` | AI callbacks | `AI_MainCall` |
| `0x00000100` | Collision-map objects | Collision-map path |
| `0x00000200` | ZDM collision objects | Collision path |
| `0x00000400` | ZDE collision objects | Collision path |
| `0x02000000` | Event game objects | `EVE_MainCall` |
| `0x00400000` | Hierarchy objects | Hierarchy pass |

The mapping comes from `external/irisEng/world.c:671-760`, where
`EOT_SetOfEOT_Build` copies entries from `activatedObjectsTable4PF` into each
table using those masks. `WOR_ActivateObjectIfFlagsAllow` inserts an eligible
object with its GAO identity flags (`world.c:44-62`), but its caller determines
which objects are offered for activation. On initial world activation,
`WOR_World_ActivateObjects` clears runtime active bit `flags2 & 1`, skips objects
whose GAO identity flag bit `0x1` is set, then calls that flags2 predicate
(`world.c:174-190`). `WOR_World_AddLoadedObject` has the same identity-bit
exclusion before calling it (`world.c:193-200`). Thus identity bit `0x1` is an
outer activation exclusion in these paths; the other listed identity bits
select EOT categories after activation. The frame path calls EVE, actor
animation, then AI (`engine.c:184-215`); AI callback-ID dispatch and render
callbacks occur in separate later phases. These are independent schedulers,
not a single “script enabled” switch.

JadeView exposes this mapping in the selected-object **Game activation** details,
the script-owner lifecycle inspector, and the conditional initial-activation
forecast beside the cinematic start-gate inspector. The object details show the
serialized state, projected initial `flags2`, separate normal/special-time
eligibility, and any EOT tables for an eligible path. The lifecycle view lists
which tables the identity bits request if the owner is activated. The shared
`GameScriptInitialActivationForecaster` projects the serialized state through
`GameScriptSerializedObjectState`, evaluates normal-time and special-time
activation independently, then attaches EOT membership only to an eligible
path. It also models the `WOR_World_ActivateObjects` wrapper's identity bit
`0x1` exclusion before evaluating `flags2`; this exclusion is separate from the
lower-level `WOR_ActivateObjectIfFlagsAllow` predicate. This means “would be
placed in these EOT tables if this initial Iris path runs,” not that retail BGE
loaded the owner, dispatched a callback, or advanced its tracks. Later runtime
flag changes and the retail executable's equivalence remain unverified.

The fresh-load state projection masks raw serialized bit `0x04000000` out
(`0x7B9C0080 & 0x04000000 == 0`), while Iris's special-time activation path
requires that runtime `flags2` bit. Therefore an archive snapshot alone cannot
prove special-time activation: it can only show that the initial projected
state lacks the bit. A runtime script or engine transition may set it later;
that transition must be established separately before forecasting later
activation.

Synthetic mask coverage lives in `tests/ExportVerification/GameScriptEotMembershipChecks.cs`;
the activation-path integration is covered by
`CinematicStartGateEvaluatorChecks.cs`, and the owner-wide lifecycle output by
`CinematicOwnerStartLifecycleChecks.cs`.

## Runtime pause commands and unresolved context

The archive-wide reachable-call audit found `WORPause` and `WORUnPause` in
shared helper callbacks, not just cinematic tracks. For example,
`helper_text_hlptxt_tests_wait` obtains its world slot with
`WORCurIndexGet()` and calls `WORPause(slot, 0x20)` under timer and
`des_i_cine_temporaire` conditions (`external/irisEng/ai/ai_bge/ai_compiledscripts_helper_text.c:934-935,903`).
Its completion path calls `WORUnPause(WORCurIndexGet(), 0x20)` only when
`des_i_cine_temporaire` is true (`:336-337,334`). This is reachable-source
evidence; it does not show that a particular owner passed its gates or ran.

JadeView's `GameScriptWorldActivationReducer` models these calls only when a
runtime world slot and activated-object snapshot are supplied. The archive
source-call graph does not provide either value: `WORCurIndexGet()` depends on
the current universe, while `WORPause` iterates the live activated-object table.
The owner inspector therefore reports arguments, source assignments and branch
conditions without applying the mutation to every object in the assembled
cinematic. Reconstructing this transition requires a known universe-slot map,
the current `flags2` values/table membership, and the callback's runtime gate
state.
