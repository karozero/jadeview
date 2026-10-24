using System.Globalization;

namespace JadeView;

internal sealed partial class CinematicGameScriptMoveTimeline
{
    private const float CommandTimestampToleranceSeconds = 1e-5f;

    internal CinematicGameScriptMoveTimeline(WorldData world,
        IEnumerable<GameScriptMoveSegment> segments,
        IEnumerable<(float Time, CinematicLiteralCall Call)> sourceCalls,
        IReadOnlySet<uint>? absolutePositionTrackTargets = null,
        IEnumerable<string>? sourceDiagnostics = null)
    {
        var objectsByKey = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var calls = sourceCalls.Where(item => float.IsFinite(item.Time)).ToArray();
        var resetTimes = calls
            .Where(item => IsPositionReset(item.Call))
            .GroupBy(item => item.Call.Target)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Time).Distinct().Order().ToArray());
        var issues = new List<string>();
        var accepted = new Dictionary<SceneObject, List<GameScriptMoveSegment>>();
        var rejectedTargets = new HashSet<uint>();

        foreach (var segment in segments)
        {
            uint targetKey = segment.Command.TargetKey;
            if (!objectsByKey.TryGetValue(targetKey, out var matches) || matches.Length != 1)
            {
                AddIssue(targetKey, "target is missing or ambiguous");
                continue;
            }

            SceneObject target = matches[0];
            string? unsupportedReason = UnsupportedTargetReason(target, absolutePositionTrackTargets);
            if (unsupportedReason != null)
            {
                AddIssue(targetKey, unsupportedReason);
                continue;
            }

            if (!accepted.TryGetValue(target, out var targetSegments))
                accepted.Add(target, targetSegments = []);
            targetSegments.Add(segment);
        }

        foreach (var (target, targetSegments) in accepted.ToArray())
        {
            float[] targetResets = resetTimes.GetValueOrDefault(target.GaoKey, []);
            bool sameTimeReset = targetSegments.Any(segment => targetResets.Any(resetTime =>
                ConflictsWithReset(segment, resetTime)));
            if (!sameTimeReset) continue;

            accepted.Remove(target);
            AddIssue(target.GaoKey,
                "OBJMove and an absolute position setter share a timestamp; source execution order is unresolved");
        }

        segmentsByTarget = accepted.ToDictionary(pair => pair.Key,
            pair => pair.Value.OrderBy(segment => segment.TimelineStartSeconds).ToArray());
        resetTimesByTarget = segmentsByTarget.Keys.ToDictionary(target => target,
            target => resetTimes.GetValueOrDefault(target.GaoKey, []));
        targets = segmentsByTarget.Keys.ToArray();
        diagnostics = issues.Concat(sourceDiagnostics ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();

        void AddIssue(uint targetKey, string reason)
        {
            if (rejectedTargets.Add(targetKey))
                issues.Add($"OBJMove target {targetKey:X8}: {reason}.");
        }
    }

    private static string? UnsupportedTargetReason(SceneObject target,
        IReadOnlySet<uint>? absolutePositionTrackTargets)
    {
        if (target.Actor != null)
            return "actor-root composition is not evaluated by this prop-only pass";
        if (absolutePositionTrackTargets?.Contains(target.GaoKey) == true)
            return "an absolute source position track is present and command ordering is unresolved";
        if (HasParent(target))
            return "world-space movement for a hierarchy-bound target is not evaluated";
        return null;
    }

    private static bool ConflictsWithReset(GameScriptMoveSegment segment, float resetTime)
    {
        bool resetFallsWithinMove = resetTime >= segment.TimelineStartSeconds
            && resetTime <= segment.TimelineEndSeconds;
        bool sharesStartTimestamp = MathF.Abs(resetTime - segment.TimelineStartSeconds)
            < CommandTimestampToleranceSeconds;
        return resetFallsWithinMove && sharesStartTimestamp;
    }

    private static bool IsPositionReset(CinematicLiteralCall call) =>
        CinematicEngineFunctionCatalog.NameFor(call.Selector) is "OBJSetPos" or "OBJSetPos_Teleport";

    private static bool HasParent(SceneObject target)
    {
        if (string.IsNullOrWhiteSpace(target.ParentId)) return false;
        return !uint.TryParse(target.ParentId, NumberStyles.HexNumber,
                   CultureInfo.InvariantCulture, out uint parentKey)
            || parentKey is not 0 and not uint.MaxValue;
    }
}
