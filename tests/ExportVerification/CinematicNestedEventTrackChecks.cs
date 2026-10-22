using JadeView;

internal static class CinematicNestedEventTrackChecks
{
    internal static void Verify()
    {
        var rootTrack = new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)
        {
            Calls = [(0, new(0x200, 0x3B64, [0]))]
        };
        var middleTrack = new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)
        {
            Calls = [(0.5f, new(0x300, 0x3B64, [1]))],
            SourceTrack = new AnimationTrack
            {
                Keys = [new AnimationKey { Position = System.Numerics.Vector3.Zero }]
            }
        };
        var cycleTrack = new CinematicEmbeddedEventTrack(1, 0, 0, 0, 0, 0)
        {
            Calls = [(1, new(0x100, 0x3B64, [0]))]
        };
        var rootList = List(0x100, 0x10, [rootTrack]);
        var middleList = List(0x200, 0x20, [middleTrack]);
        var leafList = List(0x300, 0x30, [
            new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0), cycleTrack]);
        var unrelatedEventList = List(0x200, 0x21,
            [new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)], linkName: "Events");

        CinematicNestedEventTrackGraph graph = CinematicNestedEventTrackResolver.Analyze(rootList,
            rootTrack, [rootList, middleList, leafList, unrelatedEventList]);
        if (graph.WasTruncated || graph.Routes.Length != 3
            || graph.Routes[0] is not { Depth: 1, IsResolved: true, IsCycle: false, TargetTrackIndex: 0 }
            || graph.Routes[1] is not { Depth: 2, IsResolved: true, IsCycle: false, TargetTrackIndex: 1 }
            || !graph.Routes[1].Path.Contains("transform keys: translation", StringComparison.Ordinal)
            || graph.Routes[2] is not { Depth: 3, IsResolved: true, IsCycle: true, TargetTrackIndex: 0 })
            throw new InvalidDataException("Nested TRAPlay links should resolve through SceneEvents and stop at a repeated channel.");

        var opaqueTarget = new CinematicEmbeddedEventTrack(0, uint.MaxValue, -1, 0x1234, 1, 0.5f)
        {
            SourceTrack = new AnimationTrack
            {
                ChannelFlags = 0x1234,
                Keys = [new AnimationKey
                {
                    Flags = 0x40,
                    OpaquePayloads = [new("word-pair-list", 0, [0, 0, 0, 0])]
                }]
            }
        };
        var opaqueRoot = new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)
        {
            Calls = [(0, new(0x600, 0x3B64, [0]))]
        };
        CinematicNestedEventTrackGraph opaqueGraph = CinematicNestedEventTrackResolver.Analyze(
            List(0x100, 0x10, [opaqueRoot]), opaqueRoot,
            [List(0x100, 0x10, [opaqueRoot]), List(0x600, 0x60, [opaqueTarget])]);
        string opaquePath = opaqueGraph.Routes.Single().Path;
        if (!opaquePath.Contains("key data: word-pair-list×1", StringComparison.Ordinal)
            || !opaquePath.Contains("channel 0x1234", StringComparison.Ordinal)
            || !opaquePath.Contains("key flags: 0x0040×1", StringComparison.Ordinal)
            || opaquePath.Contains("transform keys:", StringComparison.Ordinal))
            throw new InvalidDataException("Nested channel summaries must expose opaque key data without mislabeling it as transforms.");

        AnimationOpaquePayload floatCommandPayload = WordPairPayload(
            (0, 0x01000028), (0, 0x02000806), (0x41400000, 0x01000022),
            (0, 0x03001FAA), (0, 0x1F000000));
        CinematicLiteralCall? floatCommand = CinematicLiteralCall.Decode(floatCommandPayload,
            0xA9001DBB, 0xA9001DBB);
        if (floatCommand is not { Selector: 0x1FAA, Arguments.Length: 1 }
            || floatCommand.Arguments[0] != 0x41400000
            || !CinematicEngineFunctionCatalog.Describe(floatCommand)
                .Contains("12 radians", StringComparison.Ordinal))
            throw new InvalidDataException("Float-typed EVE arguments should decode and retain their serialized bits.");

        var ambiguousRoot = new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)
        {
            Calls = [(0, new(0x400, 0x3B64, [0]))]
        };
        var ambiguousList = List(0x101, 0x11, [ambiguousRoot]);
        var otherWorldOne = List(0x400, 0x40,
            [new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)], worldKey: 0x101);
        var otherWorldTwo = List(0x400, 0x41,
            [new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)], worldKey: 0x102);
        CinematicNestedEventTrackGraph ambiguousGraph = CinematicNestedEventTrackResolver.Analyze(
            ambiguousList, ambiguousRoot, [ambiguousList, otherWorldOne, otherWorldTwo]);
        if (ambiguousGraph.Routes.Length != 1 || ambiguousGraph.Routes[0].IsResolved
            || ambiguousGraph.Routes[0].Limitation?.Contains("multiple source worlds", StringComparison.Ordinal) != true)
            throw new InvalidDataException("Nested EVE traversal must preserve ambiguous cross-world owners as unresolved.");

        var allTracksRoot = new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0)
        {
            Calls = [(0, new(0x500, 0x3B60, []))]
        };
        var allTracksSource = List(0x100, 0x12, [allTracksRoot]);
        var allTracksTarget = List(0x500, 0x50,
            [new CinematicEmbeddedEventTrack(0, 0, 0, 0, 0, 0),
             new CinematicEmbeddedEventTrack(1, 0, 0, 0, 0, 0)]);
        CinematicNestedEventTrackGraph allTracksGraph = CinematicNestedEventTrackResolver.Analyze(
            allTracksSource, allTracksRoot, [allTracksSource, allTracksTarget]);
        if (allTracksGraph.Routes.Length != 2
            || allTracksGraph.Routes.Any(route => !route.IsResolved)
            || !allTracksGraph.Routes.Select(route => route.TargetTrackIndex).Order().SequenceEqual([0, 1]))
            throw new InvalidDataException("TRAPlayAll should expose every decoded channel without inventing order or runtime execution.");

        Console.WriteLine("PASS: nested SceneEvents call chains resolve by world/key, exclude Events and stop on cycles/ambiguity");
    }

    private static CinematicEmbeddedEventTrackList List(uint ownerKey, uint eventKey,
        CinematicEmbeddedEventTrack[] tracks, string linkName = "SceneEvents", uint worldKey = 0x10) =>
        new(ownerKey, $"owner {ownerKey:X8}", worldKey, linkName, eventKey, tracks);

    private static AnimationOpaquePayload WordPairPayload(params (uint Value, uint Tag)[] rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)rows.Length);
        foreach (var row in rows)
        {
            writer.Write(row.Value);
            writer.Write(row.Tag);
        }
        return new("word-pair-list", 0, stream.ToArray());
    }
}
