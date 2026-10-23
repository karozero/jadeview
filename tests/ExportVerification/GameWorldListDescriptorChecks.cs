using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class GameWorldListDescriptorChecks
{
    private static readonly uint[] PlaceholderRoots = [0, uint.MaxValue, 0];

    internal static void Verify()
    {
        VerifyDescriptorCopiesRootsAndPreservesPlaceholders();
        VerifyPcLoaderRetainsRawWolRootsAndExplicitIdentity();
        VerifySceneLoaderDoesNotGuessOuterListIdentity();
        Console.WriteLine("PASS: WOL descriptor retains ordered raw roots separately from merged worlds");
    }

    private static void VerifyDescriptorCopiesRootsAndPreservesPlaceholders()
    {
        uint[] roots = [0, uint.MaxValue, 0xFF005520];
        var descriptor = new GameWorldListDescriptor(null, roots);
        roots[2] = 17;
        Require(descriptor.OuterListKey == null
            && descriptor.OrderedWorldRootKeys.SequenceEqual([0u, uint.MaxValue, 0xFF005520u]),
            "Descriptor must preserve its input snapshot, including placeholder keys.");
        if (descriptor.OrderedWorldRootKeys is IList<uint> list)
        {
            Require(list.IsReadOnly, "Descriptor root collection must be read-only.");
            try
            {
                list[0] = 1;
                throw new InvalidOperationException("Descriptor root collection accepted mutation.");
            }
            catch (NotSupportedException) { }
        }
    }

    private static void VerifyPcLoaderRetainsRawWolRootsAndExplicitIdentity()
    {
        WithSyntheticWol((bytes, fakeArchivePath) =>
        {
            var replay = PcSceneLoader.Read(bytes, fakeArchivePath, 0xFF005520);
            Require(replay.Complete, $"Synthetic WOL should parse: {replay.Error}");
            Require(replay.OuterWorldListKey == 0xFF005520
                && replay.OrderedWorldRootKeys.SequenceEqual(PlaceholderRoots),
                "PC loader must retain outer identity and serialized zero/MAX roots.");
            Require(replay.Worlds.Count == 0,
                "Placeholder roots are not loaded worlds or universe slots.");
        });
    }

    private static void VerifySceneLoaderDoesNotGuessOuterListIdentity()
    {
        WithSyntheticWol((bytes, fakeArchivePath) =>
        {
            var unknownIdentity = SceneLoader.Load(bytes, "FF005520.bf", fakeArchivePath);
            Require(unknownIdentity.WorldList?.OuterListKey == null,
                "Container name must not be parsed as an outer list key.");
            Require(unknownIdentity.WorldList?.OrderedWorldRootKeys.SequenceEqual(PlaceholderRoots) == true,
                "Scene document should expose raw roots without transforming them into loaded worlds.");

            var explicitIdentity = SceneLoader.Load(bytes, "renamed-container", fakeArchivePath,
                worldListKey: 0xFF005520);
            Require(explicitIdentity.WorldList?.OuterListKey == 0xFF005520,
                "Only an explicitly passed archive key should become the outer list identity.");
        });
    }

    private static void WithSyntheticWol(Action<byte[], string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"jadeview-wol-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string fakeArchivePath = Path.Combine(directory, "FF005520.bf");
        File.WriteAllBytes(Path.Combine(directory, "jade.spe"), []);
        try { action(CreateWol(PlaceholderRoots), fakeArchivePath); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] CreateWol(IEnumerable<uint> roots)
    {
        uint[] orderedRoots = roots.ToArray();
        byte[] bytes = new byte[4 + orderedRoots.Length * 8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, orderedRoots.Length * 8);
        for (int index = 0; index < orderedRoots.Length; index++)
        {
            int offset = 4 + index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), orderedRoots[index]);
            Encoding.ASCII.GetBytes(".wow", bytes.AsSpan(offset + 4, 4));
        }
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
