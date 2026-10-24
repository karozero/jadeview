# AI model variable-resource reference resolution

## What the loaders do

An AI model key list is a sequence of eight-byte `(resource key, extension)` entries. Iris scans that list twice: first to count `.ofc` functions, then to queue every nonzero `.ova` reference to the single destination `aiModel->vars`, while `.ofc` references are queued to distinct `funcs[]` slots (`external/irisEng/ai_common.c:1325-1384`). Jaded follows the same shape: it counts `.ofc` entries, then queues each `.ova` into `pst_Model->pst_VarDes` and each `.ofc` into a separate function slot in key-list order (`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1860-1939`). Neither model loader keeps a separate destination slot or a merge operation for each `.ova` key.

`LOA_MakeFileRef` appends one record for every valid request. Iris writes key, destination pointer, callback, and flags at the current `LastRef`, then advances it (`external/irisEng/loading.c:257-277`). Jaded does the same with its `LOA_gast_Stack` (`external/jaded/src/libraries/SDK/Sources/BIGfiles/LOAding/LOAdefs.c:482-562`). There is no enqueue-time deduplication by key or destination address in these implementations.

The resolver consumes the queue from `CurRef` toward `LastRef`, including references appended by callbacks before the queue drains. Iris iterates in this order and invokes the resource callback only on a cache miss, then writes the resolved address to the queued destination (`external/irisEng/loading.c:336-375`). Jaded likewise resolves the current entry, invokes its callback on a cache miss, stores the result in the address cache unless the `NotSavePtr` flag is set, writes the result through `pul_Res`, and advances the queue (`external/jaded/src/libraries/SDK/Sources/BIGfiles/LOAding/LOAdefs.c:1065-1075, 1159-1212, 1255-1277, 1329-1368`).

## Consequence when a model lists multiple `.ova` entries

For a model that actually contains multiple nonzero `.ova` entries, each request targets the same `vars` pointer and is resolved in the order listed. Therefore each successful assignment replaces the previous pointer; the last successfully resolved `.ova` entry in key-list order is the final `vars` pointer after the queue drains. If two entries resolve to the same cached resource address, both assignments produce that same pointer. This is replacement, not variable-data overlay or field-wise merge.

This conclusion follows from the loader mechanics; it does **not** establish that a particular retail BGE model has multiple `.ova` entries or relies on this behavior. The current source pass has not established a retail archive example with repeated `.ova` model slots, and Iris is a reconstructed engine implementation rather than proof of every PC 2003 binary behavior. Do not collapse duplicate references during static inventory: preserve key-list order, destination identity, each key, and whether the resolver would reuse a cached address.

## Implication for JadeView

The passive model parser should represent model `.ova` entries as an ordered list, not a single candidate and not a set. JadeView's callback-slot resolver now follows this ordered replacement for the model reference list preserved by the PC loader: every candidate must resolve unambiguously in model-world scope, have a bounded payload and parse successfully before the final table can be returned. It retains repeated keys, so A/B/A selects A, and a final empty table replaces an earlier nonempty table. Missing, ambiguous or malformed inputs remain unresolved; no native pointer cache or callback execution is claimed. This avoids the unsafe “first nonzero OVA wins” behavior while retaining loader behavior for an exact, complete model key list.

Callback slots, variable snapshots, object-reference lists and dialogue tables now share the same effective model-resource resolver. Earlier model fields cannot survive replacement by a later resource. A complete five-slot callback tail is required for model storage; instance storage is validated separately and retains owner-world scope.

This resolves only AI model-level `.ova` destinations. Instance-level `.ova` references are separate: Iris queues one reference to `aiPtr->varsInstance` in `AI_ul_CallbackLoadInstance` (`ai_common.c:1387-1408`); Jaded queues its instance variable descriptor from a different destination (`AIload.c:2011-2039`). Their subsequent variable matching/instance initialization has separate semantics and is outside this note.

## Retail reference census

The read-only `--script-model-variable-audit <archive> <container>` verifier
preserves the ordered OVA references of every decoded model, including repeated
keys. It found 73 models in `FF005833`, 80 in `FF005520`, and 103 in `FF0045A2`;
none of those models listed multiple OVA references. Counts are per container,
not a deduplicated global inventory. Shared/fix resources may be outside the
decoded container records. These fixtures therefore validate the ordinary
single-model-resource case, but do not prove that ordered replacement causes
any of the reported actor-pose defects. Synthetic ordered-reference fixtures
cover replacement and absence of stale fields separately.

A before/after comparison also checked callback keys and model/instance variable, object-reference and dialogue-line counts for 159 owners in FF005833, 204 in FF005520 and 248 in FF0045A2. All owner records matched. This establishes metadata stability on those fixtures, not runtime execution or a fix for the reported poses.

## OFC inventory is not the native function array

Iris `AI_ul_CallbackLoadModel` first counts every `.ofc` tag to allocate its
function array. In its second pass, a zero key skips the entire entry; a nonzero
`.ofc` key consumes the next function destination (`ai_common.c:1345-1381`).
`LOA_MakeFileRef` treats `FFFFFFFF` specially: it clears the destination
immediately and does not enqueue a request (`loading.c:257-277`). Therefore a
zero-key tag still contributes allocated capacity without advancing the filled
index, while an `FFFFFFFF` tag advances that index and clears its destination.
Neither case supplies a callable function. Allocated count and successfully
resolved function count must remain separate concepts.

The immediate-clear behavior also matters for model OVA references. A sentinel
clears the shared destination before queued references drain; an earlier valid
queued OVA may subsequently write its resolved pointer there. Do not model a
sentinel as a final delayed assignment that necessarily overrides earlier queued
resources.

JadeView's `Record.References` represents enqueued valid-key requests and excludes
zero/`FFFFFFFF` keys. `FunctionResources` additionally deduplicates by key and sorts
its diagnostic inventory. It is useful for payload inspection, but cannot be
used as a native `funcs[]` index or as evidence of runtime assignment order.
The raw owner fixture preserves all AIM key/tag entries separately, including
null entries and duplicates. Reconstruct array destinations from that serialized
list and the loader rules, not from the deduplicated inspector inventory.
These are source-derived edge cases; the current retail fixtures do not exercise
a zero/sentinel OFC entry and do not establish retail runtime parity for it.
