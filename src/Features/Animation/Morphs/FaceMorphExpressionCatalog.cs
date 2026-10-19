using System.Numerics;

namespace JadeView;

public static class FaceMorphExpressionCatalog
{
    public static MorphExpressionPose[] Discover(FaceMorphData data) => data.Groups
        .Where(group => group.AccumulatedDeltas().Values.Any(delta => delta != Vector3.Zero))
        .Select(group => new MorphExpressionPose(group.Index, $"PC_Morph_{group.Index:D2}",
            data.Channels.Select((channel, index) => (channel, index))
                .Where(item => item.channel.GroupReferences.Contains((uint)group.Index))
                .Select(item => item.index).ToArray())).ToArray();
}
