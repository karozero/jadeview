# Retail PC AI save-state inputs

## What the archive contains

The BF archive supplies OVA variable descriptors, serialized defaults, AI model
references, and optional per-instance OVA overrides. Iris copies model defaults
and overlays instance values by variable name. These define the schema and
fresh-instance defaults; they are not necessarily the current gameplay state.

For the Universe AI instance, Iris derives the save-buffer size from OVA
variables marked `AI_CVF_Save` (`0x10`). Saving packs those variable values in
OVA descriptor order; loading restores the same sequence. A variable's offset
inside its OVA is therefore not its offset in the save payload. To locate a
saved field, walk the Universe OVA descriptors and sum only preceding saved
field sizes, including array element counts.

The global `ARRGlobalIntFlag*` functions operate on an integer bitset. The BGE
Universe OVA binds `Flag_speciaux_sauve` to an OVA value at byte offset 4488 and
passes the bitset address at offset 4492 to the flag API. A flag index is a
zero-based bit index (`word = index / 32`, `bit = index % 32`). The archive
contains the schema and defaults for this field; the current bit values may be
overridden by the save payload. In particular, cinematic saved-flag branches
cannot be settled from the BF alone.

Iris's Windows save routines use `sally.idx` and `slot%d.sav`, but the inspected
source does not establish the user's actual profile directory. Do not search
guessed locations. The Iris PC reader consumes the slot file sequentially: the
AI save buffer is the first block, followed by an 812-byte photo descriptor
and an optional photo payload. This makes the AI buffer start offset 0 in the
Iris path; its expected length comes from the opened BF's Universe OVA. Jaded
also writes an `SFDYN` block in its save path; this
was not found in Iris's retail save path and must not be assumed for BGE PC.
Jaded's `Spécial flags.csv` is optional editor metadata that labels numeric
flags, not evidence that the labels are embedded in the BF or save.

## JadeView boundary

The cinematic callback inspector can now load a `.sav` selected explicitly by
the user. It follows the opened BF's global AI startup chain, locates the unique
Universe `Flag_speciaux_sauve` descriptor marked `Save`, computes the packed
save layout from the ordered descriptors, and reads the initial AI block from
offset zero. The decoded bits seed the existing cinematic branch and
interruption-gate evaluators; explicit scenario overrides take precedence.
Changing the opened BF clears the loaded snapshot. JadeView does not search
guessed profile paths or modify the selected save. A bitset is accepted only
when its saved one-dimensional array bound matches the BF schema.

This is a bounded decoder, not a robust save/archive compatibility fingerprint:
Iris's slot format has no verified BF-version identifier, and a matching file
is inferred from its length being at least the current archive's expected AI
block size. A sufficiently large save from another game/archive version could
therefore pass that check. No actual retail slot has yet been compared against
a known in-game flag, so decoded state must be treated as an explicit user input
whose compatibility they verify. Unknown flags still remain unknown when no
slot is selected; timeline writes and scenario overrides retain their existing
semantics. The generated archive audit reports the expected ordered layout but
does not itself validate slot contents.
## Source references

- Iris OVA descriptor/default parsing and model/instance overlay:
  [`ai_common.c`](../external/irisEng/ai_common.c#L1124),
  [`ai_common.c`](../external/irisEng/ai_common.c#L1218),
  [`ai_common.c`](../external/irisEng/ai_common.c#L572),
  [`ai_common.c`](../external/irisEng/ai_common.c#L647).
- Save-buffer sizing and Universe save/load packing:
  [`ai_common.c`](../external/irisEng/ai_common.c#L1411),
  [`ai_inofunctions.c`](../external/irisEng/ai/ai_inofunctions.c#L621),
  [`ai_inofunctions.c`](../external/irisEng/ai/ai_inofunctions.c#L705),
  [`inputoutput.c`](../external/irisEng/inputoutput.c#L617).
- Sequential slot-file block reads:
  [`inputoutput.c`](../external/irisEng/inputoutput.c#L613),
  [`wininputoutput.c`](../external/irisEng/win/wininputoutput.c#L1487).
- Universe flag bitset binding and functions:
  [`ai_compiledscripts_global.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_global.c#L284),
  [`ai_arrfunctions.c`](../external/irisEng/ai/ai_arrfunctions.c#L576),
  [`AIfunctions_arr.c`](../external/jaded/src/libraries/AIinterp/Sources/Functions/AIfunctions_arr.c#L765).
- JadeView's bounded packed-layout and selected-slot readers:
  [`PcAiGlobalSaveLayoutReader.cs`](../src/Features/GameScript/PcAiGlobalSaveLayoutReader.cs),
  [`PcAiGlobalSavedFlagSnapshotReader.cs`](../src/Features/GameScript/PcAiGlobalSavedFlagSnapshotReader.cs),
  and [`EditorShell.Cinematics.ScriptCallbacks.cs`](../src/Features/SceneEditor/Cinematics/EditorShell.Cinematics.ScriptCallbacks.cs).
