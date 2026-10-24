# Cutscene preview and export

Use **Cutscenes** in the top toolbar to switch from the asset browser. Choose a
cinematic scene, select a shared source clip, then use Play, Restart or the
timeline slider. Matching actor tracks, facial controls and the animated source
camera use the same clip time. **Export synchronized actors** creates a GLB with
the selected actor animation, animated scene objects, camera transform and
sampled facial morph curves for Blender.

Large cinematic scenes upload visible geometry progressively to keep frame time
bounded. The renderer now always attempts at least one uncached visible geometry
upload per frame before applying its 2 ms upload budget; otherwise a long scene
preparation pass could defer every new mesh indefinitely. On
`FF0045A2 / 3500B8EA` at 15.18 s, the frame audit's warm-up fell from 149 to 43
frames and then rendered all 30 visible objects from 44 cached geometries. This
addresses incomplete first-playback frames caused by geometry upload starvation;
it does not claim full parity for every scripted visual effect.

Actor transforms, camera motion and facial weights for the same source clip are
exported as a single glTF animation. Blender therefore imports them into the
same animation track, so selecting the cutscene enables all participants together.
Manual morph previews remain separate actions.

Body-action playback combines actions embedded in the cinematic clip with calls
from explicitly included script-owner EVE tracks. At one timestamp, `ACTKitMerge`
and `ACTKitMergeOver` are applied before body-action requests so a slot added by
the selected EVE owner can be resolved at the clip's first frame. Iris source
confirms that these functions mutate the actor's action kit; exact execution order
between independent clip and EVE tracks at equal timestamps is not yet verified
against the retail scheduler. This is a deterministic preview rule, not proof that
the game activates that owner. Timeline intervals are bounded by the next action
handoff without truncating the outgoing root-motion sample used to calculate a
native-style transition blend.

### Inspecting script links

In the **Cinematic Timeline** panel, scroll to **Script owners and event links**
and select an owner to inspect its serialized variables, source-reachable Iris
callbacks, event calls and static trigger findings. In **Iris source evidence**,
resolved object-reference rows are clickable when the referenced GAO also has a
script owner in the loaded archive; selecting one opens that owner's callbacks
and event data. Function references show their source path and, when available,
the resolved function key and dispatch site. These links establish configured
or source-reachable relationships, not that the game scheduled or executed the
callback. The full script VM and current save state are not simulated.
Script-owned EVE previews can expose a **Flag** start action when the selected
clip is an independent source whose direct events satisfy the owner's
recognized start-flag tests. A callback-linked owner cannot use its own
`SceneEvents` clip as a start signal: that clip is decoded from the same event
resource the callback would start, so its writes are outputs of the list, not
evidence of prior activation. Those same writes are excluded from that owner's
gate-state estimate; prior save/runtime flag values stay unknown. The preview
combines an independent clip signal time with source-configured `Cine_Events`
delays and applies the resulting offset to the owner's linked channels;
synchronized export uses the same offset. When a `Cine_Events` owner has a
positive `des_f_duree_totale`, preview and synchronized export stop its tracks
at that duration after the assumed/estimated start. A zero duration maps to the
Iris sentinel timeout and does not impose a practical stop. This is a
what-if alignment for the authored data, not proof that the owner was active,
that the save already held the required initial flags, or that the callback ran.
Iris also stops playback if owner custom bit `0x4000` clears before the timer
expires. JadeView does not yet synthesize that early stop from other scripts,
because the bit can be changed by callbacks and event tracks outside the
selected preview's resolved timeline. Jaded's generic `TRAStopAll` clears each
track's running state without resetting its event cursor, and Jaded advances
EVE before AI callbacks; a callback-issued stop therefore affects a later EVE
pass. JadeView uses the authored timer deadline as a deterministic preview
boundary and does not add a guessed frame of latency. Exact retail callback
cadence and early-exit state remain runtime-dependent. Iris's interruption
callback can clear that bit when another owner wins the priority check or when
all enabled exit tests pass (collision, actor activity, capabilities and/or a
saved special flag); these tests require live world/save state and are not
reconstructed by the preview.
Enable **Include game-hidden objects** when exporting actors that appear later
than the current playhead. Visibility commands for scene props and actor
components are sampled into the GLB animation by collapsing/restoring the
renderable node's scale at source event boundaries. This includes
`OBJ_FlagInvisibleSet` / `OBJ_FlagInactiveSet` control state and bit `0x8`
changes from `OBJDrawMaskSet`; meshes hidden in the source mask are still
included when a selected cinematic clip changes that bit. Actor-component
visibility is isolated on a render child so hiding a mesh does not scale its
skeleton joint. Actor-root visibility uses a wrapper above the rig so the
skeletal joint transforms remain intact. Other draw-mask bits update the
viewport's effective visual flags during playback but their per-frame
transitions are not serialized to glTF. Script-spawned objects and
material/lighting callbacks are not baked yet; the original event records
remain available as source metadata.

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
Disable **Follow source camera** to control the viewport camera manually. Hold
RMB to look around from its current position; use WASD/QE to move, Shift to move
faster and the wheel to change movement speed. The handoff synchronizes yaw and
pitch with the followed view and restores a world-up basis, so manual rotation
uses the editor's normal free-look controls even when the source camera is rolled.
**Reset viewport view** restores the Frame All camera snapshot captured after the
cinematic world loads, before the source camera takes control, and keeps Follow
disabled. Enable Follow again to return to the source camera at the current
playhead. Releasing RMB does not warp the cursor back to its capture point.

Animation and body-action lane labels in the Event Timeline select their
uniquely resolved GAO in the cinematic scene. Clicking one of its timeline
segments also selects that GAO and seeks to the segment start. Ambiguous or
unresolved object keys remain non-selectable; ordinary timeline scrubbing still
seeks to the pointer time.

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
object arguments, serialized owner object references, and uniquely resolvable
literal GAO references in source-reachable owner callbacks. Source constants are
resolved only when their object key is unique across the loaded archive; ambiguous
cross-world keys are left out. A source-referenced object is only included as a
scene dependency; this does not execute its callback or prove its branch became
active. Nonresident actors remain hidden by default and can be inspected with
**Show hidden / conditional objects (debug)**. Assembly is scoped to the selected
cinematic world and the matching environment world (`<scene>_cine` / `<scene>`),
rather than every inventory/menu setup world. Environment matching currently
follows these world-name roles; cross-container scenery streaming is not
reconstructed.

Runtime visibility uses the saved GAO control flags and literal
`OBJ_FlagsControlSet`, `OBJ_FlagInvisibleSet` and `OBJ_FlagInactiveSet` commands.
`OBJDrawMaskSet` separately updates the visual object's draw mask using
`(oldMask | setMask) & ~clearMask`. Bit `0x8` affects rendering visibility;
other mask bits are passed to the viewport renderer. The native renderer also
uses a per-frame allowed-mask intersection, so this preview does not reproduce
all platform-specific draw-mask filtering.
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

Literal `MAT_MT_EnableLevel`, `MATEnableMltxtLvlMT`, `MAT_MT_SetLocalAlpha` and
`MAT_MT_SetAlphaTresh` commands now control viewport material passes on the
source clock. The `MT` variant resolves the explicit multi-material child and
texture-level index; alpha and threshold retain their native five-bit/six-bit
packing. Seeking reconstructs the state without changing source bytes. These
commands are not baked into GLB material animation.
The local-alpha value is read from bits 11–15 and normalized as a five-bit value;
additive passes also attenuate RGB because their native `ONE/ONE` blend factors do
not use framebuffer alpha. Global RLI colors are still only an approximation: the
game changes the RLI destination color/factor from effect callbacks as well as its
world-cycle controller, and the viewport does not yet execute those callbacks.

Environment controllers are discovered through native `world_cycle` entry points
and typed object variables. Their owners and referenced sky, environment and fog
objects are included when they belong to the cinematic, matching base, or
environment-variant world; initially inactive controllers remain available so a
later explicit event can activate them. The preview reads instance-overridden day, dawn, sunset
and night palettes, then applies the source packed-color interpolation using the
sun object's current Z position to the referenced sky/environment material
constants. It also previews the controller's native local-alpha fade on environment
submaterial 0, texture level 1 (night starts at 127/255, dawn blends to full opacity).
The controller's RLI destination color and time-of-day factor are also applied to
vertex-color material modes using the engine's subtractive lighting path. Saved
planet rotation/hour and attack state are not restored; realtime lights, water
and fog parameters are still not simulated, so full lighting parity is pending.

The controller owner is checked against its current preview control flags on
every evaluation. A decoded literal `OBJ_FlagInactiveSet(0)` can therefore
activate the controller during the cinematic, and `OBJ_FlagInactiveSet(1)` can
deactivate it again; reverse seeking restores the serialized owner state. This
is limited to explicit clip or joined `SceneEvents` commands. It does not infer
that an owner callback or saved-flag gate ran, and GLB export does not animate
this world-cycle controller state.

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
mesh or PAG generator. JadeView previews authored color, paired big/small rays,
ray size, length ratio, snap position and deterministic fades. Iris sets GFX
flag index 2 on this effect; Jaded maps that bit to
`GFX_Flare_DepthChangeSize`, so `GFX_Sun_Render` draws the rays at the serialized
world position. The configured extrusion parameter is not used by that render
branch. The preview therefore keeps the source position instead of pulling the
flare toward the camera, which would enlarge it and defeat scene depth. The
source fields named `des_f_big_alpha` and
`des_f_small_alpha` are angular jitter amplitudes, not opacity; ray brightness
comes from the effect color and its grayscale RGB texture (`490198CF`, whose
alpha channel is opaque). This distinction matters because treating those
fields as opacity produces an overly regular, harsh star. Rays now render on the
camera-facing 3D plane centered at the source position with depth testing, so
scene geometry can occlude them.
The preview still approximates the native visibility response and transient GFX
lifetime; procedural rays are not included in GLB export, and this support does
not execute arbitrary scripts. The renderer reference is
`external/jaded/src/libraries/GraphicDK/Sources/GFX/GFXflare.c`.
Extreme source ray dimensions are bounded to 22% of the viewport's short side
in the preview. This avoids a malformed or unusually scaled controller
covering the whole frame; it is a display safeguard, not a claim of native-size
parity.

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
Recognized `ACTActionSet`, `ACTActionFrameSet`, `ACTActionFrequencySet` and
`ACTKitMerge*` calls from uniquely resolved nested `SceneEvents` tracks now feed
the same body-action timeline. Their EVE-local timestamps are shifted to the
cinematic clock. Callback-bound routes still begin at time zero because the
archive does not tell the preview when runtime gates actually started them.
The timeline lists callback owners with a source-resolved `TRAPlay` or
`TRAPlayAll` route to that clip's `SceneEvents` list. Their toggles include or
omit only those callback-seeded overlays in both preview and GLB export. This
is a comparison aid: it does not evaluate runtime start gates. If multiple
owners start the same list, the overlay remains while any matching owner is
enabled.
Auto-loop tracks now guard against float timestamps that stop advancing at a
cycle boundary. This previously could trap playback-clock construction in a
zero-length segment loop, starving both rendering and the audio callback; loop
periods below the timeline's representable precision hold their current pose.

Viewport culling tests the camera's horizontal and vertical field of view as
well as its near/far depth. Scene framing uses decoded CPU geometry bounds and
does not upload every mesh to the GPU just to compute a camera fit. This keeps
off-screen inventory/setup objects from causing a burst of uploads when the
cinematic camera moves.

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

**Audio** resolves literal sound-modifier play/stop calls and supported
`SND_RequestPlay` requests through direct resource keys or serialized `.snk`
bank slots. PCM/MS ADPCM WAVE sources are decoded in the background and mixed
on the shared clock. Play/Pause, Restart and scrubbing control the stream; use
**Enable source audio** and the volume slider. The selected language bank
defaults to the source bank and sibling localized WADs can be chosen in the
audio picker (the verified intro fixture has six language variants).
Music/ambience loops and explicit stops are supported. Random-frequency
requests play at nominal pitch. Looping WAVE sources honor the `loop` cue label
and its `ltxt` range, matching Jade's cue-point handling; sources without a
usable loop range repeat the full decoded sound. Type-30 dialogue messages and
the OVA field `des_ai_son_associe` are separate from a resolved dialogue WAVE
key. Jaded's `SND_RequestPlayDialog` calls `TEXTToSound` with the text-group key
and line ID, then reads `ul_SoundKey` from the loaded language TXL record before
requesting playback. Jaded also exposes `TEXT_LipsIdxGet` and
`TEXT_FacialIdxGet` for that text reference (`AIfunctions_text.c`). These are
later-engine APIs, not a proven PC-retail mapping.

The retail PC text resources are now decoded further. A language `FD...bin`
contains sequential `.txg` chunks; each TXG references `.txi` and `.txs` child
keys embedded in the same stream, not separate FAT entries. The PC TXI table
stores byte offsets into its TXS string pool. For example, `FD105833` contains
group `3500A1C9`, with French TXI `C10093D4` and TXS `35008DC1`; its first
offsets are `0, 33, 79, 127`. The corresponding English `FD205833` has
different offsets (`0, 31, 75, 133`) because translated strings have different
byte lengths. IrisTool's `BFTextParser` and `BFTextGroupParser` follow these
embedded chunks in stream order. JadeView decodes the strings in TXI ordinal
order.

This is structurally different from Jaded's later TXL loader, whose records
include per-line `ul_SoundKey`, `c_FacialIdx`, `c_LipsIdx`, and `c_AnimIdx`.
Those TXL fields cannot be inferred from the PC TXI offsets or strings. For
`FF005833/BF00218E`, the 12 explicit type-53 facial messages and the selected
44.78-second voice WAD are independently present, but the PC text/TXI/TXS data
does not prove a per-line audio-to-mouth link. Continue to resolve explicit PC
event and script paths; do not synthesize lip weights from string order or
reuse Jaded TXL metadata as if it were present in the retail PC archive.
For `FF005833/BF00218E`, JadeView finds text group `3500A1C9` with 35 lines and
12 language resource-reference pairs. Its first language TXI/TXS pair is now
resolved inside the FD stream, so its localized strings are available by line
ordinal. The PC text resources still expose no per-line sound or facial index;
an OVA associated-sound value of zero does not prove that a line has no voice,
and it must not be treated as a WAD key. Per-line voice-to-WAD mapping remains
unresolved; explicit cinematic WAD cues continue to play independently.
Runtime pitch randomization,
spatialization, reverb, conditional callback execution and the global dialogue
sound-slot map are not reconstructed. While source audio is active, its
processed-frame clock gently corrects the cinematic playhead while normal frame
time preserves smooth motion. The mixer clock arrives in audio-device packets,
so applying each packet directly would make animation and camera movement
advance in visible steps. Corrections are limited to five percent of the frame
delta (at most 2 ms), preventing a lagging audio clock from throttling motion.
The application frame clock is used until the audio stream starts or when source
audio is unavailable. Queued PCM is submitted to Raylib from the main
update loop. Two 16384-frame buffers provide about 683 ms of audio ahead to
tolerate render-frame spikes; the viewport reports the current
queued lead beside the audio status, counts when both buffers have drained, and
counts stream resynchronizations after a large playhead/queue divergence. These
are application-side warning signals, not device-level underrun counters. The
offline mixer applies a short 5 ms edge ramp to cue starts and ends
to suppress digital clicks; this is an output safeguard, not a recovered game
fade curve. The mixer also reports up to five large one-sample jumps in the
resulting PCM (at least 12,000 16-bit sample units) as possible click locations;
these are diagnostics only and do not filter or rewrite authored audio. In the
current `3500B8EA` French-bank mix they occur at 39.538, 66.963, 71.419, 74.085
and 109.486 s. Only the 66.963 s jump has been correlated to the same transient
in the decoded WAD; the other four still need source correlation or in-game
comparison. Audio is a preview feature; GLB does not embed this PCM mix. This does
not reconstruct phoneme timing or guarantee audio/video parity when rendering
itself cannot keep up with real time.

The current stop preview truncates a cue at the timeline stop timestamp unless a
source fade is available. Jaded's `SND_StopMdF` calls `SND_StopRq`; `SND_StopRq`
allows attached sound modifiers to delay the actual stop, and the Extended Player
modifier can evaluate its serialized `FadeOutKey` curve before stopping. The
retail `sally_clean.bf` scan for `FF0045A2` contains no standalone `.smd` or `.fad`
archive entries, so that serialized envelope is not yet proven recoverable from
this PC archive representation. The 5 ms edge ramp limits clicks but cannot
reproduce authored stop fades. Source references:
`external/jaded/src/libraries/ENGine/Sources/MoDiFier/MDFmodifier_GEN.c` and
`external/jaded/src/libraries/SouND/Sources/SND.c` / `SNDmodifier.c`.

For large cinematic worlds, viewport bounds are calculated from CPU geometry
and cached. Static non-procedural objects outside the frustum are culled before
their meshes are uploaded to the GPU; animated actors, billboards and particle
carriers keep the conservative deformation path. Bounding-box transforms use
stack storage rather than allocating a temporary array per object, reducing
garbage-collector work in the frame loop. This does not reduce the cost of
first-time uploads for visible geometry or expensive animated deformations.
Animated actor meshes now use the
previous evaluated pose bounds for a conservative frustum test before skinning;
the first pose is evaluated before culling. This avoids updating off-screen
character meshes while keeping visible actor bounds current. The viewport
diagnostic reports cinematic-update and scene-render milliseconds, plus the
application frame time/FPS, cinematic-update and scene-render milliseconds,
plus the number of meshes skinned and geometry meshes uploaded in the last
frame, to help distinguish timeline/audio work from rendering and first-use uploads.
Object visibility/capability, material and PAG events now
apply only newly crossed commands during forward playback; a backward seek
rebuilds their preview state from the source. Actor appearance shapes are
reapplied only when the selected shape changes, avoiding per-frame mesh
appearance invalidation. Dynamic particle
normals are computed for the non-particle carrier only; billboard particles use
a camera-facing normal instead of recalculating reserved-capacity triangles.

`--cinematic-playback-performance-audit <archive.bf> <container> <clip-key> [time ...]`
measures warmed playback updates and viewport rendering against a real archive.
Rendering is sampled at five points through the clip by default; explicit times
can target short effect or transition windows. Each sample reports render percentiles
and peak visible-object, triangle, skin, and upload counts, so an early idle frame
does not stand in for a later effect-heavy section.
`--cinematic-realtime-audio-render-audit <archive.bf> <container> <clip-key> <start> <duration> [width height]`
also runs a hidden viewport and the real Raylib audio stream together at 60 Hz.
The default size is 1600×900; optional dimensions let the audit match a user's
display resolution. Audio output is muted, but the mixer clock, queue refill,
timeline steps, update costs and render costs are measured after a 30-frame warmup.
For `FF0045A2/3500B8EA` (2,177 assembled objects, 57 cinematic participants),
the 2026-10-23 full-clip audit measured 0.308 ms average playback update,
0.422 ms p95 and 12,689 B/frame allocated. The animation sampler now avoids
per-bone transform objects, the world-cycle evaluator reuses palette storage,
and shockwave state uses a reusable value buffer. Playback allocation fell from
29,891 to 12,689 B/frame across these changes. At 1920×1080 on an RTX 3060 Ti,
the five full-clip render samples averaged 3.98–8.65 ms (4.65–9.83 ms p95),
with 9.67–12.89 ms maximum frames and no geometry uploads after warmup. A cold
direct seek to 44.50 seconds instead uploaded 40 geometries and reached 24.61 ms,
so first-use resources can still miss a 60 Hz frame. These measurements do not
reproduce sustained lag on this machine, and this render audit does not exercise
the audio device.
The audio stream keeps two 16,384-frame buffers queued (about 0.68 seconds at
48 kHz) to tolerate longer render stalls. Audio-device processed-frame updates
arrive in packets; using each packet directly as a frame delta made the timeline
pause and jump. The playhead now follows frame time with a bounded correction
from the mixer clock; correction is limited to five percent of one frame so a
late audio clock cannot visibly slow the whole scene. On the 44–50 second real-time audit after this change, the
viewport averaged 5.57 ms per render and the full frame averaged 16.72 ms at the
60 Hz cap; no frame exceeded 33 ms, timeline steps stayed below 33 ms, and the
audio queue had no drain observations or resynchronizations. The audio clock led
by at most 12 ms and lagged by at most 6 ms in that sample. The audit mutes its
output, so it verifies scheduling and buffering but cannot confirm whether a
physical playback device still clicks. The in-app status continues to report
cinematic, audio and render timing, queued lead, queue drains and resynchronization.
After the correction limit was tightened, a 0–6 second audit of the same intro
averaged 16.72 ms/frame, peaked at 20.97 ms, and had no frame over 33 ms or queue
drains. A repeated run later averaged 16.85 ms/frame and had one 33.57 ms frame;
the longer 38–46, 64–70 and 99–105 second windows stayed at 16.71–16.81 ms/frame
average and had no frame over 33.33 ms. All repeated runs had no queue drains or
resynchronizations and audio-clock error stayed within 4–25 ms. The audit now
uses the same default localized-cue selection as the editor (one language bank,
not every locale at once). These measurements do not reproduce sustained visual
lag, but they also cannot rule out a device-specific audio click because output
is muted.
The real-time audit can now be run at an explicit viewport size. At 3440×1440,
the same intro measured 16.72 ms average / 22.24 ms maximum at 0–6 s,
16.78 ms / 20.90 ms at 44–50 s, and 16.72 ms / 23.89 ms at 100–106 s.
None of those samples exceeded 33.33 ms; each reported zero audio queue drains
and zero stream resynchronizations. These muted-device measurements do not
reproduce the reported audible interruptions or prove that the full-resolution
editor window has identical rendering cost, but they do not show a sustained
timeline slowdown in those sampled sections.
An additional 0–117.55 s run at 1920×1080 averaged 16.74 ms/frame and had no
queue drains or resynchronizations, but it recorded two frames over 33.33 ms;
the largest was 85.13 ms at 84.31 s, with a 62 ms audio-clock lag. A JadeView
window was still running during that audit, so GPU contention may have caused
the isolated spike. Treat this as an unresolved hitch to retest with the editor
alone; it does not support a sustained full-clip slowdown by itself.
The full-clip audit was repeated with per-frame subsystem timing. It averaged
16.72 ms/frame at 1920×1080; one 35.91 ms frame occurred at 41.59 s, while that
frame's cinematic update, audio update and render took 0.27, 0.00 and 3.16 ms.
Its measured frame delta was 16.9 ms, so the extra wait was outside those
subsystems (likely frame pacing or OS scheduling). The run had no queue drains
or resynchronizations and maximum audio-clock lag was 6 ms. This still does not
reproduce sustained scene lag; the audit is muted and does not verify audible
clicks. The audit now reports the timing breakdown for every frame over 33 ms.
After the report of whole-clip lag, a fresh 0–117.5 s run at 3440×1440 took
16.72 ms/frame on average (16.81 ms p95, 31.32 ms maximum), with no frame over
33.33 ms, one timeline step below 1/120 s, zero queue drains/resynchronizations,
and at most 6 ms audio-clock lag. The largest renderer sample was 14.18 ms.
This rules out a persistent slowdown in the warmed standalone viewport on this
machine, but not lag in the full editor UI or audible device playback: the audit
uses a hidden window, warms the geometry cache before playback and mutes output.
The latest repeat at the same resolution averaged 16.72 ms/frame (16.82 ms p95)
and recorded one 35.67 ms frame at 107.56 s. Cinematic update, audio update and
render measured 0.40, 0.00 and 2.00 ms for that frame; there were no queue drains,
resynchronizations or geometry uploads, and maximum audio-clock lag was 6 ms.
This is an isolated frame-pacing hitch, not sustained playback lag; the hidden,
muted audit still cannot reproduce full-editor UI stalls or audible output clicks.
The latest 0–117.5 s repeat at 3440×1440 measured 16.71 ms/frame average and
16.83 ms p95, with a 24.82 ms maximum and no frame over 33.33 ms. Maximum
cinematic update, audio update, scene render, `BeginDrawing`, and `EndDrawing`
times were 3.99, 0.71, 18.80, 0.27, and 22.50 ms respectively; the audio queue
did not resynchronize and clock lag stayed within 6 ms. This again rules out
sustained lag in the warmed, muted standalone viewport on this machine. It does
not cover the full editor UI or audible output. The largest click-like PCM jump
in the FR-bank mix (66.963 s, 17,319 sample units) corresponds to a 26,060-unit
jump in the decoded source WAD at 65.763 s; the cue starts at 1.2 s. The mixer
therefore did not create that transient, but the remaining four reported jumps
have not been correlated to source samples. The FR WAD contains 63,134 stereo
MS ADPCM blocks (72 bytes each); all initial delta headers are between 16 and
1,722, so signed-versus-unsigned delta interpretation does not explain the
decoded result. No loopback capture has confirmed whether any of the reported
jumps sound like clicks.
The editor-side Outliner was also a separate per-frame cost: it rebuilt temporary
filtered lists and submitted every scene row to ImGui. It now reuses row buffers
and clips rendering to visible rows; regression checks cover filtering, actor
expansion and revealing a selected component. Full-window frame timing still
needs confirmation in JadeView because the standalone viewport audit excludes ImGui.
The intro's `002_big_intro_FR.wad` decodes as 32 kHz stereo Microsoft ADPCM.
The bank has no RIFF cue/playlist markers, so it is a continuous source track.
The strongest one-sample jump in the mixed preview at 66.963 s maps to a larger
jump already present in that decoded source bank at 65.763 s. JadeView's decoder
matches Jaded's PC-retail MS ADPCM block layout, coefficients and sample order,
and an audit of the adjacent PCM frames confirms the same simultaneous left/right
transient in the decoded WAD before resampling: left 11,977 to -12,309 and right
15,636 to -10,424. This points away from cue mixing or a known decoder mismatch
as the origin of that particular click. The verification suite now includes a
stereo PC MS ADPCM fixture for channel pairing, nibble order, predictors and
block boundaries. Retail playback has not been captured for direct comparison,
so whether this source transient is present in the game's audible output remains
unconfirmed; no blanket de-click filter is applied to authored audio.
The two short end-of-intro cues marked as loops use 48.66 s and 102.38 s sources
for 11.15 s and 1.40 s segments respectively, so neither reaches its full-file
loop seam during this clip.
The offline mixed WAV for this intro was also checked: three of 11,284,802 samples
reach the 16-bit output limit, which does not indicate sustained mixer clipping.
This does not rule out playback-device underruns or a click inside an individual
source recording.

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

The visual frame audit must let progressive geometry uploads drain before taking
its screenshot; a one-frame capture can otherwise be black even though playback
continues to prepare meshes. In the current `sally_clean.bf` capture, the source
camera frames mostly sky at 0–5 s, brings a tree into view by 10 s, and shows the
shoreline and actors by 23.68 s. The warmed samples report zero deferred geometry.
This establishes the current renderer's camera and scene progression at those
times; it does not establish that the same early framing matches the retail game,
or that a separately running JadeView process contains the current build.

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
is not exported. The viewport reuses per-emitter position/color work buffers
between frames to avoid cloning large particle meshes every update; those
returned preview arrays are transient and must be consumed before the next
update. Animation states with zero-scale parents retain evaluated local
bone transforms rather than requiring an inverse of a singular world matrix.

SpeedFromDyna actions retain body animation and rotation while contributing no
animation-driven root translation, as in the native player. Their external
dynamics/AI movement is still not simulated. In the intro, the JADE graphic is a
separate scene mesh and must not be confused with the brief source shutter at
about 43.17 seconds. Its previously small framing was caused by missing lens
commands; its geometry scale is unchanged.

Global message-50 alpha fades are supported when the receiver's native script
role is confirmed. Their five timing phases and colors come from the source;
the intro's four camera flashes are previewed with a bounded flare radius.
In `3500B8EA`, repeated white fades from 43.267 s through 44.367 s are also
source-authored; they can make frames near 44.5 s almost entirely white and are
separate from the color-balance pass.
Alternate fade materials, higher-priority script arbitration and GLB
screen-effect export are not supported yet. This does not change the size of
the separate JADE mesh.

### Source camera lens

Native message 60 stores the transition duration (`f2`) at byte 32 and the lens
angle (`f1`, radians) at byte 36. These were previously mistaken for each other.
The player now evaluates cuts, lens ramps and interrupted ramps on the source
clock. Zero lens commands retain the preceding angle; invalid values are ignored.
Horizontal FOV converts to vertical FOV for the actual viewport aspect, and
leaving source-camera follow restores the editor lens. Native screen-format
constraints, positional smoothing and the full camera script remain incomplete.
Enabled or scheduled nested EVE tracks can also set world view slot 0 through
`VIEWFocaleSet(viewIndex, angleRadians)`. JadeView joins these instantaneous
setters to the matching camera execution receiver and the same lens timeline
used by message 60. Other view slots and setters from unrelated receivers are
left out rather than applied to every camera. In the retail intro graph,
`CINE_INCANTATION_ARRIVEE_PEYJ_EVENTS.gao` sets view 0 to 0.5 radians at
16.067 s while assigning `Cine_Intro_Cam.gao` to that view. When this owner's
EVE list is explicitly previewed, JadeView applies the call to viewport playback
and synchronized GLB lens keys; its serialized start gates are not assumed to
have passed in the game. The archive audit
`--cinematic-camera-script-fov-audit <archive> <container> <clip> <owner> <time>`
verifies the selected-owner path end to end. The `FF0045A2 / 3500B8EA` export
with owner `BF001FFA` was imported in Blender 5.2.3; its camera-lens curve
matched the GLB samples with less than 0.000001 mm error. This validates GLB
and Blender import consistency, not native game playback.

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

Procedural video materials preserve their Jade descriptor under GLB material
`extras.jadeProceduralTexture`, including the Bink resource key and filename.
The warning on export is intentional: standard glTF material textures are
still images, so the source link is retained but Bink playback is not embedded.
In the viewport, PC Bink frames are decoded through the installed game's x86
`binkw32.dll`. Cinematic video materials linked to an Iris `mpegplayer_init`
owner combine the owner's `i_Play` default, the referenced switch's initial
`0x100` capability and literal `OBJ_CapaSet` requests from the selected clip
and its nested scene-event data to start, toggle or stop playback. The material
stays black before its first start, and Bink time begins at that source event;
seeking re-evaluates the event history. Bink frame decode now runs on a background worker; only completed
RGBA frames are uploaded to GPU textures on the render thread, avoiding a
synchronous decoder-process round trip during frame rendering. Uncontrolled
materials still sample from cinematic time. This
is a source-event preview, not execution of the full AI callback VM: later live
switch changes, saved-game gates and conditional callback execution remain
unresolved. For an ordinary `mpegplayer_init` path with a uniquely resolved
switch and no configured save-flag or sound-status gate, the preview also
applies the source-derived initial visibility to the screen owner and follows
serialized video start/stop/toggle requests. This prevents a loaded but
inactive monitor from appearing before its script starts it. Callback cases
with unresolved gates use the same disclosed fallback as the video material,
so the screen owner and its Bink frame do not drift into contradictory states;
the inspector labels the initial state estimated.
The inspector reports trigger and decoder status. The bridge uses
the retail DLL installed with the game; it is not shipped with JadeView.

Run `--cinematic-video-trigger-audit <archive.bf> <container> <clip-key>` to
write the discovered procedural textures and their evaluated trigger states
to a JSON report for an archive/clip pair.
Run `--archive-bink-frame-audit <archive.bf> <media-key> <frame-index>` to
decode a representative frame directly from a BIG archive using the installed
game decoder; the extracted BIK is temporary and the PNG is written under
`artifacts/export-verification`.
For `sally_clean.bf / 61006334`, frame 0 decoded as a 128×128 Alpha logo at
29.971 FPS (1836 frames total). This verifies the archive-to-decoder path and
frame contents; it does not prove the game's live callback gates or
post-cinematic screen lifecycle.
In the verified `FF005833/BF00218E` example, the monitor's toggle is stamped at
the clip endpoint (`44.783333s`). The editor now keeps the final cinematic pose
and advances the triggered Bink track after the source clip ends. Post-roll is
kept while media metadata is still loading, then ends at the decoded media end
when the script's StopAtEnd flag is set; explicit stop events and failed media
loads end it earlier. The trigger audit samples the endpoint and one second
afterward to verify the source event remains active beyond the animation clip.
