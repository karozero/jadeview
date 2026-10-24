using System.Numerics;
using JadeView;

internal static class SceneRendererChecks
{
    internal static void Verify()
    {
        VerifyUniformValueCache();
        VerifyInstanceLightingSamples();
        VerifyReusableNormalBuffers();
        VerifyFrustumCulling();
        Require(SceneRenderer.CanCullBeforeGpuUpload(false, false, false, false)
            && !SceneRenderer.CanCullBeforeGpuUpload(true, false, false, false)
            && !SceneRenderer.CanCullBeforeGpuUpload(false, true, false, false)
            && !SceneRenderer.CanCullBeforeGpuUpload(false, false, true, false)
            && !SceneRenderer.CanCullBeforeGpuUpload(false, false, false, true),
            "Only static, non-procedural geometry may be frustum-culled before GPU upload.");

        var results = SceneRenderer.RunVerificationTests();
        var failures = results.Where(result => !result.Passed).ToArray();
        if (failures.Length > 0)
            throw new InvalidDataException("SceneRenderer checks failed: "
                + string.Join(" | ", failures.Select(result => $"{result.Name}: {result.Details}")));

        Console.WriteLine($"PASS: SceneRenderer math, camera and hot-path checks ({results.Count} checks)");
    }

    private static void VerifyUniformValueCache()
    {
        var floats = new GpuUniformValueCache<float>();
        Require(floats.Changed(0, 0f) && !floats.Changed(0, 0f)
            && floats.Changed(0, -0f) && !floats.Changed(0, -0f),
            "Shader cache must preserve signed-zero bits and skip only exact repeated values.");
        float firstNaN = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001));
        float secondNaN = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00002));
        Require(floats.Changed(1, firstNaN) && !floats.Changed(1, firstNaN)
            && floats.Changed(1, secondNaN) && !floats.Changed(-1, firstNaN),
            "Shader cache must preserve NaN payloads and ignore inactive uniform locations.");
        floats.Clear();
        Require(floats.Changed(0, -0f), "A shader reset must invalidate its previous uniform values.");
        var matrices = new GpuUniformValueCache<Matrix4x4>();
        var matrix = Matrix4x4.Identity;
        Require(matrices.Changed(2, matrix) && !matrices.Changed(2, matrix),
            "Identical matrices must reuse their shader uniform.");
        matrix.M43 = .125f;
        Require(matrices.Changed(2, matrix), "A changed matrix component must upload its new value.");
        var integers = new GpuUniformValueCache<int>();
        Require(integers.Changed(3, 0x08000001) && integers.Changed(3, 0x08000002),
            "Object draw-mask uniforms must retain every integer bit without a float conversion.");
    }
    private static void VerifyInstanceLightingSamples()
    {
        var samples = new GpuDiffuseLightingSamples();
        var first = new SceneObject { GaoKey = 1 };
        var second = new SceneObject { GaoKey = 1 };
        var firstSample = samples.Get(first, 0);
        var secondSample = samples.Get(second, 0);
        firstSample.EnsureCapacity(1);
        secondSample.EnsureCapacity(1);
        firstSample.ComputedColors[0] = 0xff0000ff;
        secondSample.ComputedColors[0] = 0xff00ff00;
        Require(!ReferenceEquals(firstSample, secondSample)
            && samples.Get(first, 0).ComputedColors[0] == 0xff0000ff,
            "Shared GRO instances, even with equal GAO keys, must retain independent CPU lighting samples.");
        samples.MarkUploaded(0, firstSample);
        Require(!samples.NeedsUpload(0, firstSample) && samples.NeedsUpload(0, secondSample),
            "A different instance must upload its own colors to the shared mesh.");
        samples.MarkUploaded(0, secondSample);
        Require(samples.NeedsUpload(0, firstSample) && !samples.NeedsUpload(0, secondSample),
            "Alternating shared-mesh draws must restore the correct cached instance colors.");
        secondSample.Revision++;
        Require(samples.NeedsUpload(0, secondSample),
            "A recomputed sample must upload again even if its owner did not change.");
        var removedOwner = AddTemporaryLightingOwner(samples);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Require(!removedOwner.IsAlive, "Lighting samples must not retain removed owners after same-count scene replacements.");
        GC.KeepAlive(samples);
        samples.Clear();
        Require(!ReferenceEquals(samples.Get(first, 0), firstSample)
            && samples.NeedsUpload(0, firstSample),
            "Changing the world or geometry cache must release samples and upload state.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AddTemporaryLightingOwner(GpuDiffuseLightingSamples samples)
    {
        var owner = new SceneObject();
        var sample = samples.Get(owner, 0);
        samples.MarkUploaded(0, sample);
        return new WeakReference(owner);
    }

    private static void VerifyReusableNormalBuffers()
    {
        Vector3[] positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
        List<Geometry.Part> parts = [new(0, [0, 1, 2], [0, 1, 2])];
        Vector3[] reusableNormals = new Vector3[positions.Length];
        int[] reusableCounts = new int[positions.Length];
        AnimationMath.FillNormals(positions, parts, positions.Length, parts.Count,
            reusableNormals, reusableCounts);
        Vector3[] expectedNormals = AnimationMath.Normals(positions, parts);
        Require(reusableNormals.SequenceEqual(expectedNormals),
            "Reusable normal buffers must match the existing normal calculation.");

        positions[2] = -Vector3.UnitY;
        AnimationMath.FillNormals(positions, parts, positions.Length, parts.Count,
            reusableNormals, reusableCounts);
        Require(reusableNormals[0].Z < -.99f && reusableCounts[0] == 1,
            "Reusable normal buffers must clear old accumulators before recomputing a changed pose.");

        var billboardGeometry = new Geometry
        {
            Positions = [new(1, 2, 3), new(1, 2, 3), new(1, 2, 3), new(1, 2, 3)],
            Billboards = [new(0, new(1, 2, 3), .5f, 1, true)]
        };
        Vector3[] expectedBillboardPositions = SpriteGenerator.FaceCamera(billboardGeometry, null,
            Matrix4x4.Identity, Vector3.UnitX, Vector3.UnitZ, .25f);
        Vector3[] reusableBillboardPositions = new Vector3[billboardGeometry.Positions.Length];
        SpriteGenerator.FillFaceCamera(billboardGeometry, null, Matrix4x4.Identity,
            Vector3.UnitX, Vector3.UnitZ, .25f, reusableBillboardPositions);
        Require(reusableBillboardPositions.SequenceEqual(expectedBillboardPositions)
            && billboardGeometry.Positions.All(position => position == new Vector3(1, 2, 3)),
            "Reusable billboard positions must match camera-facing output without mutating source geometry.");
        SpriteGenerator.FillFaceCamera(billboardGeometry, null, Matrix4x4.Identity,
            Vector3.UnitZ, Vector3.UnitY, .5f, reusableBillboardPositions);
        Require(!reusableBillboardPositions.SequenceEqual(expectedBillboardPositions),
            "Reusable billboard positions must refresh when camera orientation or wind time changes.");
    }

    private static void VerifyFrustumCulling()
    {
        bool InView(Vector3 center, float radius = .5f) => SceneRenderer.IsSphereInViewFrustum(
            center, radius, Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY,
            verticalFovDegrees: 90, viewportAspect: 16f / 9f, farClip: 100);

        Require(InView(new(0, 0, -10)), "Frustum culling must retain geometry centered in the view.");
        Require(InView(new(18, 0, -10)) && !InView(new(19, 0, -10)),
            "Frustum culling must use the viewport's horizontal field of view.");
        Require(InView(new(0, 10, -10)) && !InView(new(0, 11, -10)),
            "Frustum culling must use the camera's vertical field of view.");
        Require(!InView(new(0, 0, 10)) && !InView(new(0, 0, -101)),
            "Frustum culling must reject spheres behind the camera and beyond the far plane.");
        Require(SceneRenderer.IsSphereInViewFrustum(Vector3.Zero, .5f, Vector3.Zero,
                Vector3.Zero, Vector3.UnitY, 60, 1, 100),
            "Frustum culling must fail open when camera direction is undefined.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
