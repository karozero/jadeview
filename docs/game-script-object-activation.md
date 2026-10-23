# Object activation: serialized state and runtime tables

Iris `gameobject.c:2852-2874` initializes the GAO runtime state using mask
`0x7B9C0080`, remaps serialized bits `0x20000000` / `0x40000000` to runtime
invisible / inactive bits, and restores custom bits `8..15`. This projection
is not a complete runtime state: collision detection-list and RTL status work
also occurs during loading. In particular, serialized `0x04000000` does **not**
survive the initial mask. Do not treat raw archive flags as runtime flags2.

Iris `world.c:44-62` adds an object to its active-object table only when its
active status bit is clear. At normal time, flags2 must intersect neither
`0x04400000` nor `0x10`. At nonzero special time, bit `0x04000000` must be set
instead. The invisible bit is not an activation blocker in this predicate.
`GameScriptObjectActivation` models this predicate for an explicitly supplied
runtime snapshot; unknown time mode remains unknown. It does not insert an
object into a table or dispatch callbacks.

This is an Iris implementation rule. Later Jaded
`src/libraries/ENGine/Sources/WORld/WORmain.c:47-94` additionally consults sector
activity, can reinitialize objects, and dispatches sector callbacks. Those
side effects and their applicability to retail BGE are not established by the
Iris predicate. Neither a positive conditional forecast nor a matched archive
owner proves that the game activated or ran that owner.
