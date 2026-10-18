using System.Numerics;

namespace JadeView;

public sealed class ActorNode
{
    public uint Parent;
    public Matrix4x4 Local = Matrix4x4.Identity;
    public ActorBoneLink[] Links = [];
    public Matrix4x4[] Matrices = [];
}
