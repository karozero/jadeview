using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed class SceneExportOptions
{
    public bool Textures = true, Actors = true, Collisions, UnknownObjects, Animations, AllClips, GameHidden;
    public int Placement; // 0 current, 1 original, 2 origin
}

public sealed record SceneExportResult(int Meshes, int EmptyObjects, int Textures, int Animations, string[] Warnings);

// glTF 2.0 / GLB, right-handed Y-up. No temporary image files or GPU context required.
public sealed class SceneExporter
{
    private readonly ResourceResolver resolver;
    private readonly SceneExportOptions options;
    private readonly MemoryStream binary = new();
    private readonly List<Obj> views = [], accessors = [], nodes = [], meshes = [], materials = [], images = [], textures = [], skins = [], animations = [];
    private readonly List<int> roots = [];
    private readonly Dictionary<string,int> imageCache = [];
    private readonly HashSet<string> warnings = [];
    private int empties;
    private Matrix4x4 originOffset=Matrix4x4.Identity;
    private SceneExporter(ResourceResolver resolver, SceneExportOptions options) { this.resolver=resolver;this.options=options; }
    private static Obj D(params object[] pairs) { var d=new Obj();for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d; }
    public static string SafeFileName(string name)
    {
        var invalid=Path.GetInvalidFileNameChars();
        string value=new(name.Select(c=>char.IsControl(c)||invalid.Contains(c)?'_':c).ToArray());
        value=value.Trim().TrimEnd('.');
        if(string.IsNullOrWhiteSpace(value))value="export";
        string stem=value.Split('.')[0];
        if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem,StringComparer.OrdinalIgnoreCase))value="_"+value;
        return value.Length>160?value[..160].TrimEnd('.'):value;
    }

    public static SceneExportResult Export(string path, WorldData world, ResourceResolver resolver, SceneExportOptions options, SceneObject? selected=null)
    {
        using var exporter=new ExportLifetime(new SceneExporter(resolver,options));
        var e=exporter.Value;
        var objects=selected==null?world.Objects.ToList():selected.Actor is {} actor && selected==actor.Root?actor.Members.ToList():new List<SceneObject>{selected};
        if(selected==null && !options.Actors)objects.RemoveAll(o=>o.Actor!=null || (o.Flags&2)!=0);
        if(options.Placement==2)
        {
            var points=objects.Where(o=>o.Actor==null||o==o.Actor.Root).Select(o=>o.RenderMatrix.Translation).ToArray();
            var pivot=selected?.RenderMatrix.Translation??(points.Length>0?(points.Aggregate(Vector3.Min)+points.Aggregate(Vector3.Max))*.5f:Vector3.Zero);
            e.originOffset=Matrix4x4.CreateTranslation(-pivot);
        }
        var seen=new HashSet<SceneActor>();
        foreach(var obj in objects)
        {
            if(obj.Actor is {} a)
            {
                if(!seen.Add(a))continue;
                var parts=objects.Where(o=>o.Actor==a).ToList();
                e.Actor(a,parts);
            }
            else e.Static(obj,null,e.Placement(obj));
        }
        if(e.nodes.Count==0)throw new InvalidOperationException("Nothing matches the export options.");
        e.Write(path,world.Name);
        return new(e.meshes.Count,e.empties,e.images.Count,e.animations.Count,e.warnings.ToArray());
    }
    private sealed class ExportLifetime(SceneExporter value):IDisposable { public SceneExporter Value=value;public void Dispose()=>Value.binary.Dispose(); }

    private Matrix4x4 Placement(SceneObject obj)
    {
        var m=options.Placement==1?obj.InitialTransform.ToRenderMatrix():obj.RenderMatrix;
        if(options.Placement==2)m*=originOffset;
        return m;
    }
    private bool Included(SceneObject obj)=>SceneRenderer.IsCollision(obj)?options.Collisions:options.GameHidden || !obj.PreviewHidden && (obj.DrawFlags&8)!=0;
    private int Node(SceneObject obj, Matrix4x4 matrix, bool trs=false)
    {
        var n=D("name",obj.Name,"extras",D("gaoKey",obj.GaoKey.ToString("X8"),"groKey",obj.GeometryKey.ToString("X8"),"grmKey",obj.MaterialKey.ToString("X8"),"collision",SceneRenderer.IsCollision(obj),"sourceId",obj.Id,"flags",obj.Flags,"recordOffset",obj.RecordRange.Offset,"recordSize",obj.RecordRange.Size));
        if(trs)SetTrs(n,matrix);else n["matrix"]=Matrix(matrix);
        nodes.Add(n);return nodes.Count-1;
    }
    private static float[] Matrix(Matrix4x4 m)=>[m.M11,m.M12,m.M13,0,m.M21,m.M22,m.M23,0,m.M31,m.M32,m.M33,0,m.M41,m.M42,m.M43,1];
    private static (Vector3 Scale,Quaternion Rotation,Vector3 Position) Decompose(Matrix4x4 m)
    {
        if(!Matrix4x4.Decompose(m,out var scale,out var rotation,out var position))throw new InvalidDataException("Animation matrix cannot be decomposed into glTF transforms.");
        return (scale,Quaternion.Normalize(rotation),position);
    }
    private static void SetTrs(Obj n,Matrix4x4 m)
    {
        var(s,q,t)=Decompose(m);n["translation"]=new[]{t.X,t.Y,t.Z};n["rotation"]=new[]{q.X,q.Y,q.Z,q.W};n["scale"]=new[]{s.X,s.Y,s.Z};
    }
    private void Static(SceneObject obj,List<int>? children,Matrix4x4 matrix)
    {
        if(!Included(obj))return;
        if(!resolver.Geometries.TryGetValue(obj.RenderGeometryKey,out var geo))
        {
            if(!options.UnknownObjects)return;
            int empty=Node(obj,matrix);((Obj)nodes[empty]["extras"])["unresolvedGeometry"]=true;
            Add(empty,children);empties++;return;
        }
        int node=Node(obj,matrix);Add(node,children);
        if(geo.Particles!=null){((Obj)nodes[node]["extras"])["particleGenerator"]=true;warnings.Add("PAG simulation is a viewport effect; only its carrier is exported.");if(geo.ParticlePart==0){empties++;return;}}
        if(geo.Billboards.Count>0)warnings.Add("SPG vegetation exports as fixed-facing quads; camera billboarding and wind are viewport effects.");
        var posed=obj.Actor?.Skin(obj,geo)??geo.Positions;
        nodes[node]["mesh"]=Mesh(obj,geo,posed,null);
    }
    private void Add(int node,List<int>? children){if(children==null)roots.Add(node);else children.Add(node);}

    private void Actor(SceneActor actor,List<SceneObject> objects)
    {
        // Shape selection and sampling touch preview state only; restore all of it even on failure.
        var saved=actor.Members.Select(o=>new{Object=o,o.GeometryKey,o.MaterialKey,o.RliColors,o.DrawFlags,o.Flags,o.PreviewHidden,o.AppearanceRevision,o.PreviewMatrix,Links=o.ActorNode?.Links,Matrices=o.ActorNode?.Matrices}).ToArray();
        var oldClip=actor.Clip;float oldTime=actor.Time;bool oldPlaying=actor.Playing;
        try
        {
            actor.Evaluate(0);
            var children=new List<int>();int group=nodes.Count;nodes.Add(D("name",actor.Root.Name,"children",children,"extras",D("actor",true,"gaoKey",actor.Root.GaoKey.ToString("X8"))));roots.Add(group);
            Matrix4x4 offset=Matrix4x4.Identity;
            if(options.Placement==2)offset=originOffset;
            else if(options.Placement==1 && Matrix4x4.Invert(actor.Root.RenderMatrix,out var inv))offset=inv*actor.Root.InitialTransform.ToRenderMatrix();
            if(!options.Animations){foreach(var obj in objects)Static(obj,children,obj.RenderMatrix*offset);return;}
            nodes[group]["matrix"]=Matrix(offset);
            actor.Clip=null;actor.Evaluate(0);
            var bindPose=actor.Members.ToDictionary(o=>o,o=>o.PreviewMatrix??AnimationMath.Trs(o.Transform));
            actor.Clip=oldClip;actor.Time=oldTime;actor.Playing=false;actor.Evaluate(0);
            var animated=new List<(int Node,Func<Matrix4x4> Pose)>();
            var nodeMap=new Dictionary<SceneObject,int>();
            foreach(var obj in actor.Members)nodeMap[obj]=Node(obj,Matrix4x4.Identity,true);
            foreach(var obj in actor.Members)
            {
                var parent=obj==actor.Root?null:actor.Members.FirstOrDefault(o=>o!=obj && o.GaoKey==obj.ActorNode?.Parent)??actor.Root;
                int parentNode=parent==null?group:nodeMap[parent];
                if(!nodes[parentNode].TryGetValue("children",out var list))nodes[parentNode]["children"]=list=new List<int>();
                ((List<int>)list).Add(nodeMap[obj]);
                Matrix4x4 Pose()
                {
                    if(parent==null)return obj.RenderMatrix;
                    if(!Matrix4x4.Invert(parent.RenderMatrix,out var inverse))throw new InvalidDataException("Singular animation parent transform.");
                    return obj.RenderMatrix*inverse;
                }
                SetTrs(nodes[nodeMap[obj]],Pose());
                animated.Add((nodeMap[obj],Pose));
            }
            foreach(var obj in objects)
            {
                if(!Included(obj))continue;
                int node=nodeMap[obj];
                if(!resolver.Geometries.TryGetValue(obj.RenderGeometryKey,out var geo))
                {
                    if(options.UnknownObjects)empties++;continue;
                }
                var binding=new List<(SkinBinding Skin,int Joint,Matrix4x4 Bind)>();
                if((obj.Flags&0x200000)!=0 && obj.ActorNode is {} palette)
                foreach(var skin in geo.Skin)
                {
                    int index=skin.Bone;int parent;Matrix4x4 boneBind;
                    if((obj.Flags&0x1000000)!=0)
                    {
                        if(index<0||index>=palette.Links.Length)continue;
                        var link=palette.Links[index];var target=link.Index==-1?actor.Root:link.Index>=0&&link.Index<actor.Bones.Count?actor.Bones[link.Index]:actor.Members.FirstOrDefault(o=>o.GaoKey==link.Key);
                        if(target==null||!nodeMap.TryGetValue(target,out parent))continue;
                        boneBind=bindPose[target];
                    }
                    else
                    {
                        if(index<0||index>=palette.Matrices.Length)continue;
                        boneBind=palette.Matrices[index]*bindPose[actor.Root];
                        parent=nodes.Count;nodes.Add(D("name",$"{obj.Name} palette {index}","matrix",Matrix(AnimationMath.ToRender(palette.Matrices[index]))));
                        int rootNode=nodeMap[actor.Root];
                        if(!nodes[rootNode].TryGetValue("children",out var list))nodes[rootNode]["children"]=list=new List<int>();
                        ((List<int>)list).Add(parent);
                    }
                    // Unique joint per binding, preserving per-mesh inverse bind matrices.
                    int joint=nodes.Count;nodes.Add(D("name",$"{obj.Name} bind {binding.Count}"));
                    if(!nodes[parent].TryGetValue("children",out var descendants))nodes[parent]["children"]=descendants=new List<int>();
                    ((List<int>)descendants).Add(joint);binding.Add((skin,joint,boneBind));
                }
                if(binding.Count>0)
                {
                    int fallback=nodes.Count;nodes.Add(D("name",obj.Name+" unweighted"));
                    if(!nodes[node].TryGetValue("children",out var descendants))nodes[node]["children"]=descendants=new List<int>();
                    ((List<int>)descendants).Add(fallback);
                    var inverse=new List<float>();
                    foreach(var item in binding){if(!Matrix4x4.Invert(AnimationMath.ToRender(item.Bind)*offset,out var ib))throw new InvalidDataException("Singular bind pose.");inverse.AddRange(Matrix(ib));}
                    Matrix4x4.Invert(AnimationMath.ToRender(bindPose[obj])*offset,out var fallbackBind);inverse.AddRange(Matrix(fallbackBind));
                    var bindPositions=new Vector3[geo.Positions.Length];var sums=new float[geo.Positions.Length];
                    foreach(var item in binding)
                    {
                        var bind=item.Skin.InverseBind;
                        if((obj.DrawFlags&0x8000)==0){bind.M11=-bind.M11;bind.M21=-bind.M21;bind.M31=-bind.M31;bind.M41=-bind.M41;}
                        var m=bind*item.Bind;
                        foreach(uint packed in item.Skin.PackedWeights){int v=(int)(packed&65535);float weight=BitConverter.UInt32BitsToSingle(packed);if(v>=bindPositions.Length||weight<=0)continue;bindPositions[v]+=Vector3.Transform(geo.Positions[v],m)*weight;sums[v]+=weight;}
                    }
                    for(int v=0;v<bindPositions.Length;v++){
                        var point=sums[v]>0?bindPositions[v]/sums[v]:Vector3.Transform(geo.Positions[v],bindPose[obj]);
                        bindPositions[v]=TransformData.FromRender(Vector3.Transform(Geometry.ToRender(point),offset));
                    }
                    // Include ancestor bones as joints too. Blender can then recover
                    // the real bind pose instead of rebinding the mesh to the current clip.
                    var joints=binding.Select(b=>b.Joint).Append(fallback).ToList();
                    foreach(var ancestor in actor.Members){joints.Add(nodeMap[ancestor]);Matrix4x4.Invert(AnimationMath.ToRender(bindPose[ancestor])*offset,out var ancestorBind);inverse.AddRange(Matrix(ancestorBind));}
                    joints.Add(group);Matrix4x4.Invert(offset,out var groupBind);inverse.AddRange(Matrix(groupBind));
                    int skinIndex=skins.Count;skins.Add(D("joints",joints.ToArray(),"inverseBindMatrices",Floats(inverse.ToArray(),16,"MAT4"),"skeleton",group));
                    // A skinned mesh has no transform of its own in glTF. Keep the
                    // animated component node as a joint; the mesh is an identity root.
                    int meshNode=nodes.Count;
                    nodes.Add(D("name",obj.Name,"skin",skinIndex,"mesh",Mesh(obj,geo,bindPositions,binding.Select(b=>b.Skin).ToArray()),"extras",D("gaoKey",obj.GaoKey.ToString("X8"),"actor",actor.Root.Name,"sourceId",obj.Id)));
                    roots.Add(meshNode);
                }
                else nodes[node]["mesh"]=Mesh(obj,geo,obj.Actor?.Skin(obj,geo)??geo.Positions,null);
            }
            var clips=options.AllClips?actor.Clips:actor.Clip!=null?new List<AnimationClip>{actor.Clip}:actor.Clips.Take(1).ToList();
            foreach(var clip in clips)
            {
                clip.EnsureLoaded();if(clip.Error!=null){warnings.Add($"Animation {clip.Name}: {clip.Error}");continue;}
                actor.SelectClip(clip);actor.Playing=false;
                int frames=Math.Max(2,(int)MathF.Ceiling(clip.Duration*30)+1);
                var times=Enumerable.Range(0,frames).Select(i=>clip.Duration*i/(frames-1)).ToArray();
                if(clip.Duration<=0)continue;
                var values=animated.Select(_=>(T:new List<float>(),R:new List<float>(),S:new List<float>())).ToArray();
                Quaternion[] previous=new Quaternion[animated.Count];
                for(int frame=0;frame<frames;frame++)
                {
                    actor.Time=times[frame];actor.Evaluate(0);
                    for(int j=0;j<animated.Count;j++)
                    {
                        var(s,q,t)=Decompose(animated[j].Pose());if(frame>0&&Quaternion.Dot(previous[j],q)<0)q=new(-q.X,-q.Y,-q.Z,-q.W);previous[j]=q;
                        values[j].T.AddRange([t.X,t.Y,t.Z]);values[j].R.AddRange([q.X,q.Y,q.Z,q.W]);values[j].S.AddRange([s.X,s.Y,s.Z]);
                    }
                }
                int input=Floats(times,1,"SCALAR",true);var samplers=new List<Obj>();var channels=new List<Obj>();
                for(int j=0;j<animated.Count;j++)foreach(var(path,data,size,type) in new[]{("translation",values[j].T,3,"VEC3"),("rotation",values[j].R,4,"VEC4"),("scale",values[j].S,3,"VEC3")})
                {channels.Add(D("sampler",samplers.Count,"target",D("node",animated[j].Node,"path",path)));samplers.Add(D("input",input,"output",Floats(data.ToArray(),size,type),"interpolation","LINEAR"));}
                animations.Add(D("name",$"{actor.Root.Name} {clip.Name}","samplers",samplers,"channels",channels));
            }
        }
        finally
        {
            actor.Clip=oldClip;actor.Time=oldTime;actor.Playing=oldPlaying;
            foreach(var state in saved){var o=state.Object;o.GeometryKey=state.GeometryKey;o.MaterialKey=state.MaterialKey;o.RliColors=state.RliColors;o.DrawFlags=state.DrawFlags;o.Flags=state.Flags;o.PreviewHidden=state.PreviewHidden;o.AppearanceRevision=state.AppearanceRevision;o.PreviewMatrix=state.PreviewMatrix;if(o.ActorNode!=null){o.ActorNode.Links=state.Links!;o.ActorNode.Matrices=state.Matrices!;}}
        }
    }

    private int Mesh(SceneObject obj,Geometry geo,Vector3[] source,SkinBinding[]? binding)
    {
        var normals=AnimationMath.Normals(source,geo.Parts);var primitives=new List<Obj>();
        List<(int Joint,float Weight)>[] influences=source.Select(_=>new List<(int,float)>()).ToArray();
        if(binding!=null)
        {
            for(int b=0;b<binding.Length;b++)foreach(uint packed in binding[b].PackedWeights){int v=(int)(packed&65535);float weight=BitConverter.UInt32BitsToSingle(packed);if(v<influences.Length&&float.IsFinite(weight)&&weight>0)influences[v].Add((b,weight));}
            foreach(var list in influences)
            {
                if(list.Count==0)list.Add((binding.Length,1));
                float sum=list.Sum(i=>i.Weight);
                for(int i=0;i<list.Count;i++)list[i]=(list[i].Joint,list[i].Weight/sum);
            }
        }
        for(int partIndex=0;partIndex<geo.Parts.Count;partIndex++)
        {
            var part=geo.Parts[partIndex];if(part.VertexIndices.Length<3 || geo.Particles!=null && partIndex>=geo.ParticlePart)continue;
            var effective=resolver.Materials.GetValueOrDefault(obj.MaterialKey);
            if(effective?.Type==4&&effective.SubMaterialKeys.Count>0)effective=resolver.Materials.GetValueOrDefault(effective.SubMaterialKeys[Math.Clamp(part.MaterialId,0,effective.SubMaterialKeys.Count-1)]);
            var layers=JadeMaterialLayer.Read(effective?.SourceBytes).Where(l=>!l.Disabled).ToList();
            var layer=layers.FirstOrDefault(l=>l.WriteRgb && l.Blend is 0 or 7 && l.Texture is not(0 or uint.MaxValue))??layers.FirstOrDefault(l=>l.WriteRgb && l.Texture is not(0 or uint.MaxValue));
            if(layers.Count>1)warnings.Add("Multi-pass Jade materials export their primary color layer; game-specific effects are not glTF shaders.");
            int material=Material(obj,effective,layer);
            var pos=new List<float>();var ns=new List<float>();var uv=new List<float>();
            foreach(int corner in Enumerable.Range(0,part.VertexIndices.Length))
            {
                int v=part.VertexIndices[corner];var p=Geometry.ToRender(source[v]);var n=Geometry.ToRender(normals[v]);n=n.LengthSquared()>1e-15f?Vector3.Normalize(n):Vector3.UnitY;
                pos.AddRange([p.X,p.Y,p.Z]);ns.AddRange([n.X,n.Y,n.Z]);
                int ti=corner<part.UvIndices.Length?part.UvIndices[corner]:-1;var tex=ti>=0&&ti<geo.Uvs.Length?geo.Uvs[ti]:Vector2.Zero;
                if(layer!=null)
                {
                    if(layer.UvMode==6){var point=source[v];int space=layer.Custom>>6&3;if(space>=2)warnings.Add("View/bone planar UV is approximated with world projection.");if(space!=0)point=TransformData.FromRender(Vector3.Transform(Geometry.ToRender(point),obj.RenderMatrix));int axis=layer.Custom>>4&3;tex=axis switch{0=>new(point.Y,point.Z),1=>new(point.X,point.Z),2=>new(point.X,point.Y),_=>new(Vector3.Dot(point,new(.70711f,-.70711f,0)),Vector3.Dot(point,new(-.57735f,-.57735f,.57735f)))};}
                    if(layer.UvMode is 2 or 3)warnings.Add("View-dependent environment/diffuse UV uses the stored UV map in the export.");
                    var m=layer.UvMatrix(0,out var shift);tex=new(m.X*tex.X+m.Y*tex.Y+shift.X,m.Z*tex.X+m.W*tex.Y+shift.Y);
                }
                uv.AddRange([tex.X,1-tex.Y]);
            }
            var attributes=D("POSITION",Floats(pos.ToArray(),3,"VEC3",true,34962),"NORMAL",Floats(ns.ToArray(),3,"VEC3",false,34962));
            if(options.Textures)attributes["TEXCOORD_0"]=Floats(uv.ToArray(),2,"VEC2",false,34962);
            if(binding!=null)
            {
                int sets=(influences.Max(i=>i.Count)+3)/4;
                for(int set=0;set<sets;set++)
                {
                    var joints=new List<ushort>();var weights=new List<float>();
                    foreach(int v in part.VertexIndices)for(int k=0;k<4;k++){int i=set*4+k;var item=i<influences[v].Count?influences[v][i]:(0,0f);joints.Add((ushort)item.Item1);weights.Add(item.Item2);}
                    attributes[$"JOINTS_{set}"]=Ushorts(joints.ToArray());attributes[$"WEIGHTS_{set}"]=Floats(weights.ToArray(),4,"VEC4",false,34962);
                }
            }
            primitives.Add(D("attributes",attributes,"material",material,"mode",4));
        }
        int index=meshes.Count;meshes.Add(D("name",obj.Name,"primitives",primitives));return index;
    }
    private int Material(SceneObject obj,ResolvedMaterial? source,JadeMaterialLayer? layer)
    {
        var pbr=D("baseColorFactor",new[]{1f,1f,1f,1f},"metallicFactor",0f,"roughnessFactor",1f);
        var n=D("name",$"{obj.Name} {source?.Id.ToString()??"default"}","pbrMetallicRoughness",pbr,"doubleSided",true,"extensions",D("KHR_materials_unlit",D()));
        if(SceneRenderer.IsCollision(obj))pbr["baseColorFactor"]=new[]{.25f,.6f,1f,1f};
        else if(options.Textures&&layer!=null&&resolver.TryResolveTexture(layer.Texture,out var tex))
        {
            string key=tex.ToTextureAsset().Name;
            if(!imageCache.TryGetValue(key,out int image))
            {
                image=images.Count;images.Add(D("name",tex.Id.ToString(),"mimeType","image/png","bufferView",View(Png(tex))));imageCache[key]=image;
            }
            int sampler=textures.Count; // one sampler per material to preserve wrapping
            int texture=textures.Count;textures.Add(D("source",image,"sampler",sampler));
            pbr["baseColorTexture"]=D("index",texture);
            samplers.Add(D("wrapS",(layer.Flags&1)!=0?10497:33071,"wrapT",(layer.Flags&2)!=0?10497:33071,"magFilter",(layer.Flags&4)!=0?9729:9728,"minFilter",(layer.Flags&4)!=0?9729:9728));
        }
        if(layer!=null)
        {
            if(layer.Blend is not(0 or 7)){n["alphaMode"]="BLEND";if((layer.Flags&0x400)!=0)pbr["baseColorFactor"]=new[]{1f,1f,1f,layer.OverrideAlpha};}
            else if((layer.Flags&0x10)!=0){n["alphaMode"]="MASK";n["alphaCutoff"]=(layer.Flags>>22&0xfc)/255f;}
        }
        int index=materials.Count;materials.Add(n);return index;
    }
    private readonly List<Obj> samplers=[];
    private static byte[] Png(ResolvedTexture tex)
    {
        byte[] rgba=Preview.DecodeTga(tex.ToTextureAsset());
        using var bitmap=new System.Drawing.Bitmap(tex.Width,tex.Height,PixelFormat.Format32bppArgb);
        var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,tex.Width,tex.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{for(int i=0;i<rgba.Length;i+=4)(rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);for(int y=0;y<tex.Height;y++)Marshal.Copy(rgba,y*tex.Width*4,data.Scan0+y*data.Stride,tex.Width*4);}finally{bitmap.UnlockBits(data);}
        using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();
    }
    private int View(byte[] bytes,int target=0)
    {
        while(binary.Position%4!=0)binary.WriteByte(0);
        var view=D("buffer",0,"byteOffset",(int)binary.Position,"byteLength",bytes.Length);if(target!=0)view["target"]=target;
        views.Add(view);binary.Write(bytes);return views.Count-1;
    }
    private int Floats(float[] values,int size,string type,bool bounds=false,int target=0)
    {
        if(values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Non-finite values in exported geometry/animation.");
        var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);var a=D("bufferView",View(bytes,target),"componentType",5126,"count",values.Length/size,"type",type);
        if(bounds){a["min"]=Enumerable.Range(0,size).Select(i=>Enumerable.Range(0,values.Length/size).Min(j=>values[j*size+i])).ToArray();a["max"]=Enumerable.Range(0,size).Select(i=>Enumerable.Range(0,values.Length/size).Max(j=>values[j*size+i])).ToArray();}
        accessors.Add(a);return accessors.Count-1;
    }
    private int Ushorts(ushort[] values)
    {
        var bytes=new byte[values.Length*2];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);accessors.Add(D("bufferView",View(bytes,34962),"componentType",5123,"count",values.Length/4,"type","VEC4"));return accessors.Count-1;
    }
    private void Write(string path,string name)
    {
        var document=D("asset",D("version","2.0","generator","JadeView"),"scene",0,"scenes",new[]{D("name",name,"nodes",roots)},"nodes",nodes);
        if(meshes.Count>0){document["meshes"]=meshes;document["materials"]=materials;document["extensionsUsed"]=new[]{"KHR_materials_unlit"};}
        if(images.Count>0){document["images"]=images;document["textures"]=textures;document["samplers"]=samplers;}
        if(skins.Count>0)document["skins"]=skins;if(animations.Count>0)document["animations"]=animations;
        if(binary.Length>0){document["buffers"]=new[]{D("byteLength",(int)binary.Length)};document["bufferViews"]=views;document["accessors"]=accessors;}
        if(warnings.Count>0)document["extras"]=D("exportWarnings",warnings.ToArray());
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(document);int jsonLength=(json.Length+3)&~3;int binLength=((int)binary.Length+3)&~3;
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
        using(var output=new BinaryWriter(File.Create(temporary)))
        {
        output.Write(0x46546c67u);output.Write(2u);output.Write((uint)(12+8+jsonLength+(binLength>0?8+binLength:0)));output.Write(jsonLength);output.Write(0x4e4f534au);output.Write(json);for(int i=json.Length;i<jsonLength;i++)output.Write((byte)32);
        if(binLength>0){output.Write(binLength);output.Write(0x004e4942u);binary.Position=0;binary.CopyTo(output.BaseStream);for(long i=binary.Length;i<binLength;i++)output.Write((byte)0);}
        }
        File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
