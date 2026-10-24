using System.Numerics;
using JadeView;

internal static class CinematicScriptHierarchyPoseTimelineChecks
{
    internal static void Verify()
    {
        var commands = new[]
        {
            Decode(1, 1, 0x1fdc, 2),
            Decode(3, 1, 0x1fdd)
        };
        var endpoints = new Dictionary<uint, int> { [1] = 1, [2] = 1, [3] = 1 };
        var serializedParents = new Dictionary<uint, CinematicScriptHierarchyParent>
        {
            [3] = new(1, Matrix4x4.CreateTranslation(0, 2, 0))
        };
        CinematicScriptHierarchyPoseFrame[] frames =
        [
            Frame(1, (1, 3, 0, 0), (2, 10, 0, 0), (3, 3, 2, 0)),
            Frame(3, (1, 4, 0, 0), (2, 12, 0, 0), (3, 4, 2, 0))
        ];

        var attached = Evaluate(2, (1, 4, 0, 0), (2, 12, 0, 0), (3, 4, 2, 0));
        Near(attached.WorldMatrices[1].Translation, new(5, 0, 0),
            "Set did not preserve the child offset while following the moved parent.");
        Near(attached.WorldMatrices[3].Translation, new(5, 2, 0),
            "A serialized descendant did not follow the script-bound parent.");
        Check(attached.Statuses[1] == CinematicScriptHierarchyPoseStatus.Applied,
            "A valid script hierarchy binding was not marked as applied.");

        var detached = Evaluate(4, (1, 8, 0, 0), (2, 20, 0, 0), (3, 8, 2, 0));
        Near(detached.WorldMatrices[1].Translation, new(9, 0, 0),
            "Reset did not keep the detach-time offset while the child base pose advanced.");
        Near(detached.WorldMatrices[3].Translation, new(9, 2, 0),
            "A serialized descendant did not follow the detached child pose.");

        var invalidParentMatrices = new Dictionary<uint, Matrix4x4>
        {
            [1] = Matrix4x4.CreateTranslation(2, 0, 0),
            [2] = Matrix4x4.CreateScale(Vector3.Zero)
        };
        var invalidParent = CinematicScriptHierarchyPoseTimeline.Evaluate(
            [Decode(1, 1, 0x1fdc, 2)], new Dictionary<uint, CinematicScriptHierarchyParent>(),
            new Dictionary<uint, int> { [1] = 1, [2] = 1 },
            [new(1, invalidParentMatrices)], invalidParentMatrices, 1);
        Near(invalidParent.WorldMatrices[1].Translation, new(2, 0, 0),
            "A non-invertible parent lost the child's detached world pose.");
        Check(invalidParent.Statuses[1] == CinematicScriptHierarchyPoseStatus.InvalidParentMatrix,
            "A non-invertible parent was not exposed as an unresolved pose status.");

        var duplicateEndpoint = CinematicScriptHierarchyPoseTimeline.Evaluate(
            [Decode(1, 1, 0x1fdc, 2)], new Dictionary<uint, CinematicScriptHierarchyParent>(),
            new Dictionary<uint, int> { [1] = 1, [2] = 2 },
            [Frame(1, (1, 2, 0, 0))],
            new Dictionary<uint, Matrix4x4> { [1] = Matrix4x4.CreateTranslation(2, 0, 0) }, 1);
        Check(duplicateEndpoint.WorldMatrices.TryGetValue(1, out Matrix4x4 preservedAmbiguousChild)
            && Vector3.Distance(preservedAmbiguousChild.Translation, new(2, 0, 0)) < .001f
            && duplicateEndpoint.Statuses[1] == CinematicScriptHierarchyPoseStatus.UnresolvedEndpoint,
            "An ambiguous parent endpoint did not leave the child detached at its existing pose.");

        Console.WriteLine("PASS: cinematic script hierarchy pose timeline preserves Set/Reset continuity and reports unresolved matrices");

        CinematicScriptHierarchyPoseResult Evaluate(float time, params (uint Key, float X, float Y, float Z)[] current) =>
            CinematicScriptHierarchyPoseTimeline.Evaluate(commands, serializedParents, endpoints,
                frames, Base(current), time);

        static CinematicScriptHierarchyPoseFrame Frame(float time,
            params (uint Key, float X, float Y, float Z)[] poses) => new(time, Base(poses));

        static Dictionary<uint, Matrix4x4> Base(params (uint Key, float X, float Y, float Z)[] poses) =>
            poses.ToDictionary(pose => pose.Key,
                pose => Matrix4x4.CreateTranslation(pose.X, pose.Y, pose.Z));

        static CinematicScriptHierarchyCommand Decode(float time, uint child, int selector,
            params uint[] arguments) => CinematicScriptHierarchyCommand.Decode(time,
                new(child, selector, arguments))
                ?? throw new InvalidDataException("Expected a literal hierarchy command.");

        static void Near(Vector3 actual, Vector3 expected, string message)
        {
            if (Vector3.Distance(actual, expected) > .001f)
                throw new InvalidDataException($"{message} Expected {expected}; got {actual}.");
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
