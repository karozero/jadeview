# Static COB wire-format audit

## Scope and evidence

This is a source-level format audit for the native collision-object (`.cob`) leaf records referenced by a PC `COL map`. It defines a bounded decode boundary; it does not implement collision, raycasts, character grounding, or gameplay parity.

The real PC archive evidence already identifies Sally's collision instance `71004F89`, map `7100B463`, and the map's ordered child COB keys `7100B462` and `7100E7CF`. The map record is kind `f`, at offset `182353`, size `12`, and its bytes are `02 FF 00 00 62 B4 00 71 CF E7 00 71` in `artifacts/collision-native-inputs/FF005520-JadedPc-BinaryCompact-collision-resources.json`. The instance is kind `d`, key `71004F89`, offset `182230`, size `119`. That artifact currently resolves the map and stops short of decoding either COB payload. No claim about the two COBs' actual types, vertex counts, flags, material keys, or optional tails is made here.

## Jaded PC binary layout confirmed by load and save paths

`COL_ul_CallBackLoadCob` reads the common header as a 32-bit game-material key followed by one-byte type and flags (`external/jaded/src/libraries/ENGine/Sources/COLlision/COLload.c:236–284`). Supported type constants are box `1`, sphere `2`, cylinder `3`, and triangles `5`; the collision-flag constants are in `COLconst.h:57–69`. For triangle COBs, `COLload.c:331–394` reads a 32-bit point count and that many 3-float vectors; a 32-bit face-normal count and that many 3-float vectors; a 32-bit element count; then each element as `uint16 triangleCount, byte design, byte elementFlags, int32 materialId`, followed by `triangleCount` triples of `uint16` vertex indices. The wire fields are read individually; in-memory structure sizes/padding are not serialized evidence.

`COL_SaveCob` independently writes the same sequence (`external/jaded/src/libraries/ENGine/Sources/COLlision/COLsave.c:267–283,306–332`). Its save path confirms index triples are six contiguous bytes each. The COB header's game-material key is a resource reference, while the per-element `materialId` is a distinct signed 32-bit slot/index; do not conflate them. Source names the latter `l_MaterialId`, but a parser should preserve the raw bits/value until the game-material table semantics are joined.

The primitive formats are also source-confirmed for completeness: box is `int32 materialId + max Vec3 + min Vec3`; sphere is `int32 materialId + center Vec3 + float radius`; cylinder is `int32 materialId + center Vec3 + float radius + float height` (`COLload.c:287–329`). These shapes are not substitutes for parsing type-5 triangle COBs.

## Optional binary tail: flags are part of the wire contract

The triangle payload may continue after the indexed elements. Jaded sets `bin` to `LOA_IsBinaryData()` in non-editor builds; editor builds also accept `LOA_IsBinarizing()` (`COLload.c:396–400`). Retail PC release data therefore takes the binary-data branch. With binary mode active:

| COB header flag | Serialized data after base triangle elements | Evidence |
| --- | --- | --- |
| `0x80` Updated | For every element and every triangle, three `uint16` proximity values | Loader `COLload.c:402–419`; writer emits this block when `EDI_gb_ComputeMap` is set, `COLsave.c:277–280,334–346` |
| `0x08` OK3 together with `0x80` Updated | `uint32 boxCount`; per box `uint32 elementCount + max Vec3 + min Vec3`; per box-element `uint16 elementIndex + uint16 triangleCount + triangleCount * uint16 triangleIndex`; then recursive hierarchy words | Loader `COLload.c:421–469`; writer `COLsave.c:348–377`; flags `COLconst.h:62–69` |

Thus `0x08` alone does not make the loader consume an OK3 tail: both bits and binary mode are required. `0x80` alone consumes the proximity block. The runtime loader clears `0x80` after loading (`COLload.c:473`), so inspect the serialized header, not the post-load runtime flag, when deciding the on-disk tail shape.

The OK3 hierarchy is a recursive sequence, not a fixed-size array: `COL_Load_Nodes_Recursively` reads a box/index word followed by tagged child/sibling branches (`COLload.c:203–233`); writer tags are `1` for son, `2` for next, and `0` for end (`COLsave.c:145–175`). A first parser should either decode this with explicit recursion/node/byte limits or preserve the whole OK3 tail as opaque bytes while still exposing the validated base mesh and proximity arrays. Silently treating an unparsed optional tail as trailing garbage would reject otherwise-valid records.

Editor-only fields are a different concern from these optional collision tails. `LOA_Read*` with `LOA_eBinEditorData` does not advance the cursor for binary input (`external/jaded/src/libraries/SDK/Sources/BIGfiles/LOAding/LOAread.c:480–506` for long and `:570–600` for char; equivalent short reader is nearby). The `_Ed` macros select that flag (`LOAread.h:213–257`). The inspected COB load path uses ordinary engine-data reads throughout its COB payload; no editor-only `_Ed` read was found in the COB loader. Do not insert or skip speculative name/comment bytes inside COB records.

## Iris comparison and retail boundary

Iris's `COL_ul_CallBackLoadCob` is a useful structural corroboration: it reads the same 32-bit material key, type/flags, triangle point and normal arrays, element headers, and three 16-bit indices per triangle (`external/irisEng/collision.c:10395–10554`). It gates proximity and OK3 reads using `flags` plus a `v39` predicate requiring `LOA_gb_SpeedMode && BIG_gi_ReadMode == 2` (`:10555–10586`), whereas Jaded's PC loader uses `LOA_IsBinaryData()` for its non-editor binary branch. This is a meaningful engine/version/loading-mode difference. Use the Jaded PC load/save path to parse retail PC BF data; Iris does not prove which optional bytes a particular retail COB contains.

Iris's in-memory `tCobMeshTriangleElem` contains three indices and three proximity IDs (`external/irisEng/collision.h:102–110`), but that struct size is not wire evidence. The decompiled reader proves the six-byte base index triplet and conditionally reads proximity separately; never use the 12-byte runtime triangle struct as an on-disk stride.

## JadeView integration caveat found during this audit

The wire/resource dependency chain in current source is: a GAO's collision-instance key is enqueued as kind `d`, and its map key as kind `f` (`src/Features/SceneEditor/Formats/Pc/PcSceneLoader.Objects.cs:59–70`); kind `d` points to kind `e` (`PcSceneLoader.Resources.cs:24–26`); kind `f` lists kind `g` COB keys (`PcSceneLoader.ResourceLists.cs:24–34`); and kind `g` may reference kind `h` from the header's game-material key (`PcSceneLoader.Resources.cs:36–38`). This matches the archive artifact's Sally records: `71004F89` is kind `d`; `7100B463` is kind `f` and lists `7100B462` and `7100E7CF`.

Review caught an inverted instance/map kind in the initial catalog implementation. The builder now requests instance `d`, ColSet `e`, and map `f`, matching the loader and archive records; synthetic scoped-resource checks cover this dispatch. A future COB reader must use kind `g` and read the game-material key at bytes 0–3 as a reference to kind `h` without shifting the type/flag bytes.

## Bounded reader proposal

The next safe implementation is a passive `PcCobReader` over the exact archive record span `(offset,size)`, selected explicitly as Jaded PC binary format. Preserve source key, type, raw flags, material key, offsets, and raw per-element material values. For type 5, decode base arrays with checked count arithmetic and conservative count caps; validate every vertex index against point count and normals against the source face count policy only after actual samples establish whether those counts are expected equal. Apply the `0x80` and `(0x88 == 0x88)` binary-tail predicates exactly, with bounded nested counts and recursion/byte budgets for OK3. Keep opaque tail bytes and a `Partial/Unsupported` diagnostic when OK3 hierarchy cannot be decoded; never allow malformed tail data to invalidate the scene load.

For Sally keys `7100B462` and `7100E7CF`, the first fixture pass should report: resource kind/world/offset/size; first six header bytes; point/normal/element counts; per-element triangle counts and material IDs; min/max of indices; base-array end offset; flag-selected optional-section boundaries; final cursor and remaining bytes. Only after both source spans pass those checks should a triangle preview or static collision query be considered. A static triangle preview can then reuse the stored COB local-space points plus the owning GAO/world transform; gameplay contact, moving COL maps, filters, zone activation, and grounded movement remain separate systems.

## Explicit unknowns

- Actual COB payload bytes and header flags for Sally's two COB keys have not been captured by the current collision artifact, so their optional-tail layout is not yet fixture-verified.
- It is not established whether `faceCount` equals the sum of element triangle counts in every PC COB, or how `face normals` are mapped to element triangles. Preserve both arrays/counts without deducing correspondence.
- `COL_SaveCob` serializes native fields with raw `SAV_Buffer`; retail endianness/platform swapping must be verified against the BF archive's platform and existing PC readers before hardcoding byte order beyond the already-used little-endian PC record conventions.
- This static source does not determine runtime zone/map activation, transformed collision response, AI/proximity meaning, actor movement grounding, or collision material behavior in each game state.

## Source locations

- Jaded PC COB load: `external/jaded/src/libraries/ENGine/Sources/COLlision/COLload.c:236–473`.
- Jaded PC ColMap-to-COB references: `COLload.c:501–549`.
- Jaded PC recursive OK3 tree: `COLload.c:203–233`.
- Jaded editor COB save/tail emission: `external/jaded/src/libraries/ENGine/Sources/COLlision/COLsave.c:145–175,179–383`.
- Collision flags: `external/jaded/src/libraries/ENGine/Sources/COLlision/COLconst.h:57–69`.
- Binary/editor read behavior: `external/jaded/src/libraries/SDK/Sources/BIGfiles/LOAding/LOAread.c:480–506,570–600`; `_Ed` macros in `LOAread.h:213–257`.
- Iris divergent COB loader: `external/irisEng/collision.c:10395–10635`; runtime triangle structs: `external/irisEng/collision.h:102–110`.
- Current PC collision model/readers (no COB decoder yet): `src/Features/SceneEditor/Models/PcCollisionResources.cs`, `src/Features/SceneEditor/Formats/Pc/PcCollisionResourceReader.cs`.
- Sally references and archived map payload: `artifacts/collision-native-inputs/FF005520-JadedPc-BinaryCompact-collision-resources.json`.
