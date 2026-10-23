using System.Numerics;

namespace JadeView;

internal sealed class CinematicParticleState
{
    private const int SetFlag = 0x20D6;
    private const int SetGeneratorFrequency = 0x20D2;
    private const int SetFloat = 0x20D8;
    private const int SetColor = 0x2105;
    private const int SetObject = 0x2108;
    private const int SetMatrixReference = 0x2109;
    private const int ShiftParticles = 0x210A;

    private readonly (SceneObject Object, ParticleDefinition Source, ParticleDefinition Preview)[] systems;
    private readonly (float Time, SceneObject[] Targets, CinematicLiteralCall Call)[] changes;
    private readonly Dictionary<uint, SceneObject> objectsByKey;
    private int appliedChangeCount;
    private bool hasEvaluation;
    internal float Time { get; private set; }

    internal CinematicParticleState(AnimationClip clip, WorldData world, ResourceResolver resolver)
    {
        objectsByKey = world.Objects.GroupBy(sceneObject => sceneObject.GaoKey)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        systems = world.Objects
            .Select(sceneObject => (Object: sceneObject,
                Source: resolver.Geometries.GetValueOrDefault(sceneObject.RenderGeometryKey)?.Particles))
            .Where(item => item.Source != null)
            .Select(item => (item.Object, item.Source!, item.Source!.Clone()))
            .ToArray();

        var targetsByKey = systems.GroupBy(item => item.Object.GaoKey)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Object).ToArray());
        changes = CinematicLiteralCall.ReadTimeline(clip)
            .Where(item => IsSupportedCommand(item.Call) && targetsByKey.ContainsKey(item.Call.Target))
            .Select(item => (item.Time, targetsByKey[item.Call.Target], item.Call))
            .OrderBy(item => item.Time)
            .ToArray();
    }

    internal void Evaluate(float time)
    {
        float requestedTime = float.IsFinite(time) ? Math.Max(0, time) : 0;
        if (!hasEvaluation || requestedTime < Time)
        {
            ResetSystems();
            appliedChangeCount = 0;
            hasEvaluation = true;
        }

        Time = requestedTime;
        while (appliedChangeCount < changes.Length
            && changes[appliedChangeCount].Time <= Time)
        {
            ApplyChange(changes[appliedChangeCount]);
            appliedChangeCount++;
        }
    }

    private void ResetSystems()
    {
        foreach (var (sceneObject, source, preview) in systems)
        {
            preview.CopyFrom(source);
            sceneObject.CinematicParticles = preview;
            sceneObject.CinematicParticleShifts = [];
            sceneObject.HasCinematicParticleSourceOverride = false;
            sceneObject.CinematicParticleSourceObject = null;
            sceneObject.HasCinematicParticleMatrixOverride = false;
            sceneObject.CinematicParticleMatrixReference = null;
            sceneObject.IsCinematicParticleMatrixReferenceUnresolved = false;
            sceneObject.PreviewParticlePositions = null;
            sceneObject.PreviewParticleColors = null;
            sceneObject.PreviewParticleTime = null;
        }
    }

    private void ApplyChange((float Time, SceneObject[] Targets, CinematicLiteralCall Call) change)
    {
        var (eventTime, targets, call) = change;
        if (!float.IsFinite(eventTime)) return;
        foreach (var sceneObject in targets)
        {
            var preview = sceneObject.CinematicParticles;
            if (preview == null) continue;
            switch (call.Selector)
            {
                case SetFlag when call.Arguments.Length == 2:
                    preview.Flags = (preview.Flags | unchecked((int)call.Arguments[0]))
                        & ~unchecked((int)call.Arguments[1]);
                    break;
                case SetGeneratorFrequency when call.Arguments.Length == 2
                    && unchecked((int)call.Arguments[0]) == 1:
                    float frequency = BitConverter.UInt32BitsToSingle(call.Arguments[1]);
                    if (float.IsFinite(frequency)) preview.Values[4] = Math.Max(0, frequency);
                    break;
                case SetFloat when call.Arguments.Length == 2:
                    preview.SetFloatParameter(unchecked((int)call.Arguments[0]),
                        BitConverter.UInt32BitsToSingle(call.Arguments[1]));
                    break;
                case SetColor when call.Arguments.Length == 1:
                    preview.Color = call.Arguments[0];
                    break;
                case SetObject when call.Arguments.Length == 1:
                    sceneObject.HasCinematicParticleSourceOverride = true;
                    sceneObject.CinematicParticleSourceObject = ResolveObject(call.Arguments[0]);
                    break;
                case SetMatrixReference when call.Arguments.Length == 1:
                    sceneObject.HasCinematicParticleMatrixOverride = true;
                    sceneObject.CinematicParticleMatrixReference = ResolveObject(call.Arguments[0]);
                    sceneObject.IsCinematicParticleMatrixReferenceUnresolved = call.Arguments[0] is not (0 or uint.MaxValue)
                        && sceneObject.CinematicParticleMatrixReference == null;
                    break;
                case ShiftParticles when TryReadShift(call.Arguments, out Vector3 shift):
                    sceneObject.CinematicParticleShifts.Add(new(eventTime, shift));
                    break;
            }
        }
    }

    internal void Clear()
    {
        appliedChangeCount = 0;
        hasEvaluation = false;
        foreach (var (sceneObject, _, _) in systems)
        {
            sceneObject.CinematicParticles = null;
            sceneObject.CinematicParticleShifts = [];
            sceneObject.HasCinematicParticleSourceOverride = false;
            sceneObject.CinematicParticleSourceObject = null;
            sceneObject.HasCinematicParticleMatrixOverride = false;
            sceneObject.CinematicParticleMatrixReference = null;
            sceneObject.IsCinematicParticleMatrixReferenceUnresolved = false;
            sceneObject.PreviewParticlePositions = null;
            sceneObject.PreviewParticleColors = null;
            sceneObject.PreviewParticleTime = null;
        }
    }

    private static bool IsSupportedCommand(CinematicLiteralCall call) => call.Selector switch
    {
        SetFlag => call.Arguments.Length == 2,
        SetGeneratorFrequency => call.Arguments.Length == 2,
        SetFloat => call.Arguments.Length == 2,
        SetColor => call.Arguments.Length == 1,
        SetObject or SetMatrixReference => call.Arguments.Length == 1,
        ShiftParticles => call.Arguments.Length == 3,
        _ => false
    };

    private static bool TryReadShift(uint[] arguments, out Vector3 shift)
    {
        shift = default;
        if (arguments.Length != 3) return false;
        shift = new(BitConverter.UInt32BitsToSingle(arguments[0]),
            BitConverter.UInt32BitsToSingle(arguments[1]), BitConverter.UInt32BitsToSingle(arguments[2]));
        return float.IsFinite(shift.X) && float.IsFinite(shift.Y) && float.IsFinite(shift.Z);
    }

    private SceneObject? ResolveObject(uint key) => key is 0 or uint.MaxValue
        ? null : objectsByKey.GetValueOrDefault(key);
}

internal readonly record struct CinematicParticleShift(float Time, Vector3 Offset);
