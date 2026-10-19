using System.Buffers.Binary;

namespace JadeView;
internal sealed partial class PcSceneRebuild
{
    private void LoadScope(string scope)
    {
        string containerId = scope.Equals("FIX", StringComparison.OrdinalIgnoreCase) ? "FF0084DF" : scope;
        var entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Equals(containerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Resource container '{scope}' was not found.");
        bool isCurrentScene = entry.Id.Equals(document.ContainerName, StringComparison.OrdinalIgnoreCase) ||
            entry.Name.Equals(document.ContainerName, StringComparison.OrdinalIgnoreCase);
        bool isSupportedScope = entry.Id == "FF0084DF" || isCurrentScene;
        if (!isSupportedScope)
            throw new InvalidDataException("Importing an asset from a different scene also requires its texture container; use assets from this scene or FIX.");
        byte[] bytes = archive.Read(entry);
        var parsed = PcSceneLoader.Read(bytes, archive.PathName);
        if (!parsed.Complete)
            throw new InvalidDataException($"Cannot rebuild using {entry.Id}: {parsed.Error}");
        sources.Add(new PcSceneSource(bytes, parsed));
        loadedScopes.Add(scope);
    }

    private static byte[] Frame(PcSceneSource source, PcSceneLoader.Record record) => source.Frame(record);
    private byte[] ObjectFrame(PcSceneLoader.Record record) => detachedReferences.GetValueOrDefault((record.Key, record.Kind)) ?? Frame(local, record);
    private static byte[] UniqueFrame(IEnumerable<(PcSceneSource Source, PcSceneLoader.Record Record)> candidates, uint key)
    {
        var frames = candidates.Select(candidate => Frame(candidate.Source, candidate.Record)).ToArray();
        if (frames.Length == 0)
            throw new InvalidDataException($"No source payload for resource {key:X8}.");
        bool hasConflictingFrames = frames.Skip(1).Any(frame => !frame.AsSpan().SequenceEqual(frames[0]));
        if (hasConflictingFrames)
            throw new InvalidDataException($"Resource {key:X8} has conflicting payloads in this load scope.");
        return frames[0];
    }

    private byte[] Supply(PcSceneLoader.Request request, uint worldKey)
    {
        if (instanceCopies.TryGetValue(request.Key, out var instance))
        {
            if (instance.Kind != request.Kind)
                throw new InvalidDataException("Duplicated instance resource type mismatch.");
            return instance.Frame;
        }

        if (replacements.TryGetValue((worldKey, request.Key, request.Kind), out var changed))
            return changed;
        if (detachedReferences.TryGetValue((request.Key, request.Kind), out var detached))
            return detached;
        if (request.Kind == 'l' && additions.TryGetValue(request.Key, out var added))
            return added;
        if (request.Kind == 'm' && geometryCopies.TryGetValue(request.Key, out uint sourceGeometry))
            return Supply(new PcSceneLoader.Request(sourceGeometry, 'm'), worldKey);
        var scoped = replay.Records.Where(record => record.LoadWorldKey == worldKey && MatchesRequest(record, request)).ToArray();
        if (scoped.Length > 0)
            return UniqueFrame(scoped.Select(record => (local, record)), request.Key);
        var candidates = FindSourceFrames(request);
        if (candidates.Length == 0)
        {
            string scope = resolver?.ResourceItems.GetValueOrDefault(request.Key)?.Id.Scope ?? "FIX";
            if (!loadedScopes.Contains(scope))
                LoadScope(scope);
            candidates = FindSourceFrames(request);
        }

        return UniqueFrame(candidates, request.Key);
    }

    private (PcSceneSource Source, PcSceneLoader.Record Record)[] FindSourceFrames(PcSceneLoader.Request request) =>
        sources.SelectMany(source => source.Replay.Records
            .Where(record => MatchesRequest(record, request))
            .Select(record => (source, record)))
            .ToArray();
    private static bool MatchesRequest(PcSceneLoader.Record record, PcSceneLoader.Request request) => record.Key == request.Key && record.Kind == request.Kind;
    private static HashSet<uint> ReadSpecialKeys(string archivePath)
    {
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, "jade.spe");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length % sizeof(uint) != 0)
            throw new InvalidDataException("Invalid jade.spe size.");
        var keys = new HashSet<uint>();
        for (int offset = 0; offset < bytes.Length; offset += sizeof(uint))
            keys.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint))));
        return keys;
    }
}
