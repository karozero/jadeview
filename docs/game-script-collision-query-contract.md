# Script collision query contract

Status: source-derived prerequisites, not an implemented collision runtime.
These queries affect ordinary gameplay activation as well as cinematic state.

## Pair query

Iris `ai/ai_colfunctions.c:144-153` implements `COLBVObjectObject` by calling
`COL_b_BVOverlap(owner, target, FALSE, FALSE, TRUE)`. A null target returns false.
The two false arguments disable oriented-box refinement for this API call; they
do not mean collision geometry is absent.

`collision.c:9908-10000` uses sweep-and-prune (SnP) only when both objects have
runtime flag `0x80` and at least one has detection-list flag `0x20`. Otherwise it
falls back to `COL_b_SingleBVOverlap`. The SnP path reads the current world's
three axis pair tables using runtime object indices. A failed axis test returns
false. This requires the actual populated world, not just two exported meshes.

The direct fallback (`collision.c:9830-9905`) supports box/box, box/sphere and
sphere/sphere. Box/box adds matrix position to stored BV endpoints and considers
touching endpoints overlapping. Sphere branches use stored centers directly.
Do not apply an additional generic transform to every BV shape without verifying
its update/storage convention. The existing serialized bounds reader alone does
not establish the current runtime bounds after animation/hierarchy/dynamics.

## Overlap-list query

`ai/ai_colfunctions.c:88-122` is not a stateless scan of visible render objects:

- If owner flag `0x80` is absent and a current world exists, it sets that flag,
  inserts the owner into SnP and sorts the structure.
- It reads `owner.extended.coll.detectionList` and its current ordered entries.
- It filters entries through `OBJ_b_test_flag`, writes matching pointers to the
  caller's output array, appends NULL and returns the number of matches.

`INT_SnP_AddOverlap` (`snp.c:377-450`) maintains detection lists for participants
with flag `0x20`, checks the collision structures and avoids duplicate entries.
Consequently render visibility and a newly calculated AABB intersection are not
sufficient substitutes for this runtime list.

For `activateur_act_ini`, `whatToTest=5`: `OBJ_b_test_flag` tests runtime `flags2`
(`gameobject.c:1175-1245`). The required mask uses **any matching bit**, and no
forbidden bit may be present. A required mask of zero therefore matches nothing.
Mode 15 uses all required bits instead; the modes are not interchangeable.

## Native execution boundary

`ai/ai_bge/ai_compiledscripts_activateur.c` passes the address of a single local
pointer to `COLBVGetAllOverlaps`, while the reconstructed callee writes all
matches plus a terminator. Even one match requires two pointer slots. This is a
potential reconstructed-source buffer defect, not evidence of a retail crash.
Verify the retail ABI or original buffer intent before executing this path.
Do not mask this problem with a fabricated empty overlap result.

The fresh metadata audit of container `FF001FBE` resolves `WP_Active_Inter.gao`
(owner `10001E0C`, world `E6001FA7`, model `9E00152F`) to callback `9E00152E` in
slot 2. It establishes a real asset link; it does not prove the runtime query
result, owner scheduling, target activation, or retail behavioral parity.

## Required implementation evidence

Before connecting this family to JadeView, verify current BV update conventions,
SnP membership/list lifetime and filtering, bounded output storage, world update
order, and the effect of activation commands on scheduler tables. Tests must
cover touching bounds, null targets, any/all masks, excluded bits and changed
world membership. Preserve an explicit unresolved result when required runtime
state is missing; absence of evidence must not activate or deactivate an asset.
