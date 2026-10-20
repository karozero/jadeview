using JadeView;

internal static class CinematicTransitionChecks
{
    internal static void Verify()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(2); writer.Write(16); // Count and array granularity.
        writer.Write(1u); writer.Write(9u); // Requested action keys.
        writer.Write((ushort)51); writer.Write((byte)0); writer.Write((byte)10);
        writer.Write((ushort)9); writer.Write((byte)0x14); writer.Write((byte)0);
        writer.Write(0); writer.Write(16); // A present but empty second table.
        byte[] bytes = stream.ToArray();
        var tables = CinematicActionTransitions.Read(bytes, [true, false, true]);
        if (tables.Length != 3 || tables[1].Length != 0 || tables[2].Length != 0
            || tables[0][0].RequestedAction != 1 || tables[0][0].Action != 51
            || Math.Abs(tables[0][0].BlendDuration - 11 / 60f) > 1e-6f
            || tables[0][1].Flags != 0x14 || tables[0][1].BlendDuration != 0)
            throw new InvalidDataException("Action transition lookup, redirection or blend timing was lost.");
        Reject(bytes[..^1]);
        byte[] invalid = bytes.ToArray();
        BitConverter.GetBytes(int.MaxValue).CopyTo(invalid, 0);
        Reject(invalid);
        Reject([.. bytes, 0]);
        VerifyBridge(false);
        VerifyBridge(true);
        Console.WriteLine("PASS: native action transition tables, redirected actions, flags, blend duration and malformed bounds");
    }

    private static void VerifyBridge(bool interrupt)
    {
        var idle = Action(100);
        var walk = Action(101);
        var bridge = Action(102);
        idle = idle with
        {
            Items = [idle.Items[0] with
            { Transitions = [new(1, 2, 0, 10)] }]
        };
        var root = new SceneObject { GaoKey = 7 };
        var source = new AnimationClip
        {
            Loaded = true,
            Duration = 4,
            Tracks =
            [new() { Bone = -1, Target = 7, Keys = [Command(0, 0), Command(1, 1), Command(1.1f, 1)] }]
        };
        if (interrupt) source.Tracks[0].Keys.Add(Command(1.5f, 0));
        var timeline = CinematicBodyTimeline.Create(source, new SceneActor { Root = root, Members = [root] },
            new Dictionary<uint, Dictionary<int, CinematicAction>>
            {
                [7] = new()
                { [0] = idle, [1] = walk, [2] = bridge }
            });
        if (timeline.Sample(1.25f).Single().Track.HeaderWord != 102
            || timeline.Sample(2.25f).Single().Track.HeaderWord != (interrupt ? 100u : 101u)
            || timeline.Sample(.25f).Single().Track.HeaderWord != 100)
            throw new InvalidDataException("Source transition bridge, pending action cancellation or reverse seek failed.");
    }

    private static CinematicAction Action(uint key) => new(0, [new(new AnimationClip
    {
        Key = key, Loaded = true, Duration = 1,
        Tracks = [new() { Bone = 0, HeaderWord = key, Keys = [new() { Duration = 1, Position = System.Numerics.Vector3.Zero }] }]
    }, 0, 64, 1)]);

    internal static AnimationKey Command(float time, uint action)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(5);
        writer.Write(0u); writer.Write(0x01000028u);
        writer.Write(0u); writer.Write(0x02000806u);
        writer.Write(action); writer.Write(0x01000021u);
        writer.Write(0u); writer.Write(0x03003391u);
        writer.Write(0u); writer.Write(0x1F000000u);
        return new() { Time = time, OpaquePayloads = [new("word-pair-list", 0, stream.ToArray())] };
    }

    private static void Reject(byte[] bytes)
    {
        try { CinematicActionTransitions.Read(bytes, [true, false, true]); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Malformed action transition data was accepted.");
    }
}
