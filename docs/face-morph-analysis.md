# Sally face morphs: current findings

Status: paused at the user's request. This is a read-only analysis of the PC
`sally_clean.bf`, JadeView's parser, and the available REBGE Anniversary
analysis. The original game was not launched and project source was not
changed during the analysis.

## What the PC archive proves

The archive is `sally_clean.bf` (SHA-256
`84040591DC453A64D4EBA33E4CD339D325C23F39F3067118D33DD628B22BD1C5`). The
Sally head GAOs `CE00A4B0` (464 vertices) and `A90056F0` (1242 vertices) each
contain a type-4 modifier with 55 primary deformation groups and six channel
records. The data is present in the original PC archive, even though JadeView
currently keeps modifiers as raw bytes and does not apply them in the
viewport.

For the PC type-4 payload, each primary group is stored as a count, that many
32-bit vertex indices, then one float32 XYZ displacement per index. Thus one
entry occupies 16 bytes after the count. The ordinary head has 886 sparse
entries across non-empty groups 1, 2, 5, 6, 11, 31, 34, 37, 50, 51, 52 and 53.
Every checked index is in range, unique within its group, and every delta is
finite. Most maximum delta lengths are around 0.045.

Groups 1 and 2 are bilateral, compact deformations around the eyelids. Their
vertex pairs mirror across X, and their indices correspond exactly to the
named Anniversary groups `Morph_TeteHD_BlinkHaut` (upper eyelid) and
`Morph_TeteHD_BlinkBAS` (lower eyelid). PC channel records 0 and 1 reference
groups `[1,1]` and `[2,2]`. This makes the PC data very likely to represent
upper/lower blink targets, although the PC executable's callback has not been
verified.

## Cross-check against named REBGE data

The ordinary-head Anniversary preview contains sparse targets whose vertex
index lists match the PC groups for groups 1, 2, 5, 11, 31, 34, 37, 50, 51, 52
and 53. The named groups identify likely expressions: large eyes, brow frown,
light/bad smile, and A/O/I/scream mouth shapes. Most corresponding delta
components differ by at most about `6.5e-8`, consistent with float rounding.
Group 37 matches bit-for-bit. Two scalar components in some groups differ by
up to about `0.00553`, so the Anniversary payload is not a byte-for-byte
replacement for the PC payload. PC group 6 has the same index list as
Anniversary target 8; its identity remains uncertain because the intervening
neutral slots do not line up cleanly. The Anniversary preview also has an
extra target 39 whose indices and deltas nearly duplicate group 37, while PC
group 39 is empty. Treat neutral labels and these slots as unresolved.

The HD comparison is weaker: group counts and index arrays diverge for most
targets (for example, PC group 1 has 154 entries while the HD preview target 1
has 1223). Only groups 6, 7, 8, 37 and 46 have matching index lists. Do not
transfer the ordinary-head mapping to the HD head without further evidence.

The Anniversary type-4 records include 64-byte source labels; the PC records
use a compact layout without those labels. Matching topology and names across
these releases is useful evidence for the ordinary head, but does not prove
identical PC runtime behavior.

## How the data is likely used

Each primary group is a sparse vertex displacement target. At a conceptual
level a target weight `w` contributes `w * delta` to each affected base vertex;
multiple active targets add their contributions. This agrees with the
Anniversary static analysis and standard morph-target representation.

The six PC channel rows contain an entry count, two float words, and an ordered
list of group references. PC channel 0/1 currently reference the upper/lower
eyelid targets and have weight `1.0`, progression `0.0` on the ordinary head.
Channel 2 references group 10; channels 3 and 4 reference group 0; channel 5
references group 29. Some referenced groups are empty in the PC primary table,
so these rows cannot be interpreted as a complete list of named expressions.
The rows may be runtime channel slots or interpolation/control data, rather
than six independent morphs.

The decoded channel headers are:

| Head / channel | Entry count | Weight | Progression | First group reference |
| --- | ---: | ---: | ---: | ---: |
| CE00A4B0 / 0 | 2 | 1.0 | 0.0 | 1 |
| CE00A4B0 / 1 | 2 | 1.0 | 0.0 | 2 |
| CE00A4B0 / 2 | 24 | 0.0 | 0.0 | 10 |
| CE00A4B0 / 3 | 26 | 1.0 | 0.0 | 0 |
| CE00A4B0 / 4 | 25 | 1.0 | 0.0 | 0 |
| CE00A4B0 / 5 | 5 | 0.0 | 0.0 | 29 |
| A90056F0 / 0 | 2 | 0.0 | 0.0 | 1 |
| A90056F0 / 1 | 2 | 0.0 | 0.0 | 2 |
| A90056F0 / 2 | 29 | 0.0 | 0.0 | 9 |
| A90056F0 / 3 | 28 | 0.0 | 0.0 | 0 |
| A90056F0 / 4 | 50 | 0.0 | 0.0 | 0 |
| A90056F0 / 5 | 6 | 0.0 | 0.0 | 29 |

Only the first reference is shown for long rows; the rest contain repeated
references or `FFFFFFFF` holes. Preserve their original order and length when
decoding or exporting them.

The REBGE Anniversary static audit reports interpolation across ordered sparse
groups and callback-written channel weights/progression. Its Sally blink
callback advances a phase by `deltaTime * 15`, caps at approximately pi, and
uses a sine-like envelope to update upper/lower eyelid weights. This is
evidence for the Anniversary executable only; it is not proof of the 2003 PC
callback constants, phase, or timing.

## Why PC playback is still unknown

The installed PC `BGE.exe` has a high-entropy `.text` section and enters
through a `.bind` loader. Ghidra's ordinary code at the callback addresses
listed in JadeView comments is not valid code for this executable. Therefore
those comments cannot establish the PC type-4 update routine. The available
static view reaches the loader but not the post-unpack morph callbacks.

JadeView currently has no type-4 decoder or morph renderer. It parses modifier
boundaries to preserve the resource structure, then stores the type and raw
payload in `SceneModifier`.

## Current conclusion and next evidence needed

It is now established that the PC archive contains sparse face morph targets,
including strong upper/lower eyelid candidates, and that the ordinary-head
topology closely corresponds to named Anniversary targets. The data layout
and likely additive weighted deformation model are understood well enough to
prototype an offline preview, provided the uncertain group slots and PC/HD
differences remain visible.

The original PC playback curve, exact interpretation of the six channel
records, and callback timing remain unproven. Resolving those requires a
recoverable unpacked PC image/PDB or an authorized runtime memory observation.
No such evidence was available in this pass.

## Evidence files

- JadeView PC parsing: `src/Features/AssetBrowser/Formats/Pc/PcTextureLoader.cs`
  (`ModifierSize`, type 4) and `src/Features/SceneEditor/Formats/Pc/PcSceneLoader.cs`.
- REBGE Anniversary layout: `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/facial-morph-native-layout-20261012.md`.
- REBGE Anniversary blink and visage callbacks:
  `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/facial-blink-clip-20261012.md`.
- Sally ordinary and HD preview targets:
  `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/preview/sally-neutral-blink-20261012.glb` and
  `C:/Users/Dan/Desktop/rebge/apps/asset_exporter/reports/preview/sally-hd-neutral-blink-20261012.glb`.

The latter reports and preview files are in the separate REBGE checkout and
are not committed with JadeView.
