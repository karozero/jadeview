using System.Numerics;

namespace JadeView;

internal sealed class ActorExportSnapshot : IDisposable
{
    private readonly SceneActor actor;
    private readonly AnimationClip? clip;
    private readonly float time;
    private readonly bool playing, loop, rootMotion;
    private readonly MemberState[] members;

    internal ActorExportSnapshot(SceneActor actor)
    {
        this.actor = actor;
        clip = actor.Clip;
        time = actor.Time;
        playing = actor.Playing;
        loop = actor.Loop;
        rootMotion = actor.RootMotion;
        members = actor.Members.Select(member => new MemberState(member, member.GeometryKey,
            member.MaterialKey, member.RliColors, member.DrawFlags, member.Flags, member.PreviewHidden,
            member.AppearanceRevision, member.PreviewMatrix, member.ActorNode?.Links, member.ActorNode?.Matrices)).ToArray();
    }

    public void Dispose()
    {
        actor.Clip = clip;
        actor.Time = time;
        actor.Playing = playing;
        actor.Loop = loop;
        actor.RootMotion = rootMotion;
        foreach (var state in members)
        {
            var member = state.Object;
            member.GeometryKey = state.Geometry;
            member.MaterialKey = state.Material;
            member.RliColors = state.Colors;
            member.DrawFlags = state.DrawFlags;
            member.Flags = state.Flags;
            member.PreviewHidden = state.Hidden;
            member.AppearanceRevision = state.Revision;
            member.PreviewMatrix = state.Matrix;
            if (member.ActorNode == null) continue;
            member.ActorNode.Links = state.Links!;
            member.ActorNode.Matrices = state.Matrices!;
        }
    }

    private sealed record MemberState(SceneObject Object, uint Geometry, uint Material, uint[] Colors,
        uint DrawFlags, uint Flags, bool Hidden, int Revision, Matrix4x4? Matrix,
        ActorBoneLink[]? Links, Matrix4x4[]? Matrices);
}
