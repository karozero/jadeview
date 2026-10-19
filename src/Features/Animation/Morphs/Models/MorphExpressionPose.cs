namespace JadeView;

// A source target pose, not a guessed emotion or an actor's native expression kit.
public sealed record MorphExpressionPose(int Group, string Name, int[] Channels);
