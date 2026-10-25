# Jaded procedure triggers: a separate call path

Jaded source contains a trigger/procedure mechanism that is separate from
world-scheduled AI callbacks and EVE event keys. It is useful for identifying
another possible source of conditional behavior, but it is not yet confirmed
for the retail BGE 2003 PC executable.

## Resolution and execution in Jaded

`SCR_tt_Trigger` stores a script-file key, a trigger name, a procedure pointer,
and an embedded message (`AI2C_mdlheader.h`). `AI_ResolveTrigger` first clears
the resolved procedure. In the normal load path, it can match the file key and
trigger name against a compiled C-trigger registry; otherwise it resolves the
script-file key to a procedure list and matches a procedure name after skipping
the `@` prefix and numeric suffix (`AI2C.cpp:457-535`).

The `call_trigger` keyword reads a trigger value, resolves it dynamically in
editor builds, builds a temporary function wrapper around the resolved
procedure's node tree, pushes the trigger's embedded message as the procedure
input, and calls the AI function executor synchronously
(`Keywords/AIkeywords.c:1153-1197`; `Functions/AIfunctions_ai.c:35-71`). This
is a direct nested procedure call, not a message-queue send and not a new
scheduled AI callback. The keyword rejects certain scheduler-control outcomes
such as object suicide, track changes, and stop operations in a procedure or
trigger.

## Implication for reverse engineering

The script-call graph may have edges that are neither ordinary function calls
nor `MSGSend` routes: a trigger-valued OVA/node can identify an external
procedure by script-file key and name, with a structured message passed into
the procedure. A future trigger audit should therefore index trigger values,
resolved procedure lists, trigger-name aliases, and message fields separately
from owner callback slots and message queues.

The supplied Iris source tree does not currently provide a confirmed matching
`call_trigger` implementation in its AI keyword dispatch, so JadeView should
not infer that this Jaded path runs in retail BGE. Before implementing it as a
retail feature, establish a matching PC runtime function or a BF-backed call
site resolved through an equivalent Iris path. Until then, label it as a
Jaded-only comparative lead.

Evidence: Jaded `external/jaded/src/libraries/AIinterp/Sources/AI2C.cpp`
(`AI_ResolveTrigger`), `AI2C_mdlheader.h` (`SCR_tt_Trigger`),
`Keywords/AIkeywords.c` (`AI_EvalKeyword_CallTrigger`), and
`Functions/AIfunctions_ai.c` (`AI_EvalFunc_AIExecute_C`). The source tree is a
later community fork and does not establish behavior in the retail BGE 2003
runtime.
