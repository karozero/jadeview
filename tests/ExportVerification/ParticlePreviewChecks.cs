using System.Numerics;
using JadeView;

internal static class ParticlePreviewChecks
{
    internal static void Verify()
    {
        VerifySerializedVersions();
        VerifyParticleNormalRange();

        var transparentDefinition = new ParticleDefinition { Flags = 0x00000001 };
        var unsortedDefinition = new ParticleDefinition { Flags = 0x00010000 };
        if (!transparentDefinition.IsTransparent || !transparentDefinition.SortParticles
            || transparentDefinition.IsActive || unsortedDefinition.IsTransparent || unsortedDefinition.SortParticles)
            throw new Exception("PAG transparent and DontSortParticles flags must remain distinct preview properties.");

        var definition = new ParticleDefinition
        {
            Capacity = 1,
            Flags = 64,
            Shape = 6,
            Direction = 4,
            Values = new float[19],
            Extra = new float[11]
        };
        definition.Values[4] = 60;
        definition.Values[5] = definition.Values[6] = 1;
        definition.Values[9] = definition.Values[10] = 1;
        definition.Values[11] = definition.Values[12] = 1;
        definition.Values[13] = definition.Values[14] = 1;
        definition.Extra[0] = -1;
        definition.Extra[2] = 1;

        var geometry = new Geometry
        {
            Particles = definition,
            ParticleStart = 3,
            ParticlePart = 1,
            Positions =
            [
                new(0, 0, 0), new(2, 0, 0), new(0, 0, 2),
                Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero
            ],
            VertexColors = new uint[7],
            Parts =
            [
                new(0, [0, 1, 2], [0, 1, 2]),
                new(0, [3, 4, 5, 3, 5, 6], [0, 1, 2, 0, 2, 3])
            ]
        };
        var preview = new ParticlePreview(definition, 42);
        var firstFrame = preview.Update(geometry, geometry.Positions, Matrix4x4.Identity,
            Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0,
            geometry, geometry.Positions, Matrix4x4.Identity);
        var spawnFrame = preview.Update(geometry, firstFrame.Positions, Matrix4x4.Identity,
            Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1f / 60,
            geometry, geometry.Positions, Matrix4x4.Identity);
        Vector3 spawnedCenter = Center(spawnFrame.Positions);
        var moveFrame = preview.Update(geometry, spawnFrame.Positions, Matrix4x4.Identity,
            Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2f / 60,
            geometry, geometry.Positions, Matrix4x4.Identity);

        Vector3 Center(Vector3[] positions) =>
            (positions[3] + positions[4] + positions[5] + positions[6]) * .25f;

        Vector3 movedCenter = Center(moveFrame.Positions);
        if (!ReferenceEquals(spawnFrame.Positions, moveFrame.Positions)
            || !ReferenceEquals(spawnFrame.Colors, moveFrame.Colors))
            throw new Exception("PAG preview should reuse its per-system position and color buffers between render updates.");
        if (spawnedCenter.X is < 0 or > 2 || spawnedCenter.Z is < 0 or > 2)
            throw new Exception("Object-vertex particles must spawn on their script-bound source mesh.");
        if (movedCenter.Y <= spawnedCenter.Y + .005f)
            throw new Exception("Object-normal particles must move opposite the reconstructed face normal.");
        VerifyExternalEmitterTransform();
        VerifyTimelineShift();

        definition.Flags |= 128;
        var frozenFrame = preview.Update(geometry, moveFrame.Positions, Matrix4x4.Identity,
            Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 3f / 60);
        if (Vector3.Distance(Center(frozenFrame.Positions), movedCenter) > .001f)
            throw new Exception("PAG Freeze must keep existing particles visible without advancing them.");
        definition.Flags &= ~192;
        var inactiveFrame = preview.Update(geometry, frozenFrame.Positions, Matrix4x4.Identity,
            Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 4f / 60);
        if (Center(inactiveFrame.Positions).LengthSquared() > .000001f)
            throw new Exception("Inactive PAG emitters must hide existing particles as well as stop simulation.");

        var regularAccelerationPosition = SampleAcceleration(flags: 64);
        var speedAccelerationPosition = SampleAcceleration(flags: 64 | 0x8000);
        if (MathF.Abs(regularAccelerationPosition.Y - -0.0666667f) > .001f
            || MathF.Abs(speedAccelerationPosition.Y - -0.0833333f) > .001f)
            throw new Exception($"PAG AccSpeed must add the serialized acceleration to velocity: regular={regularAccelerationPosition}, AccSpeed={speedAccelerationPosition}.");

        var sortedParticlePositions = SampleTransparentSort();
        float firstParticleDepth = QuadCenter(sortedParticlePositions, 0).X;
        float secondParticleDepth = QuadCenter(sortedParticlePositions, 4).X;
        if (firstParticleDepth < secondParticleDepth || MathF.Abs(firstParticleDepth - secondParticleDepth) < .001f)
            throw new Exception($"Transparent PAG quads must be packed back-to-front: {firstParticleDepth}, {secondParticleDepth}.");

        var birthFrame = SampleBirthPhase();
        Vector3 birthCenter = QuadCenter(birthFrame.Positions, 0);
        float birthHalfDiagonal = Vector3.Distance(birthFrame.Positions[0], birthCenter);
        if (MathF.Abs(birthCenter.X - 1f / 60) > .001f
            || birthFrame.Colors[0] >> 24 != 127
            || MathF.Abs(birthHalfDiagonal - MathF.Sqrt(.5f)) > .001f)
            throw new Exception($"PAG particles must move, fade and grow during birth: center={birthCenter}, alpha={birthFrame.Colors[0] >> 24}, half-diagonal={birthHalfDiagonal}.");

        Vector3 sixtyFpsPosition = SampleAtFrameRate(1f / 60);
        Vector3 thirtyFpsPosition = SampleAtFrameRate(1f / 30);
        if (Vector3.Distance(sixtyFpsPosition, thirtyFpsPosition) > .001f)
            throw new Exception($"PAG fixed-step motion must not depend on viewport frame rate: 60 FPS={sixtyFpsPosition}, 30 FPS={thirtyFpsPosition}.");

        VerifyTimelineRewind();

        Vector3[] rotatedParticle = SampleRotatedParticle();
        if (Vector3.Distance(rotatedParticle[0], new Vector3(1, 1, 0)) > .001f)
            throw new Exception($"PAG billboard rotation must use Jaded's source winding: corner={rotatedParticle[0]}.");

        Vector3 zClippedParticlePosition = SampleZMinimumClip();
        if (MathF.Abs(zClippedParticlePosition.Z - 1) > .001f)
            throw new Exception($"PAG Z-min clipping must account for particle half-height: {zClippedParticlePosition}.");

        Console.WriteLine("PASS: PAG activation/freeze, mesh emission, event-time particle shifts, birth phase, fixed-step motion, AccSpeed, source-wound rotation, sorting, sine drift and size-aware Z clipping");

        static void VerifyTimelineShift()
        {
            var shiftedDefinition = new ParticleDefinition
            {
                Capacity = 2,
                Flags = 64,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11]
            };
            shiftedDefinition.Values[4] = 60;
            shiftedDefinition.Values[9] = shiftedDefinition.Values[10] = 1;
            shiftedDefinition.Values[11] = shiftedDefinition.Values[12] = 1;
            shiftedDefinition.Values[13] = shiftedDefinition.Values[14] = 10;
            var particleGeometry = shiftedDefinition.Geometry(208, 0, 0);
            var particlePreview = new ParticlePreview(shiftedDefinition, 37);
            Vector3[] positions = particleGeometry.Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0).Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60).Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2d / 60,
                cinematicShifts: [new(1.5f / 60, Vector3.UnitX)]).Positions;
            float shiftedLiveParticle = QuadCenter(positions, 0).X;
            float particleBornAfterShift = QuadCenter(positions, 4).X;
            if (MathF.Abs(shiftedLiveParticle - 1) > .001f || MathF.Abs(particleBornAfterShift) > .001f)
                throw new Exception($"PAG_ShiftParticles must translate only particles alive at its event time: existing={shiftedLiveParticle}, later={particleBornAfterShift}.");
        }

        static void VerifyTimelineRewind()
        {
            var rewindDefinition = new ParticleDefinition
            {
                Capacity = 1,
                Flags = 64,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11],
                Color = 0xFFFFFFFF
            };
            rewindDefinition.Values[4] = 60;
            rewindDefinition.Values[9] = rewindDefinition.Values[10] = 1;
            rewindDefinition.Values[11] = rewindDefinition.Values[12] = 1;
            rewindDefinition.Values[13] = rewindDefinition.Values[14] = 10;
            var rewindGeometry = rewindDefinition.Geometry(206, 0, 0);
            var rewindPreview = new ParticlePreview(rewindDefinition, 29);
            var first = rewindPreview.Update(rewindGeometry, rewindGeometry.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0);
            var emitted = rewindPreview.Update(rewindGeometry, first.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60);
            uint emittedAlpha = emitted.Colors[0] >> 24;
            var advanced = rewindPreview.Update(rewindGeometry, emitted.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2d / 60);
            var rewound = rewindPreview.Update(rewindGeometry, advanced.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60);
            if (emittedAlpha == 0 || rewound.Colors.Any(color => color != 0))
                throw new Exception("Rewinding the cinematic playhead must clear particles from the later preview frame.");
        }

        static void VerifyExternalEmitterTransform()
        {
            var externalDefinition = new ParticleDefinition
            {
                Capacity = 1,
                Flags = 64,
                Shape = 6,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11]
            };
            externalDefinition.Values[4] = 60;
            externalDefinition.Values[9] = externalDefinition.Values[10] = 1;
            externalDefinition.Values[11] = externalDefinition.Values[12] = 1;
            externalDefinition.Values[13] = externalDefinition.Values[14] = 10;
            externalDefinition.Extra[0] = -10;
            externalDefinition.Extra[2] = 10;
            var emitterGeometry = externalDefinition.Geometry(207, 0, 0);
            var sourceGeometry = new Geometry
            {
                Positions = [new(2, 0, 0), new(4, 0, 0), new(2, 0, 2)],
                Parts = [new(0, [0, 1, 2], [])]
            };
            var emitter = new ParticlePreview(externalDefinition, 31);
            var initial = emitter.Update(emitterGeometry, emitterGeometry.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0,
                sourceGeometry, sourceGeometry.Positions, Matrix4x4.CreateTranslation(10, 0, 0));
            var emitted = emitter.Update(emitterGeometry, initial.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60,
                sourceGeometry, sourceGeometry.Positions, Matrix4x4.CreateTranslation(10, 0, 0));
            Vector3 center = QuadCenter(emitted.Positions, 0);
            if (center.X is < 12 or > 14)
                throw new Exception($"Shape-6 particles must use the PAG_SetObject source geometry and its matrix reference: {center}.");

            var unbound = new ParticlePreview(externalDefinition, 31);
            var noSource = unbound.Update(emitterGeometry, emitterGeometry.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0);
            var stillUnbound = unbound.Update(emitterGeometry, noSource.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60);
            if (stillUnbound.Colors.Any(color => color != 0))
                throw new Exception("Shape-6 PAG must stay idle without its script-bound source geometry.");
        }

        static Vector3 QuadCenter(Vector3[] positions, int firstVertex) =>
            (positions[firstVertex] + positions[firstVertex + 1] + positions[firstVertex + 2] + positions[firstVertex + 3]) * .25f;

        static void VerifySerializedVersions()
        {
            foreach (int version in new[] { 8, 10 })
            {
                int recordSize = version == 8 ? 144 : 156;
                var bytes = new byte[recordSize];
                BitConverter.GetBytes(1).CopyTo(bytes, 0);
                BitConverter.GetBytes(version).CopyTo(bytes, 4);
                BitConverter.GetBytes((ushort)64).CopyTo(bytes, 8);
                bytes[10] = 0;
                bytes[11] = 0;
                WriteFloat(bytes, 12 + 4 * 4, 37.5f);
                WriteFloat(bytes, 88, 1.25f);
                WriteFloat(bytes, 100, .75f);
                WriteFloat(bytes, 104, 2.5f);
                WriteFloat(bytes, 112, -3f);
                if (version == 10)
                {
                    WriteFloat(bytes, 112 + 8 * 4, .125f);
                    WriteFloat(bytes, 112 + 10 * 4, 9f);
                }

                var definition = ParticleDefinition.Read(bytes, 0, bytes.Length);
                int modifierSize = PcTextureLoader.ModifierSize(bytes, 0, bytes.Length, 18);
                bool versionFieldsMatch = definition is { IsActive: true }
                    && MathF.Abs(definition.Values[4] - 37.5f) < .001f
                    && MathF.Abs(definition.Acceleration.X - 1.25f) < .001f
                    && MathF.Abs(definition.Damping - .75f) < .001f
                    && MathF.Abs(definition.Expansion - 2.5f) < .001f
                    && MathF.Abs(definition.Extra[0] + 3f) < .001f;
                bool versionTailMatches = version == 8
                    ? definition?.Extra[8] == 0
                    : definition != null && MathF.Abs(definition.Extra[8] - .125f) < .001f
                        && MathF.Abs(definition.Extra[10] - 9f) < .001f;
                if (!versionFieldsMatch || !versionTailMatches || modifierSize != recordSize)
                    throw new Exception($"PAG v{version} fields must be read using the source layout.");
            }

            static void WriteFloat(byte[] target, int offset, float value) =>
                BitConverter.GetBytes(value).CopyTo(target, offset);
        }

        static Vector3[] SampleTransparentSort()
        {
            var transparent = new ParticleDefinition
            {
                Capacity = 2,
                Flags = 64 | 1,
                Shape = 1,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11]
            };
            transparent.Values[1] = transparent.Values[2] = 2;
            transparent.Values[4] = 120;
            transparent.Values[9] = transparent.Values[10] = 1;
            transparent.Values[11] = transparent.Values[12] = 1;
            transparent.Values[13] = transparent.Values[14] = 1;
            var particleGeometry = transparent.Geometry(201, 0, 0);
            var particlePreview = new ParticlePreview(transparent, 11);
            var initial = particlePreview.Update(particleGeometry, particleGeometry.Positions,
                Matrix4x4.Identity, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0);
            return particlePreview.Update(particleGeometry, initial.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 1d / 60).Positions;
        }

        static Vector3[] SampleRotatedParticle()
        {
            var rotating = new ParticleDefinition
            {
                Capacity = 1,
                Flags = 64 | 4096,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11]
            };
            rotating.Values[4] = 60;
            rotating.Values[9] = rotating.Values[10] = 1;
            rotating.Values[11] = rotating.Values[12] = 1;
            rotating.Values[13] = rotating.Values[14] = 1;
            rotating.Extra[4] = rotating.Extra[5] = MathF.PI / 2;

            var particleGeometry = rotating.Geometry(205, 0, 0);
            var particlePreview = new ParticlePreview(rotating, 23);
            particlePreview.Update(particleGeometry, particleGeometry.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0);
            return particlePreview.Update(particleGeometry, particleGeometry.Positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60).Positions;
        }

        static (Vector3[] Positions, uint[] Colors) SampleBirthPhase()
        {
            var drifting = new ParticleDefinition
            {
                Capacity = 1,
                Flags = 64 | 0x100 | 0x200,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11],
                Color = 0xFFFFFFFF
            };
            drifting.Values[4] = 60;
            drifting.Values[9] = drifting.Values[10] = 1;
            drifting.Values[11] = drifting.Values[12] = 1;
            drifting.Values[13] = drifting.Values[14] = 1;
            drifting.Values[17] = drifting.Values[18] = 2f / 60;
            drifting.Extra[8] = MathF.PI * 30;

            var particleGeometry = drifting.Geometry(202, 0, 0);
            var particlePreview = new ParticlePreview(drifting, 13);
            Vector3[] positions = particleGeometry.Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0).Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60).Positions;
            return particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2d / 60);
        }

        static Vector3 SampleZMinimumClip()
        {
            var clipped = new ParticleDefinition
            {
                Capacity = 1,
                Flags = 64 | 0x400,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11]
            };
            clipped.Values[4] = 60;
            clipped.Values[9] = clipped.Values[10] = 1;
            clipped.Values[11] = clipped.Values[12] = 1;
            clipped.Values[13] = clipped.Values[14] = 1;
            clipped.Extra[0] = 0;
            clipped.Extra[1] = 1;

            var particleGeometry = clipped.Geometry(203, 0, 0);
            var particlePreview = new ParticlePreview(clipped, 17);
            Vector3[] positions = particleGeometry.Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0).Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 1d / 60).Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2d / 60).Positions;
            return QuadCenter(positions, 0);
        }

        static Vector3 SampleAtFrameRate(double frameInterval)
        {
            var frameRateDefinition = new ParticleDefinition
            {
                Capacity = 1,
                Flags = 64,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11]
            };
            frameRateDefinition.Values[4] = 60;
            frameRateDefinition.Values[5] = frameRateDefinition.Values[6] = 1;
            frameRateDefinition.Values[9] = frameRateDefinition.Values[10] = 1;
            frameRateDefinition.Values[11] = frameRateDefinition.Values[12] = 1;
            frameRateDefinition.Values[13] = frameRateDefinition.Values[14] = 10;

            var particleGeometry = frameRateDefinition.Geometry(204, 0, 0);
            var particlePreview = new ParticlePreview(frameRateDefinition, 19);
            Vector3[] positions = particleGeometry.Positions;
            positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 0).Positions;
            int framesPerSecond = (int)Math.Round(1 / frameInterval);
            for (int frame = 1; frame <= framesPerSecond + 1; frame++)
            {
                positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                    Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ,
                    frame * frameInterval).Positions;
            }
            return QuadCenter(positions, 0);
        }

        static Vector3 SampleAcceleration(int flags)
        {
            var accelerated = new ParticleDefinition
            {
                Capacity = 1,
                Flags = flags,
                Shape = 0,
                Direction = 0,
                Values = new float[19],
                Extra = new float[11],
                Acceleration = new(0, -60, 0)
            };
            accelerated.Values[4] = 60;
            accelerated.Values[5] = accelerated.Values[6] = 1;
            accelerated.Values[9] = accelerated.Values[10] = 1;
            accelerated.Values[11] = accelerated.Values[12] = 1;
            accelerated.Values[13] = accelerated.Values[14] = 1;
            var particleGeometry = accelerated.Geometry(200, 0, 0);
            var particlePreview = new ParticlePreview(accelerated, 7);
            Vector3[] positions = particleGeometry.Positions;
            foreach (double time in new[] { 0, 1d / 60, 2d / 60, 3d / 60 })
                positions = particlePreview.Update(particleGeometry, positions, Matrix4x4.Identity,
                    Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, time).Positions;
            return (positions[0] + positions[1] + positions[2] + positions[3]) * .25f;
        }
    }

    private static void VerifyParticleNormalRange()
    {
        Vector3[] positions =
        [
            new(0, 0, 0), new(1, 0, 0), new(0, 1, 0),
            new(2, 2, 2), new(3, 3, 3), new(4, 4, 4)
        ];
        List<Geometry.Part> parts =
        [
            new(0, [0, 1, 2], [0, 1, 2]),
            new(1, [3, 4, 5], [0, 1, 2])
        ];

        Vector3[] carrierNormals = AnimationMath.Normals(positions, parts, 3, 1);
        if (carrierNormals.Length != 3 || carrierNormals[0] != Vector3.UnitZ)
            throw new Exception("Particle normal optimization should compute normals only for non-particle carrier vertices.");
    }
}
