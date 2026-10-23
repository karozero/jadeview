using System.Numerics;
namespace JadeView;

// IrisTool 0.26 PAG v11 (a.a.a.a.g / iristool.b.g), independent state per GAO instance.
public sealed class ParticleDefinition
{
    private const int TransparentFlag = 0x00000001;
    private const int ActiveFlag = 0x00000040;
    private const int FreezeFlag = 0x00000080;
    private const int DontSortParticlesFlag = 0x00010000;

    public int Capacity, Flags, Shape, Direction;
    public float[] Values=[], Extra=[];
    public Vector3 Acceleration;
    public float Damping, Expansion;
    public uint Color;
    public bool IsTransparent => (Flags & TransparentFlag) != 0;
    public bool IsActive => (Flags & ActiveFlag) != 0;
    public bool IsFrozen => (Flags & FreezeFlag) != 0;
    public bool SortParticles => (Flags & DontSortParticlesFlag) == 0;
    internal void CopyFrom(ParticleDefinition source)
    {
        Capacity = source.Capacity;
        Flags = source.Flags;
        Shape = source.Shape;
        Direction = source.Direction;
        Acceleration = source.Acceleration;
        Damping = source.Damping;
        Expansion = source.Expansion;
        Color = source.Color;
        source.Values.CopyTo(Values, 0);
        source.Extra.CopyTo(Extra, 0);
    }

    internal ParticleDefinition Clone()
    {
        var clone = new ParticleDefinition { Values = new float[Values.Length], Extra = new float[Extra.Length] };
        clone.CopyFrom(this);
        return clone;
    }

    internal bool SetFloatParameter(int index, float value)
    {
        if (!float.IsFinite(value)) return false;
        if (index is >= 0 and <= 3)
        {
            Values[index] = value;
            return true;
        }
        if (index is >= 4 and <= 17)
        {
            Values[index + 1] = value;
            return true;
        }
        if (index == 18) Expansion = value;
        else if (index is >= 19 and <= 21) Acceleration = SetComponent(Acceleration, index - 19, value);
        else if (index == 22) Damping = value;
        else if (index is >= 23 and <= 30) Extra[index - 23] = value;
        else if (index is >= 32 and <= 34) Extra[index - 24] = value;
        else return false;
        return true;

        static Vector3 SetComponent(Vector3 vector, int component, float componentValue) => component switch
        {
            0 => vector with { X = componentValue },
            1 => vector with { Y = componentValue },
            _ => vector with { Z = componentValue }
        };
    }

    public static ParticleDefinition? Read(byte[] bytes,int start,int size)
    {
        if(size<8)return null;
        int version=Preview.I32(bytes,start+4);
        if(version is not (0 or 8 or 9 or 10 or 11) || size!=(version switch{0=>75,8=>144,9=>152,10=>156,11=>158,_=>0}))return null;
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
            int shift=version is 8 or 9 or 10 ? -2 : 0;
            d.Flags=version is 8 or 9 or 10 ? Preview.U16(bytes,start+8) : Preview.I32(bytes,start+8);d.Shape=bytes[start+12+shift];d.Direction=bytes[start+13+shift];
            d.Values=Enumerable.Range(0,19).Select(i=>F(14+shift+i*4)).ToArray();d.Acceleration=new(F(90+shift),F(94+shift),F(98+shift));
            d.Damping=F(102+shift);d.Expansion=F(106+shift);d.Color=Preview.U32(bytes,start+110+shift);
            int extraCount=version switch{8=>8,9=>10,_=>11};
            for(int i=0;i<extraCount;i++)d.Extra[i]=F(114+shift+i*4);
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
    private const int AccelerationAffectsSpeed = 0x8000;
    private const float ParticleUpdateInterval = 1f / 60;

    private sealed class Particle
    {
        public Vector3 Position,Velocity;public float SizeX,SizeY,Age,Delay,Life,Decay,Angle,Spin;
        public float CameraDepth;

        public static int CompareByCameraDepth(Particle first, Particle second) =>
            second.CameraDepth.CompareTo(first.CameraDepth);
    }
    private readonly int randomSeed = unchecked((int)seed);
    private Random random = new(unchecked((int)seed));
    private readonly List<Particle> particles=[];
    private Vector3[] positionBuffer = [];
    private uint[] colorBuffer = [];
    private Geometry? sourceNormalsGeometry;
    private Vector3[]? sourceNormalsPositions;
    private Vector3[]? sourceNormalsCache;
    private int sourceNormalsPartCount;
    private List<Geometry.Part>? sourcePartsCache;
    private float emissionRemainder,simulationRemainder;private double previous=-1;
    internal ParticleDefinition Definition => definition;
    private float Between(float min,float max)=>min+(max-min)*(float)random.NextDouble();
    private float R()=>Between(-1,1);
    public (Vector3[] Positions,uint[] Colors) Update(Geometry geo,Vector3[] sourcePositions,Matrix4x4 world,
        Vector3 cameraPosition,Vector3 cameraForward,Vector3 cameraRight,Vector3 cameraUp,double now,
        Geometry? emissionSourceGeometry = null, Vector3[]? emissionSourcePositions = null,
        Matrix4x4? emissionSourceMatrix = null,
        IReadOnlyList<CinematicParticleShift>? cinematicShifts = null)
    {
        var d=definition;var v=d.Values;
        if (previous >= 0 && now < previous) ResetSimulation();
        double previousTime = previous;
        float delta=previous<0?0:(float)Math.Clamp(now-previous,0,.1);previous=now;
        bool usesSourceMesh = d.Shape == 6 && emissionSourceGeometry != null
            && emissionSourcePositions != null && emissionSourceMatrix != null;
        Vector3[]? emissionPositions = usesSourceMesh ? emissionSourcePositions : null;
        int sourceVertexLimit = usesSourceMesh
            ? emissionSourceGeometry!.Particles == null ? emissionPositions!.Length : emissionSourceGeometry.ParticleStart
            : 0;
        int sourceVertexCount = emissionPositions == null ? 0 : Math.Min(sourceVertexLimit, emissionPositions.Length);
        bool hasSourceMesh = usesSourceMesh && sourceVertexCount > 0;
        bool hasInverseWorld = Matrix4x4.Invert(AnimationMath.ToRender(world), out var inverseWorld);
        bool supportedShape = d.Shape <= 5 || usesSourceMesh && hasSourceMesh && hasInverseWorld;
        bool supportedDirection = d.Direction <= 3 || d.Direction == 5 || d.Direction == 4 && usesSourceMesh && hasSourceMesh && hasInverseWorld;
        Vector3[]? sourceNormals = null;
        if (usesSourceMesh && hasSourceMesh && d.Direction == 4)
        {
            int sourcePartCount = emissionSourceGeometry!.Particles == null
                ? emissionSourceGeometry.Parts.Count
                : Math.Min(emissionSourceGeometry.ParticlePart, emissionSourceGeometry.Parts.Count);
            if (!ReferenceEquals(sourceNormalsGeometry, emissionSourceGeometry)
                || sourceNormalsPartCount != sourcePartCount)
            {
                sourceNormalsGeometry = emissionSourceGeometry;
                sourceNormalsPartCount = sourcePartCount;
                sourcePartsCache = emissionSourceGeometry.Parts.Take(sourcePartCount).ToList();
                sourceNormalsPositions = null;
                sourceNormalsCache = null;
            }
            if (!ReferenceEquals(sourceNormalsPositions, emissionPositions))
            {
                sourceNormalsPositions = emissionPositions;
                sourceNormalsCache = AnimationMath.Normals(emissionPositions!, sourcePartsCache!);
            }
            sourceNormals = sourceNormalsCache;
        }

        // Jaded emits type 6 particles from the attached geometry's vertices and
        // uses the corresponding vertex normal for speed type 4.
        bool supported = supportedShape && supportedDirection;
        if (d.IsActive && !d.IsFrozen && supported)
        {
            double segmentStart = previousTime;
            if (previousTime >= 0 && delta > 0 && cinematicShifts is { Count: > 0 })
            {
                foreach (var shift in cinematicShifts)
                {
                    if (!float.IsFinite(shift.Time) || shift.Time <= previousTime
                        || shift.Time > previousTime + delta) continue;
                    AdvanceSimulation((float)Math.Max(0, shift.Time - segmentStart));
                    ApplyShift(shift.Offset);
                    segmentStart = shift.Time;
                }
            }
            AdvanceSimulation((float)Math.Max(0, previousTime + delta - segmentStart));
        }
        else if (cinematicShifts is { Count: > 0 })
        {
            foreach (var shift in cinematicShifts)
                if (previousTime >= 0 && delta > 0 && float.IsFinite(shift.Time)
                    && shift.Time > previousTime && shift.Time <= previousTime + delta)
                    ApplyShift(shift.Offset);
        }
        if (positionBuffer.Length != sourcePositions.Length) positionBuffer = new Vector3[sourcePositions.Length];
        if (colorBuffer.Length != geo.VertexColors.Length) colorBuffer = new uint[geo.VertexColors.Length];
        Array.Copy(sourcePositions, positionBuffer, sourcePositions.Length);
        Array.Copy(geo.VertexColors, colorBuffer, geo.VertexColors.Length);
        var positions=positionBuffer;var colors=colorBuffer;
        int baseVertex=geo.ParticleStart;
        Array.Clear(positions,baseVertex,positions.Length-baseVertex);Array.Clear(colors,baseVertex,colors.Length-baseVertex);
        Matrix4x4.Invert(AnimationMath.ToRender(world),out var local);
        var right=TransformData.FromRender(Vector3.TransformNormal(cameraRight,local));var up=TransformData.FromRender(Vector3.TransformNormal(cameraUp,local));
        if(d.IsTransparent && d.SortParticles)
        {
            foreach (var particle in particles)
                particle.CameraDepth = Vector3.Dot(
                    Geometry.ToRender(Vector3.Transform(particle.Position, world)) - cameraPosition,
                    cameraForward);
            particles.Sort(Particle.CompareByCameraDepth);
        }
        int renderIndex=0;
        if (d.IsActive)
        foreach(var p in particles)
        {
            float age=p.Age-p.Delay,factor=1,fade=1;
            if(p.Age<p.Delay)
            {
                float birthProgress=Math.Clamp(p.Age/Math.Max(.001f,p.Delay),0,1);
                if((d.Flags&512)!=0)factor=birthProgress;
                if((d.Flags&256)!=0)fade=birthProgress;
            }
            else if(age>=p.Life){float t=Math.Clamp(1-(age-p.Life)/Math.Max(.001f,p.Decay),0,1);if((d.Flags&16)!=0)factor=t;else if((d.Flags&32)!=0)factor=1+(1-t)*d.Expansion;if((d.Flags&8)!=0)fade=t;}
            uint tint=d.Color;uint alpha=(uint)((tint>>24)*fade);
            if((d.Flags&2)!=0)tint=((uint)((tint&255)*fade))|((uint)((tint>>8&255)*fade)<<8)|((uint)((tint>>16&255)*fade)<<16)|(alpha<<24);else tint=(tint&0xffffff)|(alpha<<24);
            var r=right*(p.SizeX*factor);var u=up*(p.SizeY*factor);float sin=MathF.Sin(p.Angle),cos=MathF.Cos(p.Angle);
            // Jaded rotates the billboard basis as X'=X*cos+Y*sin, Y'=Y*cos-X*sin.
            // Keep the same handedness here; the previous signs mirrored PAG sprites.
            var rr=r*cos+u*sin;u=u*cos-r*sin;r=rr;
            positions[baseVertex+renderIndex*4]=p.Position-r-u;positions[baseVertex+renderIndex*4+1]=p.Position-r+u;positions[baseVertex+renderIndex*4+2]=p.Position+r+u;positions[baseVertex+renderIndex*4+3]=p.Position+r-u;
            Array.Fill(colors,tint,baseVertex+renderIndex*4,4);
            renderIndex++;
        }
        return(positions,colors);

        void ApplyShift(Vector3 offset)
        {
            foreach (var particle in particles) particle.Position += offset;
        }

        void AdvanceSimulation(float segmentDelta)
        {
            if (segmentDelta <= 0) return;
            simulationRemainder += segmentDelta;
            int simulationSteps = (int)(simulationRemainder / ParticleUpdateInterval);
            float simulationDelta = simulationSteps * ParticleUpdateInterval;
            simulationRemainder -= simulationDelta;
            for (int index = particles.Count - 1; index >= 0; index--)
            {
                var particle = particles[index];
                particle.Age += simulationDelta;
                if (particle.Age > particle.Delay + particle.Life + particle.Decay)
                {
                    particles.RemoveAt(index);
                    continue;
                }
                for (int step = 0; step < simulationSteps; step++)
                    UpdateParticleMotion(particle, ParticleUpdateInterval, world, d);
                particle.Angle += particle.Spin * simulationDelta;
                particle.Position.X += MathF.Sin(particle.Age * d.Extra[8]) * simulationDelta;
                particle.Position.Y += MathF.Sin(particle.Age * d.Extra[9]) * simulationDelta;
            }

            emissionRemainder += Math.Max(0, v[4]) * segmentDelta;
            int count = Math.Min((int)emissionRemainder, d.Capacity - particles.Count);
            emissionRemainder -= count;
            if (count == 0 && particles.Count >= d.Capacity) emissionRemainder = 0;
            for (int index = 0; index < count; index++)
            {
                var particle = new Particle
                {
                    SizeX = Between(v[9], v[10]),
                    SizeY = Between(v[11], v[12]),
                    Life = Math.Max(.001f, Between(v[13], v[14])),
                    Decay = Math.Max(0, Between(v[15], v[16])),
                    Delay = Math.Max(0, Between(v[17], v[18]))
                };
                if ((d.Flags & 4) != 0) particle.SizeY = particle.SizeX;
                float angle = Between(0, MathF.Tau), radius = Between(0, v[1]);
                int sourceVertexIndex = usesSourceMesh ? random.Next(sourceVertexCount) : -1;
                if (usesSourceMesh && (emissionPositions![sourceVertexIndex].Z < d.Extra[0]
                    || emissionPositions[sourceVertexIndex].Z > d.Extra[2])) continue;
                Vector3 sourcePosition = usesSourceMesh
                    ? ToEmitterLocal(emissionPositions![sourceVertexIndex], emissionSourceMatrix!.Value, inverseWorld)
                    : Vector3.Zero;
                particle.Position = usesSourceMesh ? sourcePosition : d.Shape switch
                {
                    1 => new(R() * v[1], 0, R() * v[2]),
                    2 => new(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius),
                    3 => new(MathF.Cos(angle) * radius, -Between(0, v[2]), MathF.Sin(angle) * radius),
                    4 => Sphere() * radius,
                    5 => new(R() * v[1], -Between(0, v[3]), R() * v[2]),
                    _ => Vector3.Zero
                };
                particle.Position.Y -= v[0];
                float speed = Between(v[5], v[6]);
                float a = Between(-v[7], v[7]), b = Between(-v[7], v[8]);
                particle.Velocity = d.Direction switch
                {
                    0 => new(0, -speed, 0),
                    1 => new(MathF.Sin(a) * speed, -MathF.Cos(a) * speed, 0),
                    2 => new(MathF.Cos(b) * MathF.Sin(a) * speed, -MathF.Cos(a) * MathF.Cos(b) * speed, MathF.Sin(b) * speed),
                    3 => particle.Position.LengthSquared() > 1e-12f ? Vector3.Normalize(particle.Position) * speed : Vector3.Zero,
                    4 => sourceNormals != null ? -TransformSourceNormal(sourceNormals[sourceVertexIndex],
                        emissionSourceMatrix!.Value, inverseWorld) * speed : Vector3.Zero,
                    5 => TargetVelocity(new Vector3(d.Extra[4], d.Extra[5], d.Extra[6]), particle.Position, world, speed),
                    _ => Vector3.Zero
                };
                if ((d.Flags & 4096) != 0)
                {
                    particle.Angle = Between(d.Extra[4], d.Extra[5]);
                    particle.Spin = Between(d.Extra[6], d.Extra[7]);
                }
                particles.Add(particle);
            }
        }

        Vector3 ToEmitterLocal(Vector3 sourcePosition, Matrix4x4 sourceMatrix, Matrix4x4 inverseEmitterMatrix)
        {
            if (!hasInverseWorld) return Vector3.Zero;
            Vector3 worldPosition = Vector3.Transform(Geometry.ToRender(sourcePosition), sourceMatrix);
            return TransformData.FromRender(Vector3.Transform(worldPosition, inverseEmitterMatrix));
        }

        Vector3 TransformSourceNormal(Vector3 sourceNormal, Matrix4x4 sourceMatrix, Matrix4x4 inverseEmitterMatrix)
        {
            if (!hasInverseWorld) return Vector3.Zero;
            Vector3 worldNormal = Vector3.TransformNormal(Geometry.ToRender(sourceNormal), sourceMatrix);
            Vector3 emitterNormal = TransformData.FromRender(Vector3.TransformNormal(worldNormal, inverseEmitterMatrix));
            return emitterNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(emitterNormal) : Vector3.Zero;
        }
    }

    private void ResetSimulation()
    {
        particles.Clear();
        emissionRemainder = 0;
        simulationRemainder = 0;
        previous = -1;
        random = new(randomSeed);
    }

    private Vector3 Sphere(){float z=R(),a=Between(0,MathF.Tau),r=MathF.Sqrt(1-z*z);return new(r*MathF.Cos(a),r*MathF.Sin(a),z);}
    private static void UpdateParticleMotion(Particle particle,float delta,Matrix4x4 world,ParticleDefinition definition)
    {
        var force=definition.Acceleration;
        if(Matrix4x4.Invert(AnimationMath.ToRender(world),out var inverse))
            force=TransformData.FromRender(Vector3.TransformNormal(Geometry.ToRender(force),inverse));
        particle.Position+=particle.Velocity*delta+force*(delta*delta*.5f);
        if((definition.Flags&AccelerationAffectsSpeed)!=0)particle.Velocity+=force*delta;
        if((definition.Flags&8192)!=0)particle.Velocity*=MathF.Max(0,definition.Damping);

        var worldPosition=Vector3.Transform(particle.Position,world);
        bool crossedMinimum=(definition.Flags&1024)!=0 && worldPosition.Z-particle.SizeY<definition.Extra[0];
        bool crossedMaximum=(definition.Flags&2048)!=0 && worldPosition.Z+particle.SizeY>definition.Extra[2];
        if(crossedMinimum)
        {
            if(WithinZMinConstraint(worldPosition,world,definition.Extra[10]))
            {
                worldPosition.Z=definition.Extra[0]+particle.SizeY;
                particle.Velocity=ReflectWorldZ(particle.Velocity,world,definition.Extra[1]);
                if(Matrix4x4.Invert(world,out var inverseWorld))particle.Position=Vector3.Transform(worldPosition,inverseWorld);
            }
        }
        else if(crossedMaximum)
        {
            worldPosition.Z=definition.Extra[2]-particle.SizeY;
            particle.Velocity=ReflectWorldZ(particle.Velocity,world,definition.Extra[3]);
            if(Matrix4x4.Invert(world,out var inverseWorld))particle.Position=Vector3.Transform(worldPosition,inverseWorld);
        }
        else particle.Velocity+=force*delta;
    }

    private static bool WithinZMinConstraint(Vector3 worldPosition, Matrix4x4 world, float distanceSquared)
    {
        if (distanceSquared <= 0) return true;
        var emitterPosition = Vector3.Transform(Vector3.Zero, world);
        var horizontalOffset = worldPosition - emitterPosition;
        horizontalOffset.Z = 0;
        return horizontalOffset.LengthSquared() <= distanceSquared;
    }

    private static Vector3 ReflectWorldZ(Vector3 velocity, Matrix4x4 world, float strength)
    {
        if (!Matrix4x4.Invert(world, out var inverse)) return velocity;
        var worldVelocity = Vector3.TransformNormal(velocity, world);
        worldVelocity.Z = -worldVelocity.Z * strength;
        return Vector3.TransformNormal(worldVelocity, inverse);
    }

    private static Vector3 TargetVelocity(Vector3 worldTarget, Vector3 localPosition, Matrix4x4 world, float speed)
    {
        if (!Matrix4x4.Invert(world, out var inverse)) return Vector3.Zero;
        var localTarget = Vector3.Transform(worldTarget, inverse);
        return Normalize(localTarget - localPosition) * speed;
    }

    private static Vector3 Normalize(Vector3 v)=>v.LengthSquared()>1e-12f?Vector3.Normalize(v):Vector3.Zero;
}
