using System.Numerics;
namespace JadeView;

// IrisTool 0.26 PAG v11 (a.a.a.a.g / iristool.b.g), independent state per GAO instance.
public sealed class ParticleDefinition
{
    public int Capacity, Flags, Shape, Direction;
    public float[] Values=[], Extra=[];
    public Vector3 Acceleration;
    public float Damping, Expansion;
    public uint Color;
    public static ParticleDefinition? Read(byte[] bytes,int start,int size)
    {
        if(size<8)return null;
        int version=Preview.I32(bytes,start+4);
        if(version is not (0 or 9 or 11) || size!=(version==11?158:version==9?152:75))return null;
        float F(int p)=>BitConverter.ToSingle(bytes,start+p);
        var d=new ParticleDefinition{Capacity=Preview.I32(bytes,start),Values=new float[19],Extra=new float[11]};
        if(version==0)
        {
            d.Flags=bytes[start+8];d.Shape=bytes[start+9];d.Direction=bytes[start+10];
            d.Values[1]=F(11);d.Values[2]=F(15);d.Values[3]=F(19);
            for(int i=0;i<4;i++)d.Values[5+i]=F(31+i*4);
            d.Values[9]=d.Values[10]=F(47);d.Values[11]=d.Values[12]=F(51);d.Values[13]=F(55);d.Values[14]=F(59);
            d.Acceleration=new(F(63),F(67),F(71));
        }
        else
        {
            int shift=version==9?-2:0;
            d.Flags=version==9?Preview.U16(bytes,start+8):Preview.I32(bytes,start+8);d.Shape=bytes[start+12+shift];d.Direction=bytes[start+13+shift];
            d.Values=Enumerable.Range(0,19).Select(i=>F(14+shift+i*4)).ToArray();d.Acceleration=new(F(90+shift),F(94+shift),F(98+shift));
            d.Damping=F(102+shift);d.Expansion=F(106+shift);d.Color=Preview.U32(bytes,start+110+shift);
            for(int i=0;i<(version==9?10:11);i++)d.Extra[i]=F(114+shift+i*4);
        }
        if(d.Capacity is <0 or >16384 || d.Values.Concat(d.Extra).Any(v=>!float.IsFinite(v)) || !float.IsFinite(d.Acceleration.LengthSquared()))return null;
        return d;
    }
    public Geometry Attach(Geometry carrier,SceneObject owner)
    {
        var particles=Geometry(carrier.Key,carrier.Offset,carrier.Size);int vertex=carrier.Positions.Length,uv=carrier.Uvs.Length;
        var colors=Enumerable.Range(0,vertex).Select(i=>i<owner.RliColors.Length?owner.RliColors[i]:i<carrier.VertexColors.Length?carrier.VertexColors[i]:0u).Concat(particles.VertexColors).ToArray();
        return new Geometry{Key=carrier.Key,Offset=carrier.Offset,Size=carrier.Size,Objects=carrier.Objects.ToList(),Skin=carrier.Skin,
            Positions=carrier.Positions.Concat(particles.Positions).ToArray(),Uvs=carrier.Uvs.Concat(particles.Uvs).ToArray(),VertexColors=colors,
            Parts=carrier.Parts.Concat(particles.Parts.Select(p=>new Geometry.Part(p.MaterialId,p.VertexIndices.Select(i=>i+vertex).ToArray(),p.UvIndices.Select(i=>i+uv).ToArray()))).ToList(),
            Billboards=carrier.Billboards.ToList(),Particles=this,ParticleStart=vertex,ParticlePart=carrier.Parts.Count};
    }
    public Geometry Geometry(uint key,int offset,int size)
    {
        var geo=new Geometry{Key=key,Offset=offset,Size=size,Particles=this,Positions=new Vector3[Capacity*4],Uvs=[new(0,1),new(1,1),new(1,0),new(0,0)],VertexColors=new uint[Capacity*4]};
        var vertices=new int[Capacity*6];var uv=new int[vertices.Length];
        int[] corners=[2,1,0,0,3,2];for(int i=0;i<Capacity;i++)for(int j=0;j<6;j++){vertices[i*6+j]=i*4+corners[j];uv[i*6+j]=corners[j];}
        if(Capacity>0)geo.Parts.Add(new(0,vertices,uv));return geo;
    }
}

internal sealed class ParticlePreview(ParticleDefinition definition,uint seed)
{
    private sealed class Particle
    {
        public Vector3 Position,Velocity;public float SizeX,SizeY,Age,Delay,Life,Decay,Angle,Spin;
    }
    private readonly Random random=new(unchecked((int)seed));
    private readonly List<Particle> particles=[];
    private float remainder;private double previous=-1;
    private float Between(float min,float max)=>min+(max-min)*(float)random.NextDouble();
    private float R()=>Between(-1,1);
    public (Vector3[] Positions,uint[] Colors) Update(Geometry geo,Vector3[] sourcePositions,Matrix4x4 world,Vector3 cameraRight,Vector3 cameraUp,double now)
    {
        var d=definition;var v=d.Values;
        float delta=previous<0?0:(float)Math.Clamp(now-previous,0,.1);previous=now;
        // Source-mesh emission and target/normal-based velocities need external geometry links.
        bool supported=d.Shape<=5 && d.Direction is <=3 or 5;
        if((d.Flags&64)!=0 && (d.Flags&128)==0 && supported)
        {
            for(int i=particles.Count-1;i>=0;i--)
            {
                var p=particles[i];p.Age+=delta;
                if(p.Age>p.Delay+p.Life+p.Decay){particles.RemoveAt(i);continue;}
                if(p.Age<p.Delay)continue;
                var force=d.Acceleration;
                if(Matrix4x4.Invert(AnimationMath.ToRender(world),out var inverse))force=TransformData.FromRender(Vector3.TransformNormal(Geometry.ToRender(force),inverse));
                p.Position+=p.Velocity*delta+force*(delta*delta*.5f);p.Velocity+=force*delta;
                if((d.Flags&8192)!=0)p.Velocity*=MathF.Pow(Math.Max(0,d.Damping),delta*60);
                // Iris uses world-Z clipping planes; apply them in Jade world coordinates.
                var wp=Vector3.Transform(p.Position,world);
                if((d.Flags&1024)!=0 && d.Extra[10]==0 && wp.Z<d.Extra[0]){wp.Z=d.Extra[0];p.Velocity.Z=-p.Velocity.Z*d.Extra[1];if(Matrix4x4.Invert(world,out var inv))p.Position=Vector3.Transform(wp,inv);}
                if((d.Flags&2048)!=0 && wp.Z>d.Extra[2]){wp.Z=d.Extra[2];p.Velocity.Z=-p.Velocity.Z*d.Extra[3];if(Matrix4x4.Invert(world,out var inv))p.Position=Vector3.Transform(wp,inv);}
                p.Angle+=p.Spin*delta;
            }
            remainder+=Math.Max(0,v[4])*delta;int count=Math.Min((int)remainder,d.Capacity-particles.Count);remainder-=count;
            if(count==0 && particles.Count>=d.Capacity)remainder=0;
            for(int i=0;i<count;i++)
            {
                var p=new Particle{SizeX=Between(v[9],v[10]),SizeY=Between(v[11],v[12]),Life=Math.Max(.001f,Between(v[13],v[14])),Decay=Math.Max(0,Between(v[15],v[16])),Delay=Math.Max(0,Between(v[17],v[18]))};
                if((d.Flags&4)!=0)p.SizeY=p.SizeX;
                float angle=Between(0,MathF.Tau),radius=Between(0,v[1]);
                p.Position=d.Shape switch{
                    1=>new(R()*v[1],0,R()*v[2]),
                    2=>new(MathF.Cos(angle)*radius,0,MathF.Sin(angle)*radius),
                    3=>new(MathF.Cos(angle)*radius,-Between(0,v[2]),MathF.Sin(angle)*radius),
                    4=>Sphere()*radius,
                    5=>new(R()*v[1],-Between(0,v[3]),R()*v[2]),_=>Vector3.Zero};
                p.Position.Y-=v[0];float speed=Between(v[5],v[6]);
                float a=Between(-v[7],v[7]),b=Between(-v[7],v[8]);
                p.Velocity=d.Direction switch{0=>new(0,-speed,0),1=>new(MathF.Sin(a)*speed,-MathF.Cos(a)*speed,0),
                    2=>new(MathF.Cos(b)*MathF.Sin(a)*speed,-MathF.Cos(a)*MathF.Cos(b)*speed,MathF.Sin(b)*speed),
                    3=>p.Position.LengthSquared()>1e-12f?Vector3.Normalize(p.Position)*speed:Vector3.Zero,
                    5=>Normalize(new Vector3(d.Extra[4],d.Extra[5],d.Extra[6])-p.Position)*speed,_=>Vector3.Zero};
                if((d.Flags&4096)!=0){p.Angle=Between(d.Extra[4],d.Extra[5]);p.Spin=Between(d.Extra[6],d.Extra[7]);}
                particles.Add(p);
            }
        }
        var positions=(Vector3[])sourcePositions.Clone();var colors=(uint[])geo.VertexColors.Clone();
        int baseVertex=geo.ParticleStart;
        Array.Clear(positions,baseVertex,positions.Length-baseVertex);Array.Clear(colors,baseVertex,colors.Length-baseVertex);
        Matrix4x4.Invert(AnimationMath.ToRender(world),out var local);
        var right=TransformData.FromRender(Vector3.TransformNormal(cameraRight,local));var up=TransformData.FromRender(Vector3.TransformNormal(cameraUp,local));
        for(int i=0;i<particles.Count;i++)
        {
            var p=particles[i];if(p.Age<p.Delay)continue;
            float age=p.Age-p.Delay,factor=1,fade=1;
            if(age<p.Life){float t=age/p.Life;if((d.Flags&512)!=0)factor=t;if((d.Flags&256)!=0)fade=t;}
            else {float t=Math.Clamp(1-(age-p.Life)/Math.Max(.001f,p.Decay),0,1);if((d.Flags&16)!=0)factor=t;else if((d.Flags&32)!=0)factor=1+(1-t)*d.Expansion;if((d.Flags&8)!=0)fade=t;}
            uint tint=d.Color;uint alpha=(uint)((tint>>24)*fade);
            if((d.Flags&2)!=0)tint=((uint)((tint&255)*fade))|((uint)((tint>>8&255)*fade)<<8)|((uint)((tint>>16&255)*fade)<<16)|(alpha<<24);else tint=(tint&0xffffff)|(alpha<<24);
            var r=right*(p.SizeX*factor);var u=up*(p.SizeY*factor);float sin=MathF.Sin(p.Angle),cos=MathF.Cos(p.Angle);
            var rr=r*cos-u*sin;u=u*cos+r*sin;r=rr;
            positions[baseVertex+i*4]=p.Position-r-u;positions[baseVertex+i*4+1]=p.Position-r+u;positions[baseVertex+i*4+2]=p.Position+r+u;positions[baseVertex+i*4+3]=p.Position+r-u;
            Array.Fill(colors,tint,baseVertex+i*4,4);
        }
        return(positions,colors);
    }
    private Vector3 Sphere(){float z=R(),a=Between(0,MathF.Tau),r=MathF.Sqrt(1-z*z);return new(r*MathF.Cos(a),r*MathF.Sin(a),z);}
    private static Vector3 Normalize(Vector3 v)=>v.LengthSquared()>1e-12f?Vector3.Normalize(v):Vector3.Zero;
}
