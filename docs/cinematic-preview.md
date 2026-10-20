# Cutscene preview and export

Use **Cutscenes** in the top toolbar to switch from the asset browser. Choose a
cinematic scene, select a shared source clip, then use Play, Restart or the
timeline slider. Matching actor tracks, facial controls and the animated source
camera use the same clip time. **Export synchronized actors** creates a GLB with
the selected actor animation, animated scene objects, camera transform and
sampled facial morph curves for Blender.

Actor transforms, camera motion and facial weights for the same source clip are
exported as a single glTF animation. Blender therefore imports them into the
same animation track, so selecting the cutscene enables all participants together.
Manual morph previews remain separate actions.
Enable **Include game-hidden objects** when exporting actors that appear later
than the current playhead. Visibility commands are retained as source metadata;
their hide/show timing is not yet baked into Blender animation.

Cinematic actor root tracks use absolute scene coordinates. Gameplay root motion
continues to use offsets from the initial pose. The Camera section selects an
animated source camera. Literal position/target references from camera message
60 take priority, including helpers whose names do not contain `Camera`.
Each receiver retains its own view, even when several receivers share a marker.
Position and target switches follow the timeline; references can point to actors.
A null target preserves the source orientation. Instant cuts apply local offsets
without object scale. `Camera` / `Cible` name pairing remains a labelled fallback.
Repeated references reset positional delay as in the native camera controller;
shake messages are kept separate from lens commands.
Native positional transition dynamics, offsets during those transitions and shake
are not reconstructed. Supported source lens commands follow the shared clock.
GLB exports bake routed views into separate camera nodes, preserving actor motion.
Hold RMB or disable **Follow source camera** to
inspect the scene freely; enable it again to return to the selected source camera.

Interpolation uses decoded `EVE_InterKeyType_Time` factor curves when present,
shared between actor bones, root movement, camera/object and facial-helper
sampling. These integrate piecewise-constant factors over event time; they are
not linearly interpolated speed controls. Export samples include curve-break
timestamps. Key timestamps and clip durations are derived from integer source
frame totals, avoiding float accumulation that could delay commands by a frame.
Quadratic and cubic translation payloads store the starting position first,
followed by Bezier controls. The decoder preserves this native order for bodies,
scene objects, cameras and exported animations; reversing it causes backwards
travel and speed spikes near curve boundaries.

Scene assembly includes actors referenced by source tracks or literal message
object arguments, scoped to the selected cinematic world, and the matching
environment world (`<scene>_cine` / `<scene>`), rather than every inventory/menu
setup world. Environment matching currently follows these world-name roles;
cross-container scenery streaming is not reconstructed.

Runtime visibility uses the saved GAO control flags and literal
`OBJ_FlagsControlSet`, `OBJ_FlagInvisibleSet` and `OBJ_FlagInactiveSet` commands.
The two-mask `OBJ_FlagsControlSet` operation follows the engine formula: apply the
set mask, then clear the requested bits from the previous flags.
`GRP_FlagInvisibleSet` and `GRP_FlagInactiveSet` visit the explicit source group
table, including actor roots listed there. Referenced groups also contribute
scene dependencies. Group commands do not implicitly recurse into nested groups.
Seeking rebuilds this state from the start. Actors unused by the selected clip
are excluded by default; **Show hidden / conditional objects (debug)** reveals
them for inspection. The overlay is removed when leaving Cutscenes. Arbitrary
script conditions, spawning, particle scheduling and the full script VM remain
unsupported; an inactive flag alone is not treated as an invisible flag.

Literal `MAT_MT_EnableLevel`, `MAT_MT_SetLocalAlpha` and `MAT_MT_SetAlphaTresh`
commands now control viewport material passes on the source clock. Native material
keys and submaterial indices are resolved separately; alpha and threshold retain
their native five-bit/six-bit packing. Seeking reconstructs the state without
changing source bytes. These commands are not baked into GLB material animation.
The local-alpha value is read from bits 11–15 and normalized as a five-bit value;
additive passes also attenuate RGB because their native `ONE/ONE` blend factors do
not use framebuffer alpha. Global RLI colors are still only an approximation: the
game changes the RLI destination color/factor from effect callbacks as well as its
world-cycle controller, and the viewport does not yet execute those callbacks.

Environment controllers are discovered through native `world_cycle` entry points
and typed object variables. Their active sky, environment and fog references are
included in assembly. The preview reads instance-overridden day, dawn, sunset
and night palettes, then applies the source packed-color interpolation using the
sun object's current Z position to the referenced sky/environment material
constants. It also previews the controller's native local-alpha fade on environment
submaterial 0, texture level 1 (night starts at 127/255, dawn blends to full opacity).
The controller's RLI destination color and time-of-day factor are also applied to
vertex-color material modes using the engine's subtractive lighting path. Saved
planet rotation/hour and attack state are not restored; realtime lights, water
and fog parameters are still not simulated, so full lighting parity is pending.

Generated SPG sprites retain their interpolated vertex alpha, including vertices
beyond the carrier's RLI array. This fixes the opaque fallback used for procedural
mist in `3500BB72`. Native post-lighting colors, special fog/distance fade, wind
and runtime random density are still only partially reconstructed.

The preview recognizes the generic `Onde_de_choc_cine_ODCc` brain by its source
function references. It evaluates the scripted wave zoom and transparency while
scrubbing, including the temporary RLI/material-constant changes. The
`ARR_GlobalIntFlagSet(2718)` event also blends the world-cycle attack palettes;
this changes the sky and environment tint during the attack. The source
randomizes wave size and speed, so preview uses the authored radius and a stable
midpoint speed to remain deterministic.

`IO_PafSet(100, 8)` is a controller haptic command in the engine
(`s_JoystickPafIntensity`), not a screen filter or post-processing effect. A
visual overlay requires a separate screen-effect event; this command alone cannot
produce one. Source references include
`external/irisEng/ai/ai_bge/ai_compiledscripts_sfx.c`,
`external/irisEng/inputoutput.c`,
`external/irisEng/ai/ai_bge/ai_compiledscripts_global_mapmanager.c`, and
`external/irisEng/ai/ai_lightfunctions.c`.

The `SFX_Flash` brain is now discovered by its native behavior function and its
typed OVA instance/default data. `OBJ_CapaSet` events drive its `0x100`
capability on the clip clock, including deterministic fade-in/out when scrubbing.
Its runtime `GFX_Add(18)` is Jade Engine's `GFX_Sun` ray effect, not a serialized
mesh or PAG generator. JadeView previews its authored color, ray count, sizes and
snap position as a stable screen-space ray burst. Ray jitter, native lighting
compositing and transient GFX objects are approximated; these procedural rays are
not included in GLB export, and this support does not execute arbitrary scripts.

Body poses also follow source events: `ACT_KitMerge` / `ACT_KitMergeOver` bind
action slots, `ACT_ActionSet`, `ACT_ActionFrameSet` and `ACT_ActionFrequencySet`
select skeletal clips, and literal type-47 cinematic messages supply the initial
action. Kit slots retain their serialized indices, including empty slots. Action
items use their source repetition count, frequency and return-item index. Their
bone poses are evaluated before the absolute cinematic root transform, including
when seeking or exporting. Repeated requests for a running action preserve its
phase; forced frame/frequency requests restart it. Missing bindings appear under
**Body animation bindings**. Dynamic expressions, AI-dependent transitions and
the full action blending/state machine remain outside this evaluator.

Retail event literals can contain compiler annotations in the middle tag byte;
the END row's unused value can also be nonzero. These are accepted while dynamic
operands remain unsupported. Type-47 mode zero ends the current cinematic action:
the preview holds its last pose and reports a gameplay handoff rather than
looping it indefinitely. The subsequent gameplay brain is still not simulated.
Source root motion now accumulates across action items/repetitions in the actor's
orientation, between absolute cinematic positioning commands. Empty track
intervals no longer sample future transform keys. This is a deterministic preview
of animation movement, not a reconstruction of collision, dynamics or native
action-transition blending; those still require comparison against the game.

Facial playback uses source `OBJ_SetModuleChannelData` events (selector 8263),
`OBJ_AnimMorphFactorNoKey` helper tracks (selector 8262), and supported type-53
mouth-controller messages. The helper event identifies its target GAO, module
and channel; its animated X coordinate supplies the morph factor and Y plus one
supplies channel progression. This is resolved from each scene's event data,
not from character names. Morph appearances with no active deltas are replaced
by an available appearance with real morph targets before playback/export.
The binding event and helper transform may be on separate tracks: the complete
helper transform is sampled continuously for the binding interval, including
intermediate frames and reverse seeks. Participating actors use only the
cinematic clock, preventing the viewport from advancing mouth cycles a second
time or during pause. **Facial animation bindings** lists resolved helper signals
and mouth commands, and reports unavailable mouth-controller bindings.

**Audio** resolves literal sound-modifier play/stop calls through each modifier's
direct resource key or serialized `.snk` bank slot. PCM/MS ADPCM WAVE sources
are decoded in the background and mixed on the shared clock. Play/Pause,
Restart and scrubbing control the stream; use **Enable source audio** and the
volume slider. The sound bank currently uses the language already loaded in
the scene stream (the verified intro fixture contains French dialogue).
Music/ambience loops and explicit stops are supported. Native spatialization,
reverb, dynamic sound requests and language switching are not reconstructed.
Audio is currently a preview feature; GLB does not embed this PCM mix.

Facial controls and audio share source time, but the phoneme schedule, runtime
script VM and message queue are not reconstructed. The exported mouth forms follow the
game's authored facial control tracks and deterministic mouth controller, but
are not claimed to be audio-phoneme-accurate. Camera position and rotation
come from the source track; its GLB lens/projection is a reasonable default
because the game's projection parameters are not recovered. Unrecognized
event layouts may still have incomplete facial motion.

The current verification fixture `FF005833` (`00_02_home_decryptage_cine`)
exports non-neutral facial curves for Sally, Pey'j and Secundo, plus animated
actor rigs and source cameras. `tests/ExportVerification/blender_cinematic_check.py`
checks that these tracks import into Blender.

`FF0045A2`, clip `3500B8EA` (`00_03_dehors_maison_Intro_cine`), is the audio and
conditional-visibility fixture. `--cinematic-audio-audit <archive.bf> FF0045A2
3500B8EA` writes a source-timed WAV and a JSON cue/state report for offline checks.

### BF00218E playback corrections (2026-10-20)

Camera/object sampling now holds the last authored component across empty
intervals instead of resetting to the original scene matrix. Body root-motion
sampling deliberately keeps empty intervals free for accumulated movement.
The renderer skips a second pose evaluation for cinematic-owned actors.
Scene assembly prefers the cinematic copy of same-key props while retaining
components belonging to separate actor instances. The retail fixture now has
282 objects and no duplicate GAO keys (previously 286 objects, four duplicates).

Build, synthetic playback checks, the retail export and Blender import pass.
These checks do not establish visual parity. Pey'j's departure at approximately
36 seconds uses action 208/BF0021AC, which encodes about 4.952 m of root travel
over 5.8 seconds at frequency 64. Its pelvis also contains that travel in the
animation reference frame. Direct skeleton children now receive the inverse
sampled action-root transform before hierarchy composition, matching native
Flash-to-MagicBox conversion. This prevents counting translation and rotation
twice, including skeleton roots with an implicit (zero) parent. Deeper bones
keep their ordinary parent-local coordinates. No character-specific speed
adjustment has been introduced. Native transition blending and physics remain
incomplete; visual comparison against gameplay is still required.

### Source action transitions

Action-item transition tables are decoded automatically. A request may point to
an intermediate animation: the timeline plays that animation before the requested
action and cancels its pending destination when a new command or release arrives.
Repeated requests for the pending action preserve the bridge. Per-item and incoming
default rules are supported; no character-name rules are used.

Transition blend frames/flags are retained. Skeleton poses now use the source
stock-pose recurrence: each fixed 60 Hz step blends the previous result towards
the incoming pose with increasing weight. Fractional times interpolate adjacent
evaluated steps, so playback, reverse seeking and GLB export share one result.
Interrupted transitions start from the preceding blended pose. Explicit cinematic
bone tracks are applied afterwards. Blend evaluation is in its own sampler module.

This is a deterministic reconstruction, not confirmed native runtime parity.
Root translation also blends while retaining the incoming rotation and accumulated
position; FreezeMagicBox and dynamics-driven modes bypass this blend. Progressive
transitions transfer the normalized outgoing animation phase, forward or inverse,
to the incoming animation. Explicit nonzero start frames override this mapping;
fractional frames are retained. Kit-wide defaults, per-bone blend exceptions and
vector-based translation blending still need further verification.
Native stock blending can depend on runtime frame cadence; this player uses 60 Hz.

### Intro movement and camera shutter

In 3500B8EA, script commands change actor scale and slow Jade/Fehn to one source
frame per second during the snapshot moment. These commands now affect both
preview and exported body motion; they do not restart the action.

The native Cine_FigeleMonde controller is recognized through its engine code
references and reads close/hold/open durations and center from the BF variables.
Its capability/activation events drive a rotating octagonal shutter in the
viewport. This is a preview screen effect; GLB does not carry it. General script
execution, world-AI pause scheduling and framebuffer photo capture remain outside
the current player. Runtime comparison is still required for full native parity.

Particle generators retain their carrier nodes in GLB; the particle simulation
is not exported. Animation states with zero-scale parents retain evaluated local
bone transforms rather than requiring an inverse of a singular world matrix.

SpeedFromDyna actions retain body animation and rotation while contributing no
animation-driven root translation, as in the native player. Their external
dynamics/AI movement is still not simulated. In the intro, the JADE graphic is a
separate scene mesh and must not be confused with the brief source shutter at
about 43.17 seconds. Its previously small framing was caused by missing lens
commands; its geometry scale is unchanged.

Global message-50 alpha fades are supported when the receiver's native script
role is confirmed. Their five timing phases and colors come from the source;
the intro's four camera flashes now cover the entire viewport. Alternate fade
materials, higher-priority script arbitration and GLB screen-effect export are
not supported yet. This does not change the size of the separate JADE mesh.

### Source camera lens

Native message 60 stores the transition duration (`f2`) at byte 32 and the lens
angle (`f1`, radians) at byte 36. These were previously mistaken for each other.
The player now evaluates cuts, lens ramps and interrupted ramps on the source
clock. Zero lens commands retain the preceding angle; invalid values are ignored.
Horizontal FOV converts to vertical FOV for the actual viewport aspect, and
leaving source-camera follow restores the editor lens. Native screen-format
constraints, positional smoothing and the full camera script remain incomplete.

GLB exports lens animation using `KHR_animation_pointer` on camera `yfov`, in
the same animation as bodies, faces and camera motion. Blender 5.2.2 import was
verified on a synthetic ramp and the full intro; importers without this extension
retain the initial lens. Raw commands and sampled lens metadata are retained.
Export uses a 4:3 reference aspect; match Blender's render aspect for that framing.

Camera routing is checked with synthetic source switches and a retail
`3500A438` export imported into Blender 5.2.2. The verification script
`tests/ExportVerification/blender_camera_motion_check.py` compares imported
camera position/orientation with GLB samples. This confirms export consistency;
comparison with retail playback is still needed for native camera dynamics.
