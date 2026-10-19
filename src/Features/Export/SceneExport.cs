using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

// glTF 2.0 / GLB, right-handed Y-up. No temporary image files or GPU context required.
public sealed partial class SceneExporter
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
