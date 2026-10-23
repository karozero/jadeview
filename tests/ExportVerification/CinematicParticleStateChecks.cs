using JadeView;

internal static class CinematicParticleStateChecks
{
    internal static void Verify()
    {
        const uint objectKey = 0x3500A264;
        const uint geometryKey = 0x71000001;
        const uint sourceKey = 0x3500A265;
        const uint sourceGeometryKey = 0x71000002;
        const uint matrixKey = 0x3500A266;
        var source = new ParticleDefinition
        {
            Capacity = 4,
            Flags = 0x40 | 0x1000,
            Values = new float[19],
            Extra = new float[11],
            Acceleration = new(1, 2, 3),
            Color = 0xFF112233
        };
        source.Values[5] = 3;
        source.Extra[4] = -.5f;
        var geometry = new Geometry { Key = geometryKey, Particles = source };
        var resolver = new ResourceResolver();
        var geometries = (Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(resolver)!;
        geometries.Add(geometryKey, geometry);
        geometries.Add(sourceGeometryKey, new Geometry
        {
            Key = sourceGeometryKey,
            Positions = [new(2, 0, 0), new(4, 0, 0), new(2, 0, 2)],
            Parts = [new(0, [0, 1, 2], [])]
        });

        var particleObject = new SceneObject { GaoKey = objectKey, GeometryKey = geometryKey };
        var sourceObject = new SceneObject { GaoKey = sourceKey, GeometryKey = sourceGeometryKey };
        var matrixObject = new SceneObject { GaoKey = matrixKey };
        var world = new WorldData();
        world.Objects.AddRange([particleObject, sourceObject, matrixObject]);
        var clip = new AnimationClip
        {
            Tracks = [new()
            {
                Target = objectKey,
                Bone = -1,
                Keys =
                [
                    CinematicTestEvents.Call(1.5f, objectKey, 0x20D2, 1, BitConverter.SingleToUInt32Bits(30)),
                    CinematicTestEvents.Call(1.2f, objectKey, 0x2108, sourceKey),
                    CinematicTestEvents.Call(1.3f, objectKey, 0x2109, matrixKey),
                    CinematicTestEvents.VectorCall(1.4f, objectKey, 0x210A, new(1, 0, 0)),
                    CinematicTestEvents.Call(2.2f, objectKey, 0x2109, 0xDEADBEEF),
                    CinematicTestEvents.Call(2, objectKey, 0x20D6, 0, 0x40),
                    CinematicTestEvents.Call(2.5f, objectKey, 0x20D2, 1, BitConverter.SingleToUInt32Bits(0)),
                    CinematicTestEvents.Call(3, objectKey, 0x20D8, 19, BitConverter.SingleToUInt32Bits(8)),
                    CinematicTestEvents.Call(4, objectKey, 0x20D8, 27, BitConverter.SingleToUInt32Bits(1.25f)),
                    CinematicTestEvents.Call(5, objectKey, 0x2105, 0x80402010),
                    CinematicTestEvents.Call(6, objectKey, 0x20D6, 0x40, 0)
                ]
            }]
        };

        var state = new CinematicParticleState(clip, world, resolver);
        state.Evaluate(1);
        var preview = particleObject.CinematicParticles
            ?? throw new InvalidDataException("Cinematic PAG state did not provide an instance-local preview definition.");
        Require(preview.IsActive && preview.Flags == source.Flags, "Unreached PAG events changed the initial state.");

        state.Evaluate(1.75f);
        Require(preview.Values[4] == 30,
            "Timed frequency changes for Jaded's default PAG generator ID 1 were ignored.");
        Require(particleObject.HasCinematicParticleSourceOverride
            && particleObject.CinematicParticleSourceObject == sourceObject
            && particleObject.HasCinematicParticleMatrixOverride
            && particleObject.CinematicParticleMatrixReference == matrixObject
            && particleObject.CinematicParticleShifts is [{ Time: 1.4f, Offset: { X: 1, Y: 0, Z: 0 } }],
            "Timed PAG source/matrix bindings and live-particle shifts must resolve from their literal calls.");

        state.Evaluate(5.5f);
        Require(!preview.IsActive && preview.Acceleration.X == 8
            && preview.Values[4] == 0 && preview.Extra[4] == 1.25f && preview.Color == 0x80402010,
            "Timed PAG rate, flag, float-parameter, or color commands were not applied.");
        Require(particleObject.IsCinematicParticleMatrixReferenceUnresolved,
            "An external PAG matrix key missing from the selected world must remain unresolved instead of falling back silently.");

        state.Evaluate(6.5f);
        Require(preview.IsActive, "PAG flag commands must apply their set mask after the clear mask.");
        state.Evaluate(3.5f);
        Require(!preview.IsActive && preview.Values[4] == 0 && preview.Acceleration.X == 8 && preview.Extra[4] == -.5f
            && preview.Color == source.Color,
            "Reverse seek must rebuild PAG state from its immutable archive definition and prior commands.");
        Require(particleObject.HasCinematicParticleSourceOverride
            && particleObject.HasCinematicParticleMatrixOverride
            && particleObject.IsCinematicParticleMatrixReferenceUnresolved,
            "Reverse seek must retain PAG object and matrix bindings whose commands still precede the playhead.");
        state.Evaluate(2f);
        Require(particleObject.CinematicParticleMatrixReference == matrixObject
            && !particleObject.IsCinematicParticleMatrixReferenceUnresolved,
            "Reverse seek must restore the last uniquely resolved PAG matrix reference.");
        state.Evaluate(1.25f);
        Require(preview.Values[4] == source.Values[4],
            "Reverse seek before a PAG frequency event must restore the serialized base emission rate.");
        Require(particleObject.HasCinematicParticleSourceOverride
            && !particleObject.HasCinematicParticleMatrixOverride,
            "Reverse seek between PAG_SetObject and PAG_SetMatrixRef must preserve only the source binding.");
        Require(particleObject.CinematicParticleShifts.Count == 0,
            "Reverse seek before a PAG shift must discard later particle translation events.");
        state.Evaluate(1.1f);
        Require(!particleObject.HasCinematicParticleSourceOverride
            && !particleObject.HasCinematicParticleMatrixOverride
            && !particleObject.IsCinematicParticleMatrixReferenceUnresolved,
            "Reverse seek before source-binding commands must restore the archive baseline.");

        state.Clear();
        Require(particleObject.CinematicParticles == null && !particleObject.HasCinematicParticleSourceOverride
            && !particleObject.HasCinematicParticleMatrixOverride && particleObject.CinematicParticleShifts.Count == 0
            && source.IsActive
            && source.Acceleration.X == 1 && source.Color == 0xFF112233,
            "Clearing cinematic state must restore archive-owned PAG data without mutating it.");
        Console.WriteLine("PASS: cinematic PAG flags, parameter/color commands, instance isolation and reverse seek");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
