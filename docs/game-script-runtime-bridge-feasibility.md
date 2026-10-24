# Native script-runtime bridge feasibility

## Finding

There is a source-supported route to execute the existing Iris BGE callback implementations without porting thousands of script bodies into C#: Iris loads a function resource, looks up its key in the compiled callback table, and `AI_pst_RunFunction` calls the registered native function pointer when compiled AI is enabled (`external/irisEng/ai_common.c:1023-1084, 688-700`; `AI_CompiledAIEnabled` defaults to 1 at `:13`). This is more promising than reimplementing every callback in JadeView.

It is **not** currently a drop-in headless library or a safe in-process JadeView plugin. The project is an application with no headless target (`external/irisEng/irisEng.vcxproj`, `external/irisEng/main.c:12-35`); callback code relies on global engine state, object/world structures, asset loaders, and many engine functions. The C code also stores pointers in 32-bit `int`/`uint32_t` locations, for example `AI2C_ResolveBuffer` writes a current variable address to a `uint32_t` (`external/irisEng/ai_common.c:1481-1504`), while `tAIFunction.funcPtr` has an `int` return signature (`external/irisEng/ai.h`, `AI_pst_RunFunction` at `ai_common.c:688-700`). Treat Iris as an x86 runtime. The current README publishes JadeView as `win-x64` (`README.md:46-54`), so the viable boundary is a separate x86 helper process with a versioned pipe/JSON protocol, not a DLL loaded into the x64 app.

## Runtime path proven by source

The intended minimal native slice is an actual Iris world load followed by its AI setup and one deterministic update tick:

1. `WOR_pst_World_Load` queues and resolves the `.wow` and object resources, calls `WOR_World_JustAfterLoadObject` for each object, and can request AI resolution (`external/irisEng/world.c:1208-1280,1283-1372`). `WOR_World_JustAfterLoadObject` reinitializes each object, resolves animation/collision references, attaches it, and conditionally calls `OBJ_ResolveAIRef` (`world.c:1060-1091`). `OBJ_ResolveAIRef` resolves and reinitializes an attached AI instance (`external/irisEng/gameobject.c:2430-2457`).
2. `WOR_pst_Universe_AddWorld` calls `ENG_InitOneWorld`, activates objects, and builds the EOT scheduler tables when `resolveAI` is requested (`world.c:2243-2259`). `AI_ReinitInstance` copies the initial variable state and prepares five callback slots/jump positions (`external/irisEng/ai_common.c:375-444`).
3. The source update order is EVE → ANI → AI → hierarchy → dynamics → collision, inside `ENG_OneWorldEngineCall` (`external/irisEng/engine.c:126-146,180-243,250+`). `AI_MainCall` walks the world EOT AI callback table and runs eligible owners (`external/irisEng/ai_common.c:765-810`). The tick should reuse this ordering rather than call AI in isolation if JadeView expects cinematic transforms and callbacks to agree.

This source path is not headless yet. `WOR_pst_Universe_AddWorld` invokes `ENG_InitOneWorld`; the full load tail invokes display attachment and Direct3D object setup (`world.c:2248-2259,2849-2865`). `ENG_InitApplication` initializes GDI, dynamics, text, collision and world modules (`external/irisEng/engine.c:809-835`), and the shipped loop initializes sound/input and repeatedly runs full frames (`engine.c:779-807`). A bridge must factor or bypass the rendering/display-only tail without bypassing world attachment, AI resolution, EOT construction, and update ordering.

## Build and host constraints checked here

The full Iris application was not built. Visual Studio 2022 Build Tools are installed, including an MSVC x86 compiler (`VC/Tools/MSVC/14.44.35207/bin/Hostx64/x86/cl.exe`) and a bundled CMake executable; they are not on this shell's `PATH`. The legacy Iris Win32 project explicitly expects `C:\BinkSDK` and `DXSDK_DIR` include/lib paths, linking `binkw32.lib`, `d3d9.lib`, `d3dx9.lib`, `dinput8.lib`, `dxguid.lib`, and Windows multimedia (`external/irisEng/irisEng.vcxproj:76-101,134`). This host has no `C:\BinkSDK`, no `DXSDK_DIR`, and no installed June 2010 DirectX SDK at the project’s referenced path. Thus its checked-in full app project is not build-ready in this environment as configured. Bounded source-callback and resource-reference probes now compile and run; they do not close the full-engine dependency gap. Iris also lists x64 project configurations, but that does not remove the 32-bit pointer-storage hazards above.

Jaded is not a suitable executable substitute: its README says Windows x86 only; its top-level CMake config adds the full library/application graph, and `src/main/CMakeLists.txt` creates the GUI `Jaded` executable linked against engine, editor, AI, graphics, sound, OpenGL and SDL (`external/jaded/readme.md`, `external/jaded/CMakeLists.txt`, `external/jaded/src/CMakeLists.txt`, `external/jaded/src/main/CMakeLists.txt:3,481-501`). It is valuable corroborating source, but its codebase is a later 2006 community fork, not a retail BGE runtime or headless target.

The external-source inventory records no root license for Iris (`external/README.md`). Before distributing a helper containing Iris code or binaries, resolve reuse rights. Until then, keep the bridge as a private, optional local experiment; do not vendor Iris implementation code into JadeView.

## Concrete vertical slice

The eventual useful proof remains a **separate x86 `IrisScriptBridge.exe`** and a synthetic single-world fixture, with no GUI, game launch, video, sound, or renderer. It should load one small BF/world through the Iris loader, keep a single owner with its real AI model/vars/function resource, resolve the callback, run `AI_ReinitInstance`, advance one tick through the same script-relevant order (EVE, ANI, `AI_MainCall`, hierarchy), and return only a state snapshot (owner key, callback slots/run counters, transforms, visibility/activity, and emitted/changed object state). The bridge should report unsupported resource types or callback dependencies explicitly instead of silently stubbing them as success.

Implementation seam: make a bridge-only host around the existing Iris loader and `ENG_OneWorldEngineCall` stages, with a compile-time no-render/no-device adapter. Initially support only the resource types needed by the fixture; the fixture must include a compiled callback whose function key maps through `AI2C_gat_fctdefs` and whose reachable engine calls are implemented by the loaded modules. Keep JadeView as a client that sends a world/owner/time request and consumes state snapshots. Do not put 32-bit pointers in the wire protocol.

Before expanding to real cinematics, prove one local trace end-to-end and enumerate every native callback's unresolved imports. The current source does not prove that the full Iris app can be stripped to this subset without additional platform shims, nor that PC 2003 archive entry points and Iris binary layouts match for every resource. If the first fixture requires most of the renderer/engine to link, that is a measured blocker for this strategy; do not respond by building the entire app or by adding hundreds of one-off stubs.

## Recommendation and limits

Proceed with a bounded x86 out-of-process feasibility prototype, not a JadeView in-process integration and not a broad callback rewrite. The first loader seam is now measured: the exact Iris AIC/AIM callbacks can parse two manifest-verified owner resources and queue the expected OVA/OFC references with their real destinations and LOA flags. Next, measure and add the bounded Vars/Function resource readers, with complete provenance and fail-fast unresolved dependencies. Only after that should the probe attempt callback resolution and `AI_ReinitInstance`; world activation, EOT scheduling, and ticks remain later checkpoints. This is still a prototype until dependency closure, PC 2003 resource compatibility, licensing, and deterministic parity against game captures are established. If those checks fail, continue implementing only the reusable subset of callback semantics supported by current JadeView state.

## Native callback command-capture proof

`Activateur_avec_SF_Acs_init` and `Activateur_avec_SF_Acs_loop` from `external/irisEng/ai/ai_bge/ai_compiledscripts_activateur_avec_sf.c` are compiled unchanged as x86 and linked into a private harness under `tools/NativeScriptBridge/`. The harness uses Iris declarations/layouts and supplies a bounded synthetic context: one owner, one target actor, a global saved-flag bitset, a clock, one callback slot, and a resolver for loop-function key `0x3500A7A4`. The build and test output live under ignored `artifacts/native-script-bridge/`.

The callback object report `artifacts/native-script-probe/activateur-symbols.txt` lists seven unresolved function imports for these two callback functions: `AI_ul_RealResolveOneRefFunction`, `AI2C_CheckActorGao`, `AI_EvalFunc_AITrackCurChangeNow_C`, `AI_EvalFunc_TIMEElapsed_C`, `AI_EvalFunc_OBJ_FlagInactiveSet_C`, `AI_EvalFunc_OBJ_FlagsControlSet_C`, and `AI_EvalFunc_ARRGlobalIntFlagTest_C`. The harness supplies only these named fixture adapters; an additional unresolved function import fails the link. The object also contains references to shared data symbols, which is not evidence those systems were implemented or executed here.

Four synthetic cases pass: saved flag matches with a positive clear mask; saved flag matches with a negative set mask and null-actor fallback; saved flag mismatch; and selecting the already-current loop callback. Assertions and JSON-lines output verify init result `5`, wait/elapsed and same-track result `1000`, timestamp reset, source actor-reference resolution, callback-slot replacement, and `FlagsControlSet`/`FlagInactiveSet` command intents. The track adapter follows the relevant Iris slot/jump-sentinel behavior (`external/irisEng/ai/ai_aifunctions.c:252-267`); time and flag tests follow the source formulas (`ai/ai_timefunctions.c:38-41`, `ai/ai_arrfunctions.c:318-322,604-607`).

This proves one real compiled callback body can run in a small x86 process without building Iris' GUI/renderer or manually porting this callback. It does **not** call Iris' native world-mutating implementations: object APIs are recorded as intents and are not applied to a world or EOT table. The key resolver recognizes only the fixture's loop key; there is no BF/OFC/OVA loader, owner activation, scheduler, EVE/ANI/world tick, other callback family, or comparison against retail BGE. Jaded headers suffice for this isolated compile, but they do not prove the complete runtime ABI or eliminate the full Iris project's SDK/library dependencies. The next feasibility step is to measure a second callback family's dependency closure and the native loader/EOT boundary before expanding the harness toward world loading.

## Native owner-to-resource reference probe

`tools/NativeScriptBridge/extract-resource-loader-slice.ps1` extracts the exact byte spans of Iris `AI_ul_CallbackLoadInstance` and `AI_ul_CallbackLoadModel` (`external/irisEng/ai_common.c:1325-1409`) and their real loader primitives `LOA_ReadLong`, `LOA_ReadCharArray`, and `LOA_MakeFileRef` (`external/irisEng/loading.c:52-71,111-123,257-278`). The generated private source is under ignored `artifacts/native-script-bridge/resource-loader-probe/`; its manifest records each source hash, encoding/EOL, source byte span, generated byte span, and span hash. The generated function bodies are written as the original source bytes, not a Unicode re-encoding. The extractor self-checks brace-like characters in comments/string/char literals and rejects missing or duplicate target definitions.

`build-resource-loader-probe.ps1` reads only resources named by the fixture manifest's resolved links, checks their byte length and SHA-256, checks AIC is exactly 8 bytes and AIM length is a bounded multiple of 8, then passes the AIC/AIM bytes to the original Iris resource callbacks in a private x86 host. The native harness verifies the four references actually queued by those source functions: model and instance-variable keys from AIC, then `.ova` and `.ofc` keys from the AIM table. It also verifies the destination pointer identity and LOA flags on each queue entry (instance refs use flags 1 and 9; model-entry refs use flag 1). It uses fail-fast functions only for `AI_ul_CallbackLoadVars` and `AI_ul_CallbackLoadFunction`; these addresses are captured in queued entries and are never called.

Both real evidence fixtures pass: `FF001FBE` owner `10001E0C` and `FF006BFC` owner `8F0027AF`. The first queues instance refs `9E00152F`/`10001E0D`, then model entries `9E00152D` (`.ova`) and `9E00152E` (`.ofc`); the second has its own instance variable key but resolves the same model resource and entries. See `artifacts/native-script-bridge/resource-loader-probe/run-FF001FBE-10001E0C.log` and `run-FF006BFC-8F0027AF.log`. `test-resource-loader-probe.ps1` runs both and also verifies that a hash mismatch is rejected and a truncated-but-rehashed AIC is rejected by the bounded length contract.

This is a native **resource-reference queue probe**, not a complete BF loader: `.ova` and `.ofc` are verified as manifest evidence, but their contents are not loaded or interpreted by this host; no callbacks are resolved or executed, and no LOA queue is drained. It does not construct an owner/world, EOT tables, or runtime state. Exact Iris bytes and local 32-bit headers establish this bounded source behavior only; they do not prove retail BGE 2003 ABI parity. The measured full-TU attempt remains too broad, while this extracted narrow slice is viable without success-stubs for missing engine APIs. The next useful seam is to add the real variable/function resource readers only if their bounded dependencies can be linked or extracted with the same byte-provenance and fail-fast policy; do not jump directly to AI execution.

## Native OVA storage proof

The source-slice probe now also executes Iris `AI_ul_CallbackLoadVars` on the
model and instance OVA resources of both fixtures. The supporting scalar,
string and vector readers are extracted without modifying their source bytes.
The original `ai/ai_types.c` type table is compiled directly; type-enum links
come from its entries using the type-loop mapping in `AI_ResolveEnums`.
Unrecognized types are rejected before entering the unbounded source reader.

The host uses the compact speed/read-mode-2 path. Its mode-2 reads of the two
unknown-block size words return zero without advancing the input: those words
are absent from the compact payload. The noncompact allocator path fails fast
and has not been implemented. Preflight validates descriptor/name/storage and
tail bounds and rejects unsupported layouts before native parsing.

Computed descriptors, names, offsets, packed counts, descriptor flags and exact
native `varData` bytes match the independently decoded fixture summaries. In
`FF001FBE`, instance storage has five descriptors/20 bytes, and model storage
has six descriptors/24 bytes. In `FF006BFC`, the instance OVA has zero descriptors
and zero storage bytes; its model matches the first fixture. These examples use
integer/object-reference fields; they do not prove every OVA type or array form.

The model OVA queues function key `9E00152E` to its callback slot 2 with flag 1.
This verifies the destination address and queued key, not a resolved callable
pointer: the host does not run `LOA_Resolve` or load OFC. It also does not perform
instance default matching/initialization, actor-reference fixups, EOT scheduling
or callback execution. The following bounded OFC stage now verifies metadata
and registry pointer identity; independently checked instance initialization
remains the next loader seam.

The combined regression passes for both real fixtures and rejects hash mismatch,
truncated AIC, truncated OVA and unknown OVA types. Separate extraction tests check original and
generated byte hashes and UTF-8/Windows-1252 handling. The shared layout contract
also constrains vector/message/text-reference host sizes to 12/52/8 bytes.

## Native compact OFC metadata and callback identity probe

`build-ofc-registry-probe.ps1` adds the next bounded stage. It reads only the
manifest-resolved model `.ofc`, checks its exact size and SHA-256, extracts the
exact Iris `AI_ul_CallbackLoadFunction` and required `loading.c` readers with
source-byte spans/hashes, and selects a single active `AI2C_gat_fctdefs` row
from `ai_compiledscripts.c` using that manifest's function key. For the two
current owners, the resource is key `9E00152E`, the source row maps it to
`activateur_act_ini` at `ai_compiledscripts.c:1981`, and the compact resource is
12 bytes. The native loader reports stack count 28, declared node bytes 504,
null node storage, null local-string storage, and the exact function pointer
from that source row.

This verifies the important compiled-fast-path distinction in
`ai_common.c:1071-1084`: with a registered `funcPtr`, speed mode enabled and
read mode 2, Iris skips the node-body loop entirely. It does not issue 63
`LOA_ReadAINode` calls. `LOA_ReadLong` mode 2 returns zero without advancing
the pointer (`loading.c:52-71`), so the extra-node-size word is absent in this
compact payload; the next stored word is the local-string-data length, zero in
this fixture. The loader's `usageCount` starts at 1. A second request returns
the same pointer and increments it to 2 through a deliberately controlled
one-entry address-cache adapter; the probe does not implement or claim the
native BAS address cache.

The harness also calls the exact source `LOA_MakeFileRef` for keys 0 and
`0xFFFFFFFF` and verifies that each clears the destination immediately while
leaving the queue cursor unchanged (`loading.c:257-277`). The selected compiled
callback body is compiled and linked solely to compare its function address
with the loader's `funcPtr`. It is never invoked; its four unresolved object,
collision and flag APIs are fail-fast adapters. The skipped `LOA_ReadAINode`,
aligned node allocation, and editor-local reader are also fail-fast so an
unexpected loader branch terminates rather than reading past this compact
resource. Unknown registry keys and
resource hash mismatches are rejected before the native OFC loader is called,
so the compact 12-byte resource cannot fall through to an interpreted-node
path that would try to consume 504 bytes.

`test-ofc-registry-probe.ps1` passes for `FF001FBE/10001E0C` and
`FF006BFC/8F0027AF`, and rejects the unknown-key and bad-hash cases. Logs and
generated exact-source slices are under ignored
`artifacts/native-script-bridge/ofc-registry-probe/`. This is a metadata and
callback-pointer identity proof only: the one-row registry slice is not the
full table (the current source definition and header both declare an active
extent of 2,530), the cache is controlled, no callback or world state is
executed, and none of this establishes retail BGE 2003 runtime ABI parity.

The extent is derived and checked against the header rather than fixed in the
probe. Manifest `Registry.ExtentContract` records the definition/declaration
source spans and hashes. `Registry.RowByteOffset`, `RowByteCount` and
`RowSha256` preserve the selected row's original encoded byte span, including
Windows-1252 source bytes; no UTF-8 re-encoding is used for that row hash.
