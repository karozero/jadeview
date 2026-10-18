using System.Numerics;

namespace JadeView;

public sealed record SkinBinding(int Bone, Matrix4x4 InverseBind, uint[] PackedWeights);
public sealed record ActorBoneLink(uint Key, int Index, Matrix4x4 Matrix);
public sealed class ActorNode
{
    public uint Parent;
    public Matrix4x4 Local = Matrix4x4.Identity;
    public ActorBoneLink[] Links = [];
    public Matrix4x4[] Matrices = [];
}
public sealed class AnimationKey
{
    public float Time, Duration;
    public Vector3? Position, Scale, Control1, Control2;
    public Quaternion? Rotation;
    public int Flags, Format;
}
public sealed class AnimationTrack
{
    public uint Target;
    public int Bone;
    public List<AnimationKey> Keys = [];
}
public sealed class AnimationClip
{
    public uint Key;
    public string Name = "";
    public float Duration;
    public List<AnimationTrack> Tracks = [];
    public string? Error;
    public int Unsupported;
    internal Func<AnimationClip>? Read;
    public bool Loaded;
    public bool SceneMotion;
    public void EnsureLoaded()
    {
        if (Loaded) return;
        Loaded = true;
        try { var value = Read!(); Tracks = value.Tracks; Duration = value.Duration; Unsupported = value.Unsupported; }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException)
        { Error = e.Message; }
        Read = null;
    }
}

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

public static class AnimationMath
{
    public static Matrix4x4 Trs(TransformData t) => Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);
    public static Matrix4x4 Matrix(byte[] bytes, int p)
    {
        var j = JadeMatrix.Read(bytes.AsSpan(p, 68));
        var m = new Matrix4x4(j.Ix,j.Iy,j.Iz,0,j.Jx,j.Jy,j.Jz,0,j.Kx,j.Ky,j.Kz,0,j.Tx,j.Ty,j.Tz,1);
        return (j.Type & 8) != 0 ? Matrix4x4.CreateScale(j.Sx,j.Sy,j.Sz) * m : m;
    }
    public static Matrix4x4 ToRender(Matrix4x4 jade)
    {
        var b = new Matrix4x4(1,0,0,0,0,0,1,0,0,-1,0,0,0,0,0,1);
        return b * jade * Matrix4x4.Transpose(b);
    }
    public static Vector3[] Normals(Vector3[] positions, List<Geometry.Part> parts)
    {
        var values = new Vector3[positions.Length]; var counts = new int[positions.Length];
        foreach (var part in parts) for (int i = 0; i < part.VertexIndices.Length; i += 3)
        {
            int a=part.VertexIndices[i],b=part.VertexIndices[i+1],c=part.VertexIndices[i+2];
            var n=Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]);
            if(n.LengthSquared()>1e-20f) n=Vector3.Normalize(n); else n=Vector3.Zero;
            values[a]+=n; values[b]+=n; values[c]+=n; counts[a]++;counts[b]++;counts[c]++;
        }
        for(int i=0;i<values.Length;i++)if(counts[i]>0)values[i]/=counts[i];
        return values;
    }
}

internal static class ActorLoader
{
    private static readonly object FixLock = new();
    private static string? fixPath;
    private static DateTime fixStamp;
    private static byte[]? fixBytes;
    private static PcSceneLoader.Result? fixReplay;
    public static void Build(byte[] bytes, PcSceneLoader.Result replay, SceneDocument doc)
    {
        var origins = replay.Records.ToDictionary(r=>r,r=>bytes);
        var all = new List<PcSceneLoader.Record>();
        if (doc.Worlds.Any(w=>w.Objects.Any(o=>(o.Flags&2)!=0)) && File.Exists(doc.BaseBfPath))
        {
            lock(FixLock)
            {
                var stamp=File.GetLastWriteTimeUtc(doc.BaseBfPath);
                if(fixReplay==null || fixPath!=doc.BaseBfPath || fixStamp!=stamp)
                {
                    var archive=new Archive(doc.BaseBfPath);var e=archive.Entries.FirstOrDefault(e=>e.Id=="FF0084DF");
                    fixBytes=e==null?null:archive.Read(e);fixReplay=fixBytes==null?null:PcSceneLoader.Read(fixBytes,doc.BaseBfPath);fixPath=doc.BaseBfPath;fixStamp=stamp;
                }
                if(fixReplay?.Complete==true && fixBytes!=null)foreach(var r in fixReplay.Records){all.Add(r);origins[r]=fixBytes;}
            }
        }
        all.AddRange(replay.Records);
        var resources=all.GroupBy(r=>r.Key).ToDictionary(g=>g.Key,g=>g.Last());
        Dictionary<uint,PcSceneLoader.Record>? scope=null;
        PcSceneLoader.Record? Find(uint key)=>scope?.GetValueOrDefault(key) ?? resources.GetValueOrDefault(key);
        List<PcSceneLoader.Record> Group(uint key)
        {
            var g=Find(key);var list=g?.References.FirstOrDefault();
            return list==null?[]:Find(list.Key)?.References.Select(r=>Find(r.Key)).Where(r=>r?.Kind=='l').Cast<PcSceneLoader.Record>().ToList()??[];
        }
        var clipCache=new Dictionary<PcSceneLoader.Record,AnimationClip>();
        var sceneEventCache=new Dictionary<PcSceneLoader.Record,AnimationClip?>();
        foreach(var w in doc.Worlds)
        {
            scope=replay.Worlds.FirstOrDefault(world=>world.Key==w.Key)?.ActorResources;
            var originals=w.Objects.ToArray();var templates=new HashSet<SceneObject>();
            foreach(var root in originals.Where(o=>(o.Flags&2)!=0))
            {
                var r=Find(root.GaoKey);if(r==null)continue;
                var children=Group(r.Actions);if(children.Count==0)continue;
                var actor=new SceneActor{Root=root};actor.Members.Add(root);root.Actor=actor;
                root.ActorNode=Node(bytes,root);
                foreach(var child in children)
                {
                    var node=SceneLoader.ParseGao(origins[child],new(child.Offset-4,child.Size+4,0,false),child.Key,0,doc.Diagnostics);
                    if(node==null){actor.Bones.Add(null);continue;}
                    node.Modifiers=child.Modifiers.Select(m=>new SceneModifier(m.Type,origins[child].AsSpan(m.Offset,m.Size).ToArray())).ToList();
                    node.Name=child.Name;node.Id=$"{root.Id}/part/{actor.Bones.Count}/{child.Key:X8}";
                    node.Actor=actor;node.ActorNode=Node(origins[child],node);
                    node.Capabilities=new(){Flags=EditCapabilities.None,RestrictionReason="Prefab component: pose is controlled by the skeleton. Select the actor to edit."};
                    int boneIndex=actor.Bones.Count;
                    var models=new List<SceneObject>();
                    foreach(var source in Group(child.Prefab))
                    {
                        var model=SceneLoader.ParseGao(origins[source],new(source.Offset-4,source.Size+4,0,false),source.Key,0,doc.Diagnostics);
                        if(model==null)continue;model.Modifiers=source.Modifiers.Select(m=>new SceneModifier(m.Type,origins[source].AsSpan(m.Offset,m.Size).ToArray())).ToList();model.ActorNode=Node(origins[source],model);models.Add(model);
                    }
                    if(models.Count>0)actor.SourceMeshes[boneIndex]=models;
                    actor.Members.Add(node);actor.Bones.Add(node);
                    foreach(var old in originals.Where(o=>o.GaoKey==child.Key && o!=root && (o.Flags&2)==0))templates.Add(old);
                    w.Objects.Add(node);
                }
                actor.DefaultShape=r.Shape;
                void Shape(uint key)
                {
                    if(key is 0 or uint.MaxValue || actor.Shapes.ContainsKey(key))return;
                    var shape=Find(key);if(shape!=null && shape.Size>0)actor.Shapes[key]=origins[shape].AsSpan(shape.Offset,shape.Size).ToArray();
                }
                Shape(r.Shape);
                var table=Find(r.Skeleton);
                if(table!=null)foreach(var request in table.References.Where(q=>q.Kind=='a'))
                {
                    var action=Find(request.Key);if(action==null)continue;
                    for(int index=0;action.Size>1 && index<origins[action][action.Offset];index++)
                    {
                        int p=action.Offset+2+index*19;if(p>action.Offset+action.Size-19)break;
                        uint eventKey=Preview.U32(origins[action],p);var events=Find(eventKey);if(events==null)continue;
                        if(!clipCache.TryGetValue(events,out var clip))
                        {
                            clip=new(){Key=eventKey,Name=$"{eventKey:X8}",Read=()=>AnimationReader.Read(origins[events],events.Offset,events.Size)};clipCache[events]=clip;
                        }
                        uint shapeKey=Preview.U32(origins[action],p+8);Shape(shapeKey);
                        if(!actor.Clips.Contains(clip)){actor.Clips.Add(clip);actor.ClipShapes[clip]=shapeKey is 0 or uint.MaxValue?r.Shape:shapeKey;}
                    }
                }
                if(actor.Clips.Count==0 && Find(r.Events) is {Kind:'i'} e)
                    actor.Clips.Add(new(){Key=e.Key,Name=$"{e.Key:X8}",Read=()=>AnimationReader.Read(origins[e],e.Offset,e.Size)});
                if(actor.Clips.Count==0)
                {
                    // Absolute scene tracks may address reusable prefab parts; only a root key unambiguously identifies this instance.
                    var memberKeys=new HashSet<uint>{root.GaoKey};
                    foreach(var events in replay.Records.Where(e=>e.Kind=='i'))
                    {
                        if(!sceneEventCache.TryGetValue(events,out var sceneClip))
                        {
                            try { sceneClip=AnimationReader.Read(origins[events],events.Offset,events.Size); }
                            catch(Exception ex) when(ex is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException) { sceneClip=null; }
                            sceneEventCache[events]=sceneClip;
                        }
                        if(sceneClip==null)continue;
                        var tracks=sceneClip.Tracks.Where(t=>t.Bone<0 && memberKeys.Contains(t.Target)).ToList();
                        if(tracks.Count==0)continue;
                        actor.Clips.Add(new(){Key=events.Key,Name=$"{events.Key:X8} (scene motion)",SceneMotion=true,Loaded=true,Tracks=tracks,Duration=sceneClip.Duration});
                    }
                    actor.Diagnostic=actor.Clips.Count>0
                        ? "Scene motion tracks only. Gameplay animation table is not linked in this actor record."
                        : "No animation table or matching scene motion tracks in loaded data.";
                }
                actor.ApplyShape(r.Shape);actor.Evaluate(0);w.Actors.Add(actor);
            }
            w.Objects.RemoveAll(o=>templates.Contains(o));
        }
        doc.Diagnostics.Add($"Actors: {doc.Worlds.Sum(w=>w.Actors.Count)}; component instances and animation pose are preview-only.");
    }
    private static ActorNode Node(byte[] b,SceneObject o)
    {
        var n=new ActorNode{Local=AnimationMath.Matrix(b,o.RecordRange.Offset+4+22)};
        int p=o.RecordRange.Offset+4,q=p+90+((o.Flags&0x80000)!=0?48:24);
        if((o.Flags&0x1000)==0)return n;
        if((o.Flags&0x4000)!=0){int count=Preview.I32(b,q+16);q+=20+count*4;}
        if((o.Flags&0x400000)!=0){n.Parent=Preview.U32(b,q);n.Local=AnimationMath.Matrix(b,q+4);q+=72;}
        if((o.Flags&2)!=0)q+=16;
        if((o.Flags&0x200000)!=0)
        {
            int count=Preview.I32(b,q);q+=4;
            if(count<0 || count>512)throw new InvalidDataException("Invalid actor bone palette.");
            if((o.Flags&0x1000000)!=0)
            {
                n.Links=new ActorBoneLink[count];for(int i=0;i<count;i++,q+=8)n.Links[i]=new(Preview.U32(b,q),Preview.I32(b,q+4),Matrix4x4.Identity);
            }
            else{n.Matrices=new Matrix4x4[count];for(int i=0;i<count;i++,q+=68)n.Matrices[i]=AnimationMath.Matrix(b,q);}
        }
        return n;
    }
}

public static class AnimationReader
{
    public static AnimationClip Read(byte[] b,int start,int size)
    {
        int q=start,end=start+size,previousSize=0,previousFormat=0;
        void Need(int n){if(n<0 || q>end-n)throw new InvalidDataException("Truncated animation record.");}
        int U8(){Need(1);return b[q++];}
        int U16(){Need(2);int v=Preview.U16(b,q);q+=2;return v;}
        int I16()=>(short)U16();
        uint U32(){Need(4);uint v=Preview.U32(b,q);q+=4;return v;}
        float F()=>BitConverter.UInt32BitsToSingle(U32());
        Vector3 V()=>new(F(),F(),F());
        var clip=new AnimationClip{Loaded=true};int channels=U16();U16();
        for(int channel=0;channel<channels;channel++)
        {
            int flags=U16(),bone=I16();uint target=U32();int mode=flags&0x3f00;
            if((flags&0x8000)!=0){if(mode==0)U32();target=0;}else target=U32();
            int count=checked((int)U32());if(count>100000)throw new InvalidDataException("Animation key budget exceeded.");
            var track=new AnimationTrack{Bone=bone,Target=target};clip.Tracks.Add(track);int firstFlags=0;float time=0;
            for(int frame=0;frame<count;frame++)
            {
                int duration=(mode&512)!=0?U8():U16();
                int f;if((mode&2048)!=0 && frame>0)f=firstFlags&~4;else{f=U16();if(frame==0)firstFlags=f;}
                f&=~2;var key=new AnimationKey{Time=time,Duration=duration/60f,Flags=f};track.Keys.Add(key);time+=key.Duration;
                if((f&1984)==64){int n=checked((int)U32());Need(n*8);q+=n*8;continue;}
                if((f&1984)!=128)continue;
                if((f&32)!=0){U32();clip.Unsupported++;continue;}
                int controls=frame>0?mode:((mode&8192)!=0?256:0),p=q;
                int bytes=(controls&8192)!=0?previousSize/2:U16();
                int format=(controls&4096)!=0?previousFormat:U16();key.Format=format;
                if((format&1920)!=0)
                {
                    if((format&128)!=0)
                    {
                        float x=I16()/32767f,y=I16()/32767f,z=I16()/32767f;
                        key.Rotation=Quaternion.Normalize(new(x,y,z,MathF.Sqrt(Math.Max(0,1-x*x-y*y-z*z))));
                    }
                    else {Need(4);q+=4;clip.Unsupported++;}
                    previousSize=(controls&8448)!=0?41:20;previousFormat=format;
                }
                else if((format&63488)!=0)
                {
                    int type=format&63488;float x=0,y=0,z=0;
                    if(type is 2048 or 4096){x=I16()/1024f;y=I16()/1024f;z=I16()/1024f;}
                    else{float s=I16()/1024f,a=(sbyte)U8()/1024f,c=(sbyte)U8()/1024f;if(type==8192){x=s;y=a;z=c;}else if(type==16384){x=a;y=s;z=c;}else if(type==32768){x=a;y=c;z=s;}else clip.Unsupported++;}
                    key.Position=new(x,y,z);previousSize=(controls&8448)!=0?33:16;previousFormat=format;
                }
                else if((format&23)!=0)
                {
                    previousSize=(controls&8448)!=0?(bytes*2|1):bytes;previousFormat=format;
                    int targetSize=previousSize;if((controls&8192)!=0)targetSize-=2;if((controls&4096)!=0)targetSize-=2;
                    switch(format&3)
                    {
                        case 3:key.Control2=V();key.Control1=V();key.Position=V();break;
                        case 2:key.Control1=V();key.Position=V();break;
                        case 1:key.Position=V();break;
                        default:
                            if((format&20)==4){Need(68);var m=AnimationMath.Matrix(b,q);q+=68;Matrix4x4.Decompose(m,out var s,out var r,out var t);key.Rotation=r;}
                            else if((format&20)==16)key.Rotation=Quaternion.Normalize(new(F(),F(),F(),F()));
                            break;
                    }
                    if((format&8)!=0 && q-p<targetSize){int n=checked((int)U32());Need((2*n-1)*4);q+=(2*n-1)*4;clip.Unsupported++;}
                }
            }
            clip.Duration=Math.Max(clip.Duration,time);
        }
        if(q!=end)throw new InvalidDataException($"Animation cursor mismatch {q:X8}/{end:X8}.");
        return clip;
    }
}
