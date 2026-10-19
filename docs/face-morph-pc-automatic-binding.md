# Automatic PC mouth-controller binding

The sequence and transition rules are recovered from game code, not randomized
morph playback. Manual triggering, chosen speed/strength and a fresh simulated
initial state remain JadeView preview inputs. Original dialogue scheduling and
lip-sync are not reproduced; see [the mouth-cycle explanation](face-morph-analysis.md#what-the-experimental-mouth-cycle-reproduces).

Mouth-cycle availability now follows actor source data instead of the geometry
profiles used for earlier blink/expression presets. It does not inspect actor
names or require known geometry keys.

`PcMorphControllerDiscovery` follows the root's brain to its script model and
checks for a supported initializer/update code-key pair:

- `7100AF12 / 7100B928`: recovered factor-aware controller and 32-form sequence.
- `35001FF5 / 9E002C81`: recovered shared-head controller. The sequence branch
  follows the effective source scalar `des_i_friend == 1`, read from model OVA
  defaults with the first case-insensitive instance-name override. Its leader
  gate is qualified only for self reference 1 or an available GAO source key.

These code keys identify recovered engine algorithms, not individual characters.
The native sequences remain explicit constants tied to those algorithms. The
application does not analyze arbitrary machine code or execute the game's VM.
Model membership establishes a capability for manual simulation; it does not
prove when the original scheduler invokes that code in gameplay.

The bounded OVA scalar reader uses descriptor storage offsets, scalar types and
packed length. Missing, truncated or incompatible required values reject the
binding. Unknown or ambiguous families do likewise. No saved-state or runtime
pointer resolution is performed.

At preview creation/lookup, selector 0 must resolve the selected component.
The head must contain channel 4 with at least two references, every recovered
form must fit its morph groups, and the sequence must contain actual nonzero
deformation. The cycle clones this appearance's own channel references. Appearance
changes recreate/requalify previews; changing the bound controller stops an
existing transient cycle. No name or geometry-key fallback selects a sequence.

Real BF checks cover Sally and the shared-head branches on militaire (first
form 6) and Pey'j (first form 4), including sampled GLB clips. A fixture with
unrelated names and geometry key still binds the same supported controller;
removing that controller removes the capability. Scalar tests cover reversed
storage order, first-match overrides, incompatible types/arrays and truncation.

The manual Play/Stop workflow and optional GLB simulation export are unchanged.
Unknown controllers, missing resources, empty targets and incompatible head
variants can legitimately lack the control. This is automatic coverage of the
two recovered families, not a claim of universal facial or dialogue support.

Native update `004F7D30` explicitly resolves module selector 0 and writes slot-4
factor/progression to morph channel 4. Local follow-up evidence:
`artifacts/export-verification/native/pc-shared-head-automatic-update.json` and
`pc-shared-head-automatic-update-assembly.json`. Earlier initialization and OVA
evidence is linked from [controller variants](face-morph-pc-controller-variants.md)
and [variable defaults](face-morph-pc-variable-defaults.md).
