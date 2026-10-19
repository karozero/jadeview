using System.Text;

namespace JadeView;
internal sealed partial class PcSceneRebuild
{
    private void RebuildWorld(PcSceneLoader.Record world)
    {
        var edits = WorldChanges(world.Key);
        if (edits.Length == 0)
            return;
        var worldObjects = WorldObjects(world.Key);
        var group = replay.Records.SingleOrDefault(record => record.LoadWorldKey == world.Key && record.Key == world.Group && record.Kind == 'n') ?? throw new InvalidDataException($"World {world.Name} has no writable object group.");
        var keys = group.References.Select(reference => reference.Key).ToList();
        UpdateSectorVisibility(world, worldObjects, edits);
        foreach (var edit in edits)
        {
            switch (edit.Kind)
            {
                case "remove":
                    RemoveObject(edit, worldObjects, keys);
                    break;
                case "add":
                    AddObject(edit, world.Key, worldObjects);
                    keys.Add(edit.GaoKey);
                    break;
                default:
                    throw new InvalidDataException($"Unknown scene edit '{edit.Kind}'.");
            }
        }

        replacements.Add((world.Key, group.Key, 'n'), EncodeObjectList(keys));
    }

    private static void RemoveObject(SceneStructureChange edit, SceneObject[] worldObjects, List<uint> keys)
    {
        var sceneObject = worldObjects.FirstOrDefault(candidate => candidate.Id == edit.ObjectId) ?? throw new InvalidDataException($"Deleted object '{edit.Name}' no longer matches the source scene.");
        bool isIndependentMember = sceneObject.Actor == null && keys.Remove(sceneObject.GaoKey);
        if (!isIndependentMember)
            throw new InvalidDataException($"'{edit.Name}' is not an independent object in the world's primary group.");
    }

    private static byte[] EncodeObjectList(List<uint> keys)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, true);
        writer.Write(checked(keys.Count * 4));
        foreach (uint key in keys)
            writer.Write(key);
        return stream.ToArray();
    }
}
