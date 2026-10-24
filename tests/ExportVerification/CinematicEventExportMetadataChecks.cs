using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class CinematicEventExportMetadataChecks
{
    internal static void Verify(string outputDirectory)
    {
        var objectRoot = new SceneObject { Name = "Event target", GaoKey = 4 };
        var world = new WorldData();
        world.Objects.Add(objectRoot);
        var source = new AnimationClip
        {
            Key = 0x3500B8EA,
            Name = "Synthetic cinematic",
            Cinematic = true,
            Loaded = true,
            Duration = 2,
            CinematicOwnerKey = 42,
            Tracks =
            [
                new()
                {
                    Bone = -1,
                    Target = 4,
                    Keys = [new() { Time = 0, Duration = 2, Position = Vector3.Zero }]
                },
                new()
                {
                    Bone = -1,
                    Target = 0,
                    Keys =
                    [
                        CinematicTestEvents.Call(.5f, 4, 0x1fbe,
                            BitConverter.SingleToUInt32Bits(2),
                            BitConverter.SingleToUInt32Bits(2),
                            BitConverter.SingleToUInt32Bits(2)),
                        CinematicTestEvents.Message(.75f, 4, 30, Vector3.Zero, Vector3.Zero, 0, 0,
                            object1: 4),
                        new()
                        {
                            Time = 1,
                            OpaquePayloads = [new("word-pair-list", 64, [1, 2, 3, 4])]
                        },
                        new()
                        {
                            Time = 1.25f,
                            OpaquePayloads = [new("extra-transform-controls", 72, [5, 6, 7, 8])]
                        }
                    ]
                }
            ]
        };
        world.CinematicClips.Add(source.Key, source);

        var inventory = CinematicEventExportInventoryBuilder.Build(source);
        Require(inventory.RawRecordCount == 4, "Raw opaque record inventory is incomplete.");
        Require(inventory.DecodedRecordCount == 2 && inventory.UnknownRecordCount == 1
            && inventory.NonEventRecordCount == 1, "Decoded, unknown, and non-event inventory counts disagree.");
        Require(inventory.Events.Length == 2
            && inventory.Events[0].SourceRecordIndex == 0
            && inventory.Events[1].SourceRecordIndex == 1,
            "Semantic events must point at their exact raw source-record indices.");
        Require(inventory.Events[0].Selector == 0x1fbe
            && inventory.Events[0].FunctionName == "OBJ_ScaleSet"
            && inventory.Events[0].Arguments?.Length == 3,
            "Literal call metadata lost selector or argument words.");
        Require(inventory.Events[1].MessageType == 30
            && inventory.Events[1].ExecutionContextKey == 42
            && inventory.Events[1].Message?.Gao == 4
            && inventory.Events[1].Message?.Float1Bits == 0,
            "Message metadata lost its track owner or encoded GAO field.");
        Require(inventory.Events.All(item => item.Status == "decoded_not_executed_by_exporter"),
            "Decoded source metadata must not claim callbacks were executed.");

        var document = new SceneDocument();
        document.CinematicClips.Add(source.Key, source);
        string exportPath = Path.Combine(outputDirectory, "cinematic-event-export-metadata.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = source.Key,
            CinematicDocument = document
        });

        using var glb = new GlbReader(exportPath);
        JsonElement animation = glb.Root.GetProperty("animations")[0];
        JsonElement extras = animation.GetProperty("extras");
        JsonElement rawRecords = extras.GetProperty("jadeViewSourceRecords");
        JsonElement semanticEvents = extras.GetProperty("jadeViewCinematicEvents");
        Require(rawRecords.GetProperty("schemaVersion").GetInt32() == 2,
            "Raw source-record schema must expose stable record indices.");
        Require(rawRecords.GetProperty("records").GetArrayLength() == 4
            && rawRecords.GetProperty("records")[0].GetProperty("sourceRecordIndex").GetInt32() == 0,
            "GLB did not preserve indexed raw payloads.");
        Require(semanticEvents.GetProperty("rawRecordCount").GetInt32() == 4
            && semanticEvents.GetProperty("unknownRecordCount").GetInt32() == 1
            && semanticEvents.GetProperty("events")[1].GetProperty("sourceRecordIndex").GetInt32() == 1,
            "GLB semantic inventory or raw-record linkage is incorrect.");
        Require(semanticEvents.GetProperty("events")[0].GetProperty("status").GetString()
            == "decoded_not_executed_by_exporter",
            "GLB metadata must distinguish decoding from runtime execution.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
