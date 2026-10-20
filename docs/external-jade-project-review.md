# External Jade project review

Reviewed on 2026-10-20 from shallow local checkouts recorded in
[`external/README.md`](../external/README.md). This is comparative research;
it does not establish that another game's data or a different Jade build has
the same behavior as the installed PC release of Beyond Good & Evil.

## irisEng

The checkout has no root README or root license, and its first commit does not
identify a specific executable or build. It contains engine sources alongside
`ai/ai_bge` compiled-script reconstructions, event-track helpers, and BGE
animation/event code. Treat it as a valuable implementation lead, but its
provenance relative to a particular 2002 prerelease remains unverified here.

The BGE script `ai/ai_bge/ai_compiledscripts_lipsync_auto.c` gives a concrete
facial-message flow. `Cine_lipsynch_auto_Lips_init` resolves keys `3500B77E`
and `3500B773`, installs the morphing function on AI track 3, and immediately
switches the current AI track to the message reader. The reader obtains message
ID 53, copies its fields into script-local state, uses the target reference,
`Int1`, and `v2.y` to set a mouth-form target and interpolation parameters. The
morphing track updates the selected channel's form, factor, and progression;
when the auto flag is set, it cycles a 32-item per-script form sequence.

The same source also exposes animation-track scheduling. `events.c` dispatches
type-64 AI-function events while their event key is active, establishes either
the track's GAO or its owner as the current script target, then calls the AI
node payload. `ai_eventtrackfunctions.c` exposes explicit play/stop/pause/reinit
operations for individual tracks and whole event lists. This helps explain how
timeline events can activate script behavior, but it does not prove that any
particular 2003 BF clip is started by a particular gameplay condition.

These names and key values strongly align with the independently captured PC
2003 native handlers and with type-53 records recovered from its BF archive.
They provide cross-version corroboration for the message reader/controller
relationship, not a proof that the 2002/prerelease and 2003 PC bodies are
identical. The active 2003 archive census remains the primary evidence for
which scenes, clips, owners, and targets contain candidate events.

## IrisTool

Its README describes opening PC and other-platform BF files, viewing levels,
textures, audio, and model animation, and supporting a limited set of GAO
modifiers including Morph. It explicitly lists scripting and cutscene playback
as unsupported and says it cannot edit or extract game data. It is useful for
independent visual inspection of assets and animation, but does not answer the
script-trigger or full dialogue-chain question. The root license is GPL-3.0.

## Jaded

The upstream README describes a community fork based on a leaked Jade Engine
copy from around May/June 2006. It is substantially later than BGE 2003. Its
`AIinterp` event code has a dedicated `EVEnt_morphkey` implementation that
updates a geometry morph modifier over time using consecutive morph keys,
target indices, channel blends, and a progression ratio. This is direct
evidence for one later Jade event/morph implementation, not proof that the
2003 PC game uses that exact event payload or geometry-modifier layout.

This distinction matters: the 2003 PC investigation currently observes actor
message type 53 driving facial-controller slots and type-4 morph channel
evaluation. A later engine's direct morph-key timeline is a separate possible
path and should only be added to the BGE chain if the original archive or its
identified PC executable demonstrates that resource/event type.

## Effect on the ongoing cutscene investigation

The Iris sources raise confidence in the proposed division of responsibility:
cutscene animation events can invoke script callbacks; generic actor script
logic consumes type-53 messages; a separate script track interpolates or cycles
mouth-form channels. They do not yet reveal the activation condition for each
cutscene owner/clip in the 2003 BF. The next archive work remains to resolve
owner-script variables and references, then trace which scene/game-state
resources start each owner animation. No external source was used as a
substitute for missing 2003 evidence.
