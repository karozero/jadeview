# Cinematic issue audit

Updated 2026-10-20 from the reported scenes and a read-only audit of
`E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf`.

## Confirmed shared causes

- **Scene export previously dropped two previewed state paths:** script-driven
  `OBJ_ScaleSet` commands and literal hide/show events were evaluated in the
  viewport but not represented in the cinematic GLB. Export now merges decoded
  scripted scale tracks with direct object tracks and samples supported
  scene-prop visibility changes as zero/restored scale at source event times.
  This does not yet bake actor-component visibility, script-spawned objects,
  or material/lighting state into GLB; those still require broader script and
  glTF animation support.

- **Local alpha precision:** Jade stores local alpha in five bits (`s_AditionalFlags[15:11]`), with 31 representing full opacity. The viewport/exporter had interpreted this as an eight-bit field. The decoder now reads the native five-bit range. Additive Jade passes use `ONE/ONE`, so changing framebuffer alpha alone cannot fade the visible RGB; the viewport now applies local alpha to additive RGB as a preview approximation.
- **The spiral is script-animated:** `Cine_SFX_Ondedechoc_Spirale.gao` (`3500D30E`) starts hidden and is enabled by clip `3500B8EA` at 33.016 seconds. Its brain resolves to the shared `Onde_de_choc_cine_ODCc` script model (`3500C024`), which grows the effect with `sin(blend * π/2)`, fades its material constants, and temporarily overrides global RLI. Preview now discovers this behavior from the brain function references and evaluates it on the cinematic clock. To keep scrubbing repeatable, preview uses the authored radius and a neutral speed; the game randomizes both within a range.
- **The green environment uses attack palettes:** at 31.217 seconds the clip calls `ARR_GlobalIntFlagSet(2718)`. The world-cycle script blends from normal to attack colors while that global flag is set. The environment reader already had the attack palettes; cinematic playback now applies them as the flag transitions. The frame audit at 42.7 seconds shows the green attack sky.
- **The nearby `IO_PafSet(100, 8)` is haptic feedback, not a visual filter:** IrisEngine routes it to joystick PAF intensity. It does not create a screen overlay. The preview only draws screen fades when the clip has a supported screen-fade message; PAF cannot substitute for that effect.
- **The sarcophagus label is a group, not a renderable mesh:** `Cine_Sarco01.gao` (`3500BD49`) has no geometry or material. Its explicit group contains `Cine_Sarco_Fehn`, `Cine_Sarco02`, `Cine_Sarco_Pablo`, `Cine_Sarco03` and `Cine_Sarco_Kip`; the visible sarcophagus geometry is supplied by shared objects such as `B_Sarcophage_Corps.gao` and `S_Sarco_vitre.gao`. Alpha diagnosis must follow those member materials and the group events, not the empty group record.
- **Other scripted global-lighting callbacks remain outside preview:** `SFX_Dome_Hyllis` and gameplay `Onde_de_Choc_ODC` set/restore RLI and may depend on live world state. Their callbacks are still not executed generically. The cinematic shockwave variant `Onde_de_choc_cine_ODCc` is now evaluated separately using its own brain and clip activation events; this does not emulate the full script VM. See the corresponding script sources under `external/irisEng/ai/ai_bge/` and `external/irisEng/ai/ai_lightfunctions.c`.
- **T-pose can mean a missing action binding, even when the actor is present:** in `01_02_entrepot_salle_fusibles_cine`, clip `C100C045` references `PNJ_XB24 Génération 1.gao` and the audit finds two source tracks, but its resolved body timeline has zero actions and zero animated bones. It needs its action-kit/runtime binding; selecting an arbitrary animation by model name would be unsafe.

## Reported issues grouped by subsystem

### Military weapon visibility (2026-10-20)

- `_PJ_Militaire.gao` (`7100596B`) in `01_09_entrepot_boss_cine` has two source
  clips with body-action events: `3500A264` (11.45 s) and `BF0000F0` (53.633 s).
  They merge action kit `3500A26A`, but `3500A264` selects actions 201 and 203,
  then 0, 3 and 2; selected motion items use `FFFFFFFF` shape keys and request
  no weapon-removal shape.
- The actor's default shape is `8F000FDD`. Alternate shape `A900532F` changes
  bone 22 (`b_mil_tete.gao`) from `S_MiliOR_TeteFull.gao` to
  `S_DoubleH_TeteHD_Def.gao`. It is a head variant, not a weapon toggle. The
  earlier shape-key hypothesis therefore did not fix or explain the reported
  weapon in `3500A264`.
- The blue item reported at militaire's hand is the `S_MiliOr_MassGeo01.gao`
  appearance attached to `B_mil_Anex2.gao`. Its native default shape marks that
  component hidden (`source = -3`). Cinematic facial-preview discovery selected
  an alternate mesh for morph support and `ApplySourceModel` cleared the hidden
  flag, making the item visible even though the active action has no shape key.
- Appearance overrides now preserve the active shape's visibility. Explicitly
  bound companion meshes remain eligible for activation by their selected
  appearance (for example, the HD head's separate hair). The fix is generic
  and does not name militaire or the hand component; regression checks cover
  both hidden morph meshes and explicit companions.
- Rechecked the retail cinematic container `FF000352`: after seeking `3500A264`
  to 3.0 s and `BF0000F0` to 53.0 s, the militaire head resolves to geometry
  `8F0066B2` and the separate hair member `A900532C` resolves to geometry
  `A90052F4` with `PreviewHidden = false`. This confirms the hair component is
  present in the preview state after the weapon-visibility change. It does not
  by itself prove its rendered placement/material; investigate those only if
  the rebuilt editor still shows a bald military model.
- `A900532F` only changes the military head model and is unrelated. The generic
  cinematic action-shape loader remains available for actual keyed changes.

### Animation discovery findings (2026-10-20)

- `FF00791B` / `BF003BD1` contains Peyj as a cinematic actor, but his
  `CinematicBodyTimeline` has no body-action segments. The actor's action kit
  exists under GAO `9E001C75`; the two root-track events at 0 and 7.283 s do not
  match the preview's supported `ACTActionSet`/kit-merge message patterns.
  Therefore this is not evidence that Peyj has no animations in the BF. The
  preview lacks the gameplay AI/action state that the scene handoff expects.
  The event payload contains `CastVector` (`0x1770`), `GRIDShortWay`
  (`0x316E`) and `GRIDTagDyn` (`0x3178`) calls, rather than a direct action
  selection. This points to AI/navigation setup in the handoff script; it does
  not identify the resulting action without evaluating the actor brain.
  Any generic repair must recover the action state or evaluate the relevant
  brain/event path; substituting a guessed idle would hide the T-pose but could
  produce the wrong action in other scenes.
- `FF006B96` / `35009EF6` has no track for `1_1_obj_verin_axe.gao`, so the
  isolated clip cannot animate it from direct transform tracks. The same object
  is a direct participant in clips `3500A1EF`, `35009E4B`, `3500AE10`, and
  `3500A061`. This is consistent with sequence-/brain-driven state or motion
  authored in neighboring clips; clip adjacency alone is not enough to infer
  timing or replay it safely.
- A useful generic discovery pass should cross-reference each clip's direct
  tracks, action events/action kits, actor brains and script-owned object
  callbacks, then report *why* a participant has no evaluable pose. Keep
  separate classifications for (1) missing action binding, (2) gameplay brain
  state not simulated, (3) motion authored in another clip, and (4) unsupported
  procedural animation. Only the first is safely fixable by resolving a
  serialized binding; the others need their source runtime semantics.

| Subsystem | Reported examples | What still needs reconstruction |
| --- | --- | --- |
| Effect scripts and object state | Spiral activation, sarcophagus visibility, the breaking barrier, enemies emerging, virus/syringe, absent Han/militaire/pearl, missing room-closing wall | Callback-driven global state, spawned/dependent objects, and game-script conditions. Current clip literals cover only a subset of the runtime VM. |
| Skeletal and prop animation | XB24 T-pose, Faucheur, grid hit by Jade, Crochax wings, cart wheels, H2 placement, weapon/barrier changes | Action-kit lookup for each rig and non-bone animation tracks for props. Presence in a scene is not evidence that an actor action was bound. |
| Materials and effects | Spiral/sarcophagus alpha, smoke, water, fog on bridge/beam, Medusa electricity and Peyj breath, monitor/video textures, Hillys map display, dome color | Material-level event playback, script-controlled RLI/constants, video-texture sources, and unsupported game shaders. |
| Geometry and scene assembly | Bad ground/ceiling, hitching surface, missing wheels or room wall | Verify source geometry parts, LOD/dependencies and conditional assembly separately from material/lighting work. |

The alpha fix is generic. The remaining rows are not verified as one shared bug;
they span missing runtime script behavior, unresolved animation data, and shader
features. They should be fixed by the relevant shared subsystem as evidence is
recovered, rather than by per-scene or per-character rules.

### Follow-up: 01_05 camera, 01_09 prop actions and source audio selection (2026-10-22)

- `FF005520 / 3500A927` is a 43.63-second clip with direct tracks for Peyj,
  Sally, two cinematic guards, camera helpers and effects. Offline frame samples
  show the source-camera preview entering scene geometry: at 0 s it frames an
  overhead beam, near 20 s it frames a floor surface at extreme close range,
  and near 30 s the characters are clipped by nearby wall/fence geometry. This
  is a camera route/transform evaluation issue, not an absent-camera case. The
  renderer currently follows literal message-60 position/target routes; native
  camera transition dynamics and collision handling are not reconstructed.
- `FF000352 / 3500A438` (121.07 s) has 579 source events and a separate
  `3500A3BE` 13-second scene-motion clip whose only participants are
  `OBJ_Ancrage_VaisseauD.gao` and `OBJ_Ancrage_VaisseauG.gao`. The editor exposes
  these as distinct clips; no verified start-time link was found in the parent
  clip, so automatically offsetting the anchor motion would be a guess.
- `Cine_Garde_Decor.gao` is a direct participant in `3500A438`. Source events
  set its module-channel table at time 0 and select actions 210–217 later. JadeView
  does not yet instantiate the selected channel components from that table;
  the missing weapon is therefore an unresolved module/appearance binding, not
  proof that the actor or animation track is absent. `SFX_ParticuleTOYOD.gao`
  also needs its own source-track/callback binding audit before attributing its
  irregular smoke to a generic particle renderer defect.
- Audio audits resolve one source sound cue for each of `3500A927` and
  `3500A438`. The audio panel now lets the user select individual resolved cues
  or choose all/none; selections rebuild the source-time mix. The preview still
  does not emulate the game's spatialization or audio script VM.
