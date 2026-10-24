using JadeView;

internal static class SceneRendererOwnerWorkChecks
{
    internal static void Verify()
    {
        var work = new SceneRendererOwnerWork();
        var firstOwner = new SceneObject { GaoKey = 0x1000, Name = "slow skin" };
        var secondOwner = new SceneObject { GaoKey = 0x1000, Name = "slow light\towner" };
        work.AddSkin(firstOwner, 3.5);
        work.AddLighting(firstOwner, 1.5);
        work.AddGeometry(secondOwner, 9);

        string summary = work.FormatHotspots(1);
        Require(summary.Contains("1000/slow light owner[geo=9.00,skin=0.00,light=0.00]ms", StringComparison.Ordinal),
            "Hotspots must sort by combined per-owner work and sanitize tab characters for TSV output.");

        work.Clear();
        Require(work.FormatHotspots() == "none",
            "A new performance frame must not retain owners from the previous frame.");
        Console.WriteLine("PASS: opt-in renderer owner hotspots sort per-instance skin/light timing and reset per frame");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
