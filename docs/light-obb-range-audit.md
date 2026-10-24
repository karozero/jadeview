# Static OBB light-range checks

Iris uses the same receiver range test for AABBs and OBBoxes. In `external/irisEng/lights.c`, `LIGHT_l_IsNodeLighted` (around lines 515–551) subtracts the receiver origin from the light position, then passes `boundingVolume->BVMin/BVMax` to `INT_SphereAABBox`. It does not branch on the OBBox identity flag or use `BVMinLocal/BVMaxLocal` in this test.

The PC loader reads an OBBox record as outer `BVMinOriginal/BVMaxOriginal` followed by local corner vectors (`external/irisEng/gameobject.c`, around lines 2890–2921). The outer pair is copied to the live `BVMin/BVMax`. `OBJ_ComputeAABBoxFromOBBox` (around lines 335–370) can recompute that pair from the local corners and the current object orientation; `OBJ_ComputeAnimBV` invokes it for eligible oriented children before merging their bounds. Static geometric bounds likewise use an outer AABB in `OBJ_ComputeGeometricObjectBV`.

JadeView can therefore apply the native sphere-versus-outer-AABB predicate to a static serialized OBBox. Its local corners are retained as metadata and are deliberately not substituted into the test. Sphere bounds remain unsupported because the verified gate calls the AABB predicate directly. Receivers known to have pose-dependent runtime bounds remain unresolved rather than using a stale serialized box.

There is one remaining boundary: source callbacks can move or rotate a nominally static object, and the current preview does not prove whether every such callback recomputes its bounds. The implementation only enables this test when the receiver context does not mark its bounds runtime-dynamic; it does not claim to emulate arbitrary script-driven bound updates.
