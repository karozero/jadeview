# Backlog

## Gameplay scripts and world-state preview

- [ ] Build an archive-wide graph of gameplay script owners, callbacks, object
  references, event tracks, and saved global-flag reads/writes.
  - Added `--archive-gameplay-script-index`: it finds 15,950 brain owners across
    all 196 worlds/91 containers; all model references resolve, 15 owners lack
    callback labels, and one OVA override has an incompatible layout. The
    generated compact report is ignored under `artifacts/`.
  - The report now joins 31,805 recognized callback/source flag accesses and
    248 owner-linked `Events`/`SceneEvents` flag commands across 1,404 keys.
    `ARRBitTest/Set/Clear` and bounded `ARRBitSetRange/ARRBitClearRange` are
    included only when the source buffer resolves to `Flag_speciaux_sauve`;
    unrelated bitsets and unresolved aliases are excluded. Range expansion
    uses Iris's inclusive endpoints and 4488-byte saved-buffer bound.
    BF00218E's timed write to flag 2166 links to two replay-suppression readers.
    Runtime order remains incomplete. Full
    potential edge counts are kept with up to 64 sample links per flag.
  - `Cine_Events` also coordinates gameplay interactions: the fuse-room world
    has 32 such owners and all 32 reference `SceneEvents`. This suggests the
    general sequence feature should not be gated on cinematic world names.
  - `Detect_o_4SpecialFlags` appears on 642 owners and evaluates global-state
    conditions with output flag/capability changes. Its source-backed static
    OVA analyzer is implemented and verified on a gameplay owner and a
    cinematic owner; evaluating current save flags, timers, and runtime
    callback dispatch remains part of the world-state preview work.
  - Initial evidence shows `CINE_BIGINTRO_EVENTS.gao` can set `0x0A5A`, while
    Monstroplante's gameplay brain reads `0x0A5A` and `0x0A5C` to gate startup
    and choose a phase. Treat this as a configured cross-feature link; runtime
    order and exact appearance timing remain unverified.
  - Recovered `action_reaction_ar_action` code combines distance/collision
    predicates, contextual actor-action requests, kick counters, capability
    changes, and saved-flag writes. Use it as a source example for generic
    interactable evaluation, not an asset-specific implementation.
  - `Detect_o_Zone` selects BV-overlap or ZDE-pivot collision, tracks enter/exit,
    and can apply timed saved-flag and capability changes. Trigger preview must
    model these as state transitions, not static collider visibility.
    The reproducible `--archive-zone-trigger-census` now finds 24 owners in
    15 worlds, all uniquely bound to the shared OVA and all using the BV route.
    It found one serialized `SceneEvents` join: the 01_04 door-zone owner writes
    invert flag 1007 and is targeted by an EVE track. Live actor-slot,
    collision, callback, timer, and saved-state results remain unknown. See
    [`gameplay-zone-trigger-census.md`](gameplay-zone-trigger-census.md).
    A source/archive join traces flag 1007 from that zone to a gameplay door
    and kickable prop, while confirming the cinematic EVE track only gates
    the zone and does not itself write the flag. Runtime order remains
    unresolved; see
    [`gameplay-saved-flag-1007-chain.md`](gameplay-saved-flag-1007-chain.md).
  - The archive-wide engine-hook census resolves 4,179 configured callbacks
    across 3,060 owners (IDs 0–2 only); Iris source has dispatch sites for IDs
    0–3, but no retail assignment for ID 3 or IDs 4–9 in this BF. A targeted
    join in `BF00218E` links OVA callbacks with EVE targets, while keeping them
    separate from time-stamped capability commands. This still does not prove
    callback execution; see [`game-script-engine-callbacks.md`](game-script-engine-callbacks.md).
  - A source-wide API scan of 143 recovered script files finds recurring calls
    for AI-track changes (74 files), object destruction (77), capability
    changes (58), collision activation (31), and collision predicates (32).
    Start with these shared state operations rather than full script emulation.
  - Jaded models five concurrent tracks per AI instance. It also distinguishes
    loaded non-generated objects made inactive/invisible by `OBJDestroy` from
    generated objects detached and removed from engine registries. Preserve
    those semantics as separate lifecycle states.
  - Iris source now confirms two separate execution systems: five mutable,
    timed AI tracks advanced by `AI_MainCall`, and phase-indexed engine
    callbacks dispatched by `AI_ExecCallback` (including dynamic registrations
    with a separate context object). Static BF preview must keep defaults,
    explicit event-derived mutations, and unknown live state distinct; full
    execution still needs track cursors/timers, callback registrations, and
    owner/world eligibility. The cinematic source inspector now lists reachable
    `AICBAdd`/`AICBDel` registration candidates, branch alternatives, raw tuple
    operands, Iris dispatch callsites, and `AICBDel` candidates whose full
    resolved tuple exactly matches a registration. Separate `AIReinitVars`
    sites show possible current-instance callback-list resets. It does not
    reconstruct the live callback list or prove any candidate executed. The
    production audit of `FF005833 / BF00218E` found 22 adds and 4 deletes but
    no fully resolved matching tuple or reachable reset site; destinations in
    the inspected adds were dynamic or null.
  - EVE `OBJ_AnimMorphFactorNoKey` now drives actor root/module and static
    type-4 morph targets from the carrier X/Y track. Static morph deformation
    is shown in the viewport and sampled into cinematic GLB animations; tests
    cover deterministic seek/reverse-seek and export sampling. The lighthouse
    physical dome now follows the generic static-object path. Its initial hide
    still depends on unsupported `sbp_init`/saved-flag handling and needs a
    comparison against the game.
  - [ ] Implement bounded source-backed AI initialization/event callback
    effects for preview, including explicit branch/save-state diagnostics.
    Keep callback candidates and unknown runtime state visible; do not execute
    arbitrary recovered script bodies.
    - The branch inspector now accepts temporary set/clear assumptions only
      for bit tests whose source explicitly resolves to Iris's
      `Flag_speciaux_sauve` buffer. Unknown flags remain unknown and arbitrary
      bitsets are not treated as save state. It lists guarded source callback
      state effects and reports their evaluated predicates, but this remains
      diagnostic-only; callback effects and resulting object visibility are
      not simulated.
  - The global-startup inspector and cinematic Script Owners now show
    `AITrackChange`, `AITrackStop`, `AITrackCurChange`,
    `AITrackCurChangeNow`, and `AITrackCurStop` candidates from reachable
    source functions, with literal slots, unique function links, branch paths,
    and source-confirmed effects. The `FF005833 / BF00218E` audit found 511
    candidates across 45 owners; 417 targets resolved uniquely and 423 carried
    branch conditions. The graph also produced current-slot candidates for all
    332 current-track operations: 330 had one source-path slot and two had
    multiple possible slots. These are possible transitions, not executed
    state. Dynamic values, current function/cursor/timer, owner admission, and
    runtime order remain unknown. Next, connect only deterministic,
    time-resolved state changes to preview.
  - Cinematic literal-state preview now models `OBJ_CapaNot` as a 16-bit XOR
    alongside add/remove `OBJ_CapaSet`, matching IrisEngine's `uw_Capacities`.
    This does not evaluate callback branches or collision/saved-flag gates.
  - Bounded callback preview now uses source-resolved receiver evidence before
    applying `OBJ_CapaSet` and `OBJDrawMaskSet` operands, and projects literal
    `COLSetActivationSet` changes onto a uniquely resolved collision instance.
    The inspector shows the effective preview mask without modifying the
    serialized collision resource. This does not run collision predicates,
    zone triggers, map-activation calls, or arbitrary AI callbacks.
    - Fresh `FF0045A2 / 3500B8EA` verification resolves 70 collision-bearing
      objects and the camera/Sally instance resources, but sampled callback
      masks remain unknown. Iris's configured `camera_cam_1_init` would clear
      collision bit 0; the preview does not yet establish startup callback
      admission. The inspector now reports that the serialized slot is due
      after fresh AI reinitialization, conditional on owner admission and a
      resolved callback function; it still does not apply the collision change.
      Use this as a regression fixture while reconstructing init dispatch, and
      do not apply the static call just because it is reachable.
  - The zone audit now separates Iris's polled BV/ZDE trigger callbacks from
    queued messages, and Jaded's serialized `TYPE_TRIGGER`/`call_trigger` path
    from its message queue. Next, surface these as distinct inspector records:
    trigger owner + actor + selected collision route + callback assignment on
    one side, sender/recipient + message payload + consumer candidate on the
    other. Runtime overlap, queue contents, dispatch, and execution stay
    unknown without a source-backed state path or runtime trace.
  - Preserve provenance and unresolved edges; do not infer callback behavior
    from asset or scene names.
  - Research notes: [`gameplay-script-system-findings.md`](gameplay-script-system-findings.md).

## Cinematic actor placement and transitions

- [x] Fix shape-hidden actor components being revived by cinematic morph previews.
  - The action-kit reader already resolves each motion item's `ShapeKey`, but
    actor shape payloads were only loaded from the ordinary actor animation
    table. The preview now loads shapes referenced by resolved cinematic body
    actions and applies the active item's shape as the action clock advances.
  - Root cause: cinematic facial-preview appearance resolution could apply an
    alternate mesh after the actor's native shape and unconditionally clear
    `PreviewHidden`. This revived militaire's shape-hidden `B_mil_Anex2.gao`
    (`S_MiliOr_MassGeo01.gao`), which appears as the blue item at his hand.
  - Appearance overrides now preserve the visibility selected by the active
    native shape. Explicitly bound companion meshes (such as HD head hair) can
    still be activated by their selected appearance. Regression checks cover
    both hidden morph components and explicitly activated companions.
  - The active actions in `3500A264` do carry no shape change (`FFFFFFFF`);
    `A900532F` only switches the head mesh and was unrelated to the item.
  - This does not resolve gameplay brain state (for example Peyj's T-pose) or
    independent/scripted prop motion; those require their own source runtime
    bindings and remain tracked below.

- [ ] Reconstruct gameplay-to-cinematic actor animation state and discover
  scripted prop motion generically.
  - Cinematic assembly now closes over effective OVA object references from
    owners linked to the selected clip, recursively follows uniquely resolved
    script owners on referenced targets, and includes nested `SceneEvents`
    command/transform targets. This is an asset-discovery step only; it does
    not infer callback execution or activation from object names. The audit of
    `FF001FBE / 35009F74` now confirms `_PJ_Militaire.gao` (`7100596B`) is in
    the assembled participant set through `CINE_PIEGE_PORTE_LOBO_EVENTS.gao`.
    Its base action table still lacks slot 201, but a separate same-world
    startup owner, `CINE_INIT_MILI_EVENTS.gao` (`35009652`), has a serialized
    `SceneEvents=35009655` channel that merges slots 200–230 from
    `CINE_01_04_ACK_Mili.gao` (`3500965A`) and then sends companion message 49
    with status 3/action 201 to Militaire. The source action kit does contain
    slot 201 (`35009636`). Iris's shared `Cine_Events` lifecycle can start this
    event list immediately, but the owner has start-gate mask `0x104`, including
    actor activity and saved special flag 2007; the BF archive does not reveal
    whether that gate passed in the user's playthrough. This is a concrete
    candidate for the missing T-pose action, not proof it ran. The generic
    **EVE & actions** inventory now lists independently gated script-owned
    `SceneEvents` with owner-specific preview controls and known flag/message
    start candidates. The `FF001FBE / 35009F74` audit identifies
    `CINE_INIT_MILI_EVENTS.gao` (`35009652`) as previewable; enabling it applies
    the serialized action-kit merge and companion action request to synchronized
    preview and export. This is an explicit inspection path, not automatic
    playback. Keep the action out of default playback until owner activation and
    runtime gates are known; no actor/scene special case is used.
    The start-gate inspector now resolves the source's outer owner capability
    branch (`OBJ_CapaTest(0x100)`) when preview capabilities are available,
    distinguishing bypassed predicates from evaluated predicates. Preview start
    estimates use serialized flag/message signals and configured delays when
    uniquely available. Callback dispatch, owner activation, and automatic
    scheduling from independently gated owners remain unresolved.
    A cross-report scan found 14 EVE channels in four representative containers
    that combine an action-kit merge with companion message 49 in the same
    channel. This is a repeated event-track pattern, not a Militaire-specific
    exception. The corresponding action merge must be applied before the
    callback-scoped companion message, while the owner-level `Cine_Events`
    gate determines whether that EVE list actually starts.
  - Generic `OBJRotateLocalY` playback is now implemented for direct and
    nested EVE calls. The authored radians are distributed over the key's
    active duration using the same pause/stop/restart/loop clock as transforms;
    uniquely resolved targets are included in playback and synchronized GLB
    export. Synthetic checks cover seeking, EVE time mapping, playback, and GLB
    output. The retail audit for `FF006B96 / 35009EF6` resolves 27 rotation
    segments for `1_1_obj_verin_axe.gao` (`A9001DBB`) from 2–22 seconds. Other
    recurring scripted prop patterns still need archive-wide validation.
  - Saved-flag gate state at the playhead now combines direct clip writes with
    literal commands from resolved nested `SceneEvents` routes. It remains a
    static route preview: callback branches, world transitions, and the retail
    save's initial flag values are not evaluated.
  - The 2026-10-24 world-scoped archive audit scans 91 containers, 38 cinematic
    worlds and 208 clips. It found 454 skeletal actor/clip bindings; 308 have
    decoded body-action segments and 146 do not. Those 146 have root-transform
    data in 117 cases and direct bone tracks in 26. Eleven bindings contain an
    unresolved action request. The report uses the same world-scoped action-kit
    resolver as playback; these are coverage results, not necessarily playback
    bugs because serialized BF data omits live AI track state.
  - For bindings with missing actions, the audit now independently enables each
    selectable script-owned `SceneEvents` source and checks whether its action
    kit merge supplies the missing slot. It found such owner-preview candidates
    for Militaire in `35009F74` and `3500A4C7`, and PNJ_Faucheur in `3500B047`.
    This is a generic discovery path to the relevant setup owner, not proof that
    the owner gate passed in the retail game. Runtime callback scheduling and
    gameplay handoff remain unresolved.
  - The script-graph audit now builds cinematic participants through
    `CinematicWorldAssembler` and includes serialized transform-track targets
    in the linked owner scope. On `FF00791B / BF003BD1` this recovers Peyj's
    `_basic_peyj` brain (`companion_co_init`, model `35001FF8`) and base
    `Events=35009544`. That event record is 2.367 s with no decoded calls;
    source-reachable companion callbacks expose many possible action requests,
    but their dynamic mode/branch inputs do not identify Peyj's active action
    at cinematic entry. The T-pose therefore remains unresolved without runtime
    state rather than being replaced with a guessed idle.
  - Iris source narrows the generic handoff protocol: companion message `49`
    with `Int1 == 4` stores `Int2` in `Perso_ID_action[des_i_friend]` and
    `i_attente_action`, then re-enters the mode selector, which applies the
    stored action via `ACTActionSet`. `companion_co_init` also reads that shared
    slot during setup. Status-change and companion-management scripts contain
    writers for message `49`, but the `BF003BD1` archive evidence does not show
    one targeting Peyj at clip start or reveal the active action value. Treat
    this as a generic gameplay-to-pose dependency, not a verified scene event.
    Next: map serialized EVE/message owners to the message-49 writers and find
    the initialization path for `Perso_ID_action`; only add playback when the
    archive provides a deterministic target, time, and action value.
  - Message IDs are receiver-script scoped: Iris's Crochax cinematic receiver
    interprets message `49`'s `f2` as scale, and the Secundo SFX receiver uses
    `f2`/`f1` as light/alpha factors. Any future event inspector must resolve
    sender + target + target AI before assigning meaning to a numeric message
    ID; a global message-ID label would be misleading.
  - The live cinematic script-owner list now also follows the selected clip's
    serialized transform-track targets, so brain-bearing props reached by a
    transform track can be inspected beside actor and command-linked owners.
  - Nested EVE transforms now use a source-time playback clock for `TRAStop`,
    `TRAPause`, `TRAUnPause`, and restart `TRAPlay`. A stop holds the sampled
    pose; pause freezes local time; unpause resumes it; play while running is a
    no-op; play after stop/completion restarts at local time zero. Nested event
    calls are remapped through the parent clock. `TRAReinit` resets the cursor
    and restores the `RunningInit` state; `TRAReinitAll` applies this per track.
    GLB sampling adds lifecycle and mapped source-key boundaries. Playback and
    export share this clock.
    Tests cover delayed child starts, interpolation at stop, pause/resume,
    post-completion replay, per-track and all-track reinitialization, and GLB
    pose hold. Current-source archive audits produced 33 nested transforms for
    `FF005833 / BF00218E` and 56 for `FF0045A2 / 3500B8EA`. Iris evidence is in
    `external/irisEng/ai/ai_eventtrackfunctions.c` and `external/irisEng/events.c`.
    `TRASetTime` now seeks every selected EVE channel without changing its
    running/paused state; seeking past the list matches the engine by clamping
    to the last event's start. Tests cover running, stopped and paused channels,
    all-channel selection, invalid values, preview and GLB. Requests beyond
    65535/60 seconds are unresolved because the source uses a 16-bit frame
    cursor and wrap behavior is not reconstructed. Callback gates, exact VM
    scheduling and live gameplay AI state remain
    unresolved; this does not claim exact runtime scheduling or native initial
    track flags.
  - `01_00_entrepot_sas_entree_cine` / `BF003BD1`: `_PJ_Peyj_le_petit_cochon.gao`
    (`9E001C75`) has scene tracks/events but no resolved body-action timeline,
    so playback leaves him in a T-pose. The clip is in archive container
    `FF00791B`; its actor kit is present, but the event program does not decode
    as a direct `ACTActionSet` in the current preview. Determine how the game
    enters/carries gameplay AI action state across this handoff before choosing
    an action; do not guess an idle/action by actor name.
  - `01_01_entrepot_ascenseur_central_cine` / `35009EF6`:
    `1_1_obj_verin_axe.gao` is not a participant in this clip's parsed tracks.
    It is directly tracked in neighboring `3500A1EF`, `35009E4B`, `3500AE10`
    and `3500A061`, which suggests scripted/sequence-driven object state may
    be missing from the isolated clip preview. Trace its brain/action/event
    references and the sequence owner before synthesizing motion.
  - Add an archive-wide coverage audit that flags actor participants with no
    decoded body actions and scene objects whose motion comes from neighboring
    clips or brain callbacks. Group findings by source mechanism so fixes apply
    to all cinematics; distinguish absent data from unsupported script/runtime
    evaluation. Verify these two cases and then rerun the audit archive-wide.
- [ ] Diagnose remaining actor jumps/overshoot on cinematic action and loop boundaries.
  - Fixed reversed quadratic/cubic Bezier payload assignment in the shared
    animation decoder. The first vector is the position, followed by controls;
    reversing them caused backwards movement and speed spikes. Recheck the intro
    `3500B8EA` around 38.85–48s before attributing remaining jumps to dynamics.
  - Added native group visibility/activity commands: the intro's six-object
    Sarco group now activates at 98s; a 103s GPU frame shows all three sarcofagi.
  - Progressive transitions now transfer normalized animation phase rather than
    restarting at zero. Exact native motion/AI parity still needs comparison.
  - User still observes lagging placement and characters flying across the scene.
  - Start with `20007991` (militaire), `3500A446` and the `BF003BD1` gameplay handoff.
  - Measure source root transforms before/after action switches, frame offsets,
    loops and explicit placement events. Distinguish authored cuts from incorrect
    root-motion/reference accumulation; do not mask the problem with smoothing.
  - Apply/verify native time curves first, then inspect ACT blending, MagicBox
    reference resets, dynamics and script ownership. Full gameplay AI remains pending.

## Cinematic video textures

- [ ] Finish native environment/material parity in `FF0045A2`.
  - User confirmed mist reproduction: `3500BB72`, at the start.
  - Fixed SPG generated-vertex alpha being replaced by opaque carrier fallback;
    the dense green veil disappears in a fresh GPU frame. User confirmed that
    the start of `3500BB72` now looks correct in JadeView. Native post-lighting
    colors, special fog and distance fade remain incomplete beyond this case.
  - Added source-clock material pass enable, local alpha and alpha threshold.
  - Discovered the native world-cycle controller and typed sky/fog references.
  - Added world-cycle palette evaluation from typed instance arrays and the sun
    object's current Z position; source packed-color interpolation updates the
    referenced sky/environment material constants. Check the start of `3500BB72`.
    Saved planet rotation/hour, attack state, native light, water and fog remain
    unavailable, so exact sky and atmosphere parity is still pending.

- [ ] Verify the size/projection of cinematic title/photo meshes against retail
  frames (intro `3500B8EA`, `Cine_SFX_JADE_Intro.gao`, around 44.5s).
  - Keep this separate from the now-supported full-viewport message-50 flashes
    and the short iris shutter. The mesh is not a fixed-resolution UI bitmap.
  - Found and fixed the reversed native message float layout: `f2` transition
    precedes `f1` lens angle. Source FOV now controls framing; a real 45.3s GPU
    render fills the frame without rescaling the JADE mesh.
  - Compare the corrected result against retail capture. Native display-format
    constraints and positional camera smoothing still need verification.

- [ ] Verify native gameplay lifecycle parity for source video textures on in-scene screens.
  - Confirmed reproduction: `FF005833`, worlds `00_02_home_decryptage_cine`
    / `00_02_home_decryptage`, object `Cine_ecran_explik_invent.gao` (`BF000DA6`).
  - Material `BF000DA8` has two RGB layers referencing `BF000F07`.
  - `FF805833` contains its 96-byte texture descriptor at `0x2429`:
    texture type 5, procedural subtype 3, 128 x 128, Bink key `61006334`.
  - That archive entry is `propaganda_01.bik`, with verified `BIKi` signature.
    The texture registry preserves a procedural descriptor rather than an
    ordinary still image; cinematic rendering handles this descriptor separately.
  - The installed game's x86 `binkw32.dll` successfully decodes `128x128` Bink
    frames through a persistent x86 PowerShell bridge; JadeView's x64 renderer
    uploads frames and samples them against cinematic playhead time. The bridge
    is runtime-only and does not ship or copy the game's DLL.
  - Bink frame decoding now runs off the renderer thread. GPU texture uploads
    remain on the render thread; seeks coalesce behind the one in-flight decode
    per media resource, so a slow frame decode cannot block scene drawing.
  - Preview now links subtype-3 materials to `mpegplayer_init` owners, reads
    `i_Play` and the linked switch's initial `0x100` capability through
    `des_o_Interrupteur`, and evaluates literal `OBJ_CapaSet` requests from the
    selected clip and nested scene-event data. Controlled textures stay black
    before their first start; Bink time is relative to that trigger, and reverse
    seeking recomputes the event state. Use
    `--cinematic-video-trigger-audit <archive.bf> <container> <clip-key>` to
    inspect texture-to-owner links and sampled states.
  - The preview is source-event reconstruction, not AI VM execution. Live
    switch state, saved-game gates, callback branch eligibility and callback
    frame latency still need recovery and representative in-game verification.
  - Audit `FF005833/BF00218E`: the monitor's `OBJ_CapaSet` toggle is at
    `44.783333s`, exactly the clip endpoint. JadeView keeps the final cinematic
    pose and plays video post-roll through decoded media end when
    `StopAtEnd` is configured. Verify against the game whether the screen
    callback remains active after the cinematic and whether the same lifecycle
    handles live switch/save gates and gameplay transitions.
  - GLB export now preserves the procedural descriptor in material
    `extras.jadeProceduralTexture` (key, subtype, dimensions, archive media key,
    media name, and source scope). The export warning explicitly states that
    glTF retains source metadata but cannot play the video; no fake image is
    substituted for a decoded frame.
  - Validate the live monitor material in `BF00218E`, including whether its
    material layers use the same procedural texture descriptor.
  - Preserve source-video references in exports; ordinary glTF materials do
    not encode video playback.
  - Audit command: `--texture-descriptor-audit <archive.bf> FF005833 BF000F07`.
    Use it with the trigger audit to compare the descriptor with the inferred
    controller timeline.

## Asset export

- [x] Bake source-alpha material overlays with instance RLI colors.
  - Reproduction: export `DEC_04_00_ilotphare.gao` from
    `04_00_vaisseau_hyllis_planete` in container `FF000988`, then import into Blender.
  - Confirmed: texture `620020F3` is the second, source-alpha material layer;
    this instance has 508 RLI colors. The former bake guard discarded that layer
    when RLI was present.
  - Alpha-overlay baking now evaluates both layers' corner tints using the same
    instance/geometry color rules as the renderer. Alpha overrides are applied
    once in the baked result, and the baked material does not apply vertex color
    a second time. No object-name special case is used.
  - Added a synthetic per-instance RLI regression. A real GLB export of the
    named asset contains two baked-layer materials, and Blender 5.2.3 imports
    both with texture nodes. Output:
    `artifacts/export-verification/DEC_04_00_ilotphare.gao-audit.glb`.
  - [ ] Compare the asset's grass coverage and tint against the JadeView
    viewport or retail game. The GLB imports and its baked layers are present,
    but an automated Blender import does not prove visual parity.

## Assets viewport performance

- [ ] Reduce wide-view frame spikes without hiding scene content or changing
  material/transparent ordering.
  - A new current-source stress audit selected
    `Cine_SFX_Faisceau_ascension.gao` in
    `00_03_dehors_maison_Intro_cine` and orbited a wide view with roughly 780–850
    visible objects. The beam itself was not a leading hotspot; cold lighting
    and warm multi-object skinning/draw work dominate different spikes.
  - Warm orbit averaged 57.40 ms (p95 67.99 ms, max 142.30 ms); separate
    warm-up/interaction frames reached about 200–218 ms. The sampled run did
    not reproduce a sustained 250 ms frame. A normal 99-object view averaged
    22.08 ms. This is a synthetic full-UI audit, not a capture of the user's
    exact camera or machine behavior during ordinary interaction.
    - A texture-parameter state cache now skips redundant wrap/filter writes.
      Pixel equality is checked with cache on/off. One sequential audit measured
      17.38 ms wide-orbit average with caching versus 24.64 ms without; repeated
      selection was 17.90 versus 27.09 ms, and ray-picking was 20.91 versus
      28.53 ms. Treat these as indicative; the comparison has not been repeated
      under controlled conditions, and a 146 ms warm-up spike remains.
    - Continue profiling cold frames and skinning/draw-pass spikes. Any skinning
      optimization must preserve positions, normals, and rendered output.
    Details and exact trace path: [`assets-viewport-performance.md`](assets-viewport-performance.md).
