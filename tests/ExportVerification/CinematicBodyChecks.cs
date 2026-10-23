using System.Numerics;
using System.Linq;
using JadeView;

internal static class CinematicBodyChecks
{
    internal static void Verify()
    {
        var root = new SceneObject { Name = "Body actor", GaoKey = 1 };
        var bone = new SceneObject
        {
            Name = "Animated bone",
            GaoKey = 2,
            ActorNode = new() { Parent = root.GaoKey, Local = Matrix4x4.Identity }
        };
        var actor = new SceneActor { Root = root, Members = [root, bone], Bones = [bone] };
        actor.DefaultShape = 100;
        actor.Shapes[100] = [1, 0, 0, 254]; // Native no-hide source for bone 0.
        actor.Shapes[101] = [1, 0, 0, 253]; // Native hide source for bone 0.
        var encodedForcedActionKey = SetAction(0, 0x800000C9);
        var encodedForcedAction = CinematicActionEvent.Decode(encodedForcedActionKey, 1,
            encodedForcedActionKey.OpaquePayloads.Single());
        var noActionKey = SetAction(0, uint.MaxValue);
        var noAction = CinematicActionEvent.Decode(noActionKey, 1, noActionKey.OpaquePayloads.Single());
        if (encodedForcedAction is not { Action: 201, Force: true }
            || noAction is not { Action: -1, Force: false })
            throw new InvalidDataException("ACTActionSet high-bit force and no-action sentinel were not decoded correctly.");
        root.Actor = bone.Actor = actor;
        var camera = new SceneObject { GaoKey = 3, Name = "Camera" };
        var source = new AnimationClip
        {
            Key = 10,
            Loaded = true,
            Cinematic = true,
            SceneMotion = true,
            Duration = 6,
            Tracks =
            [
                new() { Bone = -1, Target = 1, Keys =
                [new() { Duration = 6, Position = new(100, 20, 5) }] },
                new() { Bone = -1, Target = 3, Keys = [new() { Duration = 6, Position = new(95, 20, 5) }] },
                new() { Bone = -1, Target = 1, Keys =
                [
                    Event(0, (0, 0x01000028), (0, 0x02000806), (44, 0x01000028),
                        (200, 0x01000021), (201, 0x01000021), (0, 0x03003398)),
                    Message47(0, 200),
                    SetAction(2, 201),
                    Event(3, (0, 0x01000028), (0, 0x02000806), (201, 0x01000021),
                        (30, 0x01000021), (0, 0x0300339b)),
                    SetAction(4, 200), SetAction(4.2f, 200), SetAction(4.4f, 0x800000C9),
                    Event(5, (0, 0x01000028), (0, 0x02000806), (200, 0x01000021),
                        (32, 0x01000021), (0, 0x0300339c))
                ] }
            ]
        };
        var document = new SceneDocument();
        document.CinematicClips.Add(source.Key, source);
        var bodyActions = new Dictionary<int, CinematicAction>
        {
            [200] = Action(0),
            [201] = Action(10, 101)
        };
        document.CinematicActionKits.Add(44, bodyActions);
        document.CinematicActionKitsByOwner[(0, 44)] = bodyActions;
        var world = new WorldData();
        world.Objects.AddRange([root, bone, camera]);
        world.Actors.Add(actor);
        var playback = CinematicPlayback.Discover(document, world, new ResourceResolver()).Single();
        playback.Activate();
        VerifyBone(.5f, .5f);
        VerifyBone(2.5f, 10.5f);
        if (!bone.PreviewHidden)
            throw new InvalidDataException("Cinematic action shape was not applied to its actor.");
        var cinematicBody = actor.Clip?.CinematicBody;
        if (cinematicBody?.ShapeAt(2.5f) != 101u)
            throw new InvalidDataException("Active cinematic item shape key was not discovered from the body timeline.");
        if (cinematicBody == null || !cinematicBody.ShapeKeys.Contains(101u))
            throw new InvalidDataException("Cinematic body timeline did not expose its referenced shape key.");
        VerifyBone(3, 10.5f);
        VerifyBone(3.25f, 10.75f);
        VerifyBone(4.5f, 10.1f);
        VerifyBone(5.5f, .25f);
        if (bone.PreviewHidden)
            throw new InvalidDataException("Cinematic action shape did not return to the actor's default shape.");
        VerifyBone(.5f, .5f);
        if (actor.Clip!.CinematicBody!.Diagnostics.Count != 0)
            throw new InvalidDataException("Resolved action kit unexpectedly produced missing-binding warnings.");
        Console.WriteLine("PASS: cinematic kit merge, message-47 startup, body actions, frame/frequency controls and deterministic seek");

        (float Time, CinematicLiteralCall Call)[] nestedBodyCalls =
        [
            (1f, new(root.GaoKey, 0x3398, [44, 250, 250])),
            (2f, new(root.GaoKey, 0x3391, [250]))
        ];
        var nestedBodyKits = new Dictionary<(uint WorldKey, uint OwnerKey), Dictionary<int, CinematicAction>>
        {
            [(7, root.GaoKey)] = bodyActions,
            [(7, 44)] = new() { [250] = Action(20, 101) }
        };
        var nestedBodyTimeline = CinematicBodyTimeline.Create(new AnimationClip { Duration = 3 }, actor,
            7, nestedBodyKits, nestedBodyCalls);
        if (nestedBodyTimeline.ActionCount != 1 || nestedBodyTimeline.FirstActionTime != 2f
            || nestedBodyTimeline.Diagnostics.Count != 0)
            throw new InvalidDataException("Nested SceneEvents action-kit merges and body actions must resolve on the shared cinematic clock.");

        var nestedFrameEvent = CinematicActionEvent.FromLiteralCall(3,
            new(root.GaoKey, 0x339B, [200, 24]));
        var nestedFrequencyEvent = CinematicActionEvent.FromLiteralCall(4,
            new(root.GaoKey, 0x339C, [200, 64]));
        if (nestedFrameEvent is not { Action: 200, StartFrame: 24, Force: true }
            || nestedFrequencyEvent is not { Action: 200, Frequency: 64, Force: true })
            throw new InvalidDataException("Nested ACT frame/frequency commands must keep their serialized controls.");
        Console.WriteLine("PASS: SceneEvents body-action commands, kit merges, frames and frequencies join cinematic playback");

        var unresolvedSource = new AnimationClip
        {
            Loaded = true,
            Cinematic = true,
            Duration = 1,
            Tracks = [new() { Bone = -1, Target = root.GaoKey,
                Keys = [SetAction(0, 47)] }]
        };
        var unresolved = CinematicBodyTimeline.Create(unresolvedSource, actor,
            new Dictionary<uint, Dictionary<int, CinematicAction>>());
        if (unresolved.ActionCount != 0 || !unresolved.Diagnostics.Any(message =>
                message.Contains("No cinematic skeletal action resolved", StringComparison.Ordinal)))
            throw new InvalidDataException("Unresolved skeletal action was not surfaced as a cinematic diagnostic.");

        var unrelatedEventSource = new AnimationClip
        {
            Loaded = true,
            Cinematic = true,
            Duration = 1,
            Tracks = [new() { Bone = -1, Target = root.GaoKey,
                Keys = [Event(0, (0, 0x01000028), (0, 0x02000806), (1, 0x01000021), (0, 0x03003370))] }]
        };
        var unrelated = CinematicBodyTimeline.Create(unrelatedEventSource, actor,
            new Dictionary<uint, Dictionary<int, CinematicAction>>());
        if (unrelated.Diagnostics.Any(message =>
                message.Contains("No cinematic skeletal action resolved", StringComparison.Ordinal)))
            throw new InvalidDataException("An unrelated opaque event was misreported as a missing body action.");
        Console.WriteLine("PASS: only decoded body-action requests report missing skeletal playback");

        var rootMotionOnlySource = new AnimationClip
        {
            Loaded = true,
            Cinematic = true,
            Duration = 1,
            Tracks = [new() { Bone = -1, Target = root.GaoKey,
                Keys = [new() { Duration = 1, Position = Vector3.One }] }]
        };
        var rootMotionOnly = CinematicBodyTimeline.Create(rootMotionOnlySource, actor,
            new Dictionary<uint, Dictionary<int, CinematicAction>>());
        if (!rootMotionOnly.Diagnostics.Any(message =>
                message.Contains("preview holds its bind pose", StringComparison.Ordinal)))
            throw new InvalidDataException("Root-only cinematic motion did not explain why a rig remains in bind pose.");

        var skeletalTrackSource = new AnimationClip
        {
            Loaded = true,
            Cinematic = true,
            Duration = 1,
            Tracks =
            [
                new() { Bone = -1, Target = root.GaoKey,
                    Keys = [new() { Duration = 1, Position = Vector3.One }] },
                new() { Bone = 0, Target = root.GaoKey, Keys = [new() { Duration = 1, Position = Vector3.One }] }
            ]
        };
        var skeletalTrack = CinematicBodyTimeline.Create(skeletalTrackSource, actor,
            new Dictionary<uint, Dictionary<int, CinematicAction>>());
        if (skeletalTrack.Diagnostics.Any(message => message.Contains(
                "preview holds its bind pose", StringComparison.Ordinal)))
            throw new InvalidDataException("A rig with serialized skeletal pose data was reported as bind-pose-only.");
        Console.WriteLine("PASS: root-only actor motion distinguishes runtime rig pose from cinematic skeletal data");

        var secondRoot = new SceneObject { GaoKey = 4, Name = "Second shared-event actor" };
        var secondBone = new SceneObject { GaoKey = 5, ActorNode = new() { Parent = secondRoot.GaoKey } };
        var secondActor = new SceneActor { Root = secondRoot, Members = [secondRoot, secondBone], Bones = [secondBone] };
        secondRoot.Actor = secondBone.Actor = secondActor;
        var sharedOwnerClip = new AnimationClip
        {
            CinematicOwnerKey = root.GaoKey,
            CinematicOwnerKeys = [root.GaoKey, secondRoot.GaoKey],
            Duration = 1,
            Tracks = [new() { Bone = -1, Target = 0, Keys = [SetAction(0, 200)] }]
        };
        var sharedKits = new Dictionary<uint, Dictionary<int, CinematicAction>>
        {
            [root.GaoKey] = new() { [200] = Action(0) },
            [secondRoot.GaoKey] = new() { [200] = Action(2) }
        };
        var firstOwnerTimeline = CinematicBodyTimeline.Create(sharedOwnerClip, actor, sharedKits);
        var secondOwnerTimeline = CinematicBodyTimeline.Create(sharedOwnerClip, secondActor, sharedKits);
        if (firstOwnerTimeline.ActionCount != 1 || secondOwnerTimeline.ActionCount != 1
            || firstOwnerTimeline.Diagnostics.Count != 0 || secondOwnerTimeline.Diagnostics.Count != 0)
            throw new InvalidDataException("A target-zero body action on a shared clip did not resolve for each event owner.");
        Console.WriteLine("PASS: shared event owner contexts resolve body actions for each actor");

        var hiddenRoot = new SceneObject { Name = "Appearance actor", GaoKey = 11 };
        var hiddenBone = new SceneObject { Name = "Hidden morph component", GaoKey = 12 };
        var morphSource = new SceneObject { Name = "Morph source", GeometryKey = 13 };
        var hiddenActor = new SceneActor
        {
            Root = hiddenRoot,
            Members = [hiddenRoot, hiddenBone],
            Bones = [hiddenBone],
            DefaultShape = 200,
            Shapes = { [200] = [1, 0, 0, 253] },
            SourceMeshes = { [0] = [morphSource] }
        };
        hiddenRoot.Actor = hiddenBone.Actor = hiddenActor;
        hiddenActor.ApplyShape(hiddenActor.DefaultShape);
        if (!hiddenBone.PreviewHidden)
            throw new InvalidDataException("Test setup did not hide the morph component.");
        hiddenActor.PreviewAppearance(hiddenBone, 0);
        if (!hiddenBone.PreviewHidden)
            throw new InvalidDataException("Morph appearance override made a shape-hidden component visible.");
        hiddenActor.Evaluate(0);
        if (!hiddenBone.PreviewHidden)
            throw new InvalidDataException("Actor evaluation made a shape-hidden morph component visible.");
        Console.WriteLine("PASS: morph appearance overrides preserve shape-hidden component visibility");

        var companionRoot = new SceneObject { Name = "Companion actor", GaoKey = 21 };
        var companionHead = new SceneObject { Name = "Alternate head", GaoKey = 22, GeometryKey = 23 };
        var companionHair = new SceneObject { Name = "Separate hair", GaoKey = 0xA900532C };
        var alternateHead = new SceneObject { Name = "HD head source", GeometryKey = 0x8F0066B2 };
        var alternateHair = new SceneObject { Name = "HD hair source", GeometryKey = 0xA90052F4 };
        var companionActor = new SceneActor
        {
            Root = companionRoot,
            Members = [companionRoot, companionHead, companionHair],
            Bones = [companionHead, companionHair],
            DefaultShape = 201,
            Shapes = { [201] = [1, 1, 0, 253] },
            SourceMeshes = { [0] = [alternateHead], [1] = [alternateHair] }
        };
        companionRoot.Actor = companionHead.Actor = companionHair.Actor = companionActor;
        companionActor.ApplyShape(companionActor.DefaultShape);
        if (!companionHair.PreviewHidden)
            throw new InvalidDataException("Test setup did not hide the companion hair mesh.");
        companionActor.PreviewAppearance(companionHead, 0);
        if (companionHair.PreviewHidden || companionHair.RenderGeometryKey != 0xA90052F4)
            throw new InvalidDataException("Explicit head appearance binding did not activate its hair companion.");
        companionActor.Evaluate(0);
        if (companionHair.PreviewHidden || companionHair.RenderGeometryKey != 0xA90052F4)
            throw new InvalidDataException("Actor evaluation did not preserve the explicitly selected hair companion.");
        Console.WriteLine("PASS: explicit appearance companion binding activates shape-hidden companion meshes");

        var cachedShapeRoot = new SceneObject { Name = "Cached shape actor", GaoKey = 31 };
        var cachedShapeBone = new SceneObject
        {
            Name = "Cached shape bone",
            GaoKey = 32,
            ActorNode = new() { Parent = cachedShapeRoot.GaoKey, Local = Matrix4x4.Identity }
        };
        var firstAppearance = new SceneObject { GeometryKey = 33, MaterialKey = 34 };
        var secondAppearance = new SceneObject { GeometryKey = 35, MaterialKey = 36 };
        var cachedShapeActor = new SceneActor
        {
            Root = cachedShapeRoot,
            Members = [cachedShapeRoot, cachedShapeBone],
            Bones = [cachedShapeBone],
            DefaultShape = 301,
            Shapes = { [301] = [1, 0, 0, 0], [302] = [1, 0, 0, 1] },
            SourceMeshes = { [0] = [firstAppearance, secondAppearance] }
        };
        cachedShapeRoot.Actor = cachedShapeBone.Actor = cachedShapeActor;
        cachedShapeActor.ApplyShape(cachedShapeActor.DefaultShape);
        int originalAppearanceRevision = cachedShapeBone.AppearanceRevision;
        cachedShapeActor.Evaluate(0);
        cachedShapeActor.Evaluate(0);
        if (cachedShapeBone.AppearanceRevision != originalAppearanceRevision)
            throw new InvalidDataException("Evaluating an unchanged actor shape invalidated its appearance every frame.");
        cachedShapeActor.DefaultShape = 302;
        cachedShapeActor.Evaluate(0);
        int changedAppearanceRevision = cachedShapeBone.AppearanceRevision;
        cachedShapeActor.Evaluate(0);
        if (cachedShapeBone.GeometryKey != secondAppearance.GeometryKey
            || changedAppearanceRevision != originalAppearanceRevision + 1
            || cachedShapeBone.AppearanceRevision != changedAppearanceRevision)
            throw new InvalidDataException("Actor shape cache skipped a real shape change or reapplied the selected shape on later frames.");
        Console.WriteLine("PASS: unchanged actor shapes avoid per-frame appearance invalidation while real shape changes apply once");

        void VerifyBone(float time, float localY)
        {
            playback.Seek(time);
            var expected = new Vector3(100, 20 + localY, 5);
            if (Vector3.Distance(bone.PreviewMatrix!.Value.Translation, expected) > .0001f)
                throw new InvalidDataException($"Cinematic bone mismatch at {time}: {bone.PreviewMatrix.Value.Translation}, expected {expected}");
        }
    }

    private static CinematicAction Action(float offset, uint shapeKey = 0) => new(0,
    [new(new AnimationClip
    {
        Loaded = true, Duration = 1, Tracks =
        [new() { Bone = 0, Keys =
        [
            new() { Time = 0, Duration = 1, Position = new(0, offset, 0) },
            new() { Time = 1, Position = new(0, offset + 1, 0) }
        ] }]
    }, shapeKey, 64, 1)]);

    private static AnimationKey SetAction(float time, uint action) => Event(time,
        (0, 0x01000028), (0, 0x02000806), (action, 0x01000021), (0, 0x03003391));

    private static AnimationKey Message47(float time, uint action) => Event(time,
        (0, 0x01000028), (0, 0x02000806), (47, 0x01000021),
        (0, 0x01000028), (0, 0x01000028),
        (0, 0x01000022), (0, 0x01000022), (0, 0x01000022), (0, 0x03001770),
        (0, 0x01000022), (0, 0x01000022), (0, 0x01000022), (0, 0x03001770),
        (0, 0x010a0022), (0, 0x01720022), (action, 0x01000021), (1, 0x01000021), (0, 0x03001f78));

    private static AnimationKey Event(float time, params (uint Value, uint Tag)[] rows)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(rows.Length + 1);
        foreach (var row in rows) { writer.Write(row.Value); writer.Write(row.Tag); }
        writer.Write(0u);
        writer.Write(0x1f000000u);
        return new() { Time = time, OpaquePayloads = [new("word-pair-list", 0, buffer.ToArray())] };
    }
}
