# Iris instance-variable and callback initialization audit

This is a source audit of the local Iris engine tree, paired with bounded PC
BF resource summaries. It describes Iris behavior; it does not establish that
every Iris structure or behavior matches retail BGE 2003.

## Source loading and initialization order

Engine startup calls `AI_InitModule` after the memory/archive modules and before
world initialization (`external/irisEng/engine.c:817-834`). `AI_InitModule`
calls `AI_ResolveEnums`, which builds `AI_gaw_EnumLink` from the `funcid` fields
of the type, category, keyword, function, and field tables
(`ai_common.c:1459-1479`). The native type table has 23 entries in
`external/irisEng/ai/ai_types.c:40-64`.

The ordinary instance-loading path is:

1. `AI_ul_CallbackLoadInstance` reads the model key and optional instance-OVA
   key, and queues the model with LOA flag 1 and instance variables with flag
   9 (`ai_common.c:1387-1409`).
2. `AI_ul_CallbackLoadModel` counts `.ofc` entries, allocates model function
   slots, and queues `.ova` and `.ofc` resources (`ai_common.c:1325-1384`).
   `AI_ul_CallbackLoadVars` parses descriptors, names, compact default storage,
   five callback keys and optional instance-function keys
   (`ai_common.c:1124-1322`).
3. `LOA_Resolve` drains queued references in order, calls the resource loader
   on a cache miss, inserts the returned address unless flag bit 4 forbids it,
   and writes it through the saved destination pointer (`loading.c:336-377`).
   Missing resources assert when flag bit 1 is set; both flags 1 and 9 include
   that bit (`loading.c:349-373`).
4. `AI_ResolveInstanceRef` resolves the model first and then allocates/copies
   instance data (`ai_common.c:674-680`). `AI_pst_LoadInstance` demonstrates
   the full call order: queue instance, drain LOA, resolve, then reinitialize
   (`ai_common.c:1441-1457`). For an object-attached AI, `gameobject.c:2446-2456`
   calls the same resolve-then-reinitialize pair when the object's AI flag is
   set.

`AI_ResolveModelRef` resolves model function/node references, resolves model
OVA references and marks the model resolved (`ai_common.c:561-570`). In the
compiled compact-function case now tested by the OFC probe, `nodes` is null;
`AI_ResolveAllFctRef` skips node rewriting for that entry
(`ai_common.c:498-531`). This is a compiled-function pointer path, not evidence
that the callback itself has run.

## Variable types, references and sentinels

The serialized `AIVarElem.variableType` is a native type ID before resolution.
`AI_ResolveEnums` maps a type's source `funcid` to its table index. Then
`AI_ResolveVarRef(..., a6:1)` first tests references using the raw ID and only
after processing the values replaces the ID with `AI_gaw_EnumLink[rawId]`
(`ai_common.c:533-558`). The `a6:0` branch instead obtains the raw ID from an
already-resolved table index (`ai_common.c:547-548`).

| Serialized type ID | Type-table entry | Source treatment |
|---|---:|---|
| 33 (`0x21`) | 1 | Four-byte integer storage; it is not a pointer reference. Names such as `des_i_visibilite` may imply boolean-like use, but the table's type is integer. |
| 39 (`0x27`) | 5 | Reference; `AI_b_IsARef` includes it. |
| 40 (`0x28`) | 6 | GAO reference; included in `AI_b_IsARef`. |
| 42 (`0x2A`) | 8 | Reference; included in `AI_b_IsARef`. |
| 43 (`0x2B`) | 9 | Reference; included in `AI_b_IsARef`. |
| 45 (`0x2D`) | 11 | Key value; it is **not** included in `AI_b_IsARef`, so its stored key is not converted to a pointer. |

The mapping follows `ai_types.c:40-64`; the reference set and resolution
behavior are explicit in `ai_common.c:456-495`. For packed arrays,
`AI_ResolveVarRef` skips the high-bit dimension header, then resolves each
32-bit element in the low-30-bit element count (`ai_common.c:543-557`).

For a reference key, `AI_ul_RealResolveOneRef` returns zero for key zero. A
type-40 key of 1 or 2 is returned unchanged as a main-actor sentinel. Otherwise
it maps the key through `BIG_ul_SearchKeyToPos`; an absent key is returned as
the original integer, while a present archive key whose LOA address is not yet
cached returns the `-1` sentinel from `LOA_ul_SearchAddress`
(`ai_common.c:456-472`). Thus "resolved from a BF key" and "usable object
pointer" are separate states. Do not turn either a raw unresolved key or
`-1` into a GAO pointer in a preview.

The source later maps GAO sentinel 1 to `AI_MainActor[0]` and sentinel 2 to
`AI_MainActor[1]` when evaluating a GAO value (`ai_types.c:14-22`). Other
buffer-resolution functions also interpret numeric 1/2 as main actor slots
(`ai_common.c:1481-1505`). The mapping needs initialized actor slots; an
instance loader cannot invent those pointers. Type 45 is deliberately
different: it carries a key, not a resolved GAO reference.

## Defaults and instance-OVA overlay

`AI_InstanceAfterLoading` allocates current and initial buffers sized by the
model OVA. Without an instance OVA, it copies the model defaults directly to
the initial buffer. With an instance OVA, `AI_ResolveCarDecHiHiEditor` first
copies all model defaults, resolves instance-OVA references, then matches
instance variable names case-insensitively and overlays values onto model
defaults (`ai_common.c:572-628,647-672`). Afterward it frees the parsed
instance-OVA descriptor object and sets `ai->varsInstance` to null
(`ai_common.c:660-665`).

The call order prevents a second enum conversion of model descriptors:
`AI_ResolveInstanceRef` calls `AI_ResolveModelRef` before
`AI_InstanceAfterLoading` (`ai_common.c:674-680`). `AI_ResolveModelRef`
resolves the model OVA descriptors and marks the model resolved
(`ai_common.c:561-570`). Consequently, when the overlay reads a model
descriptor's type, `AI_ResolveCarDecHiHiEditor` takes the already-resolved
table-index path and does not apply `AI_gaw_EnumLink` again
(`ai_common.c:597-600`). The separate instance OVA still enters
`AI_ResolveVarRef(..., 1)` in raw-ID mode and is converted once there
(`ai_common.c:586,533-558`). A host that invokes the overlay must preserve this
ordering or track the descriptor-resolution state explicitly.

The source overlay rules are:

- Equal packed array-count words: copy dimension headers from the model
  defaults and copy the instance payload using the model type's element size
  (`ai_common.c:605-612`).
- Unequal packed counts: copy the minimum low-30-bit element count, using the
  model dimension-header offset. Uncopied elements remain the copied model
  defaults (`ai_common.c:613-621`).
- Variable names are compared case-insensitively; the source takes the first
  matching instance variable. It does not check that the instance and model
  variable types are equal before copying (`ai_common.c:588-611`).
- If an instance offset is greater than the instance storage size, the code
  resets that offset to zero rather than rejecting the descriptor. It does not
  validate `offset + full copied extent` at this point (`ai_common.c:601-604`).

These last two behaviors are source facts, not safe parser policy. A bounded
adapter must reject ambiguous duplicate names, type mismatches, invalid array
shape/layout and any source/destination extent that exceeds its buffer before
calling the original overlay function. It should not reproduce the offset-zero
fallback as if that were a sensible default.

## Reinitialization state

`AI_ReinitInstance` rejects null `ai` or model. For a non-universe instance it
resets callback bounds, copies the model's five callback pointers into the
instance, sets jump nodes to the function's node list (or `-1` for a compiled
function with null nodes), zeros per-slot wait times and frees the old
instance-AI callback list (`ai_common.c:375-420`). It then restores current
variable data from `instanceVarDataInitial`, or from model defaults when that
buffer is absent. A universe AI resets a variable only when descriptor flag
`0x20` is set (`ai_common.c:421-443`).

Important preconditions visible in this source:

- `ai->model->vars` must be nonnull. The callback loop dereferences
  `ai->model->vars->callBacks[i]` before a later conditional checks the vars
  pointer (`ai_common.c:385-398`).
- Every variable type used for the final copy must already be an index into the
  23-entry `AI_gast_Types` table; `AI_ReinitInstance` indexes it directly
  (`ai_common.c:433-440`). Raw serialized IDs such as 33/40/45 must have gone
  through enum resolution first.
- Copy extents and packed array dimensions must have been validated before the
  original function runs. The function trusts offsets/counts from the data.
- A callback pointer being copied into an instance slot does not mean the
  callback is scheduled or executed. This function only sets callback/jump
  state; world-table scheduling and callback dispatch are later subsystems.

## Bounded real-data init proof

The safest current fixture for executing `AI_ResolveInstanceRef` followed by
`AI_ReinitInstance` **without manufacturing actor pointers** is the documented
`FF006BFC` owner `8F0027AF` fixture:

- Its instance OVA `C100B3C4` has zero descriptors and zero storage bytes.
- Its shared model OVA `9E00152D` has six descriptors and 24 bytes of storage;
  its two type-40 GAO defaults are both zero. Remaining fields are integer
  type 33. The exact decoded summary is in
  `artifacts/export-verification/FF006BFC-8F0027AF-script-owner-fixture/manifest.json`.
- Its callback key `9E00152E` has the source registry row and compact OFC
  pointer identity already verified by
  `tools/NativeScriptBridge/test-ofc-registry-probe.ps1`.

The bounded proof is implemented in
`tools/NativeScriptBridge/instance_initialization_probe.c` and its host,
preflight, and adapter modules. Run it with
`tools/NativeScriptBridge/test-instance-initialization-probe.ps1`. The script
builds and runs the original Iris source path on the manifest's AIC/AIM/OVA/OFC
resources, then asserts that the model resolves, all five instance callback
slots match the model, slot 2 is the verified compiled-function pointer with
null nodes and jump sentinel `-1`, wait timers are zero, defaults are copied
into both buffers, the empty instance OVA is released, and no callback body is
invoked. The actual positive output is captured at
`artifacts/native-script-bridge/instance-initialization-probe/run-FF006BFC-8F0027AF.log`.

The positive run exercises the original Iris functions for loading the instance,
model, vars, and function resource; enum/reference resolution; instance overlay
and initialization; and reinitialization. A controlled LOA address-cache
adapter maps the exact fixture resources and drives the source queue; it is not
claimed to be the original LOA runtime. The host supplies the type table and
allocation/context setup required by these functions. The verified output
reports five queued source callback resolutions, defaults matching, one
resolved instance, and a source callback pointer that was not executed.

The regression script exits 0 after checking four bounded cases: positive
`FF006BFC` initialization, pre-dispatch rejection of the populated GAO
reference in `FF001FBE`, rejection of a manifest SHA-256 mismatch, and
pre-dispatch rejection of a correctly hashed but truncated OVA. The
`FF001FBE` negative log is
`artifacts/native-script-bridge/instance-initialization-probe/run-FF001FBE-10001E0C-negative.log`.
The hash and truncation cases are generated in the fixture directory and
removed by the test script.

By contrast, `FF001FBE` owner `10001E0C` has an instance-OVA GAO value
`E600201A` (`des_o_actor_to_activate`, raw type 40) in its fixture summary. Its
resource-only chain does not establish that a corresponding native
`tGameObjectMain` is loaded and registered in the LOA address cache. The
negative test verifies rejection before any source callback dispatch; it does
not fabricate or resolve that object pointer.
## What this does not establish

All source references above are local Iris source. Jaded is useful corroborating
engine code, but neither tree proves exact retail PC 2003 struct layout or
runtime behavior. The two BF fixture manifests prove only bounded serialized
resource contents. The `FF006BFC` proof establishes a small
source-executed initialization path with null GAO references, not general
world loading, actor resolution, state persistence, callback scheduling or
callback execution. Nonzero GAO references, save data, instance-OVA overlays
with populated variables, and broader OVA types still require separate source
and runtime validation.
Iris headers and local x86 compilation do not prove retail PC ABI parity.
