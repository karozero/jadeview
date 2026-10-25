# Gameplay zone-trigger census

## What the callback does

Retail-side Iris source contains a reusable `Detect_o_Zone` brain, not a
scene-specific rule. Its init callback clears capability bit `0x100`, resolves
`des_acteur_detecte1`, and selects one of two repeating checks when the target
is non-null: bounding-volume overlap when GAO identity bit `0x400` is clear,
or ZDE-pivot collision at index 2 when it is set. See
[`ai_compiledscripts_detectors.c`](../external/irisEng/ai/ai_bge/ai_compiledscripts_detectors.c#L1166).

Both check callbacks are edge-triggered by their instance-local `i_IN` latch.
On entry they set the latch and capability `0x100`, then apply configured
saved-flag operations: `des_i_SPFlag_set01` sets, `des_i_SPFlag_reset01`
clears, and `des_i_SPFlag_invert01` also **sets** its raw index on entry. On
exit they clear the capability. With a zero timer, only the invert flag is
cleared on exit; set/reset changes persist. A nonzero timer starts on entry;
when elapsed, set is cleared, reset is set, and invert toggles the index modulo
10000. The BV and ZDE branches implement the same state transitions. Exact
source: `Detect_o_Zone_doz_detec_o_BV` and
`Detect_o_Zone_doz_detec_o_ZDE` in the same file (lines 1204–1365).

Callback polling is not equivalent to a physics collision-list subscription:
the zone explicitly calls Iris's `COLBVObjectObject` or `COLZDEPivotCollide`
predicate each time the callback runs. `AITrackCurChange` installs the
repeating callback in the current track (`external/irisEng/ai/ai_aifunctions.c`,
line 214); `AI_RunInstance` advances the configured AI tracks
(`external/irisEng/ai_common.c`, line 711 onward). Whether this owner is
currently scheduled or active remains runtime state.

## Retail archive census

Run the archive-wide census with:

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --archive-zone-trigger-census "E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf"
```

The report is written to the ignored artifact
`artifacts/export-verification/archive-zone-trigger-census.json`. The audit
scans all `FF...` world-list containers, identifies owners by their configured
callback, resolves the owner's script-model `.ova` references by typed resource
provenance, and reports owner/world links, effective OVA values, actor
resolution, collision references, and incoming `SceneEvents` EVE targets. It
keeps multiple OVA candidates explicit instead of selecting one by dictionary
iteration order.

On the installed retail PC `sally_clean.bf` archive, the census found 24 zone
owners using one script model (`35008316`) across 91 scanned containers; all 24
have one unique typed `.ova` binding, with no ambiguous or unresolved binding.
All 24 use the BV route (identity bit `0x400` clear). Configured actor
references resolve to main-actor slot 1 for 12 owners, or to a unique actor in
a loaded world for the other 12. Twenty-three owners configure at least one
positive saved-flag index; none configure a nonzero timer. One owner has a
`SceneEvents` link (none has an `Events` link), and one owner is targeted by a
decoded `SceneEvents` track.

That linked example is `CINE_Ouverture_Porte_2_3.gao` (`10000473`) in
`01_04_entrepot_liberation_mili_cine` (`3500B8D5`, container `FF001FBE`). Its
effective OVA sets invert index 1007 and its `SceneEvents` points to
`10000474`. The EVE list `35009F74` owned by
`CINE_PIEGE_PORTE_LOBO_EVENTS.gao` (`35009F6F`) has track 10 targeting the zone
owner. This proves a serialized target/link relationship; it does not prove
that the event track or zone callback ran in a particular session. The zone
owner is not present in the loaded render-object list, and the census found no
typed collision instance/map reference for that owner. The source-selected BV
predicate may use bounds carried elsewhere in the GAO/runtime object; the
census does not claim to reconstruct that overlap volume.

## Evidence boundary

| Recoverable from the retail BF and current decoder | Requires runtime state or further engine work |
| --- | --- |
| Configured callback/model, unique typed `.ova` provenance, effective actor/flag/timer values, identity-bit route, serialized `Events` and `SceneEvents` links, decoded EVE target keys and messages | Current `AI_MainActor` slot contents, callback track scheduling/dispatch, `i_IN`, timer start/value, current saved flags, current owner capabilities, collision result/active detection data, EOT activation, message delivery, and the effects of branches at a specific frame |
| A direct EVE track targets a zone owner in the archive | Whether the target track causes the actor/effect to appear in-game, and when |

The initialized callback explicitly checks whether the actor resolves before
installing the collision callback. Main actor references `1` and `2` are slot
indices, not static GAO keys; the BF alone does not identify the live actor
currently occupying those slots. Iris's collision wrappers return false for a
null actor. For the BV variant this is explicit in
`external/irisEng/ai/ai_colfunctions.c:144`; ZDE delegates to the ZDE-pivot
collision routine at line 1468.

Jaded provides API-level corroboration, not retail BGE proof: its
`AIfunctions_col.c:662` implements BV overlap and `:2445` delegates ZDE pivot
collision, while `AIfunctions_obj.c:2918-2928` applies capability remove/add
masks. The retail callback behavior and archive census above are based on
IrisEngine plus the installed PC BF, not on assuming Jaded's pre-release build
is identical.

## JadeView coverage and remaining gap

`CinematicScriptBehaviorAnalyzer.AddZoneDetectorBehavior` already interprets
this model generically from callback, effective OVA values, object references,
and serialized identity flags. The archive command adds a reproducible
cross-world census and explicit EVE-owner joins. JadeView still does not run
the game's collision predicates, schedule AI callbacks, evolve saved flags or
capabilities over time, or activate event tracks from runtime gates. Those are
the key missing systems for making conditional actors and effects appear as
they do in-game; scene-specific overrides would conceal rather than solve that
gap.
