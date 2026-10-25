using JadeView;

internal static class CinematicEngineFunctionCatalogChecks
{
    internal static void Verify()
    {
        CinematicEngineFunctionSelectorEvidence? invisibleFlagEvidence =
            CinematicEngineFunctionCatalog.SourceEvidenceFor(0x1FD8);
        if (invisibleFlagEvidence is not { Status: "both-same-name" }
            || !invisibleFlagEvidence.IrisNames.Contains("OBJ_FlagInvisibleSet", StringComparer.Ordinal)
            || !invisibleFlagEvidence.JadedNames.Contains("OBJ_FlagInvisibleSet", StringComparer.Ordinal))
            throw new InvalidDataException("The invisible-flag selector must retain matching Iris and Jaded provenance.");

        CinematicEngineFunctionSelectorEvidence? positionEvidence =
            CinematicEngineFunctionCatalog.SourceEvidenceFor(0x1FFB);
        if (positionEvidence is not { Status: "both-name-divergence" }
            || !positionEvidence.IrisNames.Contains("OBJSetPos", StringComparer.Ordinal)
            || !positionEvidence.JadedNames.Contains("OBJ_PosSet_Teleport", StringComparer.Ordinal)
            || CinematicEngineFunctionCatalog.NameFor(0x1FFB) != "OBJSetPos")
            throw new InvalidDataException("The PC selector name must follow Iris and preserve Jaded's divergent label as provenance.");

        CinematicEngineFunctionSelectorEvidence? jadedOnlyEvidence =
            CinematicEngineFunctionCatalog.SourceEvidenceFor(0x36B6);
        if (jadedOnlyEvidence is not { Status: "jaded-only" }
            || jadedOnlyEvidence.IrisNames.Length != 0
            || !jadedOnlyEvidence.JadedNames.Contains("NET_Pushi", StringComparer.Ordinal))
            throw new InvalidDataException("Jaded-only selector evidence must not be presented as Iris-confirmed.");

        Check(0x2C26, [0, 1], "target collision-map activation bits 0x0, clear 0x1");
        Check(0x1F4D, [3, 0xFF804020], "world-table slot 3 ambient color 1 to 0xFF804020");
        Check(0x1F55, [3, 0xFF102030], "world-table slot 3 ambient color 2 to 0xFF102030");
        Check(0x213B, [2, 0], "clear light flag bit 2");
        Check(0x32CB, [1, 150], "animation track 1 time to frame 150 (2.5 seconds");
        Check(0x3B6D, [BitConverter.SingleToUInt32Bits(4.5f)],
            "seek every embedded event track to 4.5 seconds without changing run or pause state");
        Check(0x2F3F, [uint.MaxValue], "prefetch sound resources for all sound modifiers");
        Check(0x2F3F, [10], "prefetch sound resources for sound modifier 10");
        Check(0x30D4, [0], "assign target object to world view slot 0");
        Check(0x1FB2, [0], "reinitialize target (mode 0) and reattach it to the current world");
        Check(0x1F40, [0xFF005833], "load world key 0xFF005833, initialize and activate its objects");
        Check(0x1F41, [3], "make loaded universe world slot 3 active");
        Check(0x1F42, [3], "clear the active flag on loaded universe world slot 3");
        Check(0x1F43, [3, 12], "object-table slot 12 in loaded universe world slot 3");
        Check(0x1F4A, [3, 0x10], "activated-object table in universe world slot 0x00000003");
        Check(0x1F4B, [3, 0x10], "restores active bit 0x1 on every valid object");
        Check(0x1F45, [3, 0xA5], "delete loaded universe world slot 3");
        Check(0x1F46, [0xFF005833], "replace the current world universe with world-list key 0xFF005833");
        Check(0x1F48, [3], "rebuild visibility tables for loaded universe world slot 3");
        Check(0x1F51, [], "return 1 (PC Iris stub");
        Check(0x1F52, [], "current execution object's world");
        if (!CinematicWorldCommandResolver.IsWorldManagementOperation("WORLoadList")
            || !CinematicWorldCommandResolver.DescribeSourceOperation("WORLoadList")
                .Contains("clears the current universe", StringComparison.Ordinal)
            || CinematicWorldCommandResolver.IsWorldManagementOperation("OBJDestroy"))
            throw new InvalidOperationException("Source callback API summaries must classify and describe only known world-management calls.");
        Check(0x3CF4, [1], "Iris stub consumes value 1 and emits a debug trace");
        Check(0x1FD7, [0x200], "rotate target to face object 00000200 while preserving target position");
        Check(0x1FAA, [0x41400000], "rotate target around the local Y axis by 12 radians");
        Check(0x1FDC, [0x200], "attach target under object 00000200, retaining its current world transform");
        Check(0x1FDD, [], "clear target's parent-hierarchy binding");
        Check(0x1F76, [47, 2], "queue extended message 47 to the target script owner with float payload 2");
        uint[] messageSendAllArguments = [46, 0x201, 0x202, BitConverter.SingleToUInt32Bits(1),
            BitConverter.SingleToUInt32Bits(2), BitConverter.SingleToUInt32Bits(3),
            BitConverter.SingleToUInt32Bits(4), BitConverter.SingleToUInt32Bits(5),
            BitConverter.SingleToUInt32Bits(6), BitConverter.SingleToUInt32Bits(7),
            BitConverter.SingleToUInt32Bits(8), 4, 9];
        Check(0x1F78, messageSendAllArguments,
            "enqueue message 46 on the current execution object's queue");
        uint[] messageSendAllOwnerArguments = [47, 0x202, BitConverter.SingleToUInt32Bits(1),
            BitConverter.SingleToUInt32Bits(2), BitConverter.SingleToUInt32Bits(3),
            BitConverter.SingleToUInt32Bits(4), BitConverter.SingleToUInt32Bits(5),
            BitConverter.SingleToUInt32Bits(6), BitConverter.SingleToUInt32Bits(7),
            BitConverter.SingleToUInt32Bits(8), 4, 9];
        var sendAllCall = new CinematicLiteralCall(0x12345678, 0x1F78, messageSendAllArguments);
        var sendAllOwnerCall = new CinematicLiteralCall(0x12345678, 0x1F7D, messageSendAllOwnerArguments);
        var sendExCall = new CinematicLiteralCall(0x12345678, 0x1F76, [48, 0x3F800000]);
        if (!CinematicEngineFunctionCatalog.TryReadMessageGateFields(sendAllCall,
                out int sendAllMessageId, out int sendAllInt1, out var sendAllDestination)
            || sendAllMessageId != 46 || sendAllInt1 != 4
            || sendAllDestination != CinematicMessageQueueDestination.CurrentExecutionObject
            || !CinematicEngineFunctionCatalog.TryReadMessageGateFields(sendAllOwnerCall,
                out int sendAllOwnerMessageId, out int sendAllOwnerInt1, out var ownerDestination)
            || sendAllOwnerMessageId != 47 || sendAllOwnerInt1 != 4
            || ownerDestination != CinematicMessageQueueDestination.RegisteredOwner
            || !CinematicEngineFunctionCatalog.TryReadMessageGateFields(sendExCall,
                out int sendExMessageId, out int sendExInt1, out var sendExDestination)
            || sendExMessageId != 48 || sendExInt1 != 0
            || sendExDestination != CinematicMessageQueueDestination.CurrentExecutionObject
            || CinematicEngineFunctionCatalog.TryReadMessageGateFields(
                new(1, 0x1F7D, [47, 0x202]), out _, out _, out _))
            throw new InvalidOperationException("Message-gate decoding must centralize supported ID/Int1 layouts and distinguish the registered-owner queue.");
        var startupQueueRegistration = CinematicScriptGlobalQueueCatalog.StartupRegistration;
        if (startupQueueRegistration is not
                { QueueSymbol: "MSG_gpo_Owner", RegistrationApi: "MSGRegOwner",
                    StartupOwnerName: "GST_Sound_Manager", StartupOwnerKey: 0x7100F33F,
                    StartupSource: "ai/ai_bge/ai_compiledscripts_global.c", StartupLine: 356,
                    WorldTeardownSource: "world.c", WorldTeardownLine: 2755 })
            throw new InvalidOperationException("The source-evidence queue catalog must retain Iris startup registration and teardown provenance.");
        Check(0x1F7D, messageSendAllOwnerArguments,
            "enqueue message 47 on the registered MSG owner queue");
        Check(0x1F7E, [],
            "register target as the global MSG_gpo_Owner message queue, replacing the previous owner");
        string messageSendAllDescription = CinematicEngineFunctionCatalog.Describe(
            new(0x12345678, 0x1F78, messageSendAllArguments));
        if (!messageSendAllDescription.Contains("vectors (1, 2, 3)/(4, 5, 6)", StringComparison.Ordinal)
            || !messageSendAllDescription.Contains("f2 7, f1 8, Int1 4, Int2 9", StringComparison.Ordinal))
            throw new InvalidOperationException("MSGSendAll fields must retain native vector, float and integer argument order.");
        Check(0x2714, [], "enable target dynamics and register it for the world's dynamics update");
        Check(0x2715, [], "disable target dynamics and remove it from the world's dynamics update");
        Check(0x3BC6, [0xFFFF, uint.MaxValue], "reset visibility suppression on target group and all sections");
        Check(0x1B61, [0xFF], "replace the global joystick mask with bitwise complement of 0x000000FF");
        Check(0x1FED, [8, 1], "freeze target modifier type 8");
        Check(0x2135, [0xFF804020], "set target light packed color to 0xFF804020");
        Check(0x32D5, [3, 1], "request shape 1 on target animation channel 3");
        Check(0x1FDA, [0x200], "move target to the absolute position of object 00000200");
        CheckObjectArguments(0x1FD7, [0x200]);
        CheckObjectArguments(0x1FDC, [0x200]);
        CheckObjectArguments(0x1FDA, [0x200]);
        CheckObjectArguments(0x1FDB, [0x200]);
        CheckObjectArguments(0x20D0, [0x200, BitConverter.SingleToUInt32Bits(3)]);
        CheckObjectArguments(0x2108, [0x200]);
        CheckObjectArguments(0x2109, [0x200]);
        if (!CinematicEngineFunctionCatalog.GameObjectArgumentIndices(new CinematicLiteralCall(1, 0x1F78,
                [46, 0x201, 0x202, 1, 2, 3, 4, 5, 6, 7, 8, 4, 9]))
            .SequenceEqual([1, 2]))
            throw new InvalidOperationException("MSGSendAll must expose both serialized GAO payload fields as references.");
        if (!CinematicEngineFunctionCatalog.GameObjectArgumentIndices(new CinematicLiteralCall(1, 0x1F7D,
                messageSendAllOwnerArguments)).SequenceEqual([1]))
            throw new InvalidOperationException("MSGSendAllOwner must expose its serialized secondary GAO field as a reference.");
        if (CinematicEngineFunctionCatalog.GameObjectArgumentIndices(new(1, 0x2135, [0x200])).Length != 0)
            throw new InvalidOperationException("Packed colors must not be exposed as object references.");
        Check(0x3BC7, [2, 1], "Iris stub consumes section arguments 2 and 1");
        Check(0x2F36, [], "use target object as the global microphone-position source");
        Check(0x2EF2, [4, 0x800], "request sound slot 4 on target with flags 0x00000800; Iris also starts playback immediately");
        Check(0x3AA5, [0x1234, 1, 2, 1], "multi-texture material 00001234");
        Check(0x1FE1, [0x40, 0x20], "add identity flags 0x40, remove 0x20");
        Check(0x1FC7, [0x4100], "test whether target flags2 bits 8–15 intersect mask 0x4100");
        Check(0x2713, [0x100, 0x20], "add dynamics flags 0x100, remove 0x20");
        Check(0x3D54, [uint.MaxValue, 1], "activate matching XMEN modifiers on target and its skeleton objects (all user IDs; Jaded source behavior)");
        string particleShift = CinematicEngineFunctionCatalog.Describe(new(1, 0x210A,
            [BitConverter.SingleToUInt32Bits(1.25f), BitConverter.SingleToUInt32Bits(-2), 0]));
        if (!particleShift.Contains("(1.25, -2, 0)", StringComparison.Ordinal)
            || !particleShift.Contains("existing PAG particle positions", StringComparison.Ordinal))
            throw new InvalidOperationException("PAG_ShiftParticles must describe its serialized vector and live-particle scope.");
        Check(0x2EF6, [2, BitConverter.SingleToUInt32Bits(-1)],
            "play target sound-bank slot 2 at default volume");
        Check(0x2F47, [3, BitConverter.SingleToUInt32Bits(.5f), BitConverter.SingleToUInt32Bits(.9f),
            BitConverter.SingleToUInt32Bits(1.1f)], "play target sound-bank slot 3 at volume 0.5 with 0.9–1.1 frequency multiplier");
        Check(0x203D, [2, BitConverter.SingleToUInt32Bits(.75f)], "set morph channel 2 blend factor to 0.75");
        Check(0x203F, [1, BitConverter.SingleToUInt32Bits(.25f)], "set morph channel 1 progression to 0.25");
        Check(0x1FBE, [BitConverter.SingleToUInt32Bits(1), BitConverter.SingleToUInt32Bits(.5f),
            BitConverter.SingleToUInt32Bits(2)], "replace target matrix scale with (1, 0.5, 2)");
        Check(0x1FC0, [BitConverter.SingleToUInt32Bits(.6f)], "replace target matrix scale with uniform zoom 0.6");
        Check(0x3CF0, [7, 1, 0, BitConverter.SingleToUInt32Bits(.2f)],
            "enable remanence, factor 0.2");
        Check(0x3CF0, [0, 1, 2, BitConverter.SingleToUInt32Bits(.3f)],
            "enable depth blur, variant 2, factor 0.3");
        Check(0x3CF0, [1, 0, 0, BitConverter.SingleToUInt32Bits(.4f)],
            "disable motion blur, factor 0.4");
        Check(0x3CF0, [2, 1, 0, BitConverter.SingleToUInt32Bits(.5f)],
            "enable screen blur, factor 0.5");
        Check(0x3CF0, [4, 1, 0, 0], "enable motion smoothing");
        Check(0x3CF0, [5, 1, 0, BitConverter.SingleToUInt32Bits(.6f)],
            "enable zoom smoothing, factor 0.6");
        Check(0x3CF0, [6, 1, 0, BitConverter.SingleToUInt32Bits(.7f)],
            "enable secondary zoom smoothing, factor 0.7");
        Check(0x3CF0, [8, 1, 0, BitConverter.SingleToUInt32Bits(.45f)],
            "Jaded's Xenon path names mode 8 brightness");
        Check(0x3CF0, [9, 1, 0, BitConverter.SingleToUInt32Bits(.55f)],
            "PC Iris's color-balance parameter");
        Check(0x3CF0, [11, 1, 0, BitConverter.SingleToUInt32Bits(1)],
            "enable black-and-white effect, factor 1");
        Check(0x3CF0, [12, 1, 1, BitConverter.SingleToUInt32Bits(.8f)],
            "enable texture-factor effect, variant 1, factor 0.8");
        Check(0x3CF0, [14, 1, 0, BitConverter.SingleToUInt32Bits(.9f)],
            "enable depth-blur near plane, value 0.9");
        Check(0x3CF0, [15, 0, 0, BitConverter.SingleToUInt32Bits(.3f)],
            "disable global screen-opacity fade, rate 0.3");
        Check(0x3C3D, [1, BitConverter.SingleToUInt32Bits(2.5f), BitConverter.SingleToUInt32Bits(18)],
            "request enable depth blur (Z range 2.5 to 18); Jaded routes this command only to PS2/GameCube backends, with no PC implementation");
        Check(0x2716, [BitConverter.SingleToUInt32Bits(0), BitConverter.SingleToUInt32Bits(-9.81f),
            BitConverter.SingleToUInt32Bits(0)],
            "set the current execution object's dynamics gravity vector to (0, -9.81, 0); the object needs an allocated dynamics force block");
        Check(0x1FB6, [BitConverter.SingleToUInt32Bits(.2f), BitConverter.SingleToUInt32Bits(-.8f),
            BitConverter.SingleToUInt32Bits(0)],
            "orient the current execution object toward sight vector (0.2, -0.8, 0), deriving roll from its current matrix");

        string incomplete = CinematicEngineFunctionCatalog.Describe(new(1, 0x2C26, [1]));
        if (incomplete.Length != 0)
            throw new InvalidOperationException("A command with missing required arguments must remain undescribed.");
        foreach ((int selector, uint[] arguments) in new[]
        {
            (0x1F40, Array.Empty<uint>()), (0x1F41, Array.Empty<uint>()),
            (0x1F42, Array.Empty<uint>()), (0x1F43, new uint[] { 3 }),
            (0x1F4A, new uint[] { 3 }), (0x1F4B, new uint[] { 3 }),
            (0x1F45, new uint[] { 3 }), (0x1F46, Array.Empty<uint>()),
            (0x1F48, Array.Empty<uint>())
        })
        {
            if (CinematicEngineFunctionCatalog.Describe(new(1, selector, arguments)).Length != 0)
                throw new InvalidOperationException(
                    $"World command {CinematicEngineFunctionCatalog.NameFor(selector)} must remain undescribed when required arguments are missing.");
        }
        if (CinematicEngineFunctionCatalog.Describe(new(1, 0x1F78, [46, 0x201])).Length != 0
            || CinematicEngineFunctionCatalog.Describe(new(1, 0x1F7D, [46, 0x201])).Length != 0)
            throw new InvalidOperationException("Malformed full-message calls must remain undescribed rather than guessing field layout.");
        string nonFiniteRotation = CinematicEngineFunctionCatalog.Describe(new(1, 0x1FAA, [0x7FC00000]));
        if (!nonFiniteRotation.Contains("non-finite serialized angle 0x7FC00000", StringComparison.Ordinal))
            throw new InvalidOperationException("Non-finite serialized angles must stay visible as raw data.");
        if (CinematicEngineFunctionCatalog.Describe(new(1, 0x3CF0, [7, 1, 0])).Length != 0
            || CinematicEngineFunctionCatalog.Describe(new(1, 0x3CF0,
                [7, 1, 0, 0x7FC00000])).Length != 0
            || CinematicEngineFunctionCatalog.Describe(new(1, 0x3C3D,
                [1, BitConverter.SingleToUInt32Bits(2.5f)])).Length != 0
            || CinematicEngineFunctionCatalog.Describe(new(1, 0x2716,
                [BitConverter.SingleToUInt32Bits(0), BitConverter.SingleToUInt32Bits(-9.81f)])).Length != 0
            || CinematicEngineFunctionCatalog.Describe(new(1, 0x1FB6,
                [BitConverter.SingleToUInt32Bits(.2f), BitConverter.SingleToUInt32Bits(-.8f)])).Length != 0)
            throw new InvalidOperationException("Malformed After Effects and depth-blur calls must remain undescribed.");

        Console.WriteLine("PASS: source-confirmed cinematic engine command descriptions and argument guards");
    }

    private static void Check(int selector, uint[] arguments, string expectedText)
    {
        string description = CinematicEngineFunctionCatalog.Describe(new(0x12345678, selector, arguments));
        if (!description.Contains(expectedText, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Engine command {CinematicEngineFunctionCatalog.NameFor(selector)} was described as '{description}', "
                + $"expected text containing '{expectedText}'.");
    }

    private static void CheckObjectArguments(int selector, uint[] arguments)
    {
        int[] indices = CinematicEngineFunctionCatalog.GameObjectArgumentIndices(
            new(0x12345678, selector, arguments));
        if (!indices.SequenceEqual([0]))
            throw new InvalidOperationException(
                $"Engine command {CinematicEngineFunctionCatalog.NameFor(selector)} must expose its first object argument.");
    }
}
