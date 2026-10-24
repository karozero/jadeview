# Script-driven light state audit

This note records what the checked-in Iris source establishes about light state and how JadeView can use its existing literal cinematic-call pipeline without pretending to execute the game VM. The source references below point into the vendored `external/irisEng` checkout.

## What the engine functions change

Iris registers the light selectors in `ai_functions.c:381-397`; JadeView’s selector-name table maps the same IDs in `src/Features/Cinematics/Data/engine-function-selectors.json:354-370`.

| Selector | Operation | Source-backed behavior |
| --- | --- | --- |
| `0x2135` | `LightSetColor` | Writes the packed color to the target light, if the target has a light component. (`ai_lightfunctions.c:32-55`) |
| `0x2136` | `LightSetNearFar` | Writes nonnegative near/far values for light type codes 0, 2, and 3. It leaves type 1 unchanged. (`ai_lightfunctions.c:57-108`) |
| `0x2139` | `LightSetGO` | Sets the light’s related `lightGao` object reference. This reference filters which base objects the light affects; it is not an AI callback owner. (`ai_lightfunctions.c:186-208`; `lights.c:515-532`) |
| `0x213B` | `LightSetFlag` | Sets or clears `1 << bitIndex` on the light’s raw flags. (`ai_lightfunctions.c:211-243`) |
| `0x213E` / `0x2140` | RLI factor / destination color | Mutate device-wide rendering state rather than one `SceneLight`. (`ai_lightfunctions.c:296-378`) |
| `0x2142` | `LightFogState` | Toggles a device draw-mask bit; this is global renderer state, not a light component field. (`ai_lightfunctions.c:380-407`) |
| `0x2145` | attenuation factor | Changes either a target visual object’s attenuation fields or global draw-mask/factor state when the target is null. (`ai_lightfunctions.c:444-535`) |

All object light setters operate on Iris’s current execution object (`CurrentUltra`) and require the object’s light capability flag (`object flags & 0x10`) plus an existing `extended->light`; a syntactically valid call can therefore be a runtime no-op. The wrapper saves the previous `CurrentUltra` before switching to the current game object, then passes the saved object to the setter (`ai_lightfunctions.c:44-55,99-110,198-208,231-243`). For EVE AI-function events with no bone target, Iris sets both current-object registers from the track GAO, or falls back to the EVE owner if the track has no GAO (`events.c:185-196`). This provides a source-backed receiver rule for that specific event form.

Bit 3 of the light flags is the renderer submission switch, not a generic guessed “visible” state: the world render loop considers only lights with `flags & 8`; type 3 is submitted as fog, while other types enter the dynamic-light list subject to sectorization/culling flags (`world.c:2583-2612`). `LightGetFog` uses the same bit and type test (`ai_lightfunctions.c:409-430`). The low three flag bits are the light type (`graphicsobjects/light.h` as used by `ai_lightfunctions.c:69-81`). The PC light payload itself stores raw flags, packed color, near/far, density, `i8`, and the related GAO key (`graphicsobjects/light.c:22-38`; JadeView’s fixed-size parser is `src/Features/SceneEditor/Formats/Pc/PcLightReader.cs`). Keep unknown flag bits intact.

## Script callbacks that explain changing light state

The source contains at least two distinct mechanisms. They should not be conflated with literal calls authored directly on a cinematic event track.

* **Per-light effect callback (`Light_PAF`)**: functions are indexed as `0x9E005BD8` init, `0x9E005BD9` far-up/far-down, `0x9E006103` light-mode far-down, and `0x9E006105` far-up/color-down (`ai_compiledscripts.c:606-609`). Init reads per-instance script variables, optionally switches the AI action to a selected transition function, sets near/far and color, makes the object visible, then switches to the far-down behavior (`ai_compiledscripts_lights.c:22-65`). The transition callbacks interpolate from instance variables using frame delta; the fade-out paths can set the object invisible and destroy it (`ai_compiledscripts_lights.c:68-188`). A cinematic can therefore appear to “activate” a light by activating its scripted owner/callback, even if no `LightSetFlag` literal is present in the visible cinematic track.
* **World-cycle controller (`world_cycle`)**: the compiled function map identifies `world_cycle_world_cycle` (`0x5E000004`) and `world_cycle_world_planetes` (`0x5E00002C`) (`ai_compiledscripts.c:2331-2332`). `world_cycle_world_init` transitions its current AI action to `world_cycle_world_cycle` (`ai_compiledscripts_global_mapmanager.c:5868,6450-6455`). The cycle callback derives state from script instance variables, time, sun/moon positions and attack/fade coefficients; it toggles bit 3 on sun/moon lights and updates fog near/far/color, RLI destination color, sky/environment material constants, and water color (`ai_compiledscripts_global_mapmanager.c:7029-7160,7323-7535`). This is a recurring world/environment callback, not a cinematic-only command. The source proves the implementation exists, but a scene archive by itself does not prove that this callback was active at a given cinematic playhead.

The `LightSetGO` “related GAO” is also different from the script owner: Iris uses it while testing whether a light applies to a base GAO (`lights.c:515-532`). The light component’s serialized GAO reference is read during light construction (`graphicsobjects/light.c:22-38`), so static scene data can establish its initial association even if no script call changes it.

## What a literal evaluator can safely implement

JadeView already decodes typed EVE payloads into `(time, CinematicLiteralCall)` records. `CinematicLiteralCall.Decode` retains an explicit/inherited target, a selector, and immediate 32-bit operands; it rejects payloads containing unsupported expression rows (`src/Features/Cinematics/Models/CinematicLiteralCall.cs:31-83`). The decoder is a static call reader, not the AI VM (`docs/script-system-reverse-audit.md:115-130`). Build a light-state reducer on top of that reader with the following narrow contract:

1. Start each target from its parsed `SceneLight` baseline and preserve all raw/unrecognized bits.
2. At each supported literal command time, resolve the command receiver using the decoded EVE target/context and scene/world references. Record both source track/owner and resolved receiver. If receiver resolution fails, keep a visible unresolved command instead of guessing a light from `message.gao`, `gao2`, or a nearby actor.
3. Apply exact object-local setters only when the resolved object has a `SceneLight`: packed color (`0x2135`), nonnegative near/far with Iris’s type restrictions (`0x2136`), `lightGao` relation (`0x2139`), and flag-bit set/clear (`0x213B`). Decode floats by bit-casting the serialized `uint` operand. In particular, setting/clearing bit index 3 changes whether Iris submits the light, and type 3 routes through fog handling.
4. Keep global/device effects in a separate state lane: RLI factor, RLI destination color, fog-state draw mask and global attenuation. Do not store those as per-object `SceneLight` edits.
5. Make the reducer deterministic at arbitrary playhead seeks: rebuild from immutable source baseline and apply the ordered commands up to the requested time. This avoids accumulating mutations when the user scrubs backward/forward. Treat the result as a source-derived preview state, not proof of delivery/execution.

The current `CinematicEngineFunctionCatalog` already gives readable descriptions for `LightSetFlag` and `LightSetColor` (`CinematicEngineFunctionCatalog.cs:192-193,210-211`), and the selector catalog knows all light selectors. The next minimal implementation slice should be a separate `CinematicLightStateTimeline` plus focused tests for literal color/range/flag updates, type-1 near/far no-op behavior, and seek-reset determinism. Add `LightSetGO` target-reference metadata only with an explicit distinction between the call receiver and the referenced GAO. Keep global RLI/fog/attenuation out of the first slice unless a renderer state model already exists.

## What source-only reconstruction cannot establish

Without evaluating the retail AI VM and its live world, the static evaluator cannot reliably determine:

* whether the owner/callback was activated, scheduled, paused, or skipped by its start gate;
* computed operands from variables, arrays, branch conditions, callback state, message queues, or frame-delta interpolation;
* whether a literal setter was a no-op because the runtime receiver lacked a light capability/component;
* the order between an EVE event and an independently running `AI_ExecCallback`/world-cycle update on the same frame;
* resource/sector activation, dynamic-light culling, render-pass ordering, and device-specific renderer state;
* the actual light state after an unmodeled callback mutates it.

Represent these as unknown/provenance in the UI. Do not silently claim that a listed call executed, or infer hidden/active state from asset names. To go beyond this boundary later, the required model is a callback/owner lifecycle graph plus a small typed AI interpreter for selector operands, branch/gate flow, EVE cadence, and instance-variable mutations; a list of hard-coded light-setting constants per scene would not generalize.

## Implemented bounded preview

`CinematicLightStateTimeline` now evaluates the four object-local setters from `CinematicPlayback.ScriptStateCalls`, including scheduled child EVE commands. It snapshots parsed components, resolves only unique exact GAO receivers, keeps unresolved calls diagnostic, and caches read-only results by the reached command prefix. Rewinding reconstructs state from the baseline; scrubbing within one interval reuses the same snapshot. The Light inspector shows both serialized and evaluated values. These values do not yet drive dynamic renderer accumulation and do not represent arbitrary callback execution.

`CinematicMaterialState` and `CinematicParticleState` also receive scheduled child EVE commands. Regression checks cover material enable/alpha/disable and PAG color/disable/shift, duplicate PAG shift suppression, source isolation and backward seeking.
