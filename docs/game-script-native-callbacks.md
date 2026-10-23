# Key-to-native callback path (Iris source model)

The source-resolved path is more specific than a name/catalog join:

1. `AI_ul_CallbackLoadModel` reads each typed `(key, extension)` row and makes
   `.ofc` keys references to `AI_ul_CallbackLoadFunction`
   (`external/irisEng/ai_common.c:1366-1381`). The model's `.ova` reference is
   loaded separately (`:1374-1377`).
2. `AI_ul_CallbackLoadVars` reads five ordered function keys and creates a
   function reference for each nonzero slot (`:1292-1297`). These are actual
   callback slots, distinct from the model's complete OFC function list.
3. When the OFC resource is resolved, `AI_ul_CallbackLoadFunction` obtains its
   archive key from `LOA_ul_GetCurrentKey`, searches `AI2C_gat_fctdefs`, and
   assigns the matched C function pointer (`:1050-1065`). The registry contains
   `350091C2 -> Cine_Events_cine_init`, `350091C8 ->
   Cine_Events_cine_tests_depart`, and `350091C9 ->
   Cine_Events_cine_play_eventtrack` (`external/irisEng/ai/ai_compiledscripts.c:832-834`).
   This is a direct resource-key-to-native-pointer mapping; the catalog's
   function name is not the dispatch mechanism.
4. `AI_ReinitInstance` copies model callback slots into the live AI instance,
   initializes the callback jump state and timers, and uses sentinel `-1` for a
   callback with a native pointer but no serialized node array
   (`external/irisEng/ai_common.c:375-413`). `AI_MainCall` filters EOT table
   entries and invokes `AI_RunInstance` for eligible owners
   (`:765-809`); `AI_RunInstance` calls the selected slot via
   `AI_pst_RunFunction` once its timer is due (`:711-762`).
5. `AI_pst_RunFunction` selects `funcPtr()` when `AI_CompiledAIEnabled` and a
   pointer are present; otherwise it interprets `nodes` (`:688-700`; the Iris
   source initializes `AI_CompiledAIEnabled=1` at `:13`). This explains why a
   compiled fast-path OFC can omit its node body without meaning “empty” in the
   Iris model. It does not prove that retail PC uses Iris's registry or mode.

The Cine_Events implementation demonstrates a real downstream chain rather
than just a mapped name. In Iris's global-manager first-init callback, the
compiled code resolves key `350091C2` and installs it with
`AI_EvalFunc_AITrackCurChangeNow_C` (`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c:4042-4047`).
The `Cine_Events_cine_init` native callback performs Cine setup and returns the
engine's continuation value `1000` (`:4061-5838`). Its play-event callback starts
all animation tracks and installs the play callback in the current slot
(`:5891-5898`). `Cine_Events_cine_play_eventtrack` advances cine time, executes
the end-of-events and soundtrack-fade callbacks, and sends camera/player
messages on its completion branches (`:5901-6015`). This establishes how Iris
composes native callbacks, EVE playback, nested function calls, and messages;
it does not establish that a given retail archive owner reached those branches.

For JadeView, the corresponding PC evidence currently stops earlier: a scoped
kind-`s` model references kind-`t` OVA and kind-`r` OFC records; the OVA reader
retains ordered callback keys; the compact reader retains stack/node-size
metadata and opaque local-constant bytes. JadeView does not possess Iris's
`AI2C_gat_fctdefs` native pointer table for the retail executable, nor the
omitted compiled C bodies. The embedded name/source catalogs can identify
candidate functions and source-call paths, but they cannot be treated as a
retail callback registry or proof of execution. The generic next step is to
surface the full scoped chain `owner -> AI instance -> model -> OVA slot ->
OFC key -> registry/source match` as evidence links, while keeping missing
native mapping and runtime invocation explicitly unknown; executing a callback
would require a separately validated implementation, not the source name.

