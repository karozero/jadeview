# PC module binding

The source appearance record, not the sound bank `.snk`, provides the component
selector map used by the recovered head lookup. The sound-bank lead was rejected
after checking native readers `0043E060/0043DBC0`.

`00B5BA30` allocates a 0x84-byte table filled with FF. It reads a byte count
(native cap 64) and triples `(signed component index, module selector, appearance)`.
For each row it writes the selector at `table + 4 + component` and appearance at
`table + 0x44 + component`. Repeated component rows overwrite earlier values.
`00B5AB00` changes the active table, inheriting the previous selector wherever a
new selector is FF. `00B59CC0` searches component order and returns the first
valid component whose selector matches. Without a table it uses ordinal lookup.

Original BF, hangar actor `71003F5A`, source shape `A9001F3E`: the fixed and
scene-local shape occurrences have identical bytes. There are 47 components.
Row `(20, 0, 1)` binds selector 0 to component `CE00A4B0`, with source geometries
`620030FB` and `6200318D`. The name is not used to establish that binding.
The game export regression now selects the head through this module lookup and
checks the resulting 55-group, six-channel morph layout.

`ActorModuleMap` reads the qualified table and preserves selector inheritance.
`SceneActor.FindModule` exposes lookup for parsed components. Invalid source maps
return no binding rather than falling back to an invented ordinal. Native invalid
memory/object checks are represented only by null parsed components; this is
source lookup, not a reconstruction of native object lifetime or saved state.
The supported reader rejects counts above 64 or out-of-range component indices.

Diagnostic command:

```powershell
dotnet run --project tests/ExportVerification -- --actor-module-audit <original.bf> FF0057FD 71003F5A
```

Output `artifacts/export-verification/71003F5A-modules.json` retains raw shape
occurrences, component order, source geometry alternatives and selector-0 result.

Local native evidence: `pc-module-map-reader.json`,
`pc-module-map-shape-change.json`, `pc-module-map-appearance.json` and
`rejected-snk-module-map-lead.json`. Reader evidence:
`ev_fddf43a1ecaa22e2ffea07d21bfc6913b4185de38a797dac873ae6da6e798589`;
inheritance evidence:
`ev_fe21793d4bf88bb9649e9ed160c134e72929e6b4d373984953d523d8f3d33dfc`.
Carrier SHA-256:
`295f66ef3a1486fe84d46db745788506671958227bb7ff2c0283f5a89d0c8be0`.
These are static observations from the historical-capture-derived carrier.

## Experimental preview

The native update writes controller slot 4 to head morph channel 4:
`005AFB59..005AFB7D` sets its factor through `0046C380`, and
`005AFBD0..005AFBF4` sets progression through `0046C4F0`. Its pending-transition
path updates the same channel references. This allows a bounded manual preview.

The first **Experimental mouth cycle** implementation used a verified geometry
profile and channel-4 signature. [Automatic controller binding](face-morph-pc-automatic-binding.md)
now replaces that mouth-specific geometry restriction with supported source
initializer/update code keys, effective controller variables, module selector 0
and compatible morph data. Blink/expression presets retain their existing
independent profiles. Arbitrary script controllers are not reconstructed.

Play starts with index zero, current factor zero, settled progression and cloned
source channel references. Factor/speed are manual inputs (defaults .8/11).
The prior pose and channel controls are captured. Stop restores them, Reset
clears them, and manual/timeline/blink controls take over. Zero-time evaluation
does not advance the cycle; positive preview steps are capped at 0.1 seconds.
It does not replay cinematic scheduling, audio, save state or original live state.

Project capture preserves the pre-cycle pose/channel controls. By default GLB
exports the current evaluated pose, without recording a new facial animation. Tests on the
real head verify deformation, unrelated weights, source bytes, project baseline,
takeover/reset and pose/playback preservation through GLB export.

Native channel-write assembly is retained in
`pc-factor-aware-controller-assembly.json` (evidence
`ev_559f61182e5316b3b5bc1f28e8cb7900ebd154c0ce4c08176cfd601af2d0839d`).

## Optional sampled GLB clip

Export option **Active experimental mouth cycle** (off by default) samples an
active, source-qualified preview into a separate clip. It requires animation/
skinning export and a duration greater than zero and at most ten seconds.
Sampling starts from a copy of the current controller state/pose, with 60-Hz
steps and an exact final endpoint. Float-rounded grid endpoints are not repeated.
The GLB uses STEP weights to preserve the discrete simulated samples.

The clip's extras state `originalGameTimeline: false` and identify manual
triggering, recovered PC motion and simulated timing. Existing authored keys,
preview weights, hidden controller state and project baselines are unchanged.
The cycle remains separate from skeletal Actions; it is not an original
cinematic, phoneme sequence or synchronized speech track. If no eligible active
preview is exported, the result reports that no experimental clip was written.

Verification includes an actual Blender 5.2.2 background import of
`sally-experimental-mouth-cycle.glb`: a separate mouth NLA track was present,
the head retained all 55 targets, and evaluated weights differed across sampled
times. This checks import/evaluation, not a rendered visual comparison. Local
result: `artifacts/export-verification/blender-mouth-cycle-result.json`.
