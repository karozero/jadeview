namespace JadeView;

internal sealed class CinematicShutterTimeline
{
    private const uint RequestPause = 0x100, RequestIris = 0x200;
    private readonly List<(float Start, CinematicShutterController Profile)> triggers = [];
    internal int TriggerCount => triggers.Count;
    internal float[] TriggerTimes => triggers.Select(trigger => trigger.Start).ToArray();

    internal CinematicShutterTimeline(AnimationClip source, WorldData world, SceneDocument document)
    {
        var calls = CinematicLiteralCall.ReadTimeline(source);
        foreach (var (key, profile) in document.CinematicShutterControllers)
        {
            var owner = world.Objects.FirstOrDefault(item => item.GaoKey == key);
            if (owner == null) continue;
            uint flags = owner.ControlFlags;
            uint capabilities = profile.InitialCapabilities;
            bool hasTriggered = false;
            Trigger(0);
            foreach (var item in calls.Where(item => item.Call.Target == key))
            {
                var call = item.Call;
                if (call.Selector == 8133 && call.Arguments.Length == 2)
                    capabilities = (capabilities & ~(uint)(ushort)call.Arguments[1]) | (uint)(ushort)call.Arguments[0];
                else if (call.Selector == 8153 && call.Arguments.Length == 1)
                    flags = call.Arguments[0] != 0 ? flags | CinematicObjectState.Inactive : flags & ~CinematicObjectState.Inactive;
                else if (call.Selector == 8113 && call.Arguments.Length == 0)
                    flags |= CinematicObjectState.Inactive;
                else if (call.Selector == 8115 && call.Arguments.Length == 2)
                    flags = (flags & ~(call.Arguments[1] & 0xffff0000)) | (call.Arguments[0] & 0xffff0000);
                Trigger(item.Time);
            }

            void Trigger(float time)
            {
                bool active = (flags & CinematicObjectState.Inactive) == 0;
                bool requested = (capabilities & RequestIris) != 0
                    && ((capabilities & RequestPause) != 0 || hasTriggered);
                if (!active || !requested) return;
                triggers.Add((time, profile));
                hasTriggered = true;
                capabilities &= ~RequestIris;
            }
        }
        triggers.Sort((first, second) => first.Start.CompareTo(second.Start));
    }

    internal CinematicShutterState? Evaluate(float time)
    {
        foreach (var trigger in triggers.AsEnumerable().Reverse())
        {
            float elapsed = time - trigger.Start;
            var profile = trigger.Profile;
            if (elapsed < 0 || elapsed >= profile.CloseTime + profile.HoldTime + profile.OpenTime) continue;
            if (elapsed < profile.CloseTime)
                return new(elapsed / profile.CloseTime, profile.Center);
            elapsed -= profile.CloseTime;
            if (elapsed < profile.HoldTime) return new(1, profile.Center);
            elapsed -= profile.HoldTime;
            return new(1 - elapsed / profile.OpenTime, profile.Center);
        }
        return null;
    }
}
