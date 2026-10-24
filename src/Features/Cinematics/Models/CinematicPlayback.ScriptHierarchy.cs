using System.Globalization;

namespace JadeView;

public sealed partial class CinematicPlayback
{
    internal CinematicScriptHierarchyBinding[] ScriptHierarchyBindingsAtPlayhead(WorldData world)
    {
        if (world.Key != WorldKey) return [];

        CinematicScriptHierarchyCommand[] commands = ScriptStateCalls
            .Select(item => CinematicScriptHierarchyCommand.Decode(item.Time, item.Call))
            .OfType<CinematicScriptHierarchyCommand>()
            .ToArray();
        if (commands.Length == 0) return [];

        var endpointCounts = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .ToDictionary(group => group.Key, group => group.Count());
        var childKeys = commands.Select(command => command.ChildKey).ToHashSet();
        var initialParents = world.Objects
            .Where(sceneObject => endpointCounts[sceneObject.GaoKey] == 1)
            .ToDictionary(sceneObject => sceneObject.GaoKey, ReadInitialParent);

        return CinematicScriptHierarchyBindingReducer.Evaluate(commands, initialParents,
                endpointCounts, Time)
            .Values.Where(binding => childKeys.Contains(binding.ChildKey))
            .Select(binding => scriptHierarchyPoseStatuses.TryGetValue(binding.ChildKey, out var poseStatus)
                ? binding with { PoseStatus = poseStatus }
                : binding)
            .OrderBy(binding => binding.Status)
            .ThenBy(binding => binding.ChildKey)
            .ToArray();
    }

    private static uint? ReadInitialParent(SceneObject sceneObject)
    {
        if (!uint.TryParse(sceneObject.ParentId, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint parentKey)
            || parentKey is 0 or uint.MaxValue)
            return null;
        return parentKey;
    }
}
