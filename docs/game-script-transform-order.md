# Script movement and hierarchy

Iris `ai/ai_gameobjectfunctions.c:229-249` and Jaded
`AIinterp/Sources/Functions/AIfunctions_obj.c:849-869` add `OBJMove` directly
to the object's absolute position. The EVE path scales the displacement by
the current event ratio. Neither implementation rebases hierarchy-local state.

The later hierarchy pass computes child global pose from its saved local pose
and parent pose (Iris `gameobject.c:29-45,4534-4585`; Jaded
`ENGine/Sources/OBJects/OBJorient.c:550-571`). Explicit global-to-local rebasing
is a separate operation: Iris `gameobject.c:1101-1145`, Jaded
`OBJorient.c:625-667`. Therefore simply adding a world offset after hierarchy
evaluation is not a faithful implementation for bound children. Their support
requires the actual phase order, binding changes and dynamics; retail behavior
with those additional effects remains unverified.

## Retail hangar archive observation

The nested-EVE audit of container `FF005833`, clip `BF00218E`, completed with
322 assembled objects, four actors and 33 generated transform tracks. In the
currently decoded/scheduled EVE routes it produced **zero OBJMove segments**.
That does not exclude native callbacks or other undecoded execution paths.

The fan objects instead have generated translation/rotation tracks:
`DEC_00_02_helicesV.gao` (`1F00007E`) and `DEC_00_02_moteurV.gao` (`1F00007D`).
Both drone variants (`72005128`, `72005129`) have decoded invisible setters at
0 seconds, clear setters at 30.35 seconds and clear setters at 44.7 seconds.
At 43.216667 seconds, the blades receive `OBJ_FlagsControlSet(0x00600000, 0)`.
The next check for their pose defects is transform/hierarchy composition and
native state changes, not an assumed missing OBJMove command.

Evidence: `artifacts/move-real-hangar-audit.log` and
`artifacts/export-verification/FF005833-nested-eve-transforms.json` (generated,
not committed). These prove decoded input routes, not visual parity with game.
