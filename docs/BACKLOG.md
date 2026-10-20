# Backlog

## Gameplay scripts and world-state preview

- [ ] Build an archive-wide graph of gameplay script owners, callbacks, object
  references, event tracks, and saved global-flag reads/writes.
  - Added `--archive-gameplay-script-index`: it finds 15,950 brain owners across
    all 196 worlds/91 containers; all model references resolve, 15 owners lack
    callback labels, and one OVA override has an incompatible layout. The
    generated compact report is ignored under `artifacts/`.
  - `Cine_Events` also coordinates gameplay interactions: the fuse-room world
    has 32 such owners and all 32 reference `SceneEvents`. This suggests the
    general sequence feature should not be gated on cinematic world names.
  - `Detect_o_4SpecialFlags` appears on 642 owners and evaluates global-state
    conditions with output flag/capability changes; it is a high-value generic
    condition evaluator candidate.
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
  - A source-wide API scan of 143 recovered script files finds recurring calls
    for AI-track changes (74 files), object destruction (77), capability
    changes (58), collision activation (31), and collision predicates (32).
    Start with these shared state operations rather than full script emulation.
  - Jaded models five concurrent tracks per AI instance. It also distinguishes
    loaded non-generated objects made inactive/invisible by `OBJDestroy` from
    generated objects detached and removed from engine registries. Preserve
    those semantics as separate lifecycle states.
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

- [ ] Decode and display source video textures on in-scene screens.
  - Confirmed reproduction: `FF005833`, worlds `00_02_home_decryptage_cine`
    / `00_02_home_decryptage`, object `Cine_ecran_explik_invent.gao` (`BF000DA6`).
  - Material `BF000DA8` has two RGB layers referencing `BF000F07`.
  - `FF805833` contains its 96-byte texture descriptor at `0x2429`:
    texture type 5, procedural subtype 3, 128 x 128, Bink key `61006334`.
  - That archive entry is `propaganda_01.bik`, with verified `BIKi` signature.
    The PC registry reader consumes the descriptor but produces no image for
    procedural textures; the renderer consequently shows its missing-texture checker.
  - Add generic video-descriptor resolution, decoding, frame upload, source
    timing/control and seeking. Preserve source-video references in exports;
    ordinary glTF materials do not encode video playback.
  - Audit command: `--texture-descriptor-audit <archive.bf> FF005833 BF000F07`.
    This identifies the source; video playback is not implemented yet.

## Asset export

- [ ] Bake source-alpha material overlays with instance RLI colors.
  - Reproduction: export `DEC_04_00_ilotphare.gao` from
    `04_00_vaisseau_hyllis_planete` in container `FF000988`, then import into Blender.
  - Confirmed: grass texture `620020F3` is a second material layer. The object
    has 508 RLI colors; the current bake guard rejects ordinary alpha-overlay
    compositions when RLI is present, so only the primary layer is exported.
  - Implement generic per-layer RLI RGB/alpha handling according to renderer
    rules, including alpha overrides, without object-name checks or applying
    vertex colors twice after baking.
  - Verify grass coverage against the editor, add a synthetic RLI overlay
    regression, and check the real object's GLB in Blender.
  - Deferred on 2026-10-19; no export behavior changed for this issue.
