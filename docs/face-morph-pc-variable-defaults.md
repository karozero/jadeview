# PC controller source defaults

Follow-up to [PC facial controllers](face-morph-pc-runtime.md), 2026-10-20.

## Qualified loading path

Native `00471560` reads OVA descriptors, names and a byte-sized variable-storage
block. `00470E30` first copies model defaults into controller storage. It then
iterates model descriptors, searches instance descriptors by case-insensitive
name, and copies matching values between their respective first-word offsets.
The first matching instance descriptor wins. Duplicate model names are retained;
the military model contains both `GFX_smoke` and `GFX_Smoke`.

This resolves an earlier uncertainty: serialized cursor order must not be used
to assign a variable's value. Descriptor order and storage offsets can differ.
The native loader's sequential reads consume the storage block; field access
and the name-based overlay use descriptor offsets into that block. Reference
fixup `00470D10` independently uses those offsets. Object-reference words still
require native resolution and must not be presented as resolved pointers.

## Observed initial facial state

Both checked brains use model `35001FF8`, with 389 variables in OVA `35001FF7`.
The raw leader reference is overridden to `1` for both; its runtime pointer is
not resolved by this audit.

- Military brain `7100596D`: instance OVA `7100596C` overrides `des_i_friend`
  to integer 1. Source initialization therefore selects the first recovered
  32-form list, subject to its nonzero-leader gate.
- Peyj brain `9E001C77`: instance OVA retains the model's `des_i_friend = 0`.
  The other recovered list applies under that same gate. The scene-wide audit
  of container `FF00791B` resolves the model that was missing from the earlier
  bounded actor closure.
- Both source initial states have `i_expr_bouche_auto = 0` and five zero
  elements in `f_expr_speed`. These are pre-execution defaults, not the speed
  during dialogue. The checked native head initializer has no direct reference
  to speed-array base `B94`; message 53 writes speed in the qualified handler.

The distinction between source defaults and later state remains essential:
initialization code fills the mouth lists, and scripts/messages can change the
mode, target factors, cursor and speed. Neither these defaults nor the lists
constitute an original speech timeline.

## Diagnostic tool and limits

```text
python tools/audit_pc_variable_defaults.py <actor-scripts.json> --output <report.json>
```

For a scene-wide report, also supply `--brain <hex-key>`. Conflicting resources
with the same key are rejected rather than silently selected. The tool follows
q model/instance keys and the model's `.ova` row. It preserves offsets, array
prefixes, raw values and model/instance provenance. Only equal-type/equal-packed-
length overrides are supported; native partial-array copies remain outside its
scope. It performs no pointer resolution, initialization, save restoration,
speech scheduling or BF writes.

Verified on all 389 military/Peyj and 450 Sally model descriptors. Four focused
tests cover reversed descriptor/storage order, first-match name overrides,
duplicate names, raw non-finite array bits, storage bounds and incompatible
layouts. All 11 current Python PC audit tests pass.

Local evidence under ignored `artifacts/export-verification/native` includes
`pc-variable-storage-loader.json`, `pc-variable-name-overlay.json`,
`pc-variable-reference-fixups.json`, and the three `*-variable-defaults.json`
reports. Carrier SHA-256 remains
`295f66ef3a1486fe84d46db745788506671958227bb7ff2c0283f5a89d0c8be0`.
Loader evidence:
`ev_601f3e0fb179526bb1b983f60070737bf58d2d5fe30f1a0222a1a7cd591d6ad0`;
overlay evidence:
`ev_58658496e8dfd449444e8f3575d79fc93727e30308006e770a4aee78d113b7d8`.
