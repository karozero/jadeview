namespace JadeView;

public sealed record CinematicActionTransition(uint RequestedAction, ushort Action, byte Flags, byte BlendFrames)
{
    public float BlendDuration => BlendFrames == 0 ? 0 : (BlendFrames + 1) / 60f;
}
