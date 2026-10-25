using System.Globalization;
using System.Text.Json;
using JadeView;

internal static class ArchiveZoneTriggerCensus
{
    private const string InitCallback = "Detect_o_Zone_doz_init";

    private sealed record ZoneOwnerRow(
        string Container, string WorldKey, string WorldName, string OwnerKey, string OwnerName,
        string BrainKey, string ModelKey, string[] ModelOvaCandidates, string ModelOvaBinding,
        string RouteFromSerializedIdentityFlags, string ActorReference, string ActorResolution,
        string? CollisionInstanceKey, string? CollisionMapKey, string CollisionMetadataResolution,
        int? SetFlag, int? ResetFlag, int? InvertFlag, float? TimerSeconds,
        string EventsKey, int EventsLinkedTrackCount,
        string SceneEventsKey, int SceneEventsLinkedTrackCount, int SceneEventsEmbeddedTrackCount,
        string[] OwnerEventTrackLinks,
        string[] IncomingSceneEventTargets, string[] EventMessageKinds,
        string Limits);

    internal static void Write(string archivePath, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var rows = new List<ZoneOwnerRow>();
        int scannedContainers = 0;
        int parseErrors = 0;

        foreach (var entry in archive.Entries.Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
                     .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes = archive.Read(entry);
            var replay = PcSceneLoader.Read(bytes, archivePath, entry.Key);
            if (!replay.Complete)
            {
                parseErrors++;
                continue;
            }

            scannedContainers++;
            SceneDocument document = SceneLoader.Load(bytes, entry.Name, archivePath, worldListKey: entry.Key);
            if (document.CinematicScriptOwners.Count == 0) continue;

            var objectByWorldAndKey = document.Worlds
                .SelectMany(world => world.Objects.Select(sceneObject => (world.Key, sceneObject.GaoKey, sceneObject)))
                .GroupBy(item => (item.Key, item.GaoKey))
                .ToDictionary(group => group.Key, group => group.Select(item => item.sceneObject).ToArray());
            var objectByKey = document.Worlds.SelectMany(world => world.Objects.Select(sceneObject =>
                    (world.Key, world.Name, sceneObject)))
                .GroupBy(item => item.sceneObject.GaoKey)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var models = replay.Records.Where(record => record.Kind == 's')
                .GroupBy(record => record.Key).ToDictionary(group => group.Key, group => group.First());

            foreach (CinematicScriptOwner owner in document.CinematicScriptOwners.Values
                         .Where(owner => owner.CallbackNames.Contains(InitCallback, StringComparer.Ordinal)))
            {
                uint[] ovaKeys = models.TryGetValue(owner.ScriptModelKey, out var modelRecord)
                    ? ReadOvaKeys(bytes, modelRecord.Offset, modelRecord.Size)
                    : [];
                int foundOvaCount = ovaKeys.Count(replay.Resources.ContainsKey);
                string ovaBinding = ovaKeys.Length switch
                {
                    0 => "no typed .ova reference found",
                    1 when foundOvaCount == 1 => "unique typed .ova reference",
                    1 => "typed .ova resource unresolved",
                    _ => $"ambiguous: {ovaKeys.Length} typed .ova references ({foundOvaCount} resolved)"
                };

                uint? actorKey = ReadObjectKey(owner, "des_acteur_detecte1");
                string actorReference = actorKey is uint key ? $"{key:X8}" : "unavailable";
                string actorResolution = ResolveActor(owner, actorKey, objectByWorldAndKey, objectByKey);
                SceneObject[] matchingZoneObjects = objectByWorldAndKey
                    .GetValueOrDefault((owner.WorldKey, owner.OwnerKey), []);
                SceneObject? zoneObject = matchingZoneObjects.Length == 1 ? matchingZoneObjects[0] : null;
                string collisionMetadataResolution = matchingZoneObjects.Length switch
                {
                    0 => "owner GAO is not present in its loaded world object list",
                    > 1 => $"ambiguous: {matchingZoneObjects.Length} owner GAOs in loaded world",
                    _ when zoneObject!.CollisionInstanceKey is 0 or uint.MaxValue
                        && zoneObject.CollisionMapKey is 0 or uint.MaxValue
                        => "unique owner GAO has no typed collision instance/map reference",
                    _ => "unique owner GAO collision references resolved from typed scene record"
                };

                var ownedEvents = document.CinematicScriptEventTracks
                    .GetValueOrDefault(owner.Id, []);
                var ownedSceneEvents = document.CinematicEmbeddedEventTracks
                    .GetValueOrDefault((owner.WorldKey, owner.OwnerKey), []);
                var incomingLists = document.CinematicEmbeddedEventTracks.Values.SelectMany(lists => lists)
                    .Where(list => list.WorldKey == owner.WorldKey)
                    .SelectMany(list => list.Tracks.Where(track => track.TargetKey == owner.OwnerKey)
                        .Select(track => (list, track)))
                    .ToArray();

                rows.Add(new ZoneOwnerRow(
                    entry.Id, owner.WorldKey.ToString("X8"),
                    document.Worlds.FirstOrDefault(world => world.Key == owner.WorldKey)?.Name ?? "",
                    owner.OwnerKey.ToString("X8"), owner.Name, owner.BrainKey.ToString("X8"),
                    owner.ScriptModelKey.ToString("X8"), ovaKeys.Select(key => key.ToString("X8")).ToArray(),
                    ovaBinding,
                    (owner.SerializedGaoFlags & 0x400) != 0 ? "ZDE pivot 2" : "bounding-volume overlap",
                    actorReference, actorResolution,
                    FormatOptionalKey(zoneObject?.CollisionInstanceKey), FormatOptionalKey(zoneObject?.CollisionMapKey),
                    collisionMetadataResolution,
                    ReadInteger(owner, "des_i_SPFlag_set01"),
                    ReadInteger(owner, "des_i_SPFlag_reset01"),
                    ReadInteger(owner, "des_i_SPFlag_invert01"),
                    ReadFloat(owner, "des_f_minuterie"),
                    owner.EventsKey.ToString("X8"), ownedEvents.Count(track => track.LinkName == "Events"),
                    owner.SceneEventsKey.ToString("X8"),
                    ownedEvents.Count(track => track.LinkName == "SceneEvents"),
                    ownedSceneEvents.Sum(list => list.Tracks.Length),
                    ownedEvents.Select(track => $"{track.LinkName}:{track.EventKey:X8}").Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToArray(),
                    incomingLists.Select(item =>
                        $"{item.list.OwnerName}({item.list.OwnerKey:X8})/{item.list.EventKey:X8}/track {item.track.Index}")
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    incomingLists.SelectMany(item => item.track.Messages)
                        .Select(item => $"type {item.Message.Type} at {item.Time.ToString("0.###", CultureInfo.InvariantCulture)} s")
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    "Effective OVA values are the loader's unique resolved view; when .ova candidates are ambiguous, values are retained as observed but are not attributed to one binding. Collision overlap, live actor slots, saved flags, capability state, EOT membership, callback scheduling and message delivery are runtime-only."));
            }
        }

        var report = new
        {
            Archive = Path.GetFileName(archivePath),
            ScannedContainers = scannedContainers,
            ContainerParseErrors = parseErrors,
            ZoneOwnerCount = rows.Count,
            DistinctModels = rows.Select(row => row.ModelKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            UniqueModelOvaBindings = rows.Count(row => row.ModelOvaBinding == "unique typed .ova reference"),
            AmbiguousModelOvaBindings = rows.Count(row => row.ModelOvaBinding.StartsWith("ambiguous:", StringComparison.Ordinal)),
            UnresolvedModelOvaBindings = rows.Count(row => row.ModelOvaBinding != "unique typed .ova reference"
                && !row.ModelOvaBinding.StartsWith("ambiguous:", StringComparison.Ordinal)),
            ActorResolutionCounts = rows.GroupBy(row => row.ActorResolution)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new { Resolution = group.Key, Count = group.Count() }).ToArray(),
            OwnersWithEventsLinks = rows.Count(row => row.EventsKey is not "FFFFFFFF" and not "00000000"),
            OwnersWithSceneEventsLinks = rows.Count(row => row.SceneEventsKey is not "FFFFFFFF" and not "00000000"),
            OwnersTargetedBySceneEvents = rows.Count(row => row.IncomingSceneEventTargets.Length > 0),
            OwnersWithSavedFlagEffects = rows.Count(row => row.SetFlag is > 0 || row.ResetFlag is > 0 || row.InvertFlag is > 0),
            OwnersWithTimer = rows.Count(row => row.TimerSeconds is { } timer && timer != 0),
            Rows = rows.OrderBy(row => row.Container, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.WorldKey, StringComparer.Ordinal)
                .ThenBy(row => row.OwnerKey, StringComparer.Ordinal).ToArray(),
            Interpretation = new
            {
                Confirmed = "Iris source defines two collision predicates, edge-latched entry/exit state, capability 0x100 updates, saved-flag mutations and optional delayed inverse mutations. Rows report serialized owner/model/OVA/event links and effective initial values.",
                NotConfirmed = "The BF does not contain a live collision result, detection-list contents, resolved current actor-slot objects, runtime saved-flag values, activation/EOT membership, callback execution, or delivered messages. Jaded corroborates low-level collision/capability APIs only; the Detect_o_Zone gameplay callback itself is evidenced by Iris source and retail archive configuration.",
                Source = "external/irisEng/ai/ai_bge/ai_compiledscripts_detectors.c:1166-1365; external/irisEng/ai/ai_gameobjectfunctions.c collision wrappers; external/irisEng/collision.c detection-list update; external/jaded/src/libraries/AIinterp/Sources/AIstruct.h and COLlision sources"
            }
        };

        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, "archive-zone-trigger-census.json");
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ZONE TRIGGER CENSUS: {rows.Count} owners, {report.DistinctModels} models, "
            + $"{report.UniqueModelOvaBindings} unique OVA bindings, {report.AmbiguousModelOvaBindings} ambiguous, "
            + $"{report.OwnersWithSceneEventsLinks} SceneEvents links -> {output}");
    }

    private static uint[] ReadOvaKeys(byte[] bytes, int offset, int size)
    {
        var keys = new List<uint>();
        for (int position = offset; position + 8 <= offset + size; position += 8)
        {
            if (!bytes.AsSpan(position + 4, 4).SequenceEqual(".ova"u8)) continue;
            uint key = BitConverter.ToUInt32(bytes, position);
            if (key is not 0 and not uint.MaxValue) keys.Add(key);
        }
        return keys.Distinct().ToArray();
    }

    private static uint? ReadObjectKey(CinematicScriptOwner owner, string variableName)
    {
        CinematicScriptVariable? variable = owner.EffectiveVariable(variableName);
        if (variable == null) return null;
        string value = variable.Value;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
            return hex;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint integer)
            ? integer : null;
    }

    private static int? ReadInteger(CinematicScriptOwner owner, string variableName)
    {
        string? value = owner.EffectiveVariable(variableName)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result : null;
    }

    private static float? ReadFloat(CinematicScriptOwner owner, string variableName)
    {
        string? value = owner.EffectiveVariable(variableName)?.Value;
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result : null;
    }

    private static string ResolveActor(CinematicScriptOwner owner, uint? actorKey,
        IReadOnlyDictionary<(uint WorldKey, uint GaoKey), SceneObject[]> worldObjects,
        IReadOnlyDictionary<uint, (uint Key, string Name, SceneObject sceneObject)[]> objects)
    {
        if (actorKey == null) return "effective reference unavailable";
        if (actorKey == 0) return "null (callback collision predicate rejects null)";
        if (actorKey is 1 or 2) return $"main actor slot {actorKey}; live slot unresolved";
        if (worldObjects.TryGetValue((owner.WorldKey, actorKey.Value), out SceneObject[]? localMatches))
            return localMatches.Length switch
            {
                1 => $"unique object in owner world: {localMatches[0].Name}",
                > 1 => $"ambiguous in owner world: {localMatches.Length} matches",
                _ => "not found in owner's loaded world"
            };
        if (!objects.TryGetValue(actorKey.Value, out var crossWorldMatches))
            return "not found in any loaded world";
        return crossWorldMatches.Length switch
        {
            1 => $"unique object in loaded world {crossWorldMatches[0].Name}: {crossWorldMatches[0].sceneObject.Name}",
            > 1 => $"ambiguous across loaded worlds: {crossWorldMatches.Length} matches",
            _ => "not found in any loaded world"
        };
    }

    private static string? FormatOptionalKey(uint? key) => key is null or 0 or uint.MaxValue
        ? null : key.Value.ToString("X8");
}
