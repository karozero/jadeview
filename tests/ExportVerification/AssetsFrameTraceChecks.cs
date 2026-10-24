using System.Reflection;
using JadeView;

internal static class AssetsFrameTraceChecks
{
    internal static void Run()
    {
        string? previous = Environment.GetEnvironmentVariable("JADEVIEW_ASSETS_FRAME_TRACE");
        try
        {
            Environment.SetEnvironmentVariable("JADEVIEW_ASSETS_FRAME_TRACE", null);
            if (AssetsFrameTrace.Create() != null) throw new Exception("Trace must default to disabled.");
            Environment.SetEnvironmentVariable("JADEVIEW_ASSETS_FRAME_TRACE", "1");
            var trace = AssetsFrameTrace.Create() ?? throw new Exception("Trace did not opt in.");
            var append = typeof(AssetsFrameTrace).GetMethod("Append", BindingFlags.NonPublic | BindingFlags.Static)!;
            var rows = new Queue<string>();
            for (int index = 0; index < 350; index++) append.Invoke(null, [rows, $"frame-{index}", 300]);
            if (rows.Count != 300 || rows.Peek() != "frame-50" || rows.Last() != "frame-349")
                throw new Exception("Recent trace ring did not retain exactly the latest 300 frames.");
            var slow = new Queue<string>();
            for (int index = 0; index < 50; index++) append.Invoke(null, [slow, $"slow-{index}", 32]);
            if (slow.Count != 32 || slow.Peek() != "slow-18") throw new Exception("Slow trace ring is not bounded.");
            trace.Save();
            string path = Path.Combine(AppContext.BaseDirectory, "artifacts", $"assets-frame-trace-{Environment.ProcessId}.tsv");
            string log = File.ReadAllText(path);
            if (!log.Contains("GPU synchronization=none") || !log.Contains("presentMs")
                || !log.Contains("ownerHotspots") || !log.Contains("SLOW FRAMES"))
                throw new Exception("Trace metadata/columns were not saved.");
            Console.WriteLine($"PASS Assets trace opt-in, bounded rings and log persistence: {path}");
        }
        finally { Environment.SetEnvironmentVariable("JADEVIEW_ASSETS_FRAME_TRACE", previous); }
    }
}
