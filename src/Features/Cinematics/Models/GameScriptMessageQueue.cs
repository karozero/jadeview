namespace JadeView;

internal enum GameScriptMessageQueueEngine
{
    IrisPc,
    Jaded
}

internal enum GameScriptMessageSendResult
{
    Enqueued,
    DroppedAtCapacity,
    MissingMessageStorage
}

/// <summary>
/// Source-derived per-object message queue behavior. This is a reusable state reducer;
/// it does not imply that a script callback or queue operation ran in a particular preview.
/// </summary>
internal sealed class GameScriptMessageQueue
{
    internal const int IrisCapacity = 5;
    internal const int JadedCapacity = 1000;

    private readonly List<CinematicTimelineMessage> messages = [];

    internal GameScriptMessageQueue(GameScriptMessageQueueEngine engine, bool hasMessageStorage = true)
    {
        if (!Enum.IsDefined(engine)) throw new ArgumentOutOfRangeException(nameof(engine));
        Engine = engine;
        HasMessageStorage = hasMessageStorage;
    }

    internal GameScriptMessageQueueEngine Engine { get; }
    internal bool HasMessageStorage { get; }
    internal int Capacity => Engine == GameScriptMessageQueueEngine.IrisPc ? IrisCapacity : JadedCapacity;
    internal int Count => messages.Count;
    internal IReadOnlyList<CinematicTimelineMessage> Snapshot => messages.ToArray();

    internal GameScriptMessageSendResult Send(CinematicTimelineMessage message)
    {
        if (!HasMessageStorage) return GameScriptMessageSendResult.MissingMessageStorage;
        if (messages.Count >= Capacity) return GameScriptMessageSendResult.DroppedAtCapacity;
        messages.Add(message);
        return GameScriptMessageSendResult.Enqueued;
    }

    internal CinematicTimelineMessage? GetNewest()
    {
        if (messages.Count == 0) return null;
        int newestIndex = messages.Count - 1;
        CinematicTimelineMessage message = messages[newestIndex];
        messages.RemoveAt(newestIndex);
        return message;
    }

    internal CinematicTimelineMessage? GetById(int messageId)
    {
        int messageIndex = messages.FindIndex(message => message.Type == messageId);
        if (messageIndex < 0) return null;

        CinematicTimelineMessage matchedMessage = messages[messageIndex];
        if (Engine == GameScriptMessageQueueEngine.IrisPc)
        {
            int newestIndex = messages.Count - 1;
            messages[messageIndex] = messages[newestIndex];
            messages.RemoveAt(newestIndex);
        }
        else
        {
            messages.RemoveAt(messageIndex);
        }

        return matchedMessage;
    }

    internal CinematicTimelineMessage? Read(int index) =>
        index >= 0 && index < messages.Count ? messages[index] : null;

    internal CinematicTimelineMessage? ReadById(int messageId) =>
        messages.FirstOrDefault(message => message.Type == messageId);

    internal bool TestInt1(int messageId, int mask) =>
        messages.Any(message => message.Type == messageId && (mask & message.Int1) != 0);

    internal void Clear() => messages.Clear();
}
