# Inline AI functions inside EVE tracks

This note distinguishes AI code embedded in an EVE event from a normal AI
instance callback. The distinction matters when explaining which scene scripts
are active and when previewing callback side effects.

## Execution path

An EVE AI Function event stores an inline AI node tree in its event payload.
Iris and Jaded load those nodes with the EVE event; they do not resolve this
payload as an ordinary owner `.ofc` callback. During track playback, the event
handler sets the current game-object context and calls the node interpreter
directly (`AI_RunNode`). The handler then copies the first integer left on the
global stack to the event's private parameter (`external/irisEng/events.c:168-220`;
`external/jaded/src/libraries/AIinterp/Sources/Events/EVEnt_aifunc.c:119-160,216-302`).

The target context is selected from the current EVE track. With the sentinel
bone/gizmo index, the target is the track's referenced GAO, or the EVE owner if
the track has no target. With a bone index, the target is that indexed bone in
the referenced actor's skeleton. If the actor or bone cannot be resolved, the
handler returns without evaluating the inline tree. The prior AI function,
instance, and game-object context is restored after evaluation.

## Wait means retry

The EVE dispatcher reads event flags after the inline function returns. A
wait-while-false or wait-while-true event checks the returned integer; if the
predicate is not yet satisfied, it resets that track's local time to zero and
returns before marking the event done. The event is therefore evaluated again
on a later EVE pass. A `DoOnce` guard does not prevent these retries because
the done marker has not been set yet. This is a real source-level mechanism;
it does not imply that every serialized event waits or that every retry is
visible in a static archive (`external/irisEng/events.c:1737-1785`;
`external/jaded/src/libraries/AIinterp/Sources/Events/EVEplay.c:891-970`).

## Difference from owner callbacks

Regular AI owner callbacks are scheduled through the world's AI callback table
and `AI_MainCall`; they have instance callback slots, jump positions, wait
timers, priority ordering, and scheduler eligibility gates. An inline EVE
function instead runs while the EVE track is being evaluated, with the track's
current target context, and returns a value consumed by EVE. It can still call
engine APIs and mutate shared runtime state, but its execution is not evidence
that the track owner had an AI callback dispatched in that frame.

For JadeView, these should remain separate runtime lanes: EVE inline-node
execution and condition retry; AI-instance callback scheduling; and the
effects those calls may apply to world objects. A static inspection can show
the embedded node tree, target binding, wait flags, and possible effects. It
cannot claim an event actually retried, returned a value, or changed runtime
state without resolved gates and playback state.

## Evidence and scope

- Iris PC implementation: `external/irisEng/events.c` (`EVE_Event_AIFunction_Load`,
  `EVE_Event_AIFunction_Resolve`, `EVE_Event_AIFunction_Play`, and the EVE track
  event dispatcher).
- Jaded comparison: `external/jaded/src/libraries/AIinterp/Sources/Events/EVEnt_aifunc.c`
  and `EVEplay.c`.
- Ordinary AI callback execution: Iris `external/irisEng/ai_common.c`
  (`AI_MainCall`, `AI_RunInstance`) and Jaded
  `external/jaded/src/libraries/AIinterp/Sources/AIengine.c`.

Iris is the closer PC-oriented source reconstruction, while Jaded is a later
community fork based on a 2006 engine source tree. Their agreement corroborates
the architecture but is not a live execution trace from the retail BGE 2003
binary.
