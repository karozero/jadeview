using System.Numerics;

namespace JadeView;

// Preview state is separate from editable transforms and never produces archive patches.
public sealed partial class SceneActor
{
    public SceneObject Root = null!;
    public List<SceneObject> Members = [];
    public List<SceneObject?> Bones = [];
    public List<AnimationClip> Clips = [];
    public AnimationClip? Clip;
    public uint DefaultShape;
    public Dictionary<uint, byte[]> Shapes = [];
    public Dictionary<AnimationClip, uint> ClipShapes = [];
    public Dictionary<int, List<SceneObject>> SourceMeshes = [];
    public bool Playing;
    internal bool CinematicClockOwned;
    public bool Loop = true;
    public bool RootMotion;
    public float Time, Speed = 1;
    public string? Diagnostic;
    internal bool IsCinematicWorldResident;
    public int PoseRevision { get; private set; }
    private readonly Dictionary<SceneObject, Matrix4x4> world = [];
    private readonly Dictionary<SceneObject, Matrix4x4> evaluatedLocals = [];
    private readonly HashSet<SceneObject> visitingWorldPoses = [];
    private readonly Dictionary<AnimationTrack, SceneObject?> trackTargets = [];
    private readonly Dictionary<AnimationTrack, List<AnimationKey>> filteredTrackKeys = [];
    private AnimationClip? cachedTrackClip;
    private int cachedMemberCount = -1;
    private bool captureAllocationDiagnostics;
    private readonly long[] allocationByPhase = new long[5];
    private int allocationFrameCount;
    internal bool CaptureAllocationDiagnostics
    {
        get => captureAllocationDiagnostics;
        set => captureAllocationDiagnostics = value;
    }
    internal string AllocationDiagnostics
    {
        get
        {
            double frameCount = Math.Max(1, allocationFrameCount);
            return $"advance={allocationByPhase[0] / frameCount:F0}, locals={allocationByPhase[1] / frameCount:F0}, "
                + $"body={allocationByPhase[2] / frameCount:F0}, tracks={allocationByPhase[3] / frameCount:F0}, "
                + $"world={allocationByPhase[4] / frameCount:F0}B/frame";
        }
    }
    internal void ResetAllocationDiagnostics()
    {
        Array.Clear(allocationByPhase);
        allocationFrameCount = 0;
    }
    internal bool TryGetEvaluatedLocal(SceneObject member, out Matrix4x4 pose) =>
        evaluatedLocals.TryGetValue(member, out pose);

    internal void InvalidatePose()
    {
        PoseRevision++;
    }
    public void SelectClip(AnimationClip? clip)
    {
        if (clip?.SceneMotion == true) RootMotion = true;
        Clip = clip; Time = 0; clip?.EnsureLoaded(); Playing = clip != null && clip.Error == null;
        Evaluate(0);
    }
    public void Evaluate(float delta)
    {
        long allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        AdvancePlayback(delta);
        ApplyCurrentShape();
        RecordAllocations(0, ref allocationStart);

        Matrix4x4 rootRotation = Matrix4x4.Identity;
        Vector3 rootOffset = Vector3.Zero;
        Matrix4x4? cinematicRootPose = GetCinematicRootPose();
        var localPoses = CreateLocalPoses();
        RecordAllocations(1, ref allocationStart);
        ApplyCinematicBodyPose(localPoses);
        RecordAllocations(2, ref allocationStart);

        if (Clip is { Error: null } activeClip)
        {
            EnsureTrackCaches(activeClip);
            foreach (var track in activeClip.Tracks)
            {
                ApplyTrack(track, localPoses, ref rootRotation, ref rootOffset, ref cinematicRootPose);
            }
        }
        RecordAllocations(3, ref allocationStart);

        EvaluateWorldPoses(localPoses, cinematicRootPose, rootRotation, rootOffset);
        RecordAllocations(4, ref allocationStart);
        if (CaptureAllocationDiagnostics) allocationFrameCount++;
    }

    private void RecordAllocations(int phase, ref long allocationStart)
    {
        if (!CaptureAllocationDiagnostics) return;
        long current = GC.GetAllocatedBytesForCurrentThread();
        allocationByPhase[phase] += current - allocationStart;
        allocationStart = current;
    }

    private void AdvancePlayback(float delta)
    {
        if (CinematicClockOwned)
        {
            delta = 0;
        }

        foreach (var member in Members)
        {
            member.MorphPreview?.Advance(delta);
        }

        PoseRevision++;
        if (Clip != null)
        {
            Clip.EnsureLoaded();
            if (Playing && Clip.Duration > 0)
            {
                Time += Math.Clamp(delta, 0, .1f) * Speed;
                if (Loop) Time %= Clip.Duration;
                else if (Time >= Clip.Duration) { Time = Clip.Duration; Playing = false; }
            }
        }
    }

    private Matrix4x4? GetCinematicRootPose() =>
        Clip is { Cinematic: true, CinematicBody: { } body } ? body.RootPose(Time) : null;

    private Dictionary<SceneObject, Matrix4x4> CreateLocalPoses()
    {
        evaluatedLocals.Clear();
        foreach (var member in Members)
            evaluatedLocals[member] = member.ActorNode?.Local ?? AnimationMath.Trs(member.Transform);
        return evaluatedLocals;
    }

    private void ApplyCinematicBodyPose(Dictionary<SceneObject, Matrix4x4> localPoses)
    {
        if (Clip is not { Error: null, CinematicBody: { } bodyTimeline })
        {
            return;
        }

        bodyPoseSampler ??= new(this);
        var sampledPoses = bodyPoseSampler.Evaluate(bodyTimeline, Time, localPoses);
        if (ReferenceEquals(sampledPoses, localPoses)) return;
        foreach (var (member, pose) in sampledPoses)
        {
            localPoses[member] = pose;
        }
    }

    private void ApplyTrack(AnimationTrack track, Dictionary<SceneObject, Matrix4x4> localPoses,
        ref Matrix4x4 rootRotation, ref Vector3 rootOffset, ref Matrix4x4? cinematicRootPose)
    {
        SceneObject? target = ResolveTrackTarget(track);
        if (target == null)
        {
            return;
        }

        var keys = GetTrackKeys(track);
        if (keys.Count == 0)
        {
            return;
        }

        float trackTime = Time;
        if (track.CinematicPlaybackClock is { } playbackClock
            && !playbackClock.TryMap(Time, out trackTime)) return;

        int currentIndex = Math.Max(AnimationKeySearch.LastAtOrBefore(keys, trackTime), 0);
        AnimationKey currentKey = keys[currentIndex];
        if (Clip!.Cinematic && (currentKey.Time > trackTime || IsEmptyKey(currentKey)))
        {
            return;
        }

        AnimationKey nextKey = currentIndex + 1 < keys.Count
            ? keys[currentIndex + 1]
            : Loop ? keys[0] : currentKey;
        float amount = AnimationInterpolation.Amount(currentKey, trackTime);
        Matrix4x4.Decompose(localPoses[target], out var scale, out var rotation, out var position);

        if (currentKey.Position is Vector3 startPosition)
        {
            position = AnimationInterpolation.Position(currentKey, startPosition,
                nextKey.Position ?? startPosition, amount);
        }

        if (currentKey.Scale is Vector3 startScale)
        {
            scale = Vector3.Lerp(startScale, nextKey.Scale ?? startScale, amount);
        }

        if (currentKey.Rotation is Quaternion startRotation)
        {
            rotation = Quaternion.Slerp(startRotation, nextKey.Rotation ?? startRotation, amount);
        }

        if (target == Root)
        {
            ApplyRootTrack(track, currentKey, keys, localPoses, scale, rotation, position,
                ref rootRotation, ref rootOffset, ref cinematicRootPose);
            return;
        }

        localPoses[target] = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(position);
    }

    private List<AnimationKey> GetTrackKeys(AnimationTrack track)
    {
        if (Clip!.Cinematic)
        {
            return track.Keys;
        }

        if (!filteredTrackKeys.TryGetValue(track, out var keys))
            filteredTrackKeys[track] = keys = track.Keys
                .Where(key => key.Position != null || key.Rotation != null || key.Scale != null)
                .ToList();
        return keys;
    }

    private SceneObject? ResolveTrackTarget(AnimationTrack track)
    {
        if (trackTargets.TryGetValue(track, out var cachedTarget)) return cachedTarget;

        SceneObject? target = ResolveTrackTargetUncached(track);
        trackTargets[track] = target;
        return target;
    }

    private SceneObject? ResolveTrackTargetUncached(AnimationTrack track)
    {
        if (track.Bone >= 0)
        {
            return track.Bone < Bones.Count ? Bones[track.Bone] : null;
        }

        return track.Target is 0 or uint.MaxValue
            ? Root
            : Members.FirstOrDefault(member => member.GaoKey == track.Target);
    }

    private void EnsureTrackCaches(AnimationClip clip)
    {
        if (ReferenceEquals(cachedTrackClip, clip) && cachedMemberCount == Members.Count) return;

        cachedTrackClip = clip;
        cachedMemberCount = Members.Count;
        trackTargets.Clear();
        filteredTrackKeys.Clear();
    }

    private static bool IsEmptyKey(AnimationKey key) =>
        key.Position == null && key.Rotation == null && key.Scale == null;

    private void ApplyRootTrack(AnimationTrack track, AnimationKey currentKey,
        IReadOnlyList<AnimationKey> keys, Dictionary<SceneObject, Matrix4x4> localPoses,
        Vector3 scale, Quaternion rotation, Vector3 position,
        ref Matrix4x4 rootRotation, ref Vector3 rootOffset, ref Matrix4x4? cinematicRootPose)
    {
        if (Clip is { Cinematic: true, CinematicBody: not null } && track.Bone < 0)
        {
            return;
        }

        if (Clip!.Cinematic && track.Bone < 0)
        {
            cinematicRootPose = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(position);
            localPoses[Root] = cinematicRootPose.Value;
            return;
        }

        AnimationKey? firstRotationKey = keys.FirstOrDefault(key => key.Rotation != null);
        if (currentKey.Rotation != null && firstRotationKey?.Rotation is Quaternion initialRotation
            && Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(initialRotation), out var inverseInitial))
        {
            rootRotation = inverseInitial * Matrix4x4.CreateFromQuaternion(rotation);
        }

        AnimationKey? firstPositionKey = keys.FirstOrDefault(key => key.Position != null);
        if (currentKey.Position != null && RootMotion && firstPositionKey?.Position is Vector3 initialPosition)
        {
            rootOffset = position - initialPosition;
        }
    }

    private void EvaluateWorldPoses(Dictionary<SceneObject, Matrix4x4> localPoses,
        Matrix4x4? cinematicRootPose, Matrix4x4 rootRotation, Vector3 rootOffset)
    {
        world.Clear();
        visitingWorldPoses.Clear();
        foreach (var member in Members)
        {
            member.PreviewMatrix = EvaluateWorldPose(member, localPoses, cinematicRootPose,
                rootRotation, rootOffset, visitingWorldPoses);
        }
    }

    private Matrix4x4 EvaluateWorldPose(SceneObject sceneObject,
        IReadOnlyDictionary<SceneObject, Matrix4x4> localPoses, Matrix4x4? cinematicRootPose,
        Matrix4x4 rootRotation, Vector3 rootOffset, HashSet<SceneObject> visiting)
    {
        if (world.TryGetValue(sceneObject, out var cachedPose))
        {
            return cachedPose;
        }

        if (sceneObject == Root)
        {
            return world[sceneObject] = cinematicRootPose
                ?? rootRotation * Matrix4x4.CreateTranslation(rootOffset) * AnimationMath.Trs(Root.Transform);
        }

        if (!visiting.Add(sceneObject))
        {
            return AnimationMath.Trs(Root.Transform);
        }

        var parent = PoseParent(sceneObject);
        var worldPose = localPoses[sceneObject] * EvaluateWorldPose(parent, localPoses,
            cinematicRootPose, rootRotation, rootOffset, visiting);
        visiting.Remove(sceneObject);
        world[sceneObject] = worldPose;
        return worldPose;
    }

    private void ApplyCurrentShape()
    {
        uint selectedShape = DefaultShape;
        uint? actionShape = Clip?.CinematicBody?.ShapeAt(Time);
        if (actionShape is { } shapeKey && shapeKey is not 0 and not uint.MaxValue)
            selectedShape = shapeKey;
        else if (Clip != null && ClipShapes.TryGetValue(Clip, out uint clipShape))
            selectedShape = clipShape;

        if (appliedShapeKey == selectedShape) return;

        ApplyShape(DefaultShape);
        if (selectedShape != DefaultShape) ApplyShape(selectedShape);
        ApplyAppearanceOverrides();
    }

}

