using System.Numerics;
using JadeView;

internal static class SceneRendererChecks
{
    internal static void Verify()
    {
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
