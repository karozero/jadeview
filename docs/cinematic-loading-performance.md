# Cinematic loading and playback regression

Measured on the local PC against `sally_clean.bf`, container `FF0045A2`,
cinematic `3500B8EA` (2026-10-25).

## Causes and corrections

- Scene selection prepared every cinematic player; the browser now lists source
  clip metadata and prepares the selected player in a background job.
- Callback predicates rebuilt static function/message/OVA reachability during
  frame evaluation. Weak owner-scoped caches now retain source analysis and
  invalidate when callback/model function configuration changes.
- Unconditional branches reconstructed fields and message handlers unnecessarily.
  Empty conditions bypass field reconstruction, and owners without queued
  messages bypass message-reader analysis.
- OVA initialization repeatedly searched the complete variable list per field.
  Linear model/instance reduction preserves last-instance precedence.
- Callback effects repeatedly reconstructed the same owner's message-derived
  fields. Evaluation-local snapshots now include owner, sample time and
  capabilities; runtime snapshots are cleared for each evaluation.
- Material callback windows are cached against saved-flag assumptions. Unknown
  flags remain unknown; changing flags invalidates the corresponding cache.
- Cross-container resource fallback rebuilt cinematic/script data merely to
  inspect resource references. The resource-reference mode still reconstructs
  actor components and default shapes, while omitting cinematic analysis.
  Skipping actors entirely is invalid: their component references are required.
- Old clip cleanup must happen before activating the replacement: clearing the
  old player afterward can clear state shared by the new player.

## Spot measurements

Before these corrections, activation took about 30.4 s and initial playback
updates about 134–145 ms. The final measured activation took about 2.1 s;
initial updates took about 5.5–6.4 ms, with sampled later updates about 6.5 ms
at 29.39 s, 9.1 ms at 58.78 s and 16.2 ms at 88.16 s.

Resource fallback preparation decreased from about 14.2 s to 6.3 s on a cold
archive instance. Resource-reference parity passed for 1246 unique
GAO/geometry/material tuples. First-load archive hashing is included in the
document-loading stage and is cached by the archive instance.

The separate warmed playback audit measured 600 updates averaging 2.99 ms
(p95 3.91 ms). Its 120-frame rendering sample at the clip start, 1920×1080,
averaged 3.86 ms (p95 4.57 ms, peak 13 visible objects). These are separate
measurements, not an end-to-end editor frame rate or a visual-parity claim.
The loading audit does not include the editor UI, GPU rendering or audio.

A second loading audit on `FF005833` / `BF00218E` passed resource-reference
parity for 581 tuples and sampled later playback updates at approximately
6.3–18.0 ms. The source-analysis cache is not tied to a character or cinematic.

## Reproduce

Use the `ExportVerification` command
`--cinematic-loading-performance-audit <archive> FF0045A2 3500B8EA`.
It prints stage timings, checks resource-reference parity, and samples startup
and later playheads. The separate `--cinematic-playback-performance-audit`
command measures rendering as well. Local traces are in
`artifacts/cinematic-loading-performance.log`.
