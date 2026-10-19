using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;
public sealed partial class ModProject
{
    public void RecordSceneObjectState(SceneObject obj, string containerName, uint worldKey, bool exists)
    {
        ArgumentNullException.ThrowIfNull(obj);
        SceneStructureChanges.RemoveAll(candidateChange => string.Equals(candidateChange.ObjectId, obj.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));
        if (obj.IsUserCreated)
        {
            if (exists)
                SceneStructureChanges.Add(SceneStructureChange.Added(obj, containerName, worldKey));
        }
        else if (!exists)
        {
            SceneStructureChanges.Add(new SceneStructureChange { Kind = "remove", ContainerName = containerName, WorldKey = worldKey, ObjectId = obj.Id, GaoKey = obj.GaoKey, Name = obj.Name });
        }

        ModifiedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateAddedObject(SceneObject obj, string containerName, uint worldKey)
    {
        if (!obj.IsUserCreated)
            return;
        var existing = SceneStructureChanges.FirstOrDefault(candidateChange => candidateChange.Kind == "add" && string.Equals(candidateChange.ObjectId, obj.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(candidateChange.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return;
        var updated = SceneStructureChange.Added(obj, containerName, existing.WorldKey);
        SceneStructureChanges[SceneStructureChanges.IndexOf(existing)] = updated;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    private void ApplySceneStructureChanges(SceneDocument document, ModApplyResult result)
    {
        var relevantChanges = SceneStructureChanges.Where(change => string.Equals(change.ContainerName, document.ContainerName, StringComparison.OrdinalIgnoreCase)).ToArray();
        document.ReservedResourceKeys.UnionWith(relevantChanges.Select(change => change.GaoKey));
        foreach (var change in relevantChanges)
        {
            var world = document.Worlds.FirstOrDefault(candidate => candidate.Key == change.WorldKey);
            if (world == null)
            {
                result.Warnings.Add($"World 0x{change.WorldKey:X8} for scene object '{change.Name}' was not found.");
                continue;
            }

            ApplySceneStructureChange(world, change);
        }
    }

    private static void ApplySceneStructureChange(WorldData world, SceneStructureChange change)
    {
        if (change.Kind == "remove")
        {
            world.Objects.RemoveAll(sceneObject => string.Equals(sceneObject.Id, change.ObjectId, StringComparison.OrdinalIgnoreCase));
            return;
        }

        bool alreadyExists = world.Objects.Any(sceneObject => string.Equals(sceneObject.Id, change.ObjectId, StringComparison.OrdinalIgnoreCase));
        if (change.Kind != "add" || alreadyExists)
            return;
        var added = change.CreateObject();
        change.Transform.ApplyTo(added.Transform);
        added.InitialTransform = added.Transform.Clone();
        world.Objects.Add(added);
    }
}
