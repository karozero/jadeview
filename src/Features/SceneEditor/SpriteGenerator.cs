using System.Numerics;

namespace JadeView;

// PC SPG v8. Algorithm reference: IrisTool 0.26 iristool.b.a.e and a.a.a.b.s.
// These are procedural additions to a carrier GRO, not archive geometry replacements.
internal static class SpriteGenerator
{
    public static Geometry? Build(Geometry source, SceneObject owner, byte[] data)
    {
        if(data.Length!=156 || Preview.I32(data,4)!=8) return null;
        float F(int p)=>BitConverter.ToSingle(data,p);
        float width=F(8),aspect=F(12),offset=F(20),threshold=F(24),jitter=F(36);
        if(new[]{width,aspect,offset,threshold,jitter}.Any(v=>!float.IsFinite(v)))return null;
        int maxDepth=Math.Clamp(Preview.I32(data,28),0,8); uint mask=Preview.U32(data,32);
        if(maxDepth==0 || width<=0 || aspect<=0)return null;
        var positions=source.Positions.ToList();var uvs=source.Uvs.ToList();
        var colors=Enumerable.Range(0,positions.Count).Select(i=>i<source.VertexColors.Length?source.VertexColors[i]:0u).ToList();
        var result=new Geometry{Key=source.Key,Offset=source.Offset,Size=source.Size,Skin=source.Skin,Objects=source.Objects.ToList(),Parts=source.Parts.ToList()};
        var bankIndices=Enumerable.Range(0,4).Select(_=>new List<int>()).ToArray();
        var bankUvs=Enumerable.Range(0,4).Select(_=>new List<int>()).ToArray();
        var normals=AnimationMath.Normals(source.Positions,source.Parts);
        // Iris uses a random table of 256 entries, reused for each render batch.
        // A fixed seed makes preview/export repeatable; the game uses a runtime seed.
        var random=new Random(0x535047);var noise=Enumerable.Range(0,257).Select(_=>random.Next(256)/256f-.5f).ToArray();noise[256]=0;
        int emitted=0;
        uint Color(int i)=>i<owner.RliColors.Length?owner.RliColors[i]:i<source.VertexColors.Length?source.VertexColors[i]:0u;
        uint Mix(uint a,uint b,uint c,float u,float v)
        {
            uint color=0;for(int channel=0;channel<4;channel++){
                int shift=channel*8;float value=(a>>shift&255)*(1-u-v)+(b>>shift&255)*u+(c>>shift&255)*v;
                color|=(uint)Math.Clamp((int)value,0,255)<<shift;
            }return color;
        }
        foreach(var part in source.Parts)
        {
            if((mask & (1u<<(part.MaterialId&31)))==0)continue;
            for(int i=0;i<part.VertexIndices.Length;i+=3)
            {
                int ia=part.VertexIndices[i],ib=part.VertexIndices[i+1],ic=part.VertexIndices[i+2];
                var a=source.Positions[ia]+normals[ia]*offset;var b=source.Positions[ib]+normals[ib]*offset;var c=source.Positions[ic]+normals[ic]*offset;
                var ab=b-a;var ac=c-a;int depth=0;float edge=Math.Min(ab.LengthSquared(),ac.LengthSquared());
                if(threshold==0)depth=maxDepth;else while(edge>threshold*threshold && depth<maxDepth){edge*=.25f;depth++;}
                if(depth==0)continue;int segments=1<<depth;var stepAB=ab/segments;var stepAC=ac/segments;
                uint ca=Color(ia),cb=Color(ib),cc=Color(ic);
                void Emit(float u,float v)
                {
                    if(emitted>=65536)throw new InvalidDataException("SPG preview exceeds 65,536 sprites per carrier.");
                    int batch=emitted&255,bank=batch&3,slot=batch>>2;int bp=72+bank*16;
                    var center=a+ab*u+ac*v+(stepAB*noise[batch]+stepAC*noise[batch+1])*jitter;
                    float scale=1-noise[slot]*F(bp+8);float h=width*F(bp+4)*scale,w=h*aspect*F(bp+12);
                    emitted++;
                    if(!float.IsFinite(h)||!float.IsFinite(w)||h<=0||w<=0)return;
                    int first=positions.Count,uv=uvs.Count;
                    result.Billboards.Add(new(first,center,w,h,(Preview.U32(data,140)&16)!=0));
                    // A fixed facing is used in GLB; the viewport replaces this basis with the camera basis.
                    positions.AddRange(new[]{center+new Vector3(w,0,-h),center+new Vector3(-w,0,-h),center+new Vector3(-w,0,h),center+new Vector3(w,0,h)});
                    uvs.AddRange(new[]{new Vector2(1,0),new Vector2(0,0),new Vector2(0,1),new Vector2(1,1)});
                    uint tint=Mix(ca,cb,cc,u,v);colors.AddRange(new[]{tint,tint,tint,tint});
                    int[] corners=[0,1,2,0,2,3];bankIndices[bank].AddRange(corners.Select(k=>first+k));bankUvs[bank].AddRange(corners.Select(k=>uv+k));
                }
                for(int x=1;x<segments;x++)for(int y=1;y<segments-x;y++)Emit(x/(float)segments,y/(float)segments);
                if(ia>ib)for(int x=1;x<segments;x++)Emit(x/(float)segments,0);
                if(ib>ic)for(int x=1;x<segments;x++)Emit(1-x/(float)segments,x/(float)segments);
                if(ic>ia)for(int x=1;x<segments;x++)Emit(0,x/(float)segments);
            }
        }
        if(result.Billboards.Count==0)return null;
        for(int bank=0;bank<4;bank++)if(bankIndices[bank].Count>0)result.Parts.Add(new((int)(Preview.U32(data,72+bank*16)&255),bankIndices[bank].ToArray(),bankUvs[bank].ToArray()));
        result.Positions=positions.ToArray();result.Uvs=uvs.ToArray();result.VertexColors=colors.ToArray();return result;
    }

    public static Vector3[] FaceCamera(Geometry geo, Vector3[]? posed, Matrix4x4 world, Vector3 cameraRight, Vector3 cameraUp, float time)
    {
        var positions=(Vector3[])(posed??geo.Positions).Clone();
        if(!Matrix4x4.Invert(world,out var inverse))return positions;
        // Do not normalize local vectors: the inverse scale is required to keep world-space facing.
        var right=TransformData.FromRender(Vector3.TransformNormal(cameraRight,inverse));
        var up=TransformData.FromRender(Vector3.TransformNormal(cameraUp,inverse));
        // Match Iris's model scale for dimensions, while preserving orthogonal camera axes.
        float scale=(new Vector3(world.M11,world.M12,world.M13).Length()+new Vector3(world.M21,world.M22,world.M23).Length()+new Vector3(world.M31,world.M32,world.M33).Length())/3;
        right*=scale;up*=scale;
        foreach(var sprite in geo.Billboards)
        {
            var center=posed!=null?posed[sprite.Vertex]-geo.Positions[sprite.Vertex]+sprite.Center:sprite.Center;
            var r=right*sprite.HalfWidth;var u=up*sprite.HalfHeight;
            var wind=sprite.Wind?right*(MathF.Sin(time*2.5f+sprite.Vertex*.17f)*.2f):Vector3.Zero;
            positions[sprite.Vertex]=center+r-u;positions[sprite.Vertex+1]=center-r-u;
            positions[sprite.Vertex+2]=center-r+u+wind;positions[sprite.Vertex+3]=center+r+u+wind;
        }
        return positions;
    }
}
