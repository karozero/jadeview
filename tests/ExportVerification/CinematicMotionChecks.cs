using System.Numerics;
using JadeView;

internal static class CinematicMotionChecks
{
    internal static void Verify()
    {
        AnimationKey[] orderedKeys =
            [new() { Time = 0 }, new() { Time = .5f }, new() { Time = .5f }, new() { Time = 1 }];
        if (AnimationKeySearch.LastAtOrBefore(orderedKeys, -.1f) != -1
            || AnimationKeySearch.LastAtOrBefore(orderedKeys, 0) != 0
            || AnimationKeySearch.LastAtOrBefore(orderedKeys, .5f) != 2
            || AnimationKeySearch.LastAtOrBefore(orderedKeys, 2) != 3)
            throw new InvalidDataException("Binary animation-key search changed boundary or duplicate-time behavior.");

        var root = new SceneObject
        {
            GaoKey = 1,
            Name = "Rotated actor",
            Transform = new()
            {
                Translation = new(10, 20, 0),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)
            }
        };
        var pelvis = new SceneObject { GaoKey = 2, ActorNode = new() { Parent = root.GaoKey } };
        var child = new SceneObject
        {
            GaoKey = 3,
            ActorNode = new()
            {
                Parent = pelvis.GaoKey,
                Local = Matrix4x4.CreateTranslation(Vector3.UnitY)
            }
        };
        var actor = new SceneActor { Root = root, Members = [root, pelvis, child], Bones = [pelvis, child] };
        var motion = new AnimationClip
        {
            Loaded = true,
            Duration = 1,
            Tracks =
        [new() { Bone = -1, Target = 0, Keys =
            [new() { Duration = 1, Position = Vector3.Zero }, new() { Time = 1, Position = Vector3.UnitY }] },
            new() { Bone = 0, Keys = [new() { Duration = 1, Position = Vector3.UnitZ },
                new() { Time = 1, Position = Vector3.UnitY + Vector3.UnitZ }] }]
        };
        var action = new CinematicAction(0, [new(motion, 0, 64, 1)]);
        var source = new AnimationClip
        {
            Loaded = true,
            Duration = 4,
            Tracks =
        [new() { Bone = -1, Target = 1, Keys = [Message47(0, 1), Message47(2.5f, 0)] }]
        };
        var timeline = CinematicBodyTimeline.Create(source, actor, new Dictionary<uint, Dictionary<int, CinematicAction>>
        {
            [1] = new() { [0] = action }
        });
        CheckPosition(timeline, .5f, new(9.5f, 20, 0));
        actor.Clip = new() { Loaded = true, Cinematic = true, Duration = 4, CinematicBody = timeline };
        foreach (float sampleTime in new[] { .5f, 2.25f, 3.5f, .5f })
        {
            actor.Time = sampleTime;
            actor.Evaluate(0);
            Vector3 expectedPelvis = timeline.RootPose(sampleTime).Translation + Vector3.UnitZ;
            if (Vector3.Distance(pelvis.PreviewMatrix!.Value.Translation, expectedPelvis) > .001f
                || Vector3.Distance(child.PreviewMatrix!.Value.Translation, expectedPelvis - Vector3.UnitX) > .001f)
                throw new InvalidDataException("Body root travel was counted again in the skeleton or removed from a deeper child.");
        }
        pelvis.ActorNode!.Parent = 0; // Serialized skeleton roots use an implicit actor parent.
        actor.Time = .5f;
        actor.Evaluate(0);
        if (Vector3.Distance(pelvis.PreviewMatrix!.Value.Translation,
            timeline.RootPose(.5f).Translation + Vector3.UnitZ) > .001f)
            throw new InvalidDataException("Implicit skeleton roots did not receive root-motion compensation.");
        foreach (int boneIndex in new[] { -1, 0 })
            motion.Tracks.Add(new()
            {
                Bone = boneIndex,
                Keys =
            [new() { Duration = 1, Rotation = Quaternion.Identity },
                new() { Time = 1, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) }]
            });
        actor.Clip.CinematicBody = CinematicBodyTimeline.Create(source, actor,
            new Dictionary<uint, Dictionary<int, CinematicAction>> { [1] = new() { [0] = action } });
        actor.Evaluate(0);
        var rootForward = Vector3.TransformNormal(Vector3.UnitY, root.PreviewMatrix!.Value);
        var pelvisForward = Vector3.TransformNormal(Vector3.UnitY, pelvis.PreviewMatrix!.Value);
        if (Vector3.Distance(rootForward, pelvisForward) > .001f)
            throw new InvalidDataException("Action rotation was applied twice to the pelvis.");
        CheckPosition(timeline, 2.25f, new(7.75f, 20, 0));
        CheckPosition(timeline, 3.5f, new(7.5f, 20, 0));
        CheckPosition(timeline, .5f, new(9.5f, 20, 0));

        var sparse = new AnimationTrack
        {
            Keys =
        [new() { Duration = 1 }, new() { Time = 1, Duration = .5f, Position = new(100, 0, 0) },
            new() { Time = 1.5f, Duration = 1 }, new() { Time = 2.5f, Position = new(200, 0, 0) }]
        };
        if (CinematicTransformSampler.Evaluate(root.Transform, [sparse], .5f).Translation != root.Transform.Translation
            || CinematicTransformSampler.Evaluate(root.Transform, [sparse], 2, holdInactive: false).Translation != root.Transform.Translation)
            throw new InvalidDataException("Empty source intervals were replaced by future/previous transform keys.");
        if (CinematicTransformSampler.Evaluate(root.Transform, [sparse], 2).Translation != new Vector3(100, 0, 0))
            throw new InvalidDataException("A camera/object lost its held transform during an empty source interval.");

        VerifyRootPositionSetterDoesNotFreezeAnimation();

        var reference = Message47(0, 1);
        byte[] dynamic = reference.OpaquePayloads[0].Data.ToArray();
        dynamic[4 + 15 * 8 + 7] = 4;
        if (CinematicActionEvent.Decode(new(), 1, new("word-pair-list", 0, dynamic)) != null)
            throw new InvalidDataException("A dynamic action operand was treated as a literal.");
        Console.WriteLine("PASS: tagged retail messages, gameplay handoff, accumulated rotated root motion, loop continuity, empty intervals and reverse seek");
    }

    private static void VerifyRootPositionSetterDoesNotFreezeAnimation()
    {
        CinematicBodyTimeline CreateTimeline(bool addPositionSet)
        {
            var actorRoot = new SceneObject { GaoKey = 10 };
            actorRoot.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
            var actor = new SceneActor { Root = actorRoot, Members = [actorRoot] };
            var actionClip = new AnimationClip
            {
                Loaded = true,
                Duration = 1,
                Tracks = [new() { Bone = -1, Target = 0, Keys =
                    [new() { Duration = 1, Position = Vector3.Zero },
                        new() { Time = 1, Position = Vector3.UnitX }] }]
            };
            var action = new CinematicAction(0,
                [new CinematicActionItem(actionClip, 0, 64, 100) { Flags = 0 }]);
            var source = new AnimationClip
            {
                Loaded = true,
                Duration = 2,
                Tracks = [new() { Bone = -1, Target = actorRoot.GaoKey, Keys =
                    [Message47(0, 1)] }]
            };
            (float Time, CinematicLiteralCall Call)[] nestedCalls = [];
            if (addPositionSet)
            {
                source.Tracks.Add(new()
                {
                    Bone = -1,
                    Target = 0,
                    Keys = [CinematicTestEvents.VectorCall(0, actorRoot.GaoKey,
                            0x1fa6, new(50, 0, 0)),
                        CinematicTestEvents.VectorCall(.5f, actorRoot.GaoKey,
                            0x1fa6, new(100, 0, 0))]
                });
                nestedCalls = [(.5f, new(actorRoot.GaoKey, 0x1fa6, [Bits(200), Bits(0), Bits(0)]))];
            }
            return CinematicBodyTimeline.Create(source, actor,
                new Dictionary<uint, Dictionary<int, CinematicAction>>
                { [actorRoot.GaoKey] = new() { [0] = action } }, nestedCalls);
        }

        CinematicBodyTimeline baseline = CreateTimeline(addPositionSet: false);
        CinematicBodyTimeline withPositionSet = CreateTimeline(addPositionSet: true);
        Vector3 baselineDelta = baseline.RootPose(.75f).Translation - baseline.RootPose(.5f).Translation;
        Vector3 setPosition = withPositionSet.RootPose(.5f).Translation;
        Vector3 movedAfterSet = withPositionSet.RootPose(.75f).Translation - setPosition;
        Vector3 initialSetOffset = withPositionSet.RootPose(.25f).Translation - baseline.RootPose(.25f).Translation;
        if (Vector3.Distance(initialSetOffset, new(50, 0, 0)) > .001f)
            throw new InvalidDataException($"A time-zero OBJ_PosSet did not remain the body-motion origin: {initialSetOffset}.");
        if (Vector3.Distance(setPosition, new(200, 0, 0)) > .001f)
            throw new InvalidDataException("OBJ_PosSet did not apply its absolute position at the command time.");
        if (baselineDelta.Length() < .1f || Vector3.Distance(movedAfterSet, baselineDelta) > .001f)
            throw new InvalidDataException($"A one-shot OBJ_PosSet froze later ANI root motion: {movedAfterSet}, expected {baselineDelta}.");
        Vector3 afterSet = withPositionSet.RootPose(.75f).Translation;
        _ = withPositionSet.RootPose(.1f);
        if (Vector3.Distance(withPositionSet.RootPose(.75f).Translation, afterSet) > .001f)
            throw new InvalidDataException("Reverse seeking reapplied a one-shot position setter to a later pose.");

        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    }

    private static void CheckPosition(CinematicBodyTimeline timeline, float time, Vector3 expected)
    {
        if (Vector3.Distance(timeline.RootPose(time).Translation, expected) > .001f)
            throw new InvalidDataException($"Root motion mismatch at {time}: {timeline.RootPose(time).Translation}, expected {expected}");
    }

    private static AnimationKey Message47(float time, uint mode)
    {
        // Literal categories/types are meaningful; compiler annotations and END's
        // unused value are deliberately nonzero, as in retail BF003BD1.
        uint[] values = [0, 0, 47, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, mode, 0, 0x9A8897AA];
        uint[] tags = [0x01000028, 0x02000806, 0x01000021, 0x01000028, 0x01000028,
            0x01000022, 0x01000022, 0x01000022, 0x03001770, 0x01000022, 0x01000022,
            0x01000022, 0x03001770, 0x016E0022, 0x01780022, 0x01010021, 0x01980021, 0x03881F78, 0x1F669899];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++) { writer.Write(values[index]); writer.Write(tags[index]); }
        return new() { Time = time, OpaquePayloads = [new("word-pair-list", 0, stream.ToArray())] };
    }
}
