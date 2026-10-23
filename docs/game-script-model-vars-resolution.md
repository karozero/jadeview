# AI model variable-resource reference resolution

## What the loaders do

An AI model key list is a sequence of eight-byte `(resource key, extension)` entries. Iris scans that list twice: first to count `.ofc` functions, then to queue every nonzero `.ova` reference to the single destination `aiModel->vars`, while `.ofc` references are queued to distinct `funcs[]` slots (`external/irisEng/ai_common.c:1325-1384`). Jaded follows the same shape: it counts `.ofc` entries, then queues each `.ova` into `pst_Model->pst_VarDes` and each `.ofc` into a separate function slot in key-list order (`external/jaded/src/libraries/AIinterp/Sources/AIload.c:1860-1939`). Neither model loader keeps a separate destination slot or a merge operation for each `.ova` key.

`LOA_MakeFileRef` appends one record for every valid request. Iris writes key, destination pointer, callback, and flags at the current `LastRef`, then advances it (`external/irisEng/loading.c:257-277`). Jaded does the same with its `LOA_gast_Stack` (`external/jaded/src/libraries/SDK/Sources/BIGfiles/LOAding/LOAdefs.c:482-562`). There is no enqueue-time deduplication by key or destination address in these implementations.

The resolver consumes the queue from `CurRef` toward `LastRef`, including references appended by callbacks before the queue drains. Iris iterates in this order and invokes the resource callback only on a cache miss, then writes the resolved address to the queued destination (`external/irisEng/loading.c:336-375`). Jaded likewise resolves the current entry, invokes its callback on a cache miss, stores the result in the address cache unless the `NotSavePtr` flag is set, writes the result through `pul_Res`, and advances the queue (`external/jaded/src/libraries/SDK/Sources/BIGfiles/LOAding/LOAdefs.c:1065-1075, 1159-1212, 1255-1277, 1329-1368`).

## Consequence when a model lists multiple `.ova` entries

For a model that actually contains multiple nonzero `.ova` entries, each request targets the same `vars` pointer and is resolved in the order listed. Therefore each successful assignment replaces the previous pointer; the last successfully resolved `.ova` entry in key-list order is the final `vars` pointer after the queue drains. If two entries resolve to the same cached resource address, both assignments produce that same pointer. This is replacement, not variable-data overlay or field-wise merge.

This conclusion follows from the loader mechanics; it does **not** establish that a particular retail BGE model has multiple `.ova` entries or relies on this behavior. The current source pass has not established a retail archive example with repeated `.ova` model slots, and Iris is a reconstructed engine implementation rather than proof of every PC 2003 binary behavior. Do not collapse duplicate references during static inventory: preserve key-list order, destination identity, each key, and whether the resolver would reuse a cached address.

## Implication for JadeView

The passive model parser should represent model `.ova` entries as an ordered list, not a single candidate and not a set. A future effective-variable resolver can select the last entry only when the serialized model list is complete and its resolution order/cache identity is known; absent that proof it should expose ordered candidates and mark the effective pointer unresolved. This avoids the unsafe “first nonzero OVA wins” behavior while retaining loader behavior for an exact, complete model key list.

This resolves only AI model-level `.ova` destinations. Instance-level `.ova` references are separate: Iris queues one reference to `aiPtr->varsInstance` in `AI_ul_CallbackLoadInstance` (`ai_common.c:1387-1408`); Jaded queues its instance variable descriptor from a different destination (`AIload.c:2011-2039`). Their subsequent variable matching/instance initialization has separate semantics and is outside this note.
