using JadeView;

internal static class GameScriptWorldListResolverChecks
{
    internal static void Verify()
    {
        const uint listKey = 0xFF005520;
        uint[] roots = [0, 0x71005968, uint.MaxValue, 0x71005968];
        var descriptor = new GameWorldListDescriptor(listKey, roots);
        var command = new GameScriptLiteralCommand(GameWorldUniverseReducer.LoadListSelector, [listKey]);
        var matched = GameScriptWorldListResolver.Resolve(command, descriptor);
        Require(matched.Status == GameScriptWorldListResolutionStatus.MetadataMatched
            && matched.RequestedKey == listKey
            && matched.Descriptor!.OrderedWorldRootKeys.SequenceEqual(roots),
            "A key match must retain duplicate roots, placeholders and their serialized order.");

        foreach (var unavailable in new GameWorldListDescriptor?[]
                 { null, new(null, roots), new(0xFF005833, roots) })
        {
            var result = GameScriptWorldListResolver.Resolve(command, unavailable);
            Require(result.Status == GameScriptWorldListResolutionStatus.DescriptorUnavailable
                && result.Descriptor == null && result.RequestedKey == listKey,
                "Absent, anonymous and differently keyed metadata must not resolve by root similarity.");
        }

        foreach (uint[] arguments in new uint[][] { [], [listKey, 1] })
            Require(GameScriptWorldListResolver.Resolve(command with { Arguments = arguments }, descriptor)
                    .Status == GameScriptWorldListResolutionStatus.InvalidArguments,
                "Only the source ABI's single literal argument is accepted.");
        foreach (uint reservedKey in new uint[] { 0, uint.MaxValue })
            Require(GameScriptWorldListResolver.Resolve(command with { Arguments = [reservedKey] },
                    new(reservedKey, roots)).Status == GameScriptWorldListResolutionStatus.InvalidKey,
                "Reserved keys must not become loadable metadata matches.");
        Require(GameScriptWorldListResolver.Resolve(command with { Selector = 0x1F40 }, descriptor)
                .Status == GameScriptWorldListResolutionStatus.NotWorldListCommand,
            "WORUniverseAdd must not be interpreted as WORLoadList.");
        Console.WriteLine("PASS: world-list command bindings require explicit identity and retain raw merge inputs");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
