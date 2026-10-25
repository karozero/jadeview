using JadeView;

internal static class CinematicEventFunctionControlFlowChecks
{
    internal static void Verify()
    {
        var track = new AnimationTrack
        {
            Keys =
            [
                new() { Time = 0, Flags = 0x0040 },
                new() { Time = 1, Flags = 0x0048 },
                new() { Time = 2, Flags = 0x0050 },
                new() { Time = 3, Flags = 0x0058 },
                new() { Time = 4, Flags = 0x0080 }
            ]
        };

        var eventList = new CinematicEmbeddedEventTrackList(100, "flow fixture", 200,
            "SceneEvents", 300, []);
        var channel = new CinematicEmbeddedEventTrack(3, 0, -1, 0, 0, 0)
        {
            SourceTrack = track
        };
        CinematicEventFunctionControlFlow[] flow =
            CinematicEventFunctionControlFlowReader.Read(eventList, channel);
        Check(flow.Length == 4
            && flow[0] is { KeyIndex: 0, WaitMode: CinematicEventFunctionWaitMode.None }
            && flow[1] is { KeyIndex: 1, WaitMode: CinematicEventFunctionWaitMode.WaitWhileTrue }
            && flow[2] is { KeyIndex: 2, WaitMode: CinematicEventFunctionWaitMode.WaitWhileFalse }
            && flow[3] is { KeyIndex: 3, WaitMode: CinematicEventFunctionWaitMode.WaitWhileTrue }
            && flow[1] is { DownstreamKeyCount: 3, FirstDownstreamKeyTime: 2, LastDownstreamKeyTime: 4 }
            && flow[3] is { DownstreamKeyCount: 1, FirstDownstreamKeyTime: 4, LastDownstreamKeyTime: 4 },
            "EVE AI-function event type or Iris wait/retry flags were decoded incorrectly.");

        track.Keys =
        [
            new() { Time = 0, Flags = 0x0049, OpaquePayloads = [CinematicTestEvents.Call(0, 0, 0x2918, 2165).OpaquePayloads[0]] },
            new() { Time = 1, Flags = 0x0051, OpaquePayloads = [CinematicTestEvents.Call(1, 0, 0x2918, 2165).OpaquePayloads[0]] },
            new() { Time = 2, Flags = 0x0049, OpaquePayloads =
                [CinematicTestEvents.Call(2, 0, 0x2918, 2165).OpaquePayloads[0],
                    CinematicTestEvents.Call(2, 0, 0x2918, 2166).OpaquePayloads[0]] },
            new() { Time = 3, Flags = 0x0049, OpaquePayloads =
                [CinematicTestEvents.Call(3, 0, 0x2918, 2165).OpaquePayloads[0],
                    new AnimationOpaquePayload("unknown", 0, [])] }
        ];
        channel = channel with { SourceTrack = track };
        flow = CinematicEventFunctionControlFlowReader.Read(eventList, channel);
        Check(flow[0].TestedGlobalFlag == 2165
            && flow[1].TestedGlobalFlag == 2165
            && flow[2].TestedGlobalFlag == null
            && flow[3].TestedGlobalFlag == null,
            "An EVE wait key should expose a global flag only when the entire key has one direct predicate payload.");

        Console.WriteLine("PASS: Iris EVE AI-function keys expose wait/retry control without evaluating runtime results");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
