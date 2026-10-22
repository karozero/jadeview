using System.Numerics;
using JadeView;

internal static class CinematicClipIncomingLinkChecks
{
    internal static void Verify()
    {
        const uint targetKey = 0xD9001234;
        var calls = new (float Time, CinematicLiteralCall Call)[]
        {
            (.25f, new(targetKey, 0x2F1A, [1])),
            (.5f, new(0xD9009999, 3, []))
        };
        var messages = new (float Time, CinematicTimelineMessage Message)[]
        {
            (1, new(1, targetKey, 0, 0, Vector3.Zero, Vector3.Zero, 0, 0, 46, 0, 55)),
            (2, new(2, 0xD9009999, targetKey, 0, Vector3.Zero, Vector3.Zero, 0, 0, 53, 0, 53)),
            (3, new(3, 0xD9009999, 0, targetKey, Vector3.Zero, Vector3.Zero, 0, 0, 60, 0, 60)),
            (4, new(4, 0xD9009999, 0, 0, Vector3.Zero, Vector3.Zero, 0, 0, 0, 0, 0))
        };

        var links = CinematicClipIncomingLinkResolver.Find(targetKey, calls, messages);
        if (links.Length != 4
            || links[0].Time != .25f || links[0].Description != "SNDPlayModifier target"
            || links[1].Time != 1 || links[1].Description != "type 55 EVE execution object"
            || links[2].Time != 2 || links[2].Description != "type 53 message GAO"
            || links[3].Time != 3 || links[3].Description != "type 60 message GAO2")
            throw new InvalidDataException("Selected-cinematic incoming links must resolve command targets and typed-message reference roles at their source times.");

        if (CinematicClipIncomingLinkResolver.Find(0, calls, messages).Length != 0
            || CinematicClipIncomingLinkResolver.Find(uint.MaxValue, calls, messages).Length != 0)
            throw new InvalidDataException("Null and invalid GAO keys must not create selected-cinematic incoming links.");
    }
}
