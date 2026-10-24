# Engine callback assignments

Script-model OVAs contain two different callback tables. JadeView already reads
the five track callbacks used by `AI_RunInstance`. Iris reads an optional second
table after those five keys: a count followed by function keys indexed by engine
callback ID (`external/irisEng/ai_common.c:1292-1318`). This second table is
now decoded separately as `EngineCallbackSlots` and shown under **Callbacks and
call paths** in the cinematic script inspector.

`AI_ExecCallback` dispatches the model's function at the requested ID when the
owner has an AI instance and model, the owner passes runtime flags checks, and
that ID is in range (`ai_common.c:812-871`). It also executes
matching dynamically registered instance callbacks. An OVA assignment alone
does not prove that the object was instantiated, reached the corresponding
engine phase, or passed those checks.

Jaded names the callback IDs in `external/jaded/src/libraries/AIinterp/Sources/AIstruct.h:50-59`:

| ID | Jaded callback name | Evidence in bundled Iris source |
|---:|---|---|
| 0 | Before display | Direct dispatch in `graphicsobjects/geostr.c:2894` and `graphicsobjects/geo.c:772,778`. |
| 1 | After display | Direct dispatch in `graphicsobjects/geostr.c:3143` and `graphicsobjects/geo.c:888,893`. |
| 2 | After record | World-frame dispatch in `engine.c:288`, after the record stage. |
| 3 | After blend | Animation dispatch in `animation.c:2391,2646`. |
| 4 | When destroyed | Name is present in Jaded; no direct dispatch call site was located in the bundled Iris source. |
| 5 | Short-way event | Name is present in Jaded; no direct dispatch call site was located in the bundled Iris source. |
| 6–9 | Sector active/inactive and visible/invisible | Names are present in Jaded; no direct dispatch call site was located in the bundled Iris source. |

The inspector labels IDs 0–3 as having an Iris dispatch call site. IDs 4–9
retain the Jaded names but are explicitly marked as lacking a direct call site
in this Iris tree. Unknown IDs are preserved as numeric IDs rather than
rejected: the source loader accepts a serialized count and `AI_ExecCallback`
checks the requested ID against that count.

These engine hooks are not the five AI tracks. `AI_RunInstance` advances the
track callback span and its per-track wait timers (`ai_common.c:711-760`),
whereas `AI_ExecCallback` invokes an indexed model hook at display, animation,
world, or other engine call sites. The viewer currently inventories the hook
assignments but does not execute hook code or emulate the full engine phase.
That is why a callback assigned to an effect or actor can explain a possible
activation path without proving the effect should be visible at a given frame.

The parser accepts the source layout's optional tail and rejects a truncated
count/key list. Synthetic regression coverage checks no-tail, populated,
empty-entry, and truncated cases. The labels are source metadata, not a claim of
retail-version parity; Jaded is corroborating source and Iris is the primary
pre-release implementation used by this project.

The production `--script-model-variable-audit` was run on `sally_clean.bf`,
container `FF0045A2`. In that container it decoded 248 script owners; 51 had
nonempty engine-hook assignments (83 assignments total): 33 ID 0 hooks, 31 ID
1 hooks, and 19 ID 2 hooks. All 83 function keys mapped to names. No ID 3 or
IDs 4–9 assignment appeared in this sample. This confirms the optional table is
present and useful in real archive data, beyond synthetic parser fixtures. It
still reports configured functions only; EOT participation, runtime flag
checks, and which display/frame hooks actually fired are not serialized by
this audit.
