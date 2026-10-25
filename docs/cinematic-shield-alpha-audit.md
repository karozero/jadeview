# Cinematic lighthouse shield and dome alpha audit

## Finding

In `FF0045A2 / 3500B8EA` (`00_03_dehors_maison_Intro_cine`), the controller's
EVE morph and the physical dome's AI callback are separate stages. JadeView
evaluates the `OBJ_AnimMorphFactorNoKey` EVE command at 49.75 s for actor and
static morph objects, and now previews the source-confirmed callback handoff
that reveals the linked dome around 55.1 s. The initial hide/destroy branch
depends on save flag 2452, which is not stored in the BF archive. Later
carrier visibility toggles are discrete scene commands, not a continuous
material-alpha track. No scene or asset name special case is needed.

## Object and owner identity

- Cinematic controller: `LD_DEC_Bouclier_Base_Cine.gao`, owner key `10004AE8`,
  brain `3D002D78`, script model `3D002D5B`.
- Its `des_o_dome` OVA reference resolves to `3D002D7D`,
  `SFX_Dome_Phare@5ea4c30.gao`.
- The referenced dome has its own owner (`3D002D7D`, brain `3D002D7F`, model
  `72000C04`), but its configured callback is only an empty init. The starting
  hide/show behavior therefore comes from the shield controller's callback,
  not from an independent dome initialization or material-alpha track.
- `Cine_SFX_Dome_Phare.gao` (`3500DF23`, model `720040AC`) is a distinct scene
  object and is not the referenced dome.

## Timeline and script relationship

The cinematic sends message 1 to the controller at `50.65 s`, with `f2=0.5`;
at that time it also enables capability `0x100` and clears inactive state.
Later EVE commands set capabilities `1`, `2`, and `4` at `55.0`, `55.1`, and
`55.8 s`. Iris's `sfx_bouclier_phare_sbp_msg` copies the message floats to
`des_f_time1`/`des_f_time2` and sets `i_cine=1`. The appearance callbacks gate
transitions on capability stages and `i_cine`, and alter channel progression,
visibility, material constants, and runtime effects.

Separately, EVE calls `OBJ_AnimMorphFactorNoKey` at `49.75 s` for controller
`10004AE8`, channel 0, using carrier `3500B8EB`. Iris implements this function
by setting the selected channel's factor from the carrier X position and its
progress from carrier Y + 1 (`ai_gameobjectfunctions.c:2655-2661`). JadeView
now evaluates this generic EVE morph signal for the controller, including
static type-4 morph geometry. This only updates the controller's own morph
channel; it does not execute the controller's ordinary AI callback chain.

The controller's `sbp_init` path sets the linked dome's channel 0 progress to
1 and sets its invisible flag when saved flag 2452 is unset. If the flag is
set, a different init branch destroys the dome and related objects. The BF
archive does not contain the live save-flag value, so JadeView cannot infer
which full init branch ran. Iris's `OBJDestroy` implementation marks an
authored object invisible and inactive immediately, while generated objects
may be detached and removed. Both outcomes imply that this linked authored
dome is visually hidden after the branch executes. The bounded reducer models
Iris's immediate terminal result without requiring a callback exit window;
because both init branches
hide this authored dome, the archive probe agrees on a hidden initial state
even while flag 2452 is unknown. This consensus covers initial visibility only
and does not resolve which callback chain ran.

The opening also requires a script handoff. Iris's
`sfx_bouclier_phare_abp_apparition2` reads channel 0 progression from controller
`10004AE8`, clears the physical dome's invisible flag, and writes the
progression to channel 0 of linked dome `3D002D7D`. Earlier appearance callbacks
advance this chain behind capability and `i_cine` gates. In this clip,
capability 2 enters `abp_apparition2` near 55.1 s and capability 4 replaces the
current AI-track near 55.8 s. JadeView previews the linked-channel transfer
within that recovered callback interval. It samples the controller at the
callback's final active time, so later carrier keys do not overwrite the dome's
completed open state.

From `60.100` through `61.350 s`, EVE track 96 toggles visibility on both the
carrier `Cine_ACK_Intro_Sally.gao` (`3500B8EB`) and the physical dome
`SFX_Dome_Phare` (`3D002D7D`). Dome hide events occur at `60.100`, `60.333`,
`60.517`, `60.733`, `61.100`, and `61.350`; show events occur at `60.167`,
`60.467`, `60.650`, `60.833`, and `61.283`. Carrier toggles are paired with
`OBJ_AnimMorphFactorNoKey` calls that drive controller `10004AE8`. These are
discrete visibility events, not dome material-alpha keys. The physical dome's
opening morph is controlled by the script callback handoff; the later flicker
is an authored visibility sequence.

The bounded archive probe does not show a gradual dome morph during the reported
50–60 s opening interval: controller channel 0 remains at progression 1 from
49.75 through 55.8 s, and the clear-flag callback transfers progression 1 to the
dome at 55.1 s. The controller reaches progression 2 by 60.1 s, while the dome
remains at progression 1. This proves what the JadeView reducer currently
samples, not what those morph values look like in the retail renderer. Until
the geometry's channel states are compared against a game capture, the source
and archive do not establish whether progression 1 is already the open pose or
whether JadeView is missing an intermediate transition.

A geometry-level audit narrows this further. The physical dome modifier has no
group references on channel 0, so copying progression to it cannot deform its
vertices. Controller geometry `F3000E74` has references `[0, 1]`: progression 1
selects group 0, whose deltas are all zero; progression 2 selects group 1 and
changes 546 of 610 vertices (maximum source-space delta `35.716732`). The
controller's `des_o_haut_phare` reference resolves separately to
`DEC_00_03_sommet_phare.gao` (`8F0048F0`, geometry `8F0048F1`). That geometry
also has references `[0, 1]`, but its cinematic channel remains at progression
1 in the sampled interval, where group 0 has no displacement. Thus the archive
supports a discrete controller-geometry change at 60.1 s, not a gradual
50–60 s opening. Whether this is the intended game timing or another runtime
path is not established by the cinematic EVE data alone.

These results are reproducible with the generic verifier commands:

```powershell
dotnet run --project tests/ExportVerification -- --pc-morph-channel-audit "<path-to-sally_clean.bf>" FF0045A2 10004AE8
dotnet run --project tests/ExportVerification -- --cinematic-morph-channel-audit "<path-to-sally_clean.bf>" FF0045A2 3500B8EA 10004AE8 49.75 61.35 0.25 2452 false
```
The script does not call a material-alpha setter. Iris also registers
`sbp_bdisplay` and `sbp_adisplay` on the dome as dynamic engine callbacks for
hook IDs 0 and 1. The before-display hook saves the current RLI factor and
destination color, then sets the factor from `f_transparency_coef` and the
destination color to white; the after-display hook restores the saved values.
Iris applies the resulting packed RLI color to per-vertex RGBA before the
material pass. This draw-time override is not itself a material-alpha
animation; whether its packed alpha reaches output opacity depends on the
material color mode. The physical dome has only modifier type 4 (morph
payload); it has no `GEO_C_OTCF_UseAlphaOfRLI` wave modifier. Its standalone
material-layer flags remain unresolved, so actual blending of this dome's RLI
alpha is not yet confirmed. JadeView now evaluates paired display hooks when
their registration gates, callback windows, and scalar inputs are resolvable.
The real-archive probe agrees that the dome starts hidden both
when flag 2452 is unknown and when it is explicitly assumed clear. Under the
clear assumption, the bounded callback preview transfers channel 0 to the dome,
which becomes visible with progression 1 at 55.1 s. Under the unknown
assumption, later branch outcomes diverge, so JadeView conservatively leaves
the dome hidden rather than guessing whether the clear-flag appearance callback
ran. At 60.1 s the controller has progression 2 while the dome remains at 1
because the recovered callback window ends at its next track replacement; the
later direct EVE hide/show commands are nevertheless independent and
previewable.

The controller's scripted RLI factor is computed in
`sfx_bouclier_phare_abp_apparition2` (Iris lines 8137–8143), not stored as a
timeline key. In the `i_cine` branch, the callback reads the current object's
channel-0 progression `p`, assigns `1.29 - p`, computes `1 - value`, doubles
that temporary, then assigns `1 - temporary`; algebraically the final factor is
`1.58 - 2p`. The same callback clears the linked dome's invisible flag and
copies `p` to its channel 0. JadeView now evaluates this dynamic value from the
channel and resolved callback path, and carries instance OVA state across
callback track replacements. In the tested archive scenario (`2452=false`,
`2748=true`), the draw callback reports factor `-0.42` from 56–61 s while the
controller channel is at progression 1. The later disappearance callback does
not write this field in cinematic mode, so the value must persist from the
appearance callback instead of resetting to the OVA default `0`.

The negative factor does not produce a fade in the native RLI color path:
Iris/Jaded `COLOR_ul_Mul` returns zero for factors `<= 0.02`, so subtracting the
scaled white destination leaves the source RLI color unchanged. JadeView's
shader follows the same threshold. This confirms the callback is not driving a
visible dome alpha fade at these sampled times. The observed later change should
be attributed first to the discrete 60.1–61.35 s visibility events; this data
does not contain a gradual physical-dome alpha curve.

This verifies JadeView's script-state calculation, not retail opacity: the
negative factor's rendering meaning and the physical dome material's blending
flags remain unresolved. The physical dome has no morph groups, and the BF
does not contain a gradual alpha curve for it. Its initial hidden state,
callback-driven visibility, controller geometry morph, direct EVE hide/show
events, material constants, and per-draw RLI values are separate mechanisms.

At `58.316666 s`, the clip also issues `OBJ_FlagsControlSet` for the separate
cinematic object `3500DF23`; at `60.1 s` it sets capability state on that
object, and destroys it at `66.35 s`. This object is not the `des_o_dome`
physical shield reference, so its changes should not be attributed to the
physical dome without additional rendering evidence.

## Evidence and limits

- Iris callback chain: [`ai_compiledscripts_sfx.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_sfx.c),
  `sbp_firstinit` / `sbp_init` at lines 7755–8010, message handling at 8659,
  and appearance callbacks at 8086, 8531, 8686, and 8896.
- Iris's `OBJ_AnimMorphFactorNoKey` implementation:
  [`ai_gameobjectfunctions.c`](../external/irisEng/ai/ai_gameobjectfunctions.c#L2655).
- Iris's dynamic hook registration and implementation:
  [`ai_compiledscripts_sfx.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_sfx.c)
  (`sbp_init`, `sbp_bdisplay`, `sbp_adisplay`), dispatched around geometry by
  [`geo.c`](../external/irisEng/graphicsobjects/geo.c#L761) and
  [`ai_common.c`](../external/irisEng/ai_common.c#L812).
- RLI factor and destination-color application in the Iris draw path:
  [`geo.c`](../external/irisEng/graphicsobjects/geo.c#L526).
- JadeView EVE evaluator and static morph timeline:
  [`CinematicObjectMorphPlayback.cs`](../src/Features/Cinematics/Models/CinematicObjectMorphPlayback.cs),
  [`CinematicPlayback.cs`](../src/Features/Cinematics/Models/CinematicPlayback.cs),
  and [`SceneExport.MorphAnimations.cs`](../src/Features/Export/SceneExport.MorphAnimations.cs).
- JadeView's adapter documents its scope: [`CinematicGameScriptEventAdapter.cs`](../src/Features/Cinematics/CinematicGameScriptEventAdapter.cs)
  adapts serialized EVE calls and does not run ordinary AI callbacks.
- Reproducible archive report: ignored local artifact
  `artifacts/sfxsun-discovery-audit/FF0045A2-3500B8EA-script-graph.json`.
- The BF archive and source establish the configured relationships and the
  remaining unsupported callback path for this shield. They do not show that
  every relevant callback executed in a retail game session or establish exact
  rendered alpha parity.

The bounded preview evaluates this shield's save-flag-gated registration when
the saved-flag scenario is known, follows the callback path active at the
playhead, and replays ordered scalar assignments while preserving OVA fields
across callback transitions. A callback that skips a guarded write leaves the
prior field value intact, matching instance-state semantics. Unknown save flags
or ambiguous callback order remain unresolved. `AITrackCurChangeNow` replaces
the active callback function; each callback's writes are sampled only within
its recovered active interval, while values already stored in OVA fields persist
until another executed write changes them.

## JadeView implementation status

The generic EVE `OBJ_AnimMorphFactorNoKey` evaluator resolves actor root/module
targets and static type-4 morph objects. The shield controller is a static
object in the assembled cinematic world, with geometry `F3000E74` and a type-4
modifier, so its controller morph is sampled, rendered and exported. JadeView
now also previews a bounded subset of source-backed AI callback effects: it
evaluates uniquely recovered callback entry and exit transitions, their
supported capability/message/OVA/saved-flag predicates, and applies linked
channel-0 progression plus paired visibility changes only during the callback
interval. State changes are held after callback exit unless later EVE commands
supersede them. Unknown or unsupported gates stay unapplied. Flag 2452 remains
an explicit user scenario assumption for branch-specific effects; it is not
read from the BF archive. The fresh real-archive probe now resolves the common
initial hidden state for both unknown and explicitly clear flag scenarios:
the unknown probe has dome flags `01600000` at time zero, while the explicit
clear path is hidden at time zero and visible with progression 1 by 55.1 s.
When the flag remains unknown, branch-specific opening remains unresolved.

The script inspector correlates a current-object `OBJ_GetChannelProg` result
with `OBJ_SetChannelProg` on a uniquely resolved object reference and displays
the callback route, branch conditions, and target. During preview, the bounded
state reducer uses this same relationship to drive the physical dome from the
controller and apply source-confirmed visibility effects. It does not run the
AI scheduler or arbitrary callback code. The registered per-object RLI draw
hooks are now evaluated for this bounded path, but exact retail scheduling and
full material/shader parity remain unsupported.

The Cinematic Timeline's collapsed **Script-driven visual state** section now
reports whether each channel-transfer target has at least one valid morph-group
reference. A transferred channel with no valid group can change preview state,
but cannot deform the target mesh; this is reported as a data limitation rather
than presented as a visible animation. The lighthouse dome's channel 0 falls
into that case. This diagnostic is generic and does not infer missing geometry
or substitute a different object.

The retail graph audit now includes `BoundedCallbackPlaybackProbe`, which seeks
the clip at 0, 49.75, 50.65, 55, 55.1, 55.8, 60.1 and 61.35 s with saved flag 2452
unknown and explicitly assumed clear. It reports the controller and dome's
channel-0 state and control flags. This exercises the preview reducer against
the retail archive and confirms the common initial hidden state plus
assumption-dependent opening; it does not claim that rendered alpha or timing
is identical to the game. Re-run
it with:

```powershell
dotnet run --project tests/ExportVerification -- --cinematic-script-graph-audit "<path-to-sally_clean.bf>" FF0045A2 3500B8EA
```

## Verified playback behavior (2026-10-25)

The authored physical dome starts with flags `01600000`, including JadeView's
invisible bit `0x00200000`; the cinematic reset preserves this source flag and
the renderer culls the object. If it appears at clip start, first check that the
selected object is physical key `3D002D7D` rather than the separate
`Cine_SFX_Dome_Phare` key `3500DF23`, and that **Show hidden / conditional
objects** is disabled.

The callback's reveal is branch-dependent on saved flag 2452. With that flag
unknown, JadeView conservatively leaves the dome hidden. Under the explicit
clear-flag scenario, the callback reveals it around 55.1 s but copies
progression 1. The physical dome has no channel-0 morph groups, so this cannot
deform it. The controller's channel-0 progression stays at 1 through 60.0 s and
reaches 2 around 60.1–60.25 s; only its own geometry changes. The BF therefore
does not encode a gradual 50–60 s morph opening on the physical dome.

JadeView resolves the callback's serialized `des_o_dome` target, checks its
registration gates when they are supported, transfers channel progression,
and carries OVA fields across callback transitions. The regression fixture in
`CinematicScriptRliDisplayCallbackChecks` verifies that a later callback which
skips a guarded write preserves the value from the previous callback. The
preview does not run the full AI scheduler or arbitrary callback code. The
paired RLI draw hooks are evaluated for the bounded, resolved path, but the
retail material blend and the visual meaning of the packed RLI alpha remain
unverified.

At 60.1–61.35 s, the alternating dome commands are discrete hide/show events.
They are distinct from the callback's per-draw RLI override and from changes to
the material's packed color. `OBJ_AnimMorphFactorNoKey` carrier semantics are
confirmed in Iris and Jaded; exact retail appearance still depends on runtime
state and renderer behavior.

## Material-color and draw-callback distinction (2026-10-25)

The apparent fade is made of separate mechanisms. In
`sfx_bouclier_phare_sbp_disparition`, Iris blends `0x00FFFFFF` toward
`0x00444444` with an elapsed-time fraction, then calls
`MATSetConstant(currentObject, blendedColor, 0x43000A8B)` (Iris source around
lines 8472–8488). Jaded's `MATSetConstant` resolves the material key and writes
color slot 1 through `MAT_GetColorPtr`; its implementation selects
`ul_Specular` (`AIfunctions_MAT.c` around lines 156–180, `MATstruct.c` around
lines 773–812). This animates a packed material color, not a dome alpha curve.
JadeView's `MaterialLayerTint.ReadConstant` now reads RGB from the serialized
`ul_Specular` field and supplies alpha 1, matching Jaded's `ColorOp_Disable`
constant-color path (`MATDraw.c` masks `ul_Specular` to 24 bits). The viewport
and static/export baking previously read `ul_Diffuse` instead; their source
constant baseline is now corrected generically. The `constantColor` shader
path treats that tint as opaque, so this does not itself alter opacity in
JadeView. Whether the separate RLI callback changes visible opacity still
depends on unresolved material-pass flags. Script-driven `MATSetConstant`
callback writes are now previewed when their source inputs are resolved; see
the message-state trace below. They change the controller material color,
not the physical dome's opacity.

At a later time, EVE separately toggles the physical dome's visibility between
60.100 and 61.350 seconds. The registered Before/After Display hooks
are a third, temporary per-object RLI override around drawing. These stay
separate in the preview model: material color, RLI draw inputs, and object
visibility have different lifetimes and semantics. JadeView evaluates this
callback's dynamic RLI field with bounded OVA-state persistence. The callback
material-constant evaluator also inspects reachable `MATSetConstant` calls,
but applies dynamic colors only when their scalar inputs resolve. The dome's
material flags are now known; its Add layers do not use vertex RLI alpha as
source opacity in the Jaded renderer path.

Iris PC's `COLORBlend` implementation clamps its interpolation fraction to
`[0,1]` and uses seven mantissa-selected packed-color masks with endpoint
shortcuts at `0.02` and `0.98000002` (`external/irisEng/ai/ai_materialandcolorfunctions.c:989-1045`).
Jaded's shared Jade Engine source has the same packed helper, but its
`AI_EvalFunc_COLORBlend_C` instead blends R, G, B, and A independently as
normalized floats (`external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_COLOR.c:139-190`).
The implementations therefore disagree for intermediate values. JadeView uses
the Iris behavior for the PC-oriented preview path; retail-PC runtime parity is
not yet independently verified. The difference is not guarded by a platform
conditional in Jaded; the two source trees register the function under
different selector IDs. Jaded identifies its source as a 2006 Rayman 4
prototype fork, while Iris contains BGE-specific PC script callers. Neither
source establishes which interpolation the 2003 retail executable uses.
Both constants in this shield call have a zero high byte, but JadeView's
relevant material-color path does not use that byte as opacity. Do not convert
this call into an alpha animation without evidence from the material's render
path.

## Physical dome material and source alpha policy (2026-10-25)

A fresh object/material audit of `SFX_Dome_Phare@5ea4c30.gao` in the supplied
`FF0045A2` container resolves its gameplay-world instance to object
`3D002D7D`, geometry `8F0044E9`, material `8F0044ED`, with 84 instance RLI
colors. Its type-5 material has two identical layers: flags `C0050207`, color
operation 0 (Diffuse), blend mode 5 (Add), and both RGB and alpha write enabled.
This is stronger evidence than the earlier unresolved-material note above.

Jaded's `MATmultitexture.c` sets `MAT_ValidateMask_RLIAlpha` for Alpha and
AlphaPremult layers, and for Copy/Glow layers when the following layer uses
AlphaDest/AlphaDestPremult. It does not set that validation bit for Add or Sub
(`external/jaded/src/libraries/GraphicDK/Sources/MATerial/MATmultitexture.c`,
around lines 771–800). Therefore the dome's additive layers do not consume
vertex RLI alpha as source transparency in this source path. Their packed RLI
color can still affect the color contribution; that does not make it a smooth
opacity animation. The script's `MATSetConstant` writes the material's
specular/constant color, while these dome layers use Diffuse color operation;
it is not an alpha setter and does not establish an opacity fade for these
layers.

JadeView's renderer now follows this source policy when deciding whether a
material layer consumes instance RLI alpha. Alpha/premultiplied-alpha layers,
alpha-tested layers, and Copy/Glow layers paired with a destination-alpha pass
use the RLI alpha channel; Add/Sub layers do not unless the material explicitly
uses local alpha through its own flag. The policy is covered by
`CinematicMaterialChecks.VerifyRliAlphaPolicy` and was run through the full
`ExportVerification` suite.

The initial hidden state is a separate concern from material alpha. Iris's
shield init hides or destroys the linked physical dome according to save flag
2452, and the archive reducer resolves it hidden at the clip start for either
flag outcome. JadeView's **Show hidden / conditional objects (debug)** option
bypasses hidden-state checks; it defaults off. If the dome is visible at time
zero with that option off, the remaining defect is in initialization/state
application or an alternate visual object, not an authored alpha key. The
controller still has no gradual morph key between 50 and 60 seconds in the
current archive sample; its geometry switches from the zero-delta group to the
deforming group around 60.1 seconds.

There is an important capture caveat: `CinematicFrameAudit` sets
`ShowGameHidden = true` when a focus object is requested, so focused per-object
frame images intentionally bypass hidden-state visibility. The focused capture
at `0.000 s` showing the dome is therefore not evidence that normal cinematic
playback renders it at startup. A startup-visibility comparison must use the
full-scene audit without a focus object, or the app with its debug visibility
option off.

## Cinematic message and fade timing evidence (2026-10-25)

The cinematic event list contains a `MSGSendAll` to controller
`10004AE8` at 50.650 s with message ID 1 and `Float2 = 0.5`. The controller's
`sfx_bouclier_phare_sbp_msg` reader copies that value to `des_f_time1` and sets
`i_cine = 1` (Iris source around lines 8659–8683). The same event list sets
controller capabilities 1, 2, and 4 at 55.000, 55.100, and 55.800 s. In the
cinematic branch, capability 4 leads `sfx_bouclier_phare_sbp_wait_eteind` to
record the current main-clock time as `f_time` and transfer to
`sfx_bouclier_phare_sbp_disparition` (around lines 8608–8637). That function
uses `des_f_time1` as its `COLORBlend` duration, so the source data bounds the
material-color transition to roughly 55.800–56.300 s, subject to callback
dispatch on the scene clock. The discrete physical-dome visibility toggles
from 60.100–61.350 s are a later, separate event sequence.

This narrows the timing diagnosis: an apparent change at 60–65 s should first
be checked against dome show/hide events and RLI draw callbacks, not attributed
to the `MATSetConstant` color transition. JadeView previews source-reachable
callback `MATSetConstant` writes when the owner, material target, callback route,
and enclosing branch are uniquely resolved. The shield's init write is guarded
by saved flag 2452; an unknown initial save value remains unresolved instead of
being guessed. The v6 archive trace below now resolves the clock-derived factor,
message-derived duration, and packed color samples. It demonstrates a color
transition on the controller material, not a physical dome alpha fade; renderer
parity and native conditional-object activation remain separate questions.

## Callback material-color trace and apparent opening (2026-10-25)

The current v6 real-archive trace was generated from `FF0045A2 / 3500B8EA`
with the explicit scenario `ARRBit 2452 = false`, filtered to controller
material `43000A8B`. It distinguishes callback-window recovery from actually
evaluating the packed color:

The table below preserves an earlier callback-window-only trace. Its
`color unresolved` cells were superseded by the v6 value-flow correction and
should not be treated as the current color result.

| Playhead | `sbp_apparition` (line 8978) | `sbp_disparition` (line 8486) | `sbp_init` (line 7922) |
|---:|---|---|---|
| 55.5–55.7 s | recovered window `[50.650,55.000]`, branch true, color unresolved | starts at 55.800 s | `[0.000,50.650]`, branch true, literal `0x00444444` |
| 55.8–56.5 s | prior window, color unresolved | recovered window `[55.800,open)`, branch true, color unresolved | initial window, literal `0x00444444` |

The disappearance expression depends on v5 = (MainClock - LockTime -
f_time) / des_f_time1 and passes the result of COLORBlend(0xFFFFFF,
0x444444, v5) to the setter (ai_compiledscripts_sfx.c:8472–8486). The
source message reader copies message f2 into des_f_time1 (sbp_msg,
lines 8671–8677); the EVE event queues that message for the controller at
50.650 s with f2 = 0.5. The callback transition from sbp_wait_eteind
sets f_time immediately before replacing its AI track (source around lines
8626–8629). JadeView now resolves both values from the source-indexed
message-reader write and the uniquely targeted MSGSendAll, then seeds the
callback clock from the actual AITrackCurChangeNow call site. This is
necessary because the separate callback-path index's assignment line points
to function resolution rather than the transition's execution line.

The v6 real-archive audit resolves controller material samples from
0x00FFFFFF at 55.8 s through 0x00D4D4D4, 0x00AFAFAF, 0x00898989,
0x00656565, and 0x00444444 at 56.3 s. This confirms the scripted color
transition in JadeView's bounded evaluator. The result remains opaque and
the setter targets controller material 43000A8B, not dome material
8F0044ED; it is not the physical dome's alpha animation.
The color writes address controller material `43000A8B`, because the source
calls `MATSetConstant(AI_gpst_CurrentGameObject, ..., 0x43000A8B)`. The
physical dome `3D002D7D` uses material `8F0044ED`. Do not interpret the
controller's color blend as a dome-alpha animation. The physical dome's
visibility instead comes from `sbp_init` setting its invisible flag and
`abp_apparition2` clearing that flag; the latter also copies controller
channel-0 progress to the dome. The dome has no channel-0 morph groups, so
copied progress cannot reveal an opening deformation. The controller's own
geometry has a zero-delta group at progress 1 and a deforming group at
progress 2; the sampled cinematic state stays at progress 1 through the
opening window and switches to 2 only around 60.1 s. Thus the current archive
does not encode a gradual 50–60 s geometric opening of either the physical
dome or controller geometry.

The apparent initial dome is also not established by focused frame captures:
`CinematicFrameAudit` forces hidden objects visible when a focus object is
requested. For the physical dome, source flags include the invisible bit and
the bounded reducer resolves it hidden at t=0 under both unknown and clear
save-flag scenarios. A full-scene capture with **Show hidden / conditional
objects (debug)** off is required to establish whether the visible object is
this physical dome or the separate `Cine_SFX_Dome_Phare.gao` (`3500DF23`).

The reported later “alpha-off” near 60–65 s should be checked against the
physical dome's discrete EVE hide/show events at 60.100–61.350 s and the
controller's RLI display hook separately. The physical dome's type-5 material
has two identical Add layers (`C0050207`); Jaded's Add path does not validate
vertex RLI alpha as source opacity. The controller callback's negative RLI
factor at channel progress 1 likewise does not implement a conventional alpha
fade. These findings explain why the current data path predicts a visibility
pop/flicker rather than a smooth alpha reveal, but they do not prove exact
retail renderer behavior.

Reproduction artifact (local, ignored):
`artifacts/cinematic-message-state-v6/audit/cinematic-script-material-FF0045A2-3500B8EA-43000A8B.json`.
It records source-owner calls and per-sample callback traces; it is generated
by JadeView's bounded evaluator, not a retail-game capture.

## Callback clock and fractional predicate correction (2026-10-25)

The source-to-callback handoff audit exposed two general evaluator gaps. The
`apparition0` callback writes `f_time = TIM_gf_MainClock - TIM_gf_LockTime`
immediately before its unique `AITrackCurChangeNow` call, and its enclosing
gate is true at 50.650 s because the clip has set capability `0x100`. The
material callback now carries that uniquely proven callback-entry clock into
the next function. Ambiguous writes or dispatches remain unresolved.

That clock alone did not resolve the fade: the scalar branch evaluator had
discarded non-integer OVA values and did not parse decimal literals such as
`0.0`. It therefore could not take the `else` branch for `des_f_time1 == 0.0`
when the message supplied a duration of `0.5`. The general predicate evaluator
now preserves finite fractional values. A real-archive audit then resolved
the controller material (`43000A8B`) from `0x00444444` at 50.650 s to
`0x00FFFFFF` at 51.150 s; its scripted blend reverses from 55.800 s and reaches
`0x00444444` at 56.300 s. This setter keeps alpha at 1 and targets the
controller material, not the physical dome material `8F0044ED`; it is not the
physical shield's alpha animation.

The source/archive evidence still does not show a gradual 50–60 s deformation
of the physical dome: its channel has no morph groups, and the controller's
recorded progression remains at 1 until a later jump to 2. Therefore this
parser correction fixes missing callback color evaluation, but does not by
itself prove that the shield-opening animation is now visually correct. The
initial physical dome is predicted hidden when conditional-object debug
visibility is off; a full-scene UI check is still needed to identify whether
the reported startup shield is the separate `Cine_SFX_Dome_Phare.gao` or a
debug-focused capture.

Reproduction artifact (local, ignored):
`artifacts/callback-entry-time-audit-final/cinematic-script-material-FF0045A2-3500B8EA-43000A8B.json`.
It contains JadeView's archive-derived samples, not a retail-game capture.
