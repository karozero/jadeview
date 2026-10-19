# PC facial messages in cinematic resources

The earlier hangar event audit covered clips attached to parsed actors. It did
not cover every standalone animation/event resource. Do not interpret that
earlier negative result as absence of facial messages in the scene.

## Expanded coverage

The original `sally_cmlean1.bf`, container `FF0057FD` plus fixed resources
`FF0084DF`, contains 432 parsed `i` record occurrences. All decoded without
errors. Their category-3 source tags include 93 calls to selector 8056, compared
with nine in the earlier actor-only audit. Selector 8050 remains absent.
Counts describe source occurrences, not distinct executed calls.

Four 8056 event occurrences have literal integer 53 in their third word-pair
row (`00000035 / 01000021`):

- `3500A8AC`, record offset `0019D1E6`: time 1, track header `0000009C`.
- `3500A8B4`, record offset `001A0D5E`: time 1, same header.
- `BF00029C`, record offset `001A4CB8`: two occurrences at time 1, same header.

These are **candidate type-53 sends**, not yet a reconstructed facial timeline.
Each candidate includes two selector-6000 calls constructing three-component
values before selector 8056. One retained component is float 500
(`43FA0000`). Its final message-field meaning is not established here.
Target expression `02000806` and control/context tag bits also require decoding.

Native sender `00474670` confirms that 8056 consumes integer, float, vector and
reference arguments to assemble a 52-byte message; its type comes from an
argument rather than a fixed constant. Source row position alone does not prove
the full evaluated stack, destination actor, branch execution or timing.

## Repeatable audit

```powershell
dotnet run --project tests/ExportVerification -- --container-event-audit <original.bf> FF0057FD
python tools/audit_animation_event_words.py artifacts/export-verification/FF0057FD-container-events --selector 8056 --output artifacts/hangar-senders.json
```

The directory reader follows `index.json`, ignores stale unlisted output files
and retains container, record offset and SHA-256 for each match. It does not
deduplicate shared/fixed resources or execute event arguments.

Local evidence: `artifacts/export-verification/native/container-type53-candidates.json`,
`hangar-container-senders.json` and `container-sender-confirmation.json`.
Native evidence `ev_6f045a07545a76eecccf643374f015a5621c7700478d12b75ba0eeffa5730246`
uses the historical-capture-derived carrier, SHA-256
`295f66ef3a1486fe84d46db745788506671958227bb7ff2c0283f5a89d0c8be0`.

## Literal message reconstruction

Follow-up static analysis qualifies a narrow 19-row literal template. Selector
6000 constructs a vector from x/y/z in source order. Sender 8056 then consumes
the arguments in reverse stack order: int2, int1, float2, float1, vector2,
vector1, ref2, ref1, type. Assembly confirms their placement in the 52-byte
message. This establishes the message values conditional on reaching the send,
not whether its source control/context flags allow execution.

All four hangar occurrences reconstruct mouth command `500`, mouth speed `8`,
forced blink `0`, and zero commands in channels 0/1. The recovered controller
arithmetic selects form 0, target factor 0 and disables automatic mouth cycling.
The track source targets are Sally (`71003F5A`, three occurrences) and Peyj
(`9E001C75`, one occurrence). The conditional binding rule is qualified below;
actual delivery during a game session has not been observed.

Container `FF00791B` plus fixed resources adds 441 parsed records, zero decode
failures and 66 source calls to 8056. Three matching literal type-53 occurrences
are in clip `20007991`, track target `71003F5A`:

- At 1 second: mouth `500`, speed `8`, automatic cycling off.
- At 13.383333 seconds: mouth `-580`, speed `11`, automatic cycling on, form 0
  and target factor approximately 0.8.
- At 13.816667 seconds: mouth `500`, speed `9`, automatic cycling off.

This is a source-time start/stop pair separated by approximately 0.433334
seconds. It does not establish an audio/phoneme association or a played result.
All seven occurrences retain destination source reference zero before context
selector 2054 (`02000806`). That selector is registered at table `00B7D4C8`,
row 6, callback `00477790`; it pops a reference into destination context.
The checked ordinary code-reference resolver preserves zero. The cinematic
runner handles this prefix specially, as established in the follow-up below.

```powershell
python tools/audit_pc_cinematic_messages.py artifacts/export-verification/FF00791B-container-events --output artifacts/cinematic-messages.json
```

This tool rejects dynamic arguments and other expression shapes. It retains
context/send/terminator tags, track source key and source offsets, alongside a
conditional message interpretation. Focused tests cover argument order,
automatic-mouth enable/reset and unsupported shapes.

Additional local evidence: `pc-event-send-assembly.json`,
`pc-event-vector-assembly.json`, `pc-event-vector-push.json`,
`pc-event-target-context.json`, `pc-event-reference-resolution.json`,
`hangar-cinematic-facial-messages.json`, `peyj-cinematic-facial-messages.json`.
Sender assembly evidence:
`ev_be7f2f0534cc7fd830a9b14f645b1f7edf66510eeab361307102a9b8fb5356de`;
context callback evidence:
`ev_c84b2746adea59a0ddc5c157163a747176fb252e076c5a648800da1c460a1eb4`.
The carrier identity and historical-capture limitation above still apply.

## Inherited target context and key execution

Static native analysis now explains the null prefix without inventing a
reference substitution:

1. `00463AD0` selects the current runtime track into `00BA27E8`.
2. `00AF0970`, for a track whose bone index at `+14` is `FFFF`, sets the VM
   destination/current actor from the track's first word. If that word is zero,
   it uses animation owner `00BA27D8`. Other bone indices take a separate object
   lookup path and remain unsupported by this diagnostic binding view.
3. `0045FE10` checks whether the expression starts with operand zero followed
   by category 2 / selector 2054. If so, it skips **both** eight-byte rows.
   The inherited actor context therefore survives; the null destination setter
   is never called for this prefix.
4. `00474670` saves that destination before resetting temporary context and
   assembling/queueing the message. The recipient must have its runtime message
   buffer, and the queue has capacity five. A source send does not prove receipt.

All seven inspected occurrences have source bone `-1` and nonzero track target.
Their source binding is therefore the track object, conditional on resolving
that source key to the runtime track reference. Explicit nonzero reference
prefixes continue through selector 2054; they must not inherit the track key.

The event-key runner `004633F0` dispatches kind `flags & 0x7C0 == 0x40` only when
suppression global `00BA280C` is zero. For key flag bit 1, an already-entered key
is skipped; runtime per-track state bit 4 records entry and is cleared when
advancing to a new key. Thus source flags `0x41` in these occurrences describe
an event run once per key entry, including a new entry on looping/restarting,
rather than an event continuously sent for its duration. Stop conditions from
key bits `0x08/0x10` are absent in these seven keys.

Track activation, disabled state, playback direction, suppression, resolved
references, looping and queue pressure remain runtime inputs. This is not a
complete simulation of the game scheduler. `035C1F78`'s middle tag byte is not a
branch condition in the checked category-3 callback dispatcher (`005EBF70`):
it selects the remapped low-short callback. This does not assign semantics to
every middle tag bit in all VM contexts.

The diagnostic now reports `sourceContext` with inherited/explicit/unresolved
binding and normalized key flags. Tests cover owner fallback, unresolved bone
lookup and explicit references without replacing them by track targets.

Evidence under the same ignored native folder: `pc-event-vm-entry.json`,
`pc-event-null-prefix-assembly.json`, `pc-event-track-context-assembly.json`,
`pc-event-key-dispatch.json` and `pc-event-track-runner.json`.
Null-prefix assembly:
`ev_4ee3d3c868bda230a04bf16d5332b84fadbb41f5f574eeb7afddb037ee5852e9`;
track context assembly:
`ev_b1ddedb53238bec60323a3611971170dfc3563e261e9a7241c7a936394eefd7b`;
key dispatcher:
`ev_41df8bcd75f6bdc8703ba82ac7715505261bc2a8b9742b0907d83f7c83e2b1ec`.

## Isolated mouth-cycle evaluator

The completion rule depends on the source controller; see
[controller variants](face-morph-pc-controller-variants.md) before selecting one.

`PcMorphMouthCycleEvaluator` now models the slot-4 cycle before the existing
transition evaluator. Its caller supplies a 32-form sequence, next index,
enabled flag and transition snapshot. It never chooses a character profile or
assumes that source defaults equal the current runtime state.

If enabled and settled, one update selects `sequence[index]`, advances the index
modulo 32 and starts a pending transition. That transition advances in the same
update. At most one form is selected per update even with a large delta.
Disabling cycling leaves an ongoing transition able to finish; a reset command
can retarget it toward factor zero without snapping its current factor.

Synthetic verification includes the recovered `-580 / speed 11` followed by
`500 / speed 9` command effects over an explicitly assumed 60-Hz interval.
The fixture's sequence, initial index and initial state are supplied test data,
not a Sally runtime reconstruction. It also covers index wrap, source isolation,
one-form-per-update behavior, disabled transitions and invalid sequence inputs.
Sequences referencing unsupported form IDs are rejected instead of inventing
native behavior for invalid form writes.

Next: qualify each source controller's form sequence, channel mapping and initial
state before attaching the offline evaluation to a model. Actual speech/audio
synchronization and full game scheduling remain unresolved. The new evaluator
does not enable automatic facial playback in the UI or change GLB export yet.
