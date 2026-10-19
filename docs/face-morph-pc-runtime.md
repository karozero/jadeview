# PC facial controllers recovered from a saved dump

Date: 2026-10-19. This extends [the archive investigation](face-morph-analysis.md).
No game was launched or attached during this investigation. All native results
below are static analysis of a derived image from an earlier user-reproduced
crash, not observations of a new facial animation running in game.

Follow-up: [standalone cinematic events and candidate type-53 sends](face-morph-pc-cinematic-events.md).

## Automatic source-pose discovery and Peyj

**Discovered morph poses** is now independent of Sally's kit table. It discovers
every source group whose accumulated vertex deltas are nonzero, records channel
references for provenance, and previews the selected group at weight 1 with
other targets zero. Empty groups and groups whose repeated entries cancel are
omitted from this catalog; original slots remain present in the decoded data
and GLB. No guessed emotion names or native kit IDs are assigned to these poses.

On first opening a morph mesh in the editor controls, an empty or unreadable
current appearance is automatically replaced by the loaded source appearance
with the most nonzero poses (source order breaks ties). A current appearance
with usable poses is kept. Discovery also includes members whose morph modifier
exists only in a source appearance. Manual selection or **Use clip appearance**
prevents further automatic replacement for that bone in the actor session.
This is a bounded search of loaded bone variants, not a full archive scan.

Military/Double H has a split HD appearance: face geometry `8F0066B2`
(`S_DoubleH_TeteHD_Def.gao`) requires the separate accessory source
`S_DoubleH_Attrib.gao`, geometry `A90052F4`, on bone `A900532C`
(`B_Mil_CheveuxHH.gao`). The ordinary military shape hides that bone.
An explicit preview companion binding enables it while the HD face override
is active; clip changes and GLB export retain it, and returning to the ordinary
or clip appearance restores native visibility. This pairing is bounded to
these source keys, not a general inference from accessory names. Additional
split appearances need verified bindings or recovered complete shape presets.

**Morph appearance** enumerates type-4 source models already loaded for that
actor bone, displaying the number of discovered poses. A selection previews
the source model and its own geometry/material/modifiers/skin links; it remains
selected across clip switches until **Use clip appearance** restores the clip.
This override is session preview state, not a BF edit. Existing `.bgemod` morph
profiles bind to each exact geometry/modifier and restore when that appearance
is selected again; the appearance override itself is not serialized.

Original PC Peyj example, verified in container `FF00791B`:

- Actor `_PJ_Peyj_le_petit_cochon.gao`, root `9E001C75`.
- Head `B_peyj_Tete.gao`, GAO `D900520B`.
- Ordinary source `S_Peyj_TeteBD_Boucheneutre.gao`, GAO `8F002FF9`,
  geometry `8F002FF8`: 495 vertices, 22 groups, six channels, all group deltas empty.
- HD source `S_Peyj_TeteHD_Boucheneutre.gao`, GAO `8E000689`, geometry `8E001060`:
  846 vertices, 22 groups, seven channels, 1,776 sparse entries. Thirteen groups
  are nonempty: 1/3/4/6/7/8/10/12/15/16/19/20/21.
- A separate eight-vertex source geometry `D90051E2` contains repeated sparse
  indices. These were incorrectly rejected by `FaceMorphReader`. Native type-4
  processing adds each sparse contribution; decoding now preserves repeated
  indices, while GLB target construction sums their deltas per source vertex.
  Out-of-range indices and non-finite deltas are still rejected.

Tests cover discovery, cancellation and duplicate-index accumulation; original
Peyj HD appearance selection, clip changes, pose save/reload, GLB weights and
skinned vertex positions. Blender 5.2.2 retains all 22 slots and applying/removing
group 1 changes the head by approximately 0.01963 world units and restores it.
Artifacts: `peyj-discovered-expression.glb`, `peyj-morph-project.bgemod` and
`blender-peyj-morph-report.json`. Reproduce via the archive verification runner
and `tests/ExportVerification/blender_peyj_morph_check.py`.

The offline `--morph-discovery <archive> [container]` mode exports raw PC morph
evidence and brain closures for name-matching actors. This is a bounded discovery
tool, not proof of complete archive coverage. Peyj's brain `9E001C77` references
model `35001FF8`, not resolved in the captured loader closure. A candidate
compiled-script key/function table was found near `00B7C118` (Sally key
`7100B928` / routine `005ADBF0`); its wider registration and Peyj mapping are
not fully established. The shared controller findings below extend this lead.
The Peyj UI exposes source morph poses, not
claimed original emotion-kit combinations. Sally's verified kits remain separate.

## Shared PC head controller and blink preview

`tools/audit_pc_script_bindings.py` scans aligned non-executable PE rows for
retained OFC keys followed by pointers into an executable section. On the same
historical captured image, 77/77 Sally code keys and 83/83 military code keys
have candidate mappings. This tool does not execute code, qualify dispatch or
establish facial meaning on its own. The military closure resolves model
`35001FF8`, which is also referenced by Peyj's brain. Its table includes:

- `9E003423` at `00B7C1C0` → blink routine `00A09120`.
- `9E002C81` at `00B7C200` → face update routine `004F7D30`.
- `9E00049F` at `00B7C2F8` → message handler `005B20D0`.

OVA `35001FF7` names match the native offsets: blink phase `468`, time `46C`,
delay `470`, double flag `474`, forced flag `478`, blink module `430`; expression
target factor array `B4C`, current factor `B64`, progression `B7C`, speed `B94`,
form `BAC`, and flag `BC4`. This corroborates these descriptor first words as
runtime offsets; it does not qualify sequential serialized default assignment.

Assembly of `00A09120` confirms a weight write to channel **0** of the blink
module, with baseline `B64[0]` and the same `15 * delta` / sine envelope as
Sally. It also contains native scheduling/double-blink gates. `004F7D30` updates
slots 0 and 4, retargets their first two source references, advances progression
from 1 toward 2, and approaches the target factor using `004410A0`. A separate
32-entry mouth-form list at `AC4` feeds slot 4 when automatic mouth mode is set.
This is not a recovered audio/phoneme timeline.

`005B20D0` obtains a 52-byte message of type **53** (`0x35`) into offset `434`.
The handler decodes packed commands for slots **0, 1 and 4**, using the same
integer /1000 group and fractional weight formula as Sally. Zero commands leave
their slot unchanged. Speeds, form flags and automatic-mouth state are also
updated. Source messages, their producers and event-to-message argument mapping
are not yet reconstructed, so no new named emotions or native kits are exposed.

**PC head blink** now previews one manually triggered envelope on qualified HD
Peyj (`8E001060`, 22 groups / 7 channels) and military (`8F0066B2`, 16 / 5) heads.
Peyj's source channel 0 references `[10]`; military's settled endpoint references
`[1,10,FFFFFFFF,FFFFFFFF,FFFFFFFF,FFFFFFFF]`, with empty group 1. Both therefore
use group 10 for this isolated preview. Qualification checks geometry keys,
counts, those reference rows and nonzero target deltas; it does not guess a
blink from group numbering on arbitrary meshes. Sally retains its separate
groups 1/2 and expression-kit qualification.

The preview preserves direct-weight/channel baselines and restores them on
Stop/completion; manual edits and Reset take over. It is an isolated settled
target envelope, not a live replay of retargeted native channel references,
random triggers or speech. Current evaluated pose weights export to GLB. No
new keys or animations are recorded, and creation remains hidden.

Verification covers original BF data for both heads, peak/baseline, unrelated
weights, channel-mode restoration, unknown-geometry rejection, source-byte
isolation and GLB pose export. Blender 5.2.2 imports both peak GLBs with group
10 = 1, group 3 = 0.3 and all 22/16 targets. Returning group 10 to 0.2 produces
maximum world displacement about 0.02043 / 0.01422 respectively and restoring
it reproduces the closed pose. Reproduce with
`tests/ExportVerification/blender_shared_head_blink_check.py`.

Retained historical evidence: `native/genericFaceCandidates.json`,
`genericFaceAsm.json`, `genericBlinkAsm.json`, `genericFaceMessages.json`,
`genericMessageAsm.json` and the `*-script-bindings.json` reports under ignored
`artifacts/export-verification/`. REA evidence IDs include
`ev_0f081fe97702e5338b3f2138c82b36a3a975a1f6594163e1945bd8e5b16fcb14`
(face/blink pseudocode),
`ev_666693bedc9cae790f01212c9a7eb3f25ce76972482edb91ff14bc81a30780bb`
(blink assembly), and
`ev_d84d14e8a825e8c8ebfeeaf3a5d0a0200a0a337f32d3a4c76819c02f526dce83`
(message handler). Native execution for these particular instances was not
observed in this session.

## Evidence identity

Source: `%TEMP%/jadeview-procdump/hangar-bankfix/BGE.exe_261019_121235.dmp`,
1,029,300,440 bytes. Captured BGE module base `00400000`, size 10,006,528.
The captured `.text` has 7,767,943 bytes and SHA-256
`41afb6ffd013e172a08a178175922ee99eaa7ccb5dd11b53141e9db13d18d3f5`.
The earlier `hangar/BGE.exe_261019_034658.dmp` has exactly the same `.text` hash.
The third saved dump differs; its code is not silently substituted.

`tools/CrashCapture/extract_module_image.py` copies captured module sections into
a PE carrier, remaps section file offsets to RVAs, and clears the checksum and
certificate directory. Missing section memory is rejected. This is only an
analysis carrier; its original entry point/import state may be unusable.
The initial carrier analyzed by REA/Ghidra 12.1.4 has SHA-256
`3c203a2fc5a0bf5bbb3c5657fbc432a429b3b4b7e9c23fa16dc901a3ae3ea6b9`.
The finalized extractor preserves original PE file alignment and produces
`295f66ef3a1486fe84d46db745788506671958227bb7ff2c0283f5a89d0c8be0`;
captured section bytes and their virtual addresses are unchanged.
Native binaries, dumps and decompilations stay in ignored local artifacts.

## Expression state really reaches type-4 channels

PC routine `005ADBF0` reads script data from `*(actorContext + 0x44)`.
The offsets agree with the PC OVA descriptors:

- `+198` / `+19C`: current/previous expression kit.
- `+1A4`: cinematic expression gate. Kit presets update when this is zero and
  the kit changed.
- `+1A8`: local expression message; its type is written at `+1D8` as `0x35`.
- `+6B8`, `+6D8`, `+6F8`, `+718`, `+738`, `+758`: target factor, current
  factor, current progression, speed, form and state arrays.
- `+7F8`: the head module reference.

The preset float values match the earlier Anniversary lead, now independently
present in PC instructions: kit 2 uses 1500/2540/10600/31600/600; kits 3 and 5
use 1520/2530/11600/34550/52515; kit 4 uses
1550/2560/16600/35574/31600. These are packed controller commands, not direct
55-target weight arrays. The function converts them into form and factor state.
This establishes local preset production, not delivery of dialogue messages.

The array accessor `005EB990` uses ECX as array base and EDX as index, returning
`base + 4 + DAT_00B7D422 * index`. Pseudocode loses these register parameters;
assembly is necessary to distinguish the arrays and channels.

For head channels 0 through 4, the expression routine calls:

- `0046C380`: write `currentFactor[channel]` into type-4 row `+8`.
- `0046C4F0`: write `currentProgression[channel]` into row `+C`.
- `0046C680`: replace a group reference in the row when changing its form.

Examples: `005AE937` writes channel 2 factor from array `+6D8`, and
`005AE9AE` writes channel 2 progression from `+6F8`. Both use head `+7F8`.
`00B31430` explicitly selects modifier type 4 through `00B502B0`; it is not a
guessed material or animation property. The row stride is 16 bytes.
There is no automatic one-to-one mapping of every seven-element script array
slot onto every six-channel head row.

## PC deformation and ordered progression

Descriptor `00BE1830` starts with type 4 and contains constructor `00B33480`,
destructor `00B31D20`, deformation callback `00B31900`, and loader `00B331A0`.
The loader matches the decoded PC sparse-group/channel layout. In memory the
group count is at `+4`, groups at `+8`, channel count at `+C`, channels at `+10`.

The callback ignores empty channels, zero weight and zero progression. For
nonnegative finite progression `p`, it starts at `truncate(p)`, searches forward
for a valid group reference and backward from `truncate(p)-1` for the previous
one. References greater than or equal to the group count are holes.
Between valid slots `lower` and `upper`, the upper factor is
`(p - lower - 1) / (upper - lower)`; the lower factor is its complement.
Before the first slot there is an implicit neutral state; beyond the end it
holds the last valid group. Contributions are multiplied by channel weight
and added to vertex positions. Repeated groups and multiple channels accumulate.

`FaceMorphChannelEvaluator` implements this conversion into direct target
weights, with explicit rejection of invalid/unsafe inputs. The editor now exposes
it through **PC morph channels**; archived progression is not automatically applied
on load. Channels start at zero progression and source finite weights.
Tests cover fractional progression, integer boundaries, holes, repeated groups,
tail holding, neutral start and signed accumulation. Tests establish the
translated behavior; bit-exact x87 versus .NET floating-point parity has not
been measured.

## PC blink controller

`00977D30` reads Sally's blink variables at `+188/+18C/+190/+194/+1A0`.
Once its elapsed-time/forced-blink condition is true, phase advances by
`deltaTime * 15` and caps at float word `40490FDC`. `00464650` calls
`0040CEF0`, whose body uses x87 `FSIN`.

For head channels 0 and 1 it writes
`baseline + (1 - baseline) * sin(phase)`, where each baseline comes from its
`f_expr_factor_current` slot. Calls at `00977E8D` / `00977EFB` confirm the
head reference and channel indices. At the cap it clears the forced flag,
sets the next delay to a random value in either 2–3 or 0.2–0.3, updates its
time reference and resets phase. The random helper's distribution and complete
trigger scheduling have not been reconstructed. The envelope lasts about
`pi / 15` seconds, with frame-step-dependent endpoint handling.

## Still required

The PC expression-to-modifier bridge and blink envelope are now established
statically. Explicit channel preview, project restoration and export are
implemented. Settled expression preset preview is also available. Remaining work includes preset transitions and group-reference editing,
automatic blink trigger scheduling and tracing the speech-message producer and timing.
Skeletal event selector 12103 is still not linked to that speech path. No
original facial timeline is synthesized from those events, and morph 37 remains
unchanged per the user's decision.

## Channel preview/export checkpoint

`FaceMorphPreview.Channels.cs` owns explicit weight/progression controls for each
source channel, evaluating them into the existing direct-weight preview before
skinning. Source references remain unchanged. Channel edits replace the pose and
pause the timeline. Direct target edits, seeking a recorded pose or playing keys
deactivate channel control; Reset clears its state and returns neutral weights.
Inactive channel settings are not saved as authoritative state.

The optional `.bgemod` `channelState` field preserves active controls, including
a neutral configuration. Existing projects without it remain direct-weight
profiles. Source geometry/modifier signatures still apply, and channel counts
must match; restoring playback remains paused. BF builds do not consume this
authoring state. Channel-state arrays are copied for snapshots.

GLB mesh defaults contain the evaluated target weights; `pcMorph.previewChannels`
records the explicit control values while `pcMorph.channels` keeps original
source metadata. Recorded keys bake the pose into the existing user-authored
weights animation. They interpolate direct weights, not channel progression;
crossing multiple reference slots requires intermediate pose keys to reproduce
the channel path. No original facial timeline or speech scheduling is claimed.

Verification covers pose evaluation, neutral reset, direct/timeline takeover,
invalid-input isolation, exact source-byte preservation, project save/load and
snapshots, source-count rejection and GLB defaults/metadata. On original Sally,
six controls survive project reload and reproduce the GLB skinned pose.
Blender 5.2.2 imports `sally-channel-pose.glb` with eyelid weights 0.75/0.5 and
`sally-channel-animation.glb` with values 0/0, 0.375/0.25, 0.75/0.5,
0.375/0.25, 0/0 at 0, 0.05, 0.1, 0.15, 0.2 seconds. Reproduce with
`tests/ExportVerification/blender_morph_channels_check.py`.

## Explicit PC blink cycle checkpoint

**PC Sally blink** exposes Preview/Restart and Stop. The bake implementation is
retained for verification but its creation button is temporarily hidden at the
user's request. Availability is restricted to the known ordinary/HD Sally geometry
keys, 55 groups, six channels and eyelid reference rows `[1,1]` / `[2,2]`.
It is not automatically applied to other characters' first two groups.

`FaceMorphBlinkPlayback` accumulates phase using `delta * 15`, and
`FaceMorphBlinkCurve` evaluates `baseline + (1-baseline)*sin(phase)`.
The editor snapshots the current direct weights and active channel state.
Only eyelid groups 1/2 change; Stop, completion and restart restore the captured
pose/mode. Reset and manual edits stop the cycle, as does timeline playback.
This is an explicitly triggered envelope, not a replay of native script state,
random delays, cinematic gates, or speech events. .NET sine approximates x87
FSIN; bit-exact parity is not claimed. Completion deliberately restores the
baseline rather than retaining the tiny sine residual at `40490FDC`.

Baking samples at 120 Hz and includes the peak and exact baseline endpoints.
It creates ordinary direct-weight keyframes at the morph cursor, rejecting
overlapping existing keys or times that cannot represent the sample spacing.
Keyframes save through the existing `.bgemod` format and export as a
user-authored GLB weights Action. Transient blink playback itself is not saved;
saving during preview captures the current pose, not an automatic blink trigger.

Verification covers phase speed, baseline/peak/endpoint behavior, copied keys,
unrelated-target preservation, mode takeover, overlap rejection, reset and real
Sally project reload/export. Blender 5.2.2 verifies baseline eyelid weights
0.2/0.4, closed weights 1/1 at about 0.10472 s and baseline restoration by
0.20944 s. Group 10 stays at 0.3 throughout. Intermediate imported weights stay
within 0.002 of the analytic envelope (linear baking approximation).
Artifacts: `sally-pc-blink.bgemod`, `sally-pc-blink.glb` and
`blender-pc-blink-report.json`; reproduce with
`tests/ExportVerification/blender_pc_blink_check.py`.

## Current UI scope and settled expression presets

At the user's request, creation/replacement of morph keys and blink baking are
hidden behind `MorphKeyCreationVisible = false`. Implementations and tests remain.
The animation tree appears only for existing saved keys, as **Saved morph
animation**; seeking, playback, removal and export of those clips still work.
Direct weights, PC channel controls, blink preview and project persistence remain
available. No new keys are created by the expression buttons.

**PC Sally expressions** exposes source kit IDs 2/3/4/5, with no guessed emotion
names. `SallyExpressionPresets` decodes the five local packed commands from
`005ADBF0`: convert the positive packed float to an integer through PC `0040CF80` (nearest/even under normal FPU mode), integer-divide by
1000 for the form/group, and compute
`(abs(float32(integer / 1000) - group) - 0.5) * 10` for the target factor.
Zero commands are native no-ops. Exact packed words and slot indices are retained
in `SallyExpressionCommand`; source rounding is not replaced with ideal decimals.
For example, 52515 gives group 52 and factor 0.149993896484375.

The settled group sets are 1/2/10/31/0 for kit 2, 1/2/11/34/52 for kits 3/5,
and 1/2/16/35/31 for kit 4. The preview replaces the current direct pose with
these final factors, stops transient blink/timeline playback, and leaves source
groups, references and already saved keys intact. It does not reconstruct the
native retargeting crossfade, current controller context, or dialogue scheduling.
The existing direct-weight project/export path preserves the resulting pose;
the kit ID is not persisted as a live native controller state.

Tests verify the source group tables, kit 3/5 equivalence, float32 residual,
invalid commands, real Sally preview/GLB skinned positions and unchanged source
payload. Blender 5.2.2 imports all four `sally-pc-expression-<kit>.glb` fixtures
with all 55 targets and expected weights, within 0.00005 of ideal decimal
values. Reproduce with `tests/ExportVerification/blender_sally_expressions_check.py`.

Local REA evidence is retained under `artifacts/export-verification/native/`:
`pcVisage`, `pcVisageAsm`, `pcBlinkCandidate`, `pcBlinkAsm`, `b31900`,
`pcMorphEvaluateAsm`, `b331a0`, `pcType4Descriptor` and the referenced helper
JSON files. Relevant evidence IDs include
`ev_47031e347aa9985ae39c55323e3bfc60393c76152ba6ee21bded70979cc4d81e`
(expression), `ev_15a63e18829ed819754c949bc8b2be4f1f637d25784bfdae3f3048005af37f38`
(blink), and `ev_0dcf6caa94d50df8cf7a4ae9ac8c46dfd5e10e5ed6b83eb578d8887ed1ea7dea`
(deformation). These artifacts are derived historical-capture evidence despite
the tool's generic shipped-artifact classification.


## Message 53 transport and field layout (2026-10-19)

Static REA/Ghidra analysis of the same captured carrier (SHA-256
`295f66ef3a1486fe84d46db745788506671958227bb7ff2c0283f5a89d0c8be0`)
now qualifies the actor-local message buffer. This is historical-capture
analysis, not an observation of live dialogue dispatch.

- `004744A0`: ECX is the destination actor and EDX points to a message.
  The buffer is reached through `actor + 18` then `+14`. Its count is at
  `+104`. With a valid count below five, the function copies 13 words into
  `buffer + count * 34` and increments the count. A full buffer is not appended.
- `00474CF0`: ECX is the actor, EDX the requested type. It searches records
  by their type word at `+30`, swaps the matching record with the last record,
  decrements the count, and returns the removed record's address. With no match,
  it returns shared fallback storage after clearing its type word.
- `00474B40` removes the last record without filtering by type; `00474BB0`
  wraps it. These operations do not implement FIFO delivery. The returned
  storage must be copied before another queue mutation can reuse it.
- `005B48D0` sets EDX to `35`, calls `00474CF0` for the current actor, and
  copies 13 words into controller state `+434`. Type is checked at `+464`.

Offsets below are relative to the 52-byte message, not the controller:

- `+08`: packed command for controller slot 0.
- `+0C`: packed command for controller slot 1.
- `+18`: packed command for controller slot 4.
- `+20`: float speed copied to slots 0/1 when their commands are nonzero.
- `+28`: signed integer converted to float speed for slot 4 when nonzero.
- `+2C`: raw word copied into the forced-blink variable at controller `+478`.
- `+30`: message type `53`.

Assembly is required here: the three command inputs pass through `00406020`,
which copies three words using EAX/ECX; pseudocode omits these arguments.
The instruction sequence at `005B4913`, `005B4A4D` and `005B4B93` establishes
which vector component feeds each slot. Direct application of negative forms and original speech scheduling remain
unqualified for generic playback; the bounded signed arithmetic is documented below.

`tools/audit_pc_facial_message.py <file> --offset <file-offset>` inspects one
explicitly selected raw record. It preserves all 13 words, including unknown,
negative and non-finite bit patterns. It does not search a BF/dump for messages,
translate virtual addresses into file offsets, execute controllers, assign
emotion names or claim that arbitrary matching bytes are a live message.
Tests: `python -m unittest discover -s tools -p test_audit_pc_facial_message.py`.

Local evidence: `native/facial-handler-evidence.json`,
`facial-handler-assembly.json`, `message-queue-evidence.json`,
`message-queue-wrappers.json` and `message-vector-copy.json` under
`artifacts/export-verification/`. Assembly evidence:
`ev_5d7ae9fe3ef6360b8d52ef01dfd2b6fb0debb388fb874bd70b851026733eeff9`;
queue evidence:
`ev_d263da8a7a2f06843a207ec7ca5b101973053766eef6be5f6036667bdef9563f`.

The next unresolved boundary is the producer of dialogue/cinematic message 53.
Direct sender references are numerous; nearby raw `35 00 00 00` bytes did not
qualify a producer. Two checked windows resolve to unrelated or receiving
logic, so proximity must not be treated as message provenance. No facial UI,
recording controls or export behavior changed in this investigation.


## PC message selector registration (2026-10-19 follow-up)

The captured carrier's registration initializer `0045FA50` builds a source-ID
lookup at `00BB5A00`. Its native-function loop covers 860 rows (stride 8) at
`00B7D718`. `005EBF70` dispatches through callback base `00B7D71C` using the
remapped short at instruction `+4`. Registration is therefore stronger evidence
than assigning meaning to a raw selector based on its numeric proximity.

Recovered rows in this exact carrier:

- Source selector 8050 (`1F72`), row 166: callback `00474520`. It obtains a
  typed argument through `00460B60`, then sends its record pointer through
  `004744A0` to the saved destination actor. `00460B60` is a generic typed
  argument pop/copy, not a facial-message constructor.
- Selector 8056 (`1F78`), row 172: `00474670` assembles a 52-byte record from
  integer, float, vector and actor arguments and appends it to the destination
  actor's message buffer. The message type is an argument, not a literal 53.
- Selector 8061 (`1F7D`), row 177: `00474820` assembles the same record shape
  but uses destination global `00BA2834`; the sender word is `00BA27AC`.
  The semantic identity of that destination global is not established here.
- Selector 12103 (`2F47`), row 642: wrapper `00ADA4F0` consumes three floats
  and an integer, then calls `00ADA450`. This registration does not qualify
  it as a facial-message producer or lip-sync command.

Type-copy dispatch uses 8-byte rows with callback base `00B7D414`.
`00460B60` pops a short type and source pointer from `00BC0600` / `00BC0604`,
then calls the registered copier. Type index 7 selects `00AF01C0`, which copies
13 words. This confirms a first-class message-sized argument path; it does not
identify the original script resource holding a particular message.

Census of the retained decoded clips (211 Sally, 88 military, zero decode
failures) found no category-3 selector 8050 rows. Sally has four 8056 rows,
in clips `320001E7` and `320001E5`, whose candidate integer type argument is
95 (`5F`). The observed 8061 event templates contain candidate type integers
80/81/82 for Sally and 80 for military. These are bounded source-template
observations, not execution of their argument expressions. They do not provide
an observed message 53 timeline. Other scene controllers/cinematic resources
remain possible producers; absence here is not archive-wide absence.

`tools/audit_pc_function_registry.py` records the selected table entries,
source SHA-256, source IDs, row indices, callbacks, executable-section membership
and missing IDs. Table address/count are explicit caller inputs; they must be
qualified for each carrier rather than reused silently across game versions.
Reproduce the registration lookup with:

```text
python tools/audit_pc_function_registry.py <captured-carrier> --table-address 0xB7D718 --count 0x35c --selector 8050 --selector 8056 --selector 8061 --selector 12103 --output <report.json>
```

Tests cover virtual/file address mapping, selected/missing rows, callback bounds,
truncated input and cross-section reads. Native evidence is stored locally as
`selector-registration-resolver.json`, `typed-argument-pop-assembly.json`,
`typed-message-copy.json`, `event-message-send-helpers.json` and
`event-message-send-fields-assembly.json` under `artifacts/export-verification/native`.
Registration evidence:
`ev_973576764bcfb4e3bebc4f55aa4cc24a4a663759646f5be3d724cca189f178b2`;
dispatch/send evidence:
`ev_9e00a1fca92f05e3eedeaf85ba6b8e77b8f03ae3d60873905502dce567da5f74`.

Next: inspect scene/cinematic producers using these qualified sender callbacks
and resolve the original typed message argument. No new automatic facial
playback, recording UI or guessed emotion labels have been added.


## Hangar controller coverage and automatic mouth cycle

The diagnostic runner now supports `--scene-script-audit <BF> <container>` and
`--scene-event-audit <BF> <container>`. The first retains brain owners and raw
q/s/t/r resources from the selected scene plus fixed resources. It preserves
container/world identity rather than claiming globally unique keys. The second
writes per-world/per-GAO animation-event reports and an index, including actors
other than the two earlier character fixtures. No game process is launched.

For `FF0057FD`, the script inventory contains 230 brain owners and 1479 resources.
The native key/function scan matches 837/837 retained code keys to candidate rows;
these remain candidate bindings, not evidence of execution. Examples:

- `CINE__MANADJEUR.gao`, GAO `3500A99F`, brain `3500A9A1`: code key `3500A52F`
  maps to `00A338E0`. The function delegates to `00468310`; its object name does
  not establish a dedicated facial controller.
- Eleven cinematic event objects share candidate code `350091C9` / `00764AD0`.
  Its two inspected sends at `00764CEE` and `00764D36` use types 47 and 60.
  They are not producers of facial message 53.
- `WP_active_talk_home_peyj.gao` has three code leaves; checked code `008DF310`
  writes dialogue queue state on another object. It does not directly call the
  qualified queue sender in its recovered body. This is a dialogue lead, not
  a recovered facial-message path.

All 18 parsed actors in this container yield 414 decoded clips, zero failures.
Category-3 sender selector 8050 remains absent. There are nine 8056 rows and
219 8061 rows. The extra observed 8056 templates include integer type arguments
49 (Enterprise) and 33 (Aircraft), alongside Sally's 95. These results bound
this container's retained actor events only; they do not establish global absence
of facial keys or messages. Reports are under `FF0057FD-scene-events`.

A separate confirmed result concerns the automatic mouth implementation.
`004F7D30`, at `004F80A3` onward, reads the next form from controller
`AC4 + 4 * B44`, stores it in form slot 4, then assigns `(B44 + 1) % 32`
and marks slot 4 pending. `00466700` is a remainder helper, not a random-number
function. The gate requires slot-4 state 2 and nonzero `B48`. This path cycles
through a deterministic list when a transition settles; it is not a demonstrated
phoneme or audio-amplitude reader. Other speech paths remain unexamined.

Initializer code key `35001FF5` maps to `00A07680`. It writes both source lists
when the resolved leader reference at controller `+4` is nonzero. The branch
uses `+8 == 1`; the OVA names this field `des_i_friend`. The source also names
`B44` as `i_expr_bouche_blabla_id` and `B48` as `i_expr_bouche_auto`.

Branch `des_i_friend == 1`:

```text
6,4,4,4,1,4,5,5,5,3,5,1,1,4,4,3,
4,4,1,5,5,4,1,4,5,5,1,6,4,3,4,1
```

Other branch:

```text
4,1,4,1,1,3,1,4,3,1,5,1,4,3,1,5,
4,4,1,1,3,5,1,5,1,4,3,3,1,1,5,1
```

Do not select a branch from a character name. Actual instance values, activation,
initial index, factor/speed and original speech timing must be qualified before
adding native playback. These are form IDs passed through the controller, not
new emotion names or a timeline ready to attach to every head.

Native evidence: `hangar-cinematic-event-assembly.json`,
`pc-mouth-cycle-assembly.json`, `pc-mouth-index-helper.json`,
`pc-head-init-mouth-tables.json` and `pc-mouth-sequences.json` under the local
ignored `artifacts/export-verification/native` directory. Mouth-cycle assembly:
`ev_526657db150e48b21853914e8d82d7324e3fd52ed40444e733ac30510f50c3f3`;
initializer:
`ev_4382ab62698996ea20b82bd914cf6b6d8495dfa2b10357e15037cd6c0bf4c196`.

The next focused target is the writer of `i_expr_bouche_auto` and the source
instance's `des_i_friend`, followed by retargeting/timing. Message 53 remains
one possible control path, not an assumed universal lip-sync timeline.


## Automatic-mouth gate and integer-conversion correction

The message-53 branch at `005B4CCD..005B4D46` first checks the original mouth
command float. If it is zero, the automatic-mouth flag is unchanged. If nonzero,
a negative signed quotient residual enables `B48` and sets slot-4 state to 2;
a nonnegative residual disables `B48`. The following head update can then select
a form from the 32-entry list before normal retargeting. A negative intermediate
form value is not a directly applicable mesh target.

The shared residual local starts at zero in `005B20D0`, then changes whenever
slots 0, 1 or 4 decode a nonzero converted integer. If a nonzero mouth float
converts to zero, the final gate can reuse the preceding slot's residual.
Diagnostics preserve this ordering rather than assigning an independent mode to
each packed word. This is a bounded source-arithmetic result, not a claim that
fractional sub-unit commands occur in original dialogue data.

Important correction: `0040CF80` is not a truncating float-to-int conversion.
Assembly adds the double at `00BA4420`, stores a double and returns its low
32-bit word. Captured constant bits are `4338000000000000`
(6755399441055744.0). Under normal nearest/even FPU rounding, this rounds to the
nearest integer, including ties to even. `005ADBF0` calls the same helper for
Sally's five preset commands. The public positive preset decoder now emulates
this operation and retains its rejection of unsupported negative direct presets.

Boundary examples: 999.5 converts to 1000 (form 1, factor -5), 1000.5 also to
1000, and 1001.5 to 1002. A converted zero remains a no-op. Existing source
presets use integer commands and are unaffected. The decoder's native residual
rounding is still float32; it is not replaced with decimal weights.

`audit_pc_facial_message.py` now adds a bounded `controllerInterpretation` for
finite signed inputs in the Int32 range, explicitly assuming normal nearest/even
FPU mode. It retains all raw words and refuses to invent conversion semantics
for non-finite/out-of-range inputs. It does not claim to validate native form IDs
against a mesh or reconstruct scheduling. Python tests cover signed residuals,
no-op mode preservation, midpoint conversion, residual reuse and raw invalid bits.

A read-only exact-pattern search for both 128-byte mouth tables in the saved
`hangar-bankfix/BGE.exe_261019_121235.dmp` found no matches. Thus this capture
provides no qualified controller instance for `des_i_friend`, the mouth index
or active speed. The negative search does not prove the feature is unused; this
is a crash during location reload. No game launch or live memory mutation was used.

Validation: the complete export-verification suite passes, including original
Sally/military/Peyj clips and morph previews. All 43 available GLB fixtures match
the pre-correction baseline hashes. Python facial-message diagnostics pass five
tests. This corrects command arithmetic; it does not add automatic mouth playback.

Evidence under local `artifacts/export-verification/native`:
`pc-packed-command-integer-assembly.json`, `pc-sally-command-conversion-calls.json`,
`pc-mouth-auto-handler-assembly.json`, `mouth-state-candidates.json`.
Conversion evidence:
`ev_a2ce7a6a8a3ac2d6ee86eac63ad5782c485c0caad48e87659adc02439a397a6a`.
The remaining boundary is a qualified instance/producer and its activation/timing;
controller retargeting can be studied separately without inventing audio phonemes.


## Isolated controller transition evaluator

`PcMorphTransitionEvaluator` now evaluates one explicitly supplied controller
slot using the qualified transition branch in `004F7D30` and lerp `004410A0`.
It is independent of actor names, UI, message producers and audio. Snapshot and
phase types live under `Animation/Morphs/Models`. It does not select a head,
trigger a speech sequence, record keys or modify the source channel.

On pending state 1, the old second reference is copied to position 0, the
explicit new valid group is written to position 1, progression becomes 1 and
state becomes transitioning (0). Trailing source references remain intact.
The native setter ignores out-of-range positive old references and accepts
negative hole words through its signed upper-bound comparison; the isolated
implementation retains those rules for existing endpoints. New negative forms,
short channels, non-finite state and negative speeds are outside its supported
input domain and are rejected, not guessed.

For one explicit nonnegative delta step:

```text
blend = clamp(float32(speed * delta), 0, 1)
currentFactor = targetFactor * blend + currentFactor * (1 - blend)
progression = min(2, float32(progression + speed * delta))
state = settled if progression >= float32(1.99), otherwise transitioning
```

The x87 lerp returns an extended intermediate which is stored as float32.
The evaluator uses a double intermediate before its float32 result; this is
bounded arithmetic emulation, not a claim of bit-exact x87 behavior for every
possible input. For small repeated steps, factor motion is exponential, not a
constant-distance approach or a linear ramp. A zero-speed pending step retargets
references but leaves factor unchanged. Settled state 2 performs no further
updates inside this isolated branch; automatic sequencing remains external.

Retargeting during an unfinished transition restarts from its previous target
reference, not from a synthetic group representing the current blended pose.
The native 1.99 threshold also does not force progression/factor to their ideal
endpoint when it settles early. Both behaviors are preserved explicitly.

Tests cover a two-step factor trajectory, evaluated group crossfade through
`FaceMorphChannelEvaluator`, interruption, trailing references, source isolation,
large-step clamping, zero speed, the 1.99 threshold, invalid positive endpoints,
negative hole words and rejected inputs. The complete archive verification passes
for Sally, military and Peyj. The final extra endpoint tests also pass in the
synthetic verification run. Existing manual morph preview/export is unchanged.

Local evidence: `native/pc-transition-helpers.json` and
`native/pc-transition-update-assembly.json` under `artifacts/export-verification`.
Helper evidence:
`ev_9aae2b97197dfe75d1966e87bf735c51af674ef169a21b63ff5e9b9c5738bd9d`;
update assembly:
`ev_526657db150e48b21853914e8d82d7324e3fd52ed40444e733ac30510f50c3f3`.

Next integration boundary: qualify a real controller snapshot, instance branch
and activation/speed source before connecting transitions to automatic facial
preview or exporting original speech timing. The evaluator alone does not
recover those missing inputs.


## Source-default binding follow-up

[PC variable defaults](face-morph-pc-variable-defaults.md) documents the qualified
native OVA storage/name-overlay path and the initial `des_i_friend` values for
military and Peyj. This resolves the earlier uncertainty for equal-layout
source defaults, without claiming live state or original speech timing.
