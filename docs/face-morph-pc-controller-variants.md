# PC controller variants

Follow-up to [cinematic messages](face-morph-pc-cinematic-events.md).

The shared-head controller and Sally's controller do not have identical
completion rules. A source message or matching variable name is insufficient
to select the complete playback algorithm.

- Shared-head `004F7D30`: progression at least 1.99 marks the transition settled.
  The current factor is retained; it is not snapped to the target.
- `005ADBF0`: progression must equal 2 **and** the absolute current/target factor
  difference, stored as float, must be strictly less than float 0.05. It then
  marks the slot settled and copies the target factor into the current factor.
  The mouth path uses this rule too (`005AFA3D..005AFB09`).

Constant `00B73300` in the historical-capture-derived carrier is float
`0.05000000074505806`; `00B7338C` is float 2. The progression/factor lerp still
uses the previously qualified update arithmetic. A slot at progression 2 can
continue blending for multiple updates before becoming settled. This also
delays automatic mouth cycling: its gate checks settled state before the
current update's transition computation.

`PcMorphTransitionCompletion` exposes these two rules explicitly. Callers must
select one from qualified controller code. The default preserves the earlier
shared-head evaluator. Neither the evaluator nor its mouth-cycle wrapper checks
character names. Unknown modes are rejected. Tests cover incomplete progression,
factor convergence, strict tolerance, exact factor snap and cycle gating.

The Sally source model has seven-element controller arrays at offsets
`6B8/6D8/6F8/718/738/758`; the shared-head arrays have five elements at
`B4C/B64/B7C/B94/BAC/BC4`. These array lengths are not mesh channel counts.
Sally's `o_module_tete` reference at `7F8` and mouth table at `62C` are zero in
the checked pre-execution defaults. The native code later uses that head-module
reference to write channels. Their static initialization is recovered below;
runtime invocation order and a concrete module-table instance remain separate.
Do not select the shared-head friend-based sequence just because a cinematic
message targets Sally.

Local evidence: `artifacts/export-verification/native/pc-factor-aware-controller.json`
and `pc-factor-aware-controller-assembly.json`, plus `sally-variable-defaults.json`.
Assembly evidence:
`ev_559f61182e5316b3b5bc1f28e8cb7900ebd154c0ce4c08176cfd601af2d0839d`.
Carrier SHA-256:
`295f66ef3a1486fe84d46db745788506671958227bb7ff2c0283f5a89d0c8be0`.
This is static analysis of a historical capture, not an observed live facial run.

## Recovered initialization

Actor-closure code key `7100AF12` has a candidate native table binding to
`009763A0`. Inspection of that actual function and its assembly confirms writes
to all 32 mouth-list elements at controller `630..6AC`, and a loop setting all
seven `f_expr_flag` elements (`758`) to float 2. These writes precede a later
return-code gate in the function; invoking the function itself still requires
the actor scheduler. The recovered sequence is retained as research data in
[pc-controller-7100AF12.json](data/pc-controller-7100AF12.json), keyed by source
code keys rather than a display name. It uses form IDs 31, 50, 51, 52 and 54.

Code key `26003A64` binds to `004F0D00`. Its first lookup calls `00AE9360` with
selector zero and writes the returned reference to `o_module_tete` at `7F8`.
The wrapper passes the current actor and low-byte selector to `00B59CC0`.
That helper scans actor components, rejecting invalid entries. Without a mapping
table it compares the component ordinal; with one it compares its mapped byte.
It returns the first matching component or zero. Selector zero therefore does
not universally mean the first mesh or an object named "head".

The initialization fixture now feeds this recovered sequence into the isolated
cycle and evaluates its channel weights. Under explicitly assumed zero initial
factor/index, references `[0,0]`, enable-command effects `-580 / speed 11`, and
60-Hz updates, it selects three forms in 26 updates. The shared-head completion
rule would select five. This is a regression fixture for the algorithm variant,
not evidence that those assumed references or initial conditions match a live
Sally instance. Output is `artifacts/export-verification/pc-source-mouth-cycle-fixture.json`.

Additional native evidence: `pc-controller-source-initialization-assembly.json`,
`pc-head-module-binding-assembly.json`, `pc-head-module-selector-assembly.json`,
and `pc-head-module-search.json`. Initializer assembly:
`ev_cefda431b11ac05d141208df3924a3362d75d0e5b699fff85eba9ca8ee40c55e`;
binding assembly:
`ev_9795af22da5f121f227fe9d2586e85e26b1ba188da215960874aebfd63d803ea`;
module search:
`ev_4344ef5c130ab738b95d03e315e957a484608e34abeda938f9ab407854b0a44e`.

Next: recover the source module mapping for the inspected actor and verify its
head reference/channel layout. Do not attach a guessed mesh using its name.

Follow-up: [source module mapping and head selection](face-morph-pc-module-binding.md)
now resolves that head without a name-based search.

## Cinematic automatic lip-sync variants

The Iris source `external/irisEng/ai/ai_bge/ai_compiledscripts_lipsync_auto.c`
defines seven explicit `des_i_type_acteur` branches in
`Cine_lipsynch_auto_Lips_init`. JadeView now records each branch's 32-entry
mouth-form sequence and `i_canal_tete_ouverture_bouche` channel, instead of
accepting only type 2. Types 4 and 5 also assign `i_shape_canal_tete` to module
selectors 3 and 4; the other branches retain that selector from their serialized
instance data. The per-channel target factor, speed, cycle index, and auto flag
are read from that instance, then the selected sequence is matched against
loaded morph data before binding. Unknown types or mismatched meshes remain
unbound rather than falling back to a character-name guess.

`CinematicMouthVariantChecks` compares all seven exact channel/selector/sequence
tuples against the decompiled Iris initializer. This establishes source-configured
controller variants, not that every actor instance receives this callback or that
the callback was active at a particular time. Message 53 still supplies speech
timing, while the sequence itself is a fixed 32-form source cycle; the archive
does not provide phoneme-level audio analysis.

Archive verification on `sally_clean.bf` / `FF0045A2` found a concrete type-3
controller on `Cine_PNJ_Journaleux.gao`; it now binds to
`b_journaleux_tete.gao` (11 groups, 3 channels) using module selector 0 and
channel 2. The same archive still binds type 2 on `Cine_PNJ_Secundo.gao` to
`b_Crumble_TeteBD.gao` (13 groups, 5 channels), channel 3. The run also exported
the synchronized cinematic GLB and passed visibility sampling for the selected
clip. Report: `artifacts/export-verification/lipsync-variant-audit/FF0045A2-cinematic-playback.json`.
