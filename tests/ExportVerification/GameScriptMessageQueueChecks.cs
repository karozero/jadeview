using JadeView;

internal static class GameScriptMessageQueueChecks
{
    internal static void Verify()
    {
        VerifyIrisCapacityAndLifoRead();
        VerifyIrisIdReadSwapsWithNewest();
        VerifyJadedCapacityAndStableIdRemoval();
        VerifyMissingStorageAndClear();
        Console.WriteLine("PASS: Iris/Jaded object-message queue capacity, LIFO reads, ID removal and clear semantics");
    }

    private static void VerifyIrisCapacityAndLifoRead()
    {
        var queue = new GameScriptMessageQueue(GameScriptMessageQueueEngine.IrisPc);
        for (int messageId = 1; messageId <= GameScriptMessageQueue.IrisCapacity; messageId++)
            Require(queue.Send(Message(messageId, messageId)) == GameScriptMessageSendResult.Enqueued,
                "Iris should append messages while its five-slot queue has space.");

        GameScriptMessageSendResult overflow = queue.Send(Message(6, 6));
        CinematicTimelineMessage? newest = queue.GetNewest();
        Require(overflow == GameScriptMessageSendResult.DroppedAtCapacity
            && queue.Count == 4
            && newest?.Type == 5
            && queue.Read(0)?.Type == 1,
            "Iris should silently drop a sixth queued message and MSGGet should pop the newest entry.");
    }

    private static void VerifyIrisIdReadSwapsWithNewest()
    {
        var queue = new GameScriptMessageQueue(GameScriptMessageQueueEngine.IrisPc);
        queue.Send(Message(30, 1));
        queue.Send(Message(53, 2));
        queue.Send(Message(47, 4));

        CinematicTimelineMessage? matched = queue.GetById(30);
        Require(matched?.Type == 30
            && queue.Count == 2
            && queue.Read(0)?.Type == 47
            && queue.Read(1)?.Type == 53
            && queue.TestInt1(53, 2)
            && queue.ReadById(47)?.Int1 == 4,
            "Iris MSGGetByID should remove the first matching entry by swapping the newest into its slot; reads/tests must not mutate the queue.");
    }

    private static void VerifyJadedCapacityAndStableIdRemoval()
    {
        var queue = new GameScriptMessageQueue(GameScriptMessageQueueEngine.Jaded);
        queue.Send(Message(30, 1));
        queue.Send(Message(53, 2));
        queue.Send(Message(47, 4));

        CinematicTimelineMessage? matched = queue.GetById(30);
        Require(queue.Capacity == GameScriptMessageQueue.JadedCapacity
            && matched?.Type == 30
            && queue.Count == 2
            && queue.Read(0)?.Type == 53
            && queue.Read(1)?.Type == 47
            && queue.GetNewest()?.Type == 47,
            "Jaded's linked queue should use its 1000-message limit and preserve remaining order when removing by ID.");
    }

    private static void VerifyMissingStorageAndClear()
    {
        var queue = new GameScriptMessageQueue(GameScriptMessageQueueEngine.IrisPc, hasMessageStorage: false);
        Require(queue.Send(Message(30, 1)) == GameScriptMessageSendResult.MissingMessageStorage
            && queue.Count == 0,
            "A send to an object without allocated message storage should not create a queue entry.");

        var allocatedQueue = new GameScriptMessageQueue(GameScriptMessageQueueEngine.Jaded);
        allocatedQueue.Send(Message(30, 1));
        allocatedQueue.Clear();
        Require(allocatedQueue.Count == 0 && allocatedQueue.GetNewest() == null,
            "MSGClear should empty the queue and subsequent reads should report no message.");
    }

    private static CinematicTimelineMessage Message(int type, int int1) => new(0, 0x100, 0, 0,
        default, default, 0, 0, int1, 0, type);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
