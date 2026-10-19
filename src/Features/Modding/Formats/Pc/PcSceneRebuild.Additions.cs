using System.Buffers.Binary;

namespace JadeView;
internal sealed partial class PcSceneRebuild
{
    private void AddObject(SceneStructureChange change, uint worldKey, SceneObject[] worldObjects)
    {
        bool hasInvalidKey = change.GaoKey is 0 or uint.MaxValue || !allKeys.Add(change.GaoKey);
        if (hasInvalidKey)
            throw new InvalidDataException($"Added object '{change.Name}' has a conflicting GAO key {change.GaoKey:X8}.");
        var activeResolver = RequireResolver();
        bool hasGeometry = activeResolver.Geometries.TryGetValue(change.GeometryKey, out var geometry);
        if (!hasGeometry || geometry!.Positions.Length == 0)
            throw new InvalidDataException($"Geometry {change.GeometryKey:X8} for '{change.Name}' is unavailable or has no vertex bounds.");
        var copySource = FindCopySource(change, worldObjects);
        var template = FindObjectTemplate(change, copySource);
        var templateRecord = ObjectRecord(template)!;
        bool hasSelfReference = copySource != null && templateRecord.References.Any(reference => reference.Key == copySource.GaoKey);
        if (hasSelfReference)
            throw new InvalidDataException($"'{change.Name}' has self-references that require explicit remapping before duplication.");
        uint geometryKey = copySource == null ? change.GeometryKey : CopyGeometry(change.GeometryKey);
        exportedGeometry.Add(change.GaoKey, geometryKey);
        uint? brainKey = copySource == null ? null : CopyBehavior(templateRecord, worldKey);
        if (brainKey.HasValue)
            exportedBrains.Add(change.GaoKey, brainKey.Value);
        byte[] header = original.AsSpan(template.RecordRange.Offset + 4, 22).ToArray();
        var sourceRecord = copySource == null ? null : templateRecord;
        byte[] frame = PcSceneObjectEncoder.Write(change, geometry!, geometryKey, brainKey, header, sourceRecord, original);
        additions.Add(change.GaoKey, frame);
    }

    private ResourceResolver RequireResolver()
    {
        if (resolver != null)
            return resolver;
        resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        var entry = archive.Entries.First(candidate => candidate.Id.Equals(document.ContainerName, StringComparison.OrdinalIgnoreCase) || candidate.Name.Equals(document.ContainerName, StringComparison.OrdinalIgnoreCase));
        resolver.LoadSceneContainer(archive, entry);
        return resolver;
    }

    private static SceneObject? FindCopySource(SceneStructureChange change, SceneObject[] worldObjects)
    {
        if (change.SourceObjectId == null)
            return null;
        var copySource = worldObjects.FirstOrDefault(sceneObject => sceneObject.Id == change.SourceObjectId) ?? throw new InvalidDataException($"Copy source for '{change.Name}' was not found.");
        const uint dependentFlags = SceneLoader.IdentityFlagHierarchy | SceneLoader.IdentityFlagAnims | SceneLoader.IdentityFlagBone;
        bool hasDependentState = (copySource.Flags & dependentFlags) != 0;
        if (hasDependentState)
            throw new InvalidDataException($"'{change.Name}' is not an independent static GAO and cannot be duplicated into BF.");
        return copySource;
    }

    private SceneObject FindObjectTemplate(SceneStructureChange change, SceneObject? copySource)
    {
        if (copySource != null)
            return copySource;
        var sceneObjects = baseline.Worlds.SelectMany(world => world.Objects).ToArray();
        var matchingGeometry = sceneObjects.FirstOrDefault(sceneObject => sceneObject.Actor == null && sceneObject.GeometryKey == change.GeometryKey);
        var independentVisual = sceneObjects.FirstOrDefault(sceneObject => sceneObject.Actor == null && sceneObject.Capabilities.CanTranslate && sceneObject.GeometryKey != 0);
        return matchingGeometry ?? independentVisual ?? throw new InvalidDataException("The source scene has no independent visual GAO template.");
    }

    private uint CopyGeometry(uint sourceKey)
    {
        // The copy owns a GRO resource so mutable runtime state is not shared.
        uint geometryKey = 0xFDFFFFFF;
        while (geometryKey > 0xFD000000 && IsGeometryKeyOccupied(geometryKey))
            geometryKey--;
        if (geometryKey == 0xFD000000)
            throw new InvalidDataException("No free key for duplicated GRO resource.");
        allKeys.Add(geometryKey);
        geometryCopies.Add(geometryKey, sourceKey);
        return geometryKey;
    }

    private bool IsGeometryKeyOccupied(uint key) => allKeys.Contains(key) || changes.Any(change => change.GaoKey == key) || resolver!.ResourceItems.ContainsKey(key);
    private uint? CopyBehavior(PcSceneLoader.Record template, uint worldKey)
    {
        if (template.BrainFieldOffset < 0)
            return null;
        uint sourceKey = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(template.BrainFieldOffset, 4));
        if (sourceKey is 0 or uint.MaxValue)
            return null;
        byte[] brain = Supply(new PcSceneLoader.Request(sourceKey, 'q'), worldKey).ToArray();
        bool hasSupportedLayout = brain.Length == 12 && BinaryPrimitives.ReadInt32LittleEndian(brain) == 8;
        if (!hasSupportedLayout)
            throw new InvalidDataException("Unsupported duplicated behavior-instance layout.");
        CopyBehaviorVariables(brain, worldKey);
        uint brainKey = AllocateInstanceKey(sourceKey);
        instanceCopies.Add(brainKey, ('q', brain));
        return brainKey;
    }

    private void CopyBehaviorVariables(byte[] brain, uint worldKey)
    {
        uint sourceKey = BinaryPrimitives.ReadUInt32LittleEndian(brain.AsSpan(8));
        if (sourceKey is 0 or uint.MaxValue)
            return;
        byte[] variables = Supply(new PcSceneLoader.Request(sourceKey, 't'), worldKey).ToArray();
        uint variablesKey = AllocateInstanceKey(sourceKey);
        instanceCopies.Add(variablesKey, ('t', variables));
        BinaryPrimitives.WriteUInt32LittleEndian(brain.AsSpan(8), variablesKey);
    }

    private uint AllocateInstanceKey(uint sourceKey)
    {
        // Keep copied state in the source bank; keys are opaque to the editor.
        uint bank = sourceKey & 0xFF000000;
        for (uint suffix = 0x00FFFFFF; suffix > 0; suffix--)
        {
            uint key = bank | suffix;
            bool isOccupied = allKeys.Contains(key) || specialKeys.Contains(key) || changes.Any(change => change.GaoKey == key) || resolver?.ResourceItems.ContainsKey(key) == true;
            if (isOccupied)
                continue;
            if (allKeys.Add(key))
                return key;
        }

        throw new InvalidDataException($"No free key in resource bank {bank >> 24:X2} for duplicated object state.");
    }
}
