# Saved-flag 1007 chain in 01_04 Entrepot

This is an archive/source reconstruction of one end-to-end gameplay state
chain. The archive establishes configured links and values; Iris source
establishes callback behavior. Neither proves a callback ran in a particular
play session.

## Reproduce the evidence

Run these from the JadeView source root (adjust the BF path if needed):

```powershell
$archive = "E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf"
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --archive-gameplay-script-index $archive external/irisEng
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --archive-zone-trigger-census $archive
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --world-script-graph-audit $archive FF001FBE 3500B8D5
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --world-script-graph-audit $archive FF001FBE E6001FA7
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --event-track-payload-audit $archive FF001FBE 35009F6F 35009F74 10
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --archive-world-transition-call-audit $archive
```

The first two commands produce
`artifacts/export-verification/archive-gameplay-script-index.json` and
`archive-zone-trigger-census.json`; the other commands produce the two
world-script reports and `FF001FBE-35009F74-35009F6F-EVE10-payloads.json`.
The transition audit writes `archive-world-transition-call-audit.json`.
The all-world index's `SourceFlagAccessOwners` is the archive-wide source
reachability join. Filtering `Accesses` and `EventAccesses` for flag 1007
returns exactly these three configured owners:

| World | Owner | Configured relation to 1007 |
| --- | --- | --- |
| `01_04_entrepot_liberation_mili_cine` (`3500B8D5`) | `CINE_Ouverture_Porte_2_3.gao` (`10000473`, model `35008316`) | Zone callback sets 1007 on entry and clears it on exit. |
| `01_04_entrepot_liberation_mili` (`E6001FA7`) | `OBJ_porte_prisomili01.gao` (`1600272A`, model `7100BF05`) | `porte_po_init` reads 1007; open and close callbacks set/clear it. |
| `01_04_entrepot_liberation_mili` (`E6001FA7`) | `WP_Kick_Porte.gao` (`10000D58`, model `9E000655`) | `action_reaction_ar_init` reads 1007; the configured kick-threshold action writes it. |

The zone census confirms the first owner has one unambiguous typed model `.ova`
binding, `des_i_SPFlag_invert01=1007`, actor reference `1` (main-actor slot
1), BV-overlap route, and no timer. The source for `Detect_o_Zone` interprets
the variable name `invert` asymmetrically: it sets the raw flag index on entry
and, with timer zero, clears it on exit. It is not a toggle on entry.

## Zone activation and the EVE track

The same zone owner links `SceneEvents` key `10000474`. In its linked EVE owner,
`CINE_PIEGE_PORTE_LOBO_EVENTS.gao` (`35009F6F`), `SceneEvents` track 10
(`35009F74`) targets `10000473`. Its decoded commands are:

| Time | Command | Meaning from the literal arguments |
| ---: | --- | --- |
| 0.00 s | `OBJ_FlagInactiveSet(1)` | Make the zone owner inactive. |
| 0.93 s | `OBJ_CapaSet(0, 256)` | Remove capability bit `0x100`. |
| 4.40 s | `OBJ_FlagInactiveSet(0)` | Make the zone owner active. |
| 19.80 s | `OBJ_CapaSet(0, 256)` and `OBJ_FlagInactiveSet(0)` | Remove bit `0x100` and leave the owner active. |

This is direct serialized EVE-to-owner targeting and decoded EVE data. It does
not prove when the list is dispatched, that the zone's init/check callback is
scheduled while inactive, or that the actor crossed its BV. There are no
decoded message commands on this EVE track; its observed operations are owner
activity and capability changes.

## Gameplay consumers

In Iris `ai/ai_bge/ai_compiledscripts_porte.c`, `porte_po_init` tests the
effective `SF_qui_me_commande` flag at line 150. A set bit selects
`porte_po_ouvre_toi` at line 156; otherwise it follows the configured
close-on-init or idle path. The open callback sets its configured flag at line
2091, and the close callback clears it at line 530. The broader callbacks also
control door animation tracks, collision-map activation, and configured
objects to activate/deactivate. For this retail owner, the archive index's
source-reachable flag join confirms that the effective OVA resolves this gate
to 1007. The exact per-door activation object values are not reported by the
current all-world index, so this note does not name a specific spawned or
hidden child object.

In `ai/ai_bge/ai_compiledscripts_action_reaction.c`,
`WP_Kick_Porte.gao`'s init reads its effective `des_SF_dois_je_m_activer`
value (lines 62–82). If that global flag is set, an identity-bit branch can
play the configured break tracks; otherwise the callback is replaced with the
interactive kick action. When its configured kick count reaches the
break-threshold, the action can set its configured completion flag at line
344, and it changes collision activation at line 338. The source/index join
confirms this owner's effective flag is also 1007. This is a second gameplay
consumer/writer of the same global flag, but the static data does not prove
that this owner is the door actor or that it ran before/after the zone.

## What is and is not a complete chain

The static chain is complete through configured data and source-reachable
behavior:

```text
SceneEvents EVE track 10 ──targets──> zone owner 10000473
                                         │
                       actor/BV callback entry sets SF 1007
                                         │
                                         ├──> door init reads SF 1007
                                         │      open/close callbacks persist it
                                         └──> kickable prop init reads SF 1007
                                                threshold action may set it
```

The zone's EVE record does **not** itself set 1007. A possible route is that
EVE activity allows the zone owner to participate, after which its collision
callback writes the flag, and a later-loaded gameplay door/prop reads it. This
sequence remains an inference: actual EVE dispatch time, zone callback
scheduling, actor slot 1 contents, collision, saved-flag value/order, and
world transition order are not encoded as an executed trace in the BF. Since
the same flag also has gameplay writers and the door itself sets/clears it, a
runtime state trace is needed to attribute a visible door state to this
cinematic trigger.

The archive also contains EVE and callback links for the cinematic owner, but
does not establish a trigger-to-world-transition edge. The next missing layer
is the runtime owner lifecycle: how `SceneEvents` playback activates the
owner, how the scene/gameplay AI tracks are scheduled across a world change,
and when saved flags are sampled or persisted. JadeView currently reports the
static chain; it does not execute those lifecycle transitions.

The all-container world-transition source audit lists reachable
`WORLoadList`, `WORUniverseAdd`, `WORActive`, `WORDesactive`, `WORPlayAbsTime`,
`WORCurIndexGet`, `WORPause`, and `WORUnPause` callsites. It scanned 91/91
containers (0 failures); none of the three flag-1007 owners has a matching
transition API callsite in this report. That negative result is scoped to
represented/reachable source callsites: it does not exclude opaque OFC bodies,
indirect engine dispatch, or a transition caused by another owner.
