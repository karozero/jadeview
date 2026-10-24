# Script owner resource fixtures

Resolved OVA resources additionally expose a bounded storage summary from the existing variable readers: descriptor names/types/offsets, packed array count/dimensions, value previews and available integer arrays. Raw storage words are explicitly untyped little-endian words, with any trailing bytes preserved as hex. Unsupported types retain the existing unavailable-value preview; malformed layouts carry an `InvalidLayout` summary diagnostic. Other resource kinds have no OVA summary. This does not initialize native variable state or apply instance overlays.

Export a bounded resource fixture from a decoded BF container:

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --script-owner-resource-fixture "E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf" FF001FBE 10001E0C
```

Output defaults to `artifacts/export-verification/FF001FBE-10001E0C-script-owner-fixture/`. Set `JADEVIEW_EXPORT_VERIFICATION_OUTPUT` to select another output root. The ignored directory contains `manifest.json` and raw binary resource slices.

The fixture follows the parsed GAO brain-key field to AIC (`q`), reads its AIM (`s`) and instance OVA (`t`) keys, then preserves every ordered AIM key/tag entry, including repeated model OVA and OFC (`r`) references. Model references resolve in the model's load-world scope; instance variables resolve separately in the owner's world. The manifest records preferred and actual world keys, resource kind, decoded-container offset/size, SHA256, ordered links and expected five callback keys. Missing, ambiguous, wrong-kind and unbounded resources are explicit failures rather than guessed slices. Only this container's decoded records are available; shared resources outside it can remain unresolved.

This is passive evidence for investigating the native loader. It does not execute callbacks, register compiled functions, construct native pointers, prove that instance variables are usable, or reproduce world scheduling. A resolved empty instance OVA is distinct from a missing resource.

Verified fixtures: `FF001FBE / 10001E0C` and `FF006BFC / 8F0027AF` each resolve six resources and callback slot 2 to `9E00152E`. Their instance OVA payloads are 262 and 32 bytes respectively. The latter has no instance-variable fields; resource resolution alone does not establish an input context for the callback.
