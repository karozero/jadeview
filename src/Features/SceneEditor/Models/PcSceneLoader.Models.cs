namespace JadeView;

internal sealed partial class PcSceneLoader
{
    internal sealed record Request(uint Key, char Kind, bool Cache = true);
    internal sealed class Record(uint key, char kind, int offset, int size)
    {
        public uint Key = key;
        public char Kind = kind;
        public int Offset = offset, Size = size;
        public SourceRange FrameRange;
        public uint LoadWorldKey;
        public int NameFieldOffset;
        public int BrainFieldOffset = -1;
        public ushort InitialCapabilities;
        public uint Parent = uint.MaxValue, Prefab = uint.MaxValue;
        public uint Flags, Geometry, Material, Group, Events = uint.MaxValue, Actions = uint.MaxValue, Shape = uint.MaxValue, Skeleton = uint.MaxValue;
        public uint SoundBank = uint.MaxValue;
        public uint SerializedStateFlags;
        public uint SceneEvents = uint.MaxValue;
        public string Name = "";
        public List<Request> References = [];
        // Serialized GAO keys, including weak OVA references which do not enqueue a load.
        public List<int> GaoReferenceOffsets = [];
        public List<(uint Target, SourceRange Range)> AnimationChannels = [];
        public List<Record> WorldMembers = [];
        public List<(uint Type, int Offset, int Size)> Modifiers = [];
        public Dictionary<uint, Record> ActorResources = [];
    }

    internal sealed class Result
    {
        public List<Record> Records = [];
        public List<Record> Worlds = [];
        public Dictionary<uint, Record> Resources = [];
        public int End, SpeSkipped, CachedSkipped;
        public string? Error;
        public bool Complete => Error == null;
    }

    private sealed class WorldExpansion(List<Record> members)
    {
        internal List<Record> Members { get; } = members;
        internal HashSet<uint> MemberKeys { get; } = members.Select(member => member.Key).ToHashSet();
        internal HashSet<uint> Processing { get; } = [];

        internal void Add(Record member)
        {
            if (MemberKeys.Add(member.Key))
                Members.Add(member);
        }
    }

    private sealed class AnimationCompressionState
    {
        internal int Size;
        internal int Format;
    }

    private readonly record struct VariableDescriptor(uint PackedLength, ushort Type);
}
