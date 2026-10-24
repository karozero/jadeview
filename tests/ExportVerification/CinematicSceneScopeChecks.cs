using JadeView;

internal static class CinematicSceneScopeChecks
{
    internal static void Verify()
    {
        VerifySourceObjectDependencies();
        var current = new WorldData { Key = 1000, Name = "Room_cine" };
        var other = new WorldData { Key = 2000, Name = "Other_cine" };
        var setup = new WorldData { Key = 3000, Name = "Setup" };
        var environmentVariant = new WorldData { Key = 4000, Name = "Room_Waf" };
        var baseWorld = new WorldData { Key = 5000, Name = "Room" };
        var required = Actor(7);
        var unrelated = Actor(8);
        var grouped = Actor(51);
        var scriptLinked = Actor(90);
        var group = new SceneObject { GaoKey = 50, GroupMemberKeys = [50, 51] };
        setup.Actors.AddRange([required, unrelated, grouped, scriptLinked]);
        setup.Objects.AddRange([required.Root, unrelated.Root, grouped.Root, scriptLinked.Root, group]);
        var target = new SceneObject { GaoKey = 9, Name = "Referenced target" };
        var scriptDependency = new SceneObject { GaoKey = 91, Name = "Script dependency" };
        var unrelatedScriptDependency = new SceneObject { GaoKey = 92, Name = "Unrelated script dependency" };
        var callbackLiteralDependency = Actor(0x5E006934);
        callbackLiteralDependency.Root.Name = "Callback literal actor";
        setup.Actors.Add(callbackLiteralDependency);
        setup.Objects.Add(target);
        setup.Objects.AddRange([scriptDependency, unrelatedScriptDependency]);
        var environment = new SceneObject { GaoKey = 61 };
        var unusedEnvironment = new SceneObject { GaoKey = 71 };
        baseWorld.Objects.AddRange([new() { GaoKey = 60 }, environment,
            new() { GaoKey = 70, ControlFlags = CinematicObjectState.Inactive }, unusedEnvironment]);
        var unrelatedWorldCycleTarget = new SceneObject { GaoKey = 161, Name = "Unrelated cycle target" };
        other.Objects.AddRange([new() { GaoKey = 160 }, unrelatedWorldCycleTarget]);
        var variantLandmark = new SceneObject { GaoKey = 81, Name = "Variant landmark", GeometryKey = 82 };
        var hiddenVariantGeometry = new SceneObject { GaoKey = 83, GeometryKey = 84, DrawFlags = 0 };
        var unrelatedVariantActor = Actor(85);
        environmentVariant.Objects.AddRange([variantLandmark, hiddenVariantGeometry, unrelatedVariantActor.Root]);
        environmentVariant.Actors.Add(unrelatedVariantActor);
        var source = new AnimationClip
        {
            Key = 1001,
            Loaded = true,
            Cinematic = true,
            CinematicWorldKey = current.Key,
            Duration = 1,
            Tracks =
            [new() { Bone = -1, Target = 99, Keys = [CinematicTestEvents.Message(0, 99, 60,
                new(), new(), 1, 0, object1: 7, object2: 9), CinematicTestEvents.Call(0, 50, 15300, 0)] }]
        };
        var otherSource = new AnimationClip
        {
            Key = 2001,
            Loaded = true,
            Cinematic = true,
            CinematicWorldKey = other.Key,
            Tracks = [new() { Bone = -1, Target = 8 }]
        };
        var document = new SceneDocument();
        document.CinematicEnvironmentControllers.Add(new(baseWorld.Key, 60), new(60, baseWorld.Key, [61]));
        document.CinematicEnvironmentControllers.Add(new(baseWorld.Key, 70), new(70, baseWorld.Key, [71]));
        document.CinematicEnvironmentControllers.Add(new(other.Key, 160), new(160, other.Key, [161]));
        document.CinematicScriptOwners[new(current.Key, 1200)] = new(1200, 0, 0, current.Key,
            uint.MaxValue, source.Key, "Sequence owner", ["Cine_FigeleMonde_flm_fige"],
            [new("des_o_actor", scriptLinked.Root.GaoKey, "instance", scriptLinked.Root.Name)], [])
        {
            ModelFunctionNames = ["Cine_FigeleMonde_flm_fige"]
        };
        document.CinematicScriptOwners[new(setup.Key, scriptLinked.Root.GaoKey)] = new(
            scriptLinked.Root.GaoKey, 0, 0, setup.Key, uint.MaxValue, uint.MaxValue,
            "Linked actor brain", [],
            [new("des_o_prop", scriptDependency.GaoKey, "instance", scriptDependency.Name)], []);
        document.CinematicScriptOwners[new(current.Key, 1400)] = new(1400, 0, 0, current.Key,
            2001, uint.MaxValue, "Unrelated sequence owner", [],
            [new("des_o_unrelated", unrelatedScriptDependency.GaoKey, "instance", unrelatedScriptDependency.Name)], []);
        document.Worlds.AddRange([current, other, setup, environmentVariant, baseWorld]);
        document.CinematicClips.Add(source.Key, source);
        document.CinematicClips.Add(otherSource.Key, otherSource);
        var assembled = CinematicWorldAssembler.Assemble(document, current);
        if (!assembled.Actors.Contains(required) || !assembled.Actors.Contains(grouped)
            || !assembled.Actors.Contains(scriptLinked) || assembled.Actors.Contains(unrelated)
            || !assembled.Objects.Contains(target) || !assembled.Objects.Contains(scriptDependency)
            || !assembled.Actors.Contains(callbackLiteralDependency)
            || assembled.Objects.Contains(unrelatedScriptDependency) || !assembled.Objects.Contains(environment)
            || !assembled.Objects.Contains(unusedEnvironment)
            || assembled.Objects.Contains(unrelatedWorldCycleTarget) || !assembled.Objects.Contains(variantLandmark)
            || assembled.Objects.Contains(hiddenVariantGeometry) || assembled.Objects.Contains(unrelatedVariantActor.Root)
            || assembled.CinematicClips.Count != 1
            || assembled.CinematicClips.ContainsKey(otherSource.Key))
            throw new InvalidDataException("Scene assembly lost literal dependencies, omitted initially inactive controller assets, or mixed another cinematic world.");
        Console.WriteLine("PASS: per-world clip scope, scene-scoped environment controllers and camera-only actor/target inclusion");
    }

    private static void VerifySourceObjectDependencies()
    {
        CinematicScriptReachableSourceReference[] sourceReferences =
        [
            new("callback", "Object", "single", 101, "iris.c", 10, "callback"),
            new("callback", "Object", "main-actor-slot", 1, "iris.c", 10, "callback"),
            new("callback", "Object", "ambiguous", 202, "iris.c", 11, "callback"),
            new("callback", "Function", "not-an-object", 303, "iris.c", 12, "callback"),
            new("callback", "Object", "not-loaded", 404, "iris.c", 13, "callback")
        ];
        CinematicScriptNamedReference[] availableReferences =
        [
            new(1, 101, "Unique prop", IsObject: true),
            new(1, 1, "not an archive object", IsObject: true),
            new(1, 202, "Duplicate prop A", IsObject: true),
            new(2, 202, "Duplicate prop B", IsObject: true),
            new(1, 303, "Function key collision", IsObject: true)
        ];
        var resolved = CinematicScriptSourceObjectDependencyResolver.ResolveUniqueObjects(
            sourceReferences, availableReferences);
        if (resolved.Length != 1 || resolved[0].Key != 101 || resolved[0].Name != "Unique prop")
            throw new InvalidDataException("Cinematic source dependencies must include only uniquely available literal object references.");
    }

    private static SceneActor Actor(uint key)
    {
        var root = new SceneObject { GaoKey = key, Name = $"Actor {key}" };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        return actor;
    }
}
