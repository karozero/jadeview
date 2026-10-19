using System.Buffers.Binary;

namespace JadeView;
internal sealed partial class PcSceneRebuild
{
    private void Verify(byte[] output)
    {
        var verified = PcSceneLoader.Read(output, document.BaseBfPath);
        if (!verified.Complete)
            throw new InvalidDataException("Rebuilt FF0 did not reload: " + verified.Error);
        VerifyRemovedObjects(output, verified);
        if (verified.Worlds.Count != replay.Worlds.Count)
            throw new InvalidDataException("Rebuilt FF0 changed the world inventory.");
        for (int worldIndex = 0; worldIndex < replay.Worlds.Count; worldIndex++)
            VerifyWorld(output, verified, worldIndex);
        VerifyInstanceCopies(output, verified);
        VerifyDisabledControllers(verified);
    }

    private void VerifyRemovedObjects(byte[] output, PcSceneLoader.Result verified)
    {
        bool hasUnownedObject = verified.Records.Any(record => record.Kind == 'l' && unownedKeys.Contains(record.Key));
        bool hasUnownedReference = verified.Records.Any(record => record.GaoReferenceOffsets.Any(offset => unownedKeys.Contains(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset, 4)))));
        if (hasUnownedObject || hasUnownedReference)
            throw new InvalidDataException("Rebuilt FF0 retained a reference to a deleted object without a world owner.");
    }

    private void VerifyWorld(byte[] output, PcSceneLoader.Result verified, int worldIndex)
    {
        var world = replay.Worlds[worldIndex];
        var verifiedWorld = verified.Worlds[worldIndex];
        if (verifiedWorld.Key != world.Key)
            throw new InvalidDataException("Rebuilt FF0 changed the world load order.");
        if (!FrameMatches(Frame(local, world), output, verifiedWorld))
            throw new InvalidDataException($"Rebuilt FF0 changed the opaque WOW fields for '{world.Name}'.");
        var expectedKeys = world.WorldMembers.Select(record => record.Key).ToList();
        var edits = WorldChanges(world.Key);
        foreach (var change in edits)
        {
            if (change.Kind == "add")
                expectedKeys.Add(change.GaoKey);
            else
                expectedKeys.Remove(change.GaoKey);
        }

        expectedKeys.RemoveAll(unownedKeys.Contains);
        bool matchesInventory = expectedKeys.Order().SequenceEqual(verifiedWorld.WorldMembers.Select(record => record.Key).Order());
        if (!matchesInventory)
            throw new InvalidDataException($"Rebuilt world '{world.Name}' has an unexpected object inventory.");
        foreach (var change in edits.Where(change => change.Kind == "add"))
            VerifyAddedObject(output, verified, verifiedWorld, change);
    }

    private void VerifyAddedObject(byte[] output, PcSceneLoader.Result verified, PcSceneLoader.Record world, SceneStructureChange change)
    {
        var record = world.WorldMembers.Single(candidate => candidate.Key == change.GaoKey);
        var matrix = JadeMatrix.Read(output.AsSpan(record.Offset + 22, JadeMatrix.SizeInBytes));
        var expectedMatrix = JadeMatrix.Identity;
        expectedMatrix.SetTransform(change.Transform.ToTranslation(), change.Transform.ToRotation(), change.Transform.ToScale());
        uint expectedMaterial = change.MaterialKey == 0 ? uint.MaxValue : change.MaterialKey;
        bool matchesObject = matrix.Equals(expectedMatrix) && record.Geometry == exportedGeometry[change.GaoKey] && record.Material == expectedMaterial && record.Name == change.Name;
        if (!matchesObject)
            throw new InvalidDataException($"Added object '{change.Name}' failed reload verification.");
        VerifyCopiedGeometry(output, verified, world.Key, record.Geometry, change.Name);
        if (!exportedBrains.TryGetValue(change.GaoKey, out uint expectedBrain))
            return;
        bool matchesBrain = record.BrainFieldOffset >= 0 && BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(record.BrainFieldOffset, 4)) == expectedBrain;
        if (!matchesBrain)
            throw new InvalidDataException($"Duplicated behavior for '{change.Name}' failed reload verification.");
    }

    private void VerifyCopiedGeometry(byte[] output, PcSceneLoader.Result verified, uint worldKey, uint geometryKey, string objectName)
    {
        if (!geometryCopies.ContainsKey(geometryKey))
            return;
        var geometry = verified.Records.FirstOrDefault(record => record.Key == geometryKey && record.Kind == 'm') ?? throw new InvalidDataException($"Duplicated GRO for '{objectName}' is missing.");
        byte[] expected = Supply(new PcSceneLoader.Request(geometryKey, 'm'), worldKey);
        if (!FrameMatches(expected, output, geometry))
            throw new InvalidDataException($"Duplicated GRO for '{objectName}' changed its payload.");
    }

    private void VerifyInstanceCopies(byte[] output, PcSceneLoader.Result verified)
    {
        foreach (var(key, instance)in instanceCopies)
        {
            var records = verified.Records.Where(record => record.Key == key && record.Kind == instance.Kind).ToArray();
            bool matchesAllFrames = records.Length > 0 && records.All(record => FrameMatches(instance.Frame, output, record));
            if (!matchesAllFrames)
                throw new InvalidDataException($"Duplicated state {key:X8} failed reload verification.");
        }
    }

    private void VerifyDisabledControllers(PcSceneLoader.Result verified)
    {
        foreach (var(worldKey, key)in disabledVisibilityControllers)
        {
            var world = verified.Worlds.Single(candidate => candidate.Key == worldKey);
            var record = world.WorldMembers.Single(candidate => candidate.Key == key);
            bool retainsBehavior = record.BrainFieldOffset >= 0 || (record.Flags & 0x20) != 0 || record.References.Any(reference => reference.Kind == 'q');
            if (retainsBehavior)
                throw new InvalidDataException($"Sector visibility controller {key:X8} was not disabled.");
        }
    }

    private static bool FrameMatches(byte[] expected, byte[] output, PcSceneLoader.Record record) => expected.AsSpan().SequenceEqual(output.AsSpan(record.FrameRange.Offset, record.FrameRange.Size));
}
