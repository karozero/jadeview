# PC dialogue and facial-event chain

Status: archive-wide static inventory and trigger-path reconstruction, with a
user-reported in-game facial-animation check. This is an ongoing
reverse-engineering map, not a claim that the complete dialogue runtime or
every speech path has been recovered.

### 2026-10-20: supported cinematic sound and visibility path

The generic preview now resolves literal sound-modifier play/stop events to
serialized `.snk` bank slots and archive WAVE/MS ADPCM resources. For FF0045A2,
clip 3500B8EA is owned by CINE_BIGINTRO_EVENTS.gao (100045A4): its modifier 0
selects slot 0 of bank 3500BAA0, which resolves in the loaded stream to
61009A64 / 002_big_intro_FR.wad. The source play event is at 1.2 seconds.
The separate intro-event owner 3500DEF5 uses slot 6 (001_intro_keck_FR.wad);
these owners and clips must not be conflated by matching scene names.

The player mixes these cues on the facial/body/camera clock and rebuilds
literal control-flag changes when seeking. It also restores the saved GAO
invisible/inactive bits and excludes actors unused by the selected clip.
This closes one concrete event-to-audio join described as unresolved below;
the historical inventory and limits still apply to general dialogue, dynamic
messages, text-to-voice joins and phoneme-level synchronization. The verified
scene contains French bank data; language switching is not implemented.

See [Cutscene preview](cinematic-preview.md) for controls and verification.

## Scope and archive-wide result

The census walks every archive entry whose key is in the `FFxxxxxx` container
range and parses its scene records. For the supplied archive, all **91/91**
containers parsed, including **4,365/4,365** animation records, with zero clip
decode failures. It found 4,362 category-3 source occurrences of selector 8056.
That selector alone is not a facial identifier: after decoding the observed
literal source shape and checking the constructed message's type word, **442**
occurrences are type 53 (`0x35`) candidates.

Those candidates are in **27 containers**, across **62 container/clip pairs**,
and resolve by same-container track key to **18 target keys**. Seventeen target
keys match a named record with a brain in the same container; five candidates
use a zero target and remain unresolved. Named targets include Sally, Pey'j,
militaire, Mr. Hahn, Secundo, Nino, Journaleux, Meii, the Gouverneuse, and other
NPCs. The report preserves each source container, clip key, record offset and
SHA-256, event offset/time/flags, target identity, and decoded 52-byte message.

This is an inventory of candidate source events, not a count of unique runtime
dialogues or executions. Shared resources, repeated clips, conditional event
keys, dynamically constructed messages, non-FF dependencies, gameplay dialogue
paths, and audio-to-event links need separate analysis.

## Reproduce the broad search

From the repository root, with the original BF archive available:

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -- --archive-cinematic-census "<path-to-original.bf>"
python tools/audit_pc_archive_facial_messages.py artifacts/export-verification/archive-cinematic-census.json --output artifacts/face-morph-dialogue-chain/archive-facial-message-candidates.json
python tools/audit_pc_archive_message_types.py artifacts/export-verification/archive-cinematic-census.json --output artifacts/face-morph-dialogue-chain/archive-message-types.json
```

The first command writes the raw FF-container census under ignored `artifacts`;
the second applies a deliberately narrow literal-template decoder and filters
for message type 53; the third inventories all recognized types and records the
type-30 line selectors. A candidate is accepted only when the full 19-row
event shape matches. Other 8056 calls are left out of the facial candidate set;
they may be ordinary messages or dynamically assembled variants and are not
thereby classified.

## Recovered path so far

```text
BF archive
  └─ FF scene/container records
      ├─ actor brain and q/s/t/r dependency resources
      └─ i animation records
          └─ animation track + keyed event payload
              └─ event-key runner (004633F0; conditional on playback state/flags)
                  └─ category-3 selector dispatch (005EBF70)
                      └─ selector 8056 / native sender (00474670)
                          └─ 52-byte actor message queue (capacity 5)
                              └─ type-53 actor handler (005B20D0)
                                  └─ facial controller slots/channels
                                      └─ type-4 morph geometry evaluation
```

The archive records and the functions in parentheses are supported by the
static parser/native evidence linked below. The arrows mean “recovered or
candidate connection,” not unconditional execution:

- The event runner dispatches matching key events only when the relevant track
  is active and suppression/state checks allow it. Flag `0x41` marks the
  observed type-53 keys as once-per-key-entry events. The archive census does
  not simulate playback, branches, loops, or key suppression.
- Selector 8056 is registered to `00474670`. It consumes typed VM arguments,
  builds a 52-byte message, and appends it to the current destination actor's
  bounded message buffer. A matching source template does not prove that its
  destination reference resolves or that the queue accepts it.
- The actor's registered type-53 handler `005B20D0` consumes the message and
  updates facial-controller state. Its command conversion, mouth-auto gate,
  transition state, and type-4 channel evaluation are documented in the
  runtime findings. A form number is not automatically an emotion name or a
  valid target in every head mesh.
- Actor brains point to script/model/variable/code resources. For the 62
  cinematic owners linked to these candidate facial clips, the census now
  resolves that closure and decodes start-condition defaults. It does not
  cover every gameplay dialogue object or prove that a candidate clip ran.
  A dialogue queue write is not by itself proof that a facial message is sent.

## Recovered cinematic trigger lifecycle

The second archive pass links the facial-event clips to their owning GAOs and
their brain resources instead of treating the clip as a free-standing timeline.
All **62/62** owners resolve to a brain and successfully decode model plus
instance OVA defaults. **61** use the common `350091C3` controller with the same
11 code keys; one (`WP_Paye_Facture.gao`) uses a separate model/package. This
shows a shared trigger implementation for most cinematic actors, with at least
one real variant; it does not establish that all dialogue in the game uses this
controller.

```text
scene/container GAO owner
  → brain and model/instance variables
  → first-init / cine_init
      → already-completed story-flag check (one-shot destruction)
      → configure which start tests are enabled
  → cine_tests_depart
      → configured save-flag / actor / state / collision tests
      → cutscene priority and interruption checks
  → InitialiseCine → GoToEvents
      → set playing state and call TRAPlayAll
  → clip's keyed event track
      → selector 8056 sends message type 53 to the actor
  → actor type-53 handler → face morph controller
```

The names and order above are recovered from the `irisEng` compiled-script
source. The linked function keys occur in the PC BF dependency closures, and
the installed 2003 native image has a matching large conditional procedure at
`0x009B96E0` plus the keyed native routines. This is strong cross-evidence for
the control flow, but `irisEng`'s exact build provenance is unknown, and the
native key-to-address mapping is a candidate table rather than a symbolized
source map. Treat the source names as labels for the correlated script keys,
not as proof the pre-release source and retail native implementation are byte
or behavior identical.

### Worked example: `CINE_OUVRE_SAS_EVENTS.gao`

In container `FF00791B` (`01_00_entrepot_sas_entree_cine`), owner GAO
`2000798C` references brain `20007990`, model `350091C3`, and clip `20007991`.
The clip has three type-53 source events for actor `71003F5A` (Sally), at
1.000, 13.383333, and 13.816667 seconds. Their reconstructed commands are
documented in [the cinematic event audit](face-morph-pc-cinematic-events.md).

The serialized defaults show this instance is not unconditional:

- `des_i_specialFlag_test1 = 0x7F5`; the common controller tests this save bit
  before proceeding.
- `des_o_actor_zone1 = 0x2000798C` points back to the owner zone and
  `des_o_actor_detec1 = 1` resolves to `AI_MainActor[0]` (the main player
  actor). `des_i_actor_type1 = 0` selects the zone collision/overlap test.
- `des_i_SPFlag_associe_vrai = 0xF0D` is the completion-associated flag. The
  initializer removes an already-completed event object when this flag is set;
  the end routine sets it, making this event one-shot across the relevant save
  state.
- `des_i_cine_type = 7` and `des_i_blocage_joy = 1` configure cinematic
  behavior/input handling. These values are not the trigger itself.

Therefore the supported statement is: this object waits for its configured
save and player/zone conditions, passes the shared cinematic priority path,
then starts its event tracks; the Sally messages occur later at the clip times
above if that track reaches those keys. The BF alone does not establish that a
particular playthrough met the conditions or that the actor queue accepted each
message.

### The one different owner: `WP_Paye_Facture.gao`

This object is not driven by `Cine_Events`. Its keys map in the external
compiled-script source to `INV_SecundoEtLaFacture_firstinit/init/loop`. The
instance points to Secundo (`3500BDC4`) and the invoice animation clip
`3500DFB3`. In that source, `firstinit` initializes the invoice manager and
switches the object's AI track to `init`; `init` prepares Secundo's visibility,
position and action, creates the invoice text/audio feedback, then calls
`TRAPlayAll` and switches to the transaction loop. Thus this facial-event clip
is started by a gameplay transaction controller, not by the shared
cutscene-priority manager. Its messages remain time-keyed message-53 events on
the animation track.

The archive census finds seven such events for Secundo at 1.07, 1.73, 1.83,
3.40, 4.05, 4.60, and 4.83 seconds. The script source clarifies the distinct
launch mechanism, but its exact correspondence to the installed PC binary is
not independently symbol-verified; do not generalize its behavior beyond this
owner without checking other packages.

### Manual in-game check

The user manually ran the three-character dialogue cutscene discussed in this
investigation and reported that the faces changed during dialogue for all
three heroes (Sally, Pey'j, and Secundo). This confirms that facial animation
is visibly active in at least one real in-game dialogue sequence, beyond the
static archive/native evidence above. The check was not captured by an external
logger, so exact event times and per-actor message delivery were not
independently recorded. It does not prove that every candidate event in the
archive executes or establish a universal audio-to-lip-sync relationship.

The scene discussed for this check is `FF005833`
(`00_02_home_decryptage_cine`). Its archive census contains seven literal
type-53 candidates: one for Sally and six for Pey'j across clips `BF00012B`,
`BF00026A`, and `BF00218E`. None is a literal type-53 message to Secundo. Some
messages decode to a nonzero channel-4 factor, while others decode to zero; the
native form ID is still not validated against a specific head mesh.

The all-track event audit found a separate Secundo path in that scene. Root
`Cine_PNJ_Secundo.gao` (`3500BDC4`, brain `3500D45C`) resolves to the
`Cine_lipsynch_auto` functions `3500B774` (init), `3500B77E` (morph update),
and `3500B773` (message reader). Its instance OVA sets
`des_i_type_acteur = 2`; the recovered init branch selects mouth channel 3 and
installs the type-2 mouth-form list. The message reader consumes type 53 and
can address a target GAO carried in the message.

More directly, clip `BF00218E`, owned by `CINE_APRES_MDISK_EVT02.gao`, has **20**
selector `8263` (`0x2047`, `OBJ_SetModuleChannelData`) events on the Secundo
track, between 2.20 and 15.10 seconds. The recovered native function writes a
morph modifier's per-channel morph-data index (`ptr2[channel].ptr[key] = form`):

| Morph channel | Key slots changed | Source morph indices |
| --- | --- | --- |
| 1 | 0–5 | 3, 2, 3, 2, 3, 2 |
| 2 | 1 | 10 |
| 3 | 1–13 | 6, 7, 8, 6, 8, 7, 7, 8, 7, 8, 7, 8, 7 |

Secundo's exported head mesh `b_Crumble_TeteBD.gao` has 13 PC morph targets.
The same clip also contains morph-helper tracks whose selector `8262`
(`OBJ_AnimMorphFactorNoKey`) payload names the target GAO, module selector and
morph channel. The helper's animated position supplies channel state: X is the
factor, while Y plus one is progression. The mapping is read from event payloads
and helper transforms, then applied to the addressed actor module; no character
name or guessed weight is needed.

The `FF005833` cutscene playback/export audit now produces non-neutral facial
curves for Sally, Pey'j and Secundo from their source-timed morph helpers and
channel-form updates. Their tracks import into Blender together with actor
actions and two animated source cameras. `tests/ExportVerification/blender_cinematic_check.py`
checks the imported GLB curves. This establishes the archive's authored facial
control timeline for the verified clip. It still does not join those morph
curves to a specific localized sound sample or phoneme stream.

## Dialogue messages and where their data lives

Playback correction (2026-10-20): helper bindings and helper transforms can be on
separate tracks. The preview now samples their factor/progression continuously,
rather than once at the binding event. The viewport no longer advances cinematic
mouth controllers a second time. BF00218E's source audio resolves to the single
scene mix `61009A6F_014_apres_m-disk_FR.wad` at time zero. Changing facial weights
for Sally, Pey'j and Secundo were verified in Blender after these fixes; exact
per-phoneme/audio parity is still not established.

The archive-wide census also distinguishes literal type-53 facial messages
from other selector-8056 message types. Applying the same strict 19-row source
template check to all parsed FF containers yields **721** literal messages:

| Message type | Occurrences | What can currently be said |
| ---: | ---: | --- |
| 30 | 85 | Dialogue/text-manager messages, identified by the matching irisEng handler. |
| 53 | 442 | Facial-controller messages, as described above. |
| 60 | 90 | A distinct type; meaning not assigned in this audit. |
| 46, 47, 48, 49, 50, 62, 87, and 0/1 | 104 | Other literal message types; not classified here. |

The 85 literal type-30 messages occur in four containers and target four
dialogue GAOs: `Cine_01_04_dialogues.gao`, `Cine__dialogue.gao`,
`Cine_0500_dialogues.gao`, and `Cine__Dialogues.gao`. Their clip keys and
timestamps differ, but the target objects share the same observed OVA schema:
100 line slots, a text-reference array, and parallel arrays for sound ID,
variant count, caption placement, volume, and near/far distance. The four
inspected instances populate different line slots and text references; their
serialized sound-ID arrays are zero. This supports a reusable dialogue data
model, not a claim that every game dialogue uses it.

The matching `Cine_Textes_CiTxt_reception_messages` implementation in irisEng
shows the intended type-30 path: the message's `Int1` chooses a line slot
(including a special “advance” value), the selected text reference is copied
to the shared `GST_AffichageTexte` object, and the line's sound ID plus optional
random variation, volume, and near/far values are passed to the sound system.
The line table supplies data; the keyed animation/event track supplies timing
and line selection. A message to the manager does not itself contain the
localized sentence or encoded audio waveform.

One archive example demonstrates the join without depending on a character
name: in `FF00948D` (`05_00_ilot_sas_accueil_cine`), the clip track sends type
30 to `Cine_0500_dialogues.gao` at 7.00 seconds with `Int1 = 40`. That object's
OVA slot 40 stores text reference `(C1009667, 32)`; slot 41 stores
`(C1009667, 33)`, and the next type-30 event at 10.77 seconds selects slot 41.
Thus the event selects an OVA row, and the row selects a text-bank key plus a
text entry ID. A raw-byte scan of both installed BF files found 14 stored
occurrences of `C1009667`: two within `FF00948D` and one in each of twelve
`FD...948D` text-catalogue records (`FD10948D` through `FDC0948D`). The
catalogue records contain the ASCII `.txg` extension immediately after that
key, matching the later Jaded source's TXG/TXL indirection model. After
decompressing `FF00948D`, the resource contains 54 `(C1009667, id)` pairs,
including IDs 32 and 33; 50 of those references form the dialogue object's
line-reference table. This confirms the scene's text-group references and the
catalogue relationship. The key is not a top-level BF directory entry,
however, and we have not decoded the embedded TXG/TXL mapping to localized
text strings for IDs 32 and 33. A string scan of an exported FD catalogue
shows localized dialogue, but does not associate a specific string with either
ID. The text-manager source routes negative `Int1` values to its clear/hide
path rather than looking up a text slot.

There is a promising potential bridge from dialogue entries to face animation
in the later `jaded` source checkout. Its type-44 text value is an 8-byte
`TEXT_tdst_Eval` pair (`i_FileKey`, `i_Id`), matching the pair stored in the
PC OVA rows. Its TXT loader reads a catalogue of TXG references; each TXG
selects a language-specific TXL. A TXL entry stores the text ID, sound key,
object key, text offset, priority/version, facial/lips/animation indices, and
editor metadata, followed by the text buffer. The engine exposes functions to
read facial and lips indices from a text entry. This is a concrete
cross-version data model, not a decoded PC entry. It could explain how dialogue
metadata can be associated with voice and facial/animation cues at the text-bank
layer. However, `jaded` is a later engine snapshot: we have not decoded the
embedded TXG/TXL mapping for this PC archive or proved that the 2003 retail
dialogue path consumes those three fields. Treat this as a strong cross-version
lead, not as a recovered retail behavior.

The retail PC archive now provides a direct format comparison. IrisTool's
`BFTextParser` reads an FD text catalogue as sequential embedded chunks: a TXG
group list, each TXG's language references, then the selected TXI identifier
table and TXS string pool. These child keys are not separate FAT entries.
JadeView's existing `TextDocument` reader decodes the TXI values as byte offsets
into the TXS pool and returns strings in table order. This interpretation is
confirmed by `FD105833 / 3500A1C9`: the French first offsets are `0, 33, 79,
127`, while the English `FD205833 / 3500A1C9` offsets are `0, 31, 75, 133`;
both address the corresponding string starts, whose lengths differ by
translation. The PC TXI/TXS pair contains no Jaded TXL `ul_SoundKey`,
`c_FacialIdx`, `c_LipsIdx`, or `c_AnimIdx` record. The shared TXG/TXL naming
does not make those wire layouts interchangeable.

Jaded's source does establish a later-engine dialogue API: `TEXT_ToSound`,
`TEXT_LipsIdxGet`, and `TEXT_FacialIdxGet` each resolve the same
`TEXT_tdst_Eval` (`AIfunctions_text.c`); `SND_RequestPlayDialog` calls
`TEXTToSound` and requests the resolved runtime sound index
(`AIfunctions_snd.c`). The text loader separately loads the display language's
facial/lip metadata and overlays the sound language's `ul_SoundKey`
(`TEXTload.c`). This is useful architectural evidence for how a dialogue system
can connect text, voice, face and animation. It is not evidence that the retail
PC executable uses this later TXL record or those APIs. For current PC preview,
keep explicit type-53 messages and the scene's WAD cues as separate decoded
inputs until a PC-specific join is proven; do not derive mouth motion from the
TXI offsets or string order.

I also checked the installed retail `BGE.exe` (SHA-256
`70ae232d58bafe5cbf5c27712538af91e8b13a98104ae1bf6aea6dcf3506f5db`) with
Ghidra string searches for `FacialIdx`, `LipsIdx`, and `TEXT_FacialIdxGet`;
none appeared among Ghidra's defined strings (Evidence IDs
`ev_e1645662e70f685332cfd47d97a1c9df3fcc960908f53b1e8b9732effd2b987c`,
`ev_4a5ac219d0b9af6c8a1dd3b03162fb508eec4b6487ff7e394d5301727a20cd42`, and
`ev_0717af2674f6271dca86f598e9d9d3907a140fed8e8f05165631664a3f84b6ea`). The
binary references `BGE.pdb`, but Ghidra could not establish that PDB's load
state. Since this search only covers defined string objects and retail
symbols/types are unavailable, the empty result is not evidence that these
native functions or fields are absent.

The PC archive inventory separately contains localized cinematic sound-bank
entries under paths such as
`EngineDatas/05 Audio Bank/Sound Library/Localisations/US/Cines`, with `.wad`
names corresponding to numbered cinematic packages. Jaded's importer
classifies `.wad` as a Dialog sound type, which supports that interpretation
but comes from the later engine source. The archive inventory confirms that
voice/audio assets are stored in a separate bank from FF scene records.
We have not proved the complete join from a specific type-30 line or facial
event to a specific WAD entry and sample. In particular, the inspected
dialogue GAOs have zero serialized per-line sound IDs, so any voice heard in a
sequence may be driven by another event/script path or data not represented in
those instance arrays. Do not infer a line-to-WAD mapping from matching names
alone.

This gives a general model while keeping unresolved joins explicit:

```text
BF archive directory
  ├─ FF scene container: worlds, GAO roots, brain references, animation clips
  ├─ shared object/script resources: q/s/t/r references resolved by key/world
  ├─ line table: text-bank key + text entry ID, plus per-line metadata
  └─ localized audio bank: cinematic WAD packages and sound resources

cutscene controller / gameplay controller
  → starts animation tracks after its own conditions
  → keyed events send typed messages to a target GAO or shared manager
  → type 30 selects dialogue data; type 53 updates actor facial state
  → separate audio requests resolve and play sound-bank entries
```

The branches can share a timeline without being one indivisible event: clip
events are time-keyed, dialogue and facial messages have different types and
recipients, and audio is handled by sound-manager code. Static archive evidence
does not establish that all three requests occur on every line or that their
timestamps are phoneme-synchronized.

### `BF00218E` lane comparison (rechecked 2026-10-24)

I regenerated the source motion audit from `sally_clean.bf` for
`FF005833 / BF00218E` and compared the recovered message and morph-command
lanes. The type-53 event times are:

| Track context | Type-53 times (seconds) | Recovered mouth-cycle intent |
| --- | --- | --- |
| Sally `71003F5A` | 6.62, 10.23, 13.83, 17.25, 21.00, 27.53, 29.37, 44.70 | Source commands alternate automatic-cycle start/stop; the clip also has separate channel writes. |
| Pey'j `9E001C75` | 24.10, 24.97, 26.68, 27.62 | Source commands start/stop automatic cycling with different authored speeds. |
| Secundo `3500BDC4` | none on this type-53 lane | His face is driven by 20 direct channel-form writes, type-8263 (`OBJ_SetModuleChannelData`), from 2.20 to 15.10 s. |

The same clip has 24 type-8263 face-channel writes on Sally (0–35.02 s) and
15 on Pey'j (0–27.70 s). Type-30 line-selection messages occur on their own
track and continue beyond several of these face-control intervals. This matches
the source-level split: type 30 selects a dialogue table row and forwards audio
metadata, while facial changes use separate messages and direct morph-channel
events. It explains why the preview can have dialogue audio without a matching
mouth cycle on every line. It does **not** prove which voice sample is paired
with each message or that every static event passed its runtime owner/queue
gates.

The paired audio audit resolves six localized alternatives for the same
`014_apres_m-disk` WAD (FR, DE, ES, IT, NL, US), each spanning the full
44.78-second clip; one is selected for playback at a time. It also reads 31
dialogue text references, whose inspected OVA sound IDs are zero. These are
language variants of the clip's source bank, not 31 per-line voice assets. The
separate text references do not identify which samples inside the WAD belong to
which line.

The result is reproducible with `--cinematic-motion-audit` and
`--cinematic-audio-audit` against the original archive. `CinematicMotionAudit`
records source keys and event payloads; it does not execute the dialogue queue or
infer runtime speaker/audio timing.

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release --no-restore -- --cinematic-motion-audit "E:/SteamLibrary/steamapps/common/Beyond Good and Evil/sally_clean.bf" FF005833 BF00218E
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release --no-restore -- --cinematic-audio-audit "E:/SteamLibrary/steamapps/common/Beyond Good and Evil/sally_clean.bf" FF005833 BF00218E
```

### Remaining limits

The archive-to-trigger linkage and the common cinematic controller are now
mapped for all 62 owners. The following parts still prevent calling the whole
game's dialogue/cutscene system fully solved:

1. The census covers literal selector-8056 templates in parsed FF scene
   containers, not all dynamic script sends, non-FF resources, or gameplay
   dialogue producers. The type-30 count is the subset matching this literal
   template, not a complete count of dialogue executions.
2. Static OVA defaults describe authored conditions; they do not simulate
   runtime save state, actor resolution, collisions, priorities, event track
   activation, queue capacity, or successful receipt.
3. The data model and separate localized audio-bank organization are
   established, but the join from line/event IDs to a specific localized WAD
   sample, and the relation between audio, subtitles, phonemes, and facial
   timing, remain unproven. Do not describe the 32-step mouth cycle as dialogue
   audio lip-sync.
4. Dynamic type-53 sends and other gameplay dialogue packages beyond the
   invoice manager are not yet inventoried.
5. Expression presets, blink scheduling, and gameplay dialogue may feed the
   same actor controller through different producers.

## Evidence and limits

- [Cinematic event and message reconstruction](face-morph-pc-cinematic-events.md)
  covers event-key execution conditions, destination context, sender argument
  order, and the focused Sally/Pey'j examples.
- [PC runtime findings](face-morph-pc-runtime.md) covers selector registration,
  message-53 handling, brain resource closures, controller state, and known
  unresolved dialogue/audio links.
- [Face morph analysis](face-morph-analysis.md) covers type-4 source geometry,
  morph discovery, and JadeView's preview/export behavior.
- [External Jade project review](external-jade-project-review.md) compares
  irisEng, IrisTool, and Jaded with explicit version/provenance limits.

### External source comparison

The irisEng checkout contains BGE script source using the same facial message
ID 53 and native keys `3500B77E`/`3500B773` found in this PC archive/native
audit. Its named `Cine_lipsynch_auto` reader shows one controller path from a
queued message to mouth-form interpolation and an optional 32-step mouth
cycle. This supports the currently recovered 2003 path, but its exact build
provenance is not established and it cannot prove that another 2003 cutscene
starts unconditionally. Jaded's morph-key event implementation is from a later
engine snapshot and remains a distinct, unconfirmed path for BGE 2003.

The IrisTool comparison is useful for asset and animation inspection, but it
explicitly lacks scripts and cutscene playback, so it cannot close trigger
conditions by itself.

### Facial message module targeting

The Iris source initializes Sally's `o_module_tete` with
`ANICanalGaoGet(0)` in `external/irisEng/ai/ai_bge/ai_compiledscripts_sally.c`
(`Sally_sa_exec_get_modules`). Her type-53 cinematic-expression handler then
updates facial channels through `o_module_tete`. The companion script also
initializes its head module from selector 0 in
`external/irisEng/ai/ai_bge/ai_compiledscripts_companion.c`. JadeView now
routes the decoded Sally/companion type-53 morph and mouth commands through
`SceneActor.FindModule(0)`, which uses the actor's active PC shape selector map.
It no longer applies those facial effects to every morph-bearing part of the
actor (for example, hair or equipment). Sally's explicit blink overlay is also
bound to that source-selected module. If selector 0 cannot resolve to a preview
mesh, the effect is skipped rather than broadcast to unrelated meshes.

`CinematicFacialSignalChecks` covers this rule with a synthetic actor whose
selector map points module 0 to a head mesh and module 1 to a separate morph
mesh. This verifies JadeView's routing against the recovered source contract;
it does not establish that all character scripts use the same module selector.

The matching type-30 source and the `tMessage` field layout are in
`external/irisEng/ai/ai_bge/ai_compiledscripts_global_managers.c` and
`external/irisEng/gameobject.h`. Jaded's text-entry format and facial/lips/anim
metadata are in `external/jaded/src/libraries/ENGine/Sources/TEXT/TEXTstruct.h`,
`TEXTload.c`, and `external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_text.c`;
these latter files are cross-version leads, not confirmation of identical
retail PC behavior.

To regenerate the owner/brain/default census after producing the literal
facial-message candidate JSON above:

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -- --archive-facial-script-census "<path-to-original.bf>" "artifacts/face-morph-dialogue-chain/archive-facial-message-candidates.json" "artifacts/export-verification"
```

The generated JSON is ignored build evidence. Its owner rows include container,
clip, scene, track target, resolved brain closure, model and instance OVA
defaults, missing dependencies, and code keys; it is not a runtime execution
trace.

The archive audit is structural and read-only; candidate counts depend on the
exact BF archive analyzed. The manual check above is user-reported runtime
evidence, not an instrumented execution trace. It does not provide exact event
delivery/timing, infer dialogue text, map every message to audio, or claim that
the FF-prefixed scene/container inventory includes every resource reachable
by the engine.
