using System.Numerics;

namespace JadeView;

// Preview state is separate from editable transforms and never produces archive patches.
public sealed class SceneActor
{
    public SceneObject Root = null!;
    public List<SceneObject> Members = [];
    public List<SceneObject?> Bones = [];
    public List<AnimationClip> Clips = [];
    public AnimationClip? Clip;
    public uint DefaultShape;
    public Dictionary<uint, byte[]> Shapes = [];
    public Dictionary<AnimationClip,uint> ClipShapes = [];
    public Dictionary<int,List<SceneObject>> SourceMeshes = [];
    public bool Playing;
    public bool Loop = true;
    public bool RootMotion;
    public float Time, Speed = 1;
    public string? Diagnostic;
    public int PoseRevision { get; private set; }
    private readonly Dictionary<SceneObject, Matrix4x4> world = [];
    public void SelectClip(AnimationClip? clip)
    {
        ApplyShape(DefaultShape);
        if (clip != null && ClipShapes.TryGetValue(clip, out var shape) && shape != DefaultShape) ApplyShape(shape);
        if (clip?.SceneMotion == true) RootMotion = true;
        Clip = clip; Time = 0; clip?.EnsureLoaded(); Playing = clip != null && clip.Error == null;
        Evaluate(0);
    }
    public void ApplyShape(uint key)
    {
        if (!Shapes.TryGetValue(key,out var shape)) return;
        for(int i=0;i<shape[0];i++)
        {
            int p=1+i*3;if(p+2>=shape.Length)break;
            int bone=(sbyte)shape[p], source=(sbyte)shape[p+2];
            if(bone<0 || bone>=Bones.Count || Bones[bone] is not {} target)continue;
            if(source==-3){target.PreviewHidden=true;continue;}
            if(source==-2){target.PreviewHidden=false;continue;}
            if(source<0 || !SourceMeshes.TryGetValue(bone,out var models) || source>=models.Count)continue;
            var model=models[source];target.PreviewHidden=false;
            target.AppearanceRevision++;target.GeometryKey=model.GeometryKey;target.MaterialKey=model.MaterialKey;
            target.RliColors=model.RliColors;target.DrawFlags=model.DrawFlags;
            target.Flags=(target.Flags&~0x1200000u)|(model.Flags&0x1200000u);
            if(target.ActorNode!=null && model.ActorNode!=null){target.ActorNode.Links=model.ActorNode.Links;target.ActorNode.Matrices=model.ActorNode.Matrices;}
        }
    }

    public void Evaluate(float delta)
    {
        PoseRevision++;
        if (Clip != null)
        {
            Clip.EnsureLoaded();
            if (Playing && Clip.Duration > 0)
            {
                Time += Math.Clamp(delta, 0, .1f) * Speed;
                if (Loop) Time %= Clip.Duration;
                else if (Time >= Clip.Duration) { Time = Clip.Duration; Playing = false; }
            }
        }
        Matrix4x4 rootRotation=Matrix4x4.Identity;Vector3 rootOffset=Vector3.Zero;
        var locals = Members.ToDictionary(o => o, o => o.ActorNode?.Local ?? AnimationMath.Trs(o.Transform));
        if (Clip is { Error: null }) foreach (var track in Clip.Tracks)
        {
            SceneObject? target = track.Bone >= 0 ? track.Bone < Bones.Count ? Bones[track.Bone] : null
                : track.Target is 0 or uint.MaxValue ? Root : Members.FirstOrDefault(o => o.GaoKey == track.Target);
            if (target == null) continue;
            var keys = track.Keys.Where(k => k.Position != null || k.Rotation != null || k.Scale != null).ToList();
            if (keys.Count == 0) continue;
            var first = keys.LastOrDefault(k => k.Time <= Time) ?? keys[0];
            int index = keys.IndexOf(first); var next = index + 1 < keys.Count ? keys[index + 1] : keys[0];
            float t = first.Duration > 0 ? Math.Clamp((Time - first.Time) / first.Duration, 0, 1) : 0;
            Matrix4x4.Decompose(locals[target], out var scale, out var rotation, out var position);
            if ((first.Flags & 2048) != 0) t=0;
            if (first.Position is Vector3 p)
            {
                var end=next.Position??p;float u=1-t;
                position=(first.Format&3) switch
                {
                    2 => p*u*u + 2*(first.Control1??p)*u*t + end*t*t,
                    3 => p*u*u*u + 3*(first.Control1??p)*u*u*t + 3*(first.Control2??end)*u*t*t + end*t*t*t,
                    _ => Vector3.Lerp(p,end,t)
                };
            }
            if (first.Scale is Vector3 s) scale = Vector3.Lerp(s, next.Scale ?? s, t);
            if (first.Rotation is Quaternion q) rotation = Quaternion.Slerp(q, next.Rotation ?? q, t);
            if (target == Root)
            {
                if(first.Rotation!=null){var initial=keys.First(k=>k.Rotation!=null).Rotation!.Value;Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(initial),out var inverse);rootRotation=inverse*Matrix4x4.CreateFromQuaternion(rotation);}
                if(first.Position!=null && RootMotion)rootOffset=position-keys.First(k=>k.Position!=null).Position!.Value;
            }
            else locals[target] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }
        world.Clear(); var visiting = new HashSet<SceneObject>();
        Matrix4x4 Pose(SceneObject o)
        {
            if (world.TryGetValue(o, out var result)) return result;
            if (o == Root) return world[o] = rootRotation*Matrix4x4.CreateTranslation(rootOffset)*AnimationMath.Trs(Root.Transform);
            if (!visiting.Add(o)) return AnimationMath.Trs(Root.Transform);
            var parent = Members.FirstOrDefault(n => n.GaoKey == o.ActorNode?.Parent && n != o) ?? Root;
            result = locals[o] * Pose(parent); visiting.Remove(o); return world[o] = result;
        }
        foreach (var o in Members) o.PreviewMatrix = Pose(o);
    }
    public Vector3[]? Skin(SceneObject obj, Geometry geometry)
    {
        if (geometry.Skin.Length == 0 || obj.ActorNode == null || (obj.Flags & 0x200000) == 0) return null;
        if (!Matrix4x4.Invert(obj.PreviewMatrix ?? AnimationMath.Trs(obj.Transform), out var inverseMesh)) return null;
        var positions = new Vector3[geometry.Positions.Length]; var touched = new bool[positions.Length];
        foreach (var binding in geometry.Skin)
        {
            Matrix4x4 bone;
            if ((obj.Flags & 0x1000000) != 0)
            {
                if (binding.Bone < 0 || binding.Bone >= obj.ActorNode.Links.Length) continue;
                var link = obj.ActorNode.Links[binding.Bone];
                var target = link.Index == -1 ? Root : link.Index >= 0 && link.Index < Bones.Count ? Bones[link.Index] : Members.FirstOrDefault(m => m.GaoKey == link.Key);
                if (target == null) continue;
                bone = target.PreviewMatrix ?? AnimationMath.Trs(target.Transform);
            }
            else
            {
                if (binding.Bone < 0 || binding.Bone >= obj.ActorNode.Matrices.Length) continue;
                bone = obj.ActorNode.Matrices[binding.Bone] * (Root.PreviewMatrix ?? AnimationMath.Trs(Root.Transform));
            }
            var flashed = binding.InverseBind;
            // GEO_SKN_MulMatrixMatrixSym / Iris l.a: mirrored geometry reflects X.
            if ((obj.DrawFlags & 0x8000) == 0) { flashed.M11 = -flashed.M11; flashed.M21 = -flashed.M21; flashed.M31 = -flashed.M31; flashed.M41 = -flashed.M41; }
            var matrix = flashed * bone * inverseMesh;
            foreach (uint packed in binding.PackedWeights)
            {
                int vertex = (int)(packed & 65535);
                if (vertex >= positions.Length) continue;
                float weight = BitConverter.UInt32BitsToSingle(packed);
                positions[vertex] += Vector3.Transform(geometry.Positions[vertex], matrix) * weight;
                touched[vertex] = true;
            }
        }
        for (int i = 0; i < positions.Length; i++) if (!touched[i]) positions[i] = geometry.Positions[i];
        return positions;
    }
}

