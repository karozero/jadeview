# Archive AI dependency census

The `--archive-ai-dependency-census` audit reports serialized script-owner
dependencies across all parseable `FF...` world-list containers in a retail BF
archive. It is intended to find missing or ambiguous links outside cinematic
scenes as well as inside them.

Run it with:

```powershell
dotnet run --project tests/ExportVerification/ExportVerification.csproj -c Release -- --archive-ai-dependency-census "E:\SteamLibrary\steamapps\common\Beyond Good and Evil\sally_clean.bf"
```

The JSON report is written to the ignored
`artifacts/export-verification/archive-ai-dependency-census.json`. Each row
keeps the container/world, owner, serialized identity flags, brain and model
keys, fixed and engine callback slots, function-resource statuses, `Events` and
`SceneEvents` links, instance overrides, and object references together. This
makes resource provenance and broken links inspectable without conflating the
five fixed AI callback tracks with EVE event lists or engine callback IDs.

The audit describes archive structure, not execution. A resolved model or
callback slot does not prove that the game activated that owner, placed it in
the relevant runtime table, passed its gates, or ran the function. Iris names
and call mappings are source-derived; Jaded is a later-engine comparison and
is not assumed to have identical retail-PC scheduling rules.

## Current `sally_clean.bf` result

The installed archive scan parsed 91 world-list containers without a container
parse error and found 16,036 serialized owner records: 15,297 in worlds whose
names do not contain `cine`, and 739 in worlds whose names do. These are
container records, not necessarily 16,036 unique GAOs. There are 10,023
distinct `(world, owner-key)` pairs, 10,023 brain keys, and 435 script models.
All records have decoded fixed callback-slot records; 3,272 also expose engine
callback slots. The archive contains 687 records with an `Events` link and 970
with a `SceneEvents` link. The `cine` split is only a name-based grouping.

The 16,036 records contain 117,484 model function-resource occurrences across
the script owners: 117,482 parse as `CompactMetadataCandidate`, two have an
unsupported layout, and none decode as a full serialized function body in this
audit. The repeated occurrences should not be read as unique functions. The
separate cinematic-only index found 607 unique compact payloads among its
smaller subset. Compact metadata includes declared node size and local
constants, but the node body is absent, so these records cannot reveal each
owner's executable implementation.

The most reused mapped initial callbacks include `SFX_HaloSimple_init` (945
owners), `gencam_mario_gcm_first` (699), `Cine_Events_cine_firstinit` (654),
`Detect_o_4SpecialFlags_D4sf_init` (642), and `helper_text_hlptxt_init` (515).
The generic `sc_dummy` slot appears on 3,189 owners. These counts show why a
scene-name rule is the wrong abstraction: the same reusable script models and
callbacks serve many objects and worlds, while runtime gates still determine
which configured owners actually execute.

For example, gameplay-world `EVT_Start.gao` in `07_03_course_03` links owner
`02000B70` → brain `02000B72` → model `350091C3`, assigns slot 2 to
`Cine_Events_cine_firstinit`, and links `SceneEvents` `02000B73`. The model
appears in 478 non-cinematic-named and 176 cinematic-named records. This is a
serialized dependency chain and reusable controller model, not evidence that
the track or its gates ran in a game session.
