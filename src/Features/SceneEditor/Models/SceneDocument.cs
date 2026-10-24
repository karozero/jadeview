using System.Numerics;

namespace JadeView;

public sealed class SceneDocument
{
    public string BaseBfPath { get; set; } = "";
    public string BaseBfSha256 { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public GameWorldListDescriptor? WorldList { get; internal set; }
    public List<WorldData> Worlds { get; } = [];
    public Dictionary<uint, AnimationClip> CinematicClips { get; } = [];
    internal Dictionary<CinematicScriptOwnerId, CinematicScriptOwner> CinematicScriptOwners { get; } = [];
    internal Dictionary<CinematicScriptOwnerId, CinematicScriptEventTrack[]> CinematicScriptEventTracks { get; } = [];
    internal List<CinematicScriptMessageTrack> CinematicMessageTracks { get; } = [];
    internal Dictionary<(uint WorldKey, uint OwnerKey), CinematicEmbeddedEventTrackList[]> CinematicEmbeddedEventTracks { get; } = [];
    public Dictionary<uint, Dictionary<int, CinematicAction>> CinematicActionKits { get; } = [];
    internal Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>> CinematicActionKitsByOwner { get; } = [];
    public Dictionary<uint, uint[]> CinematicSoundBanks { get; } = [];
    public Dictionary<uint, CinematicShutterController> CinematicShutterControllers { get; } = [];
    public HashSet<uint> CinematicFadeReceivers { get; } = [];
    public Dictionary<CinematicScriptOwnerId, CinematicEnvironmentController> CinematicEnvironmentControllers { get; } = [];
    internal Dictionary<uint, CinematicShockwaveController> CinematicShockwaveControllers { get; } = [];
    internal Dictionary<uint, CinematicFlashController> CinematicFlashControllers { get; } = [];
    internal Dictionary<uint, CinematicLightningController> CinematicLightningControllers { get; } = [];
    public Dictionary<uint, ResourceItem> Resources { get; } = [];
    public List<string> Diagnostics { get; } = [];
    // Retain keys after deletion: the source stream and undo history still own them.
    internal HashSet<uint> ReservedResourceKeys { get; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal PcCollisionResourceCatalog? CollisionResources { get; set; }

    public uint AllocateObjectKey()
    {
        ReservedResourceKeys.UnionWith(Resources.Keys);
        ReservedResourceKeys.UnionWith(Worlds.SelectMany(w => w.Objects).Select(o => o.GaoKey));
        for (uint key = 0xFEFFFFFF; key > 0xFE000000; key--)
            if (ReservedResourceKeys.Add(key)) return key;
        throw new InvalidOperationException("No free key for a new scene object.");
    }

    public WorldData? ActiveWorld => Worlds.Count > 0 ? Worlds[0] : null;

    public SceneObject? FindObjectById(string id)
    {
        foreach (var w in Worlds)
        {
            var obj = w.Objects.FirstOrDefault(o => o.Id == id);
            if (obj != null) return obj;
        }
        return null;
    }
}

