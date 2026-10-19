using System.Buffers.Binary;

namespace JadeView;

internal static class PcPickupPersistence
{
    internal static void Prepare(byte[] original, SceneDocument document, ModProject project, bool verifyExecutable = true)
    {
        var additions = project.SceneStructureChanges.Where(change => change.Kind == "add" &&
            change.SourceObjectId != null && change.ContainerName.Equals(document.ContainerName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (additions.Length == 0)
            return;

        var replay = PcSceneLoader.Read(original, document.BaseBfPath);
        if (!replay.Complete)
            throw new InvalidDataException("Cannot inspect pickup persistence: " + replay.Error);
        var baseline = SceneLoader.Load(original, document.ContainerName, document.BaseBfPath);
        var pickups = additions.Where(change => HasPersistence(original, replay, baseline, change))
            .OrderBy(change => change.ObjectId, StringComparer.Ordinal).ToArray();
        if (pickups.Length == 0)
            return;

        if (verifyExecutable)
            PcPickupFlagProfile.VerifyExecutable(document.BaseBfPath);
        var occupied = ReadOccupiedFlags(new Archive(document.BaseBfPath));
        var projectFlags = new HashSet<int>();
        foreach (var assignment in project.PickupSaveFlags)
        {
            bool valid = PcPickupFlagProfile.Candidates.Contains(assignment.Value) &&
                !occupied.Contains(assignment.Value) && projectFlags.Add(assignment.Value);
            if (!valid)
                throw new InvalidDataException($"Pickup save flag {assignment.Value} conflicts with the source archive or another object. Keep the original project and source BF together.");
        }

        foreach (var pickup in pickups)
        {
            if (project.PickupSaveFlags.ContainsKey(pickup.ObjectId))
                continue;
            int flag = PcPickupFlagProfile.Candidates.FirstOrDefault(candidate =>
                !occupied.Contains(candidate) && !projectFlags.Contains(candidate), -1);
            if (flag < 0)
                throw new InvalidDataException("No audited unused save flags remain for duplicated pickups.");
            project.PickupSaveFlags.Add(pickup.ObjectId, flag);
            projectFlags.Add(flag);
        }
    }

    private static bool HasPersistence(byte[] original, PcSceneLoader.Result replay,
        SceneDocument baseline, SceneStructureChange change)
    {
        var source = baseline.Worlds.Where(world => world.Key == change.WorldKey)
            .SelectMany(world => world.Objects).FirstOrDefault(sceneObject => sceneObject.Id == change.SourceObjectId);
        if (source == null)
            return false; // The structural writer reports the missing source separately.
        var gao = replay.Records.First(record => record.Kind == 'l' &&
            record.Offset == source.RecordRange.Offset + 4);
        if (gao.BrainFieldOffset < 0)
            return false;
        uint brainKey = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(gao.BrainFieldOffset));
        var brain = replay.Records.FirstOrDefault(record => record.Kind == 'q' &&
            record.Key == brainKey && record.LoadWorldKey == change.WorldKey);
        brain ??= replay.Records.FirstOrDefault(record => record.Kind == 'q' && record.Key == brainKey);
        if (brain == null || brain.Size != 8)
            return false;
        uint model = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(brain.Offset));
        if (model != PcPickupVariables.BehaviorModel)
            return false;
        uint variablesKey = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(brain.Offset + 4));
        var variables = replay.Records.First(record => record.Key == variablesKey && record.Kind == 't');
        byte[] frame = original.AsSpan(variables.FrameRange.Offset, variables.FrameRange.Size).ToArray();
        return PcPickupVariables.FindFlagFields(frame).HasValue;
    }

    private static HashSet<int> ReadOccupiedFlags(Archive archive)
    {
        var occupied = new HashSet<int>();
        foreach (var entry in archive.Entries.Where(candidate => candidate.Id.StartsWith("FF0", StringComparison.Ordinal)))
        {
            byte[] bytes = archive.Read(entry, int.MaxValue);
            var parsed = PcSceneLoader.Read(bytes, archive.PathName);
            if (!parsed.Complete)
                throw new InvalidDataException($"Cannot audit save flags in {entry.Id}: {parsed.Error}");
            foreach (var variables in parsed.Records.Where(record => record.Kind == 't'))
                ReserveIntegers(bytes.AsSpan(variables.Offset, variables.Size), occupied);
        }

        return occupied;
    }

    private static void ReserveIntegers(ReadOnlySpan<byte> payload, HashSet<int> occupied)
    {
        // OVA descriptors/names are even-sized, but the words need not be 4-byte aligned.
        // Include every even-position word, not just fields whose names mention SF.
        for (int offset = 0; offset <= payload.Length - 4; offset += 2)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
            if (value >= 6000 && value < 6144)
                occupied.Add(value);
        }
    }
}
