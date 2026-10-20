using JadeView;

internal static class CinematicSceneScopeChecks
{
    internal static void Verify()
    {
        var current = new WorldData { Key = 1000, Name = "Room_cine" };
        var other = new WorldData { Key = 2000, Name = "Other_cine" };
        var setup = new WorldData { Key = 3000, Name = "Setup" };
        var required = Actor(7);
        var unrelated = Actor(8);
        var grouped = Actor(51);
        var group = new SceneObject { GaoKey = 50, GroupMemberKeys = [50, 51] };
        setup.Actors.AddRange([required, unrelated, grouped]);
        setup.Objects.AddRange([required.Root, unrelated.Root, grouped.Root, group]);
        var target = new SceneObject { GaoKey = 9, Name = "Referenced target" };
        setup.Objects.Add(target);
        var environment = new SceneObject { GaoKey = 61 };
        var unusedEnvironment = new SceneObject { GaoKey = 71 };
        setup.Objects.AddRange([new() { GaoKey = 60 }, environment,
            new() { GaoKey = 70, ControlFlags = CinematicObjectState.Inactive }, unusedEnvironment]);
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
        document.CinematicEnvironmentControllers.Add(60, new(60, setup.Key, [61]));
        document.CinematicEnvironmentControllers.Add(70, new(70, setup.Key, [71]));
        document.Worlds.AddRange([current, other, setup]);
        document.CinematicClips.Add(source.Key, source);
        document.CinematicClips.Add(otherSource.Key, otherSource);
        var assembled = CinematicWorldAssembler.Assemble(document, current);
        if (!assembled.Actors.Contains(required) || !assembled.Actors.Contains(grouped) || assembled.Actors.Contains(unrelated)
            || !assembled.Objects.Contains(target) || !assembled.Objects.Contains(environment)
            || assembled.Objects.Contains(unusedEnvironment) || assembled.CinematicClips.Count != 1
            || assembled.CinematicClips.ContainsKey(otherSource.Key))
            throw new InvalidDataException("Scene assembly lost literal camera references or mixed another cinematic world.");
        Console.WriteLine("PASS: per-world clip scope, literal-only scene dependencies and camera-only actor/target inclusion");
    }

    private static SceneActor Actor(uint key)
    {
        var root = new SceneObject { GaoKey = key, Name = $"Actor {key}" };
        var actor = new SceneActor { Root = root, Members = [root] };
        root.Actor = actor;
        return actor;
    }
}
