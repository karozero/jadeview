using System.Buffers.Binary;

namespace JadeView;
internal sealed partial class PcSceneRebuild
{
    private void UpdateSectorVisibility(PcSceneLoader.Record world, SceneObject[] worldObjects, SceneStructureChange[] edits)
    {
        var removedIds = edits.Where(change => change.Kind == "remove").Select(change => change.ObjectId).ToHashSet();
        var retainedObjects = worldObjects.Where(sceneObject => !removedIds.Contains(sceneObject.Id)).ToArray();
        foreach (var sceneObject in retainedObjects)
        {
            var record = ObjectRecord(sceneObject);
            bool needsLegacyRepair = record != null && PcSectorVisibility.IsLegacyDisabledController(original, record);
            if (needsLegacyRepair)
                DisableVisibilityController(world.Key, record!);
        }

        bool removesSolidGeometry = worldObjects.Any(sceneObject => removedIds.Contains(sceneObject.Id) && sceneObject.GeometryKey != 0 && (sceneObject.Flags & 0x100) != 0);
        if (!removesSolidGeometry)
            return;
        // Deleting a barrier does not run the story's portal-opening event.
        // Disconnect recognized hide controllers while retaining sector groups.
        foreach (var controller in retainedObjects.Where(sceneObject => (sceneObject.Flags & 0x800000) != 0))
            UpdatePortalController(world.Key, controller);
    }

    private void UpdatePortalController(uint worldKey, SceneObject controller)
    {
        var record = ObjectRecord(controller);
        bool canUpdateBrain = record != null && record.BrainFieldOffset >= 0 && !replacements.ContainsKey((worldKey, record.Key, 'l'));
        if (!canUpdateBrain)
            return;
        byte[]? variables = PortalControllerVariables(record!, worldKey);
        if (variables == null || !PcSectorVisibility.IsPortalController(variables))
            return;
        DisableVisibilityController(worldKey, record!);
    }

    private byte[]? PortalControllerVariables(PcSceneLoader.Record record, uint worldKey)
    {
        uint brainKey = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(record.BrainFieldOffset, 4));
        if (brainKey is 0 or uint.MaxValue)
            return null;
        byte[] brain = Supply(new PcSceneLoader.Request(brainKey, 'q'), worldKey);
        if (brain.Length != 12)
            return null;
        uint variablesKey = BinaryPrimitives.ReadUInt32LittleEndian(brain.AsSpan(8, 4));
        return variablesKey is 0 or uint.MaxValue ? null : Supply(new PcSceneLoader.Request(variablesKey, 't'), worldKey);
    }

    private void DisableVisibilityController(uint worldKey, PcSceneLoader.Record record)
    {
        replacements[(worldKey, record.Key, 'l')] = PcSectorVisibility.WithoutBrain(ObjectFrame(record), record);
        disabledVisibilityControllers.Add((worldKey, record.Key));
    }
}
