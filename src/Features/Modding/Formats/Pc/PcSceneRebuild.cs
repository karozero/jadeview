using System.Buffers.Binary;

namespace JadeView;
/// <summary>One isolated build session; source bytes and the editor document stay unchanged.</summary>
internal sealed partial class PcSceneRebuild
{
    private readonly byte[] original;
    private readonly SceneDocument document;
    private readonly PcSceneLoader.Result replay;
    private readonly SceneDocument baseline;
    private readonly PcSceneSource local;
    private readonly List<PcSceneSource> sources;
    private readonly SceneStructureChange[] changes;
    private readonly Dictionary<string, int> pickupSaveFlags;
    private readonly Archive archive;
    private readonly HashSet<uint> allKeys;
    private readonly HashSet<uint> specialKeys;
    private readonly HashSet<string> loadedScopes;
    private ResourceResolver? resolver;
    private HashSet<uint> unownedKeys = [];
    private readonly Dictionary<(uint World, uint Key, char Kind), byte[]> replacements = [];
    private readonly Dictionary<(uint Key, char Kind), byte[]> detachedReferences = [];
    private readonly Dictionary<uint, byte[]> additions = [];
    private readonly Dictionary<uint, uint> geometryCopies = [];
    private readonly Dictionary<uint, uint> exportedGeometry = [];
    private readonly Dictionary<uint, (char Kind, byte[] Frame)> instanceCopies = [];
    private readonly Dictionary<uint, uint> exportedBrains = [];
    private readonly List<(uint World, uint Key)> disabledVisibilityControllers = [];
    internal PcSceneRebuild(byte[] original, SceneDocument document, ModProject project, ResourceResolver? resolver)
    {
        this.original = original;
        this.document = document;
        this.resolver = resolver;
        pickupSaveFlags = new(project.PickupSaveFlags);
        replay = PcSceneLoader.Read(original, document.BaseBfPath);
        if (!replay.Complete)
            throw new InvalidDataException("Structural editing requires a complete typed PC FF0 parse: " + replay.Error);
        baseline = SceneLoader.Load(original, document.ContainerName, document.BaseBfPath);
        var transformProject = new ModProject
        {
            Changes = project.Changes
        };
        var transformPlan = transformProject.BuildWritePlan(baseline, original);
        byte[] transformed = SceneWriter.CreatePatchedContainer(original, transformPlan);
        local = new PcSceneSource(transformed, replay);
        sources = [local];
        changes = project.SceneStructureChanges.Where(change => string.Equals(change.ContainerName, document.ContainerName, StringComparison.OrdinalIgnoreCase)).ToArray();
        bool hasAmbiguousWorld = changes.Any(change => replay.Worlds.Count(world => world.Key == change.WorldKey) > 1);
        if (hasAmbiguousWorld)
            throw new InvalidDataException("The edited world is loaded more than once in this container; its occurrence cannot be identified by this project's world key.");
        allKeys = replay.Records.Select(record => record.Key).ToHashSet();
        archive = new Archive(document.BaseBfPath);
        specialKeys = ReadSpecialKeys(document.BaseBfPath);
        loadedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            document.ContainerName
        };
    }

    internal byte[] Build()
    {
        unownedKeys = DetachRemovedObjects();
        foreach (var world in replay.Worlds)
            RebuildWorld(world);
        bool hasMissingWorld = changes.Any(change => !replay.Worlds.Any(world => world.Key == change.WorldKey));
        if (hasMissingWorld)
            throw new InvalidDataException("A structural edit refers to a world absent from the source container.");
        int headerSize = checked(4 + BinaryPrimitives.ReadInt32LittleEndian(original));
        byte[] header = original.AsSpan(0, headerSize).ToArray();
        byte[] output = PcSceneLoader.Rewrite(header, document.BaseBfPath, Supply);
        Verify(output);
        return output;
    }

    private SceneObject[] WorldObjects(uint worldKey) => baseline.Worlds.Where(world => world.Key == worldKey).SelectMany(world => world.Objects).ToArray();
    private SceneStructureChange[] WorldChanges(uint worldKey) => changes.Where(change => change.WorldKey == worldKey).ToArray();
    private PcSceneLoader.Record? ObjectRecord(SceneObject sceneObject) => replay.Records.FirstOrDefault(record => record.Kind == 'l' && record.Offset == sceneObject.RecordRange.Offset + 4);
}
