using JadeView;

internal static class OutlinerRowChecks
{
    internal static void Verify()
    {
        var actorRoot = new SceneObject { Id = "actor-root", Name = "Actor root", GaoKey = 1 };
        var matchingComponent = new SceneObject { Id = "actor-mesh", Name = "Actor mesh", GaoKey = 2 };
        var filteredComponent = new SceneObject { Id = "actor-hand", Name = "Actor hand", GaoKey = 3 };
        var actor = new SceneActor { Root = actorRoot, Members = [actorRoot, matchingComponent, filteredComponent] };
        actorRoot.Actor = actor;
        matchingComponent.Actor = actor;
        filteredComponent.Actor = actor;
        var standalone = new SceneObject { Id = "crate", Name = "Crate", GaoKey = 4 };
        var world = new WorldData();
        world.Actors.Add(actor);
        world.Objects.AddRange([actorRoot, matchingComponent, filteredComponent, standalone]);

        var rows = new List<OutlinerRow>();
        var matches = new HashSet<SceneObject>();
        var expandedActors = new HashSet<SceneObject>();
        bool MatchesFilter(SceneObject sceneObject) =>
            sceneObject.Name.Contains("mesh", StringComparison.OrdinalIgnoreCase)
            || sceneObject.Name.Contains("crate", StringComparison.OrdinalIgnoreCase);

        OutlinerRowBuilder.Build(world, MatchesFilter, expandedActors, null, rows, matches);
        Require(rows.Count == 2
            && rows[0] == new OutlinerRow(OutlinerRowKind.Actor, actorRoot, actor)
            && rows[1] == new OutlinerRow(OutlinerRowKind.SceneObject, standalone),
            "Collapsed actors should show one group row, and matching standalone objects should remain visible.");

        expandedActors.Add(actorRoot);
        OutlinerRowBuilder.Build(world, MatchesFilter, expandedActors, null, rows, matches);
        Require(rows.Count == 3 && rows[1] == new OutlinerRow(OutlinerRowKind.ActorComponent, matchingComponent),
            "Expanded actors should show only components matching the active Outliner filter.");

        expandedActors.Clear();
        OutlinerRowBuilder.Build(world, MatchesFilter, expandedActors, matchingComponent, rows, matches);
        Require(expandedActors.Contains(actorRoot) && rows.Count == 3
            && rows[1] == new OutlinerRow(OutlinerRowKind.ActorComponent, matchingComponent),
            "Revealing a selected actor component should expand its group and include that row.");

        Console.WriteLine("PASS: Outliner filtered rows, actor expansion, selected-component reveal and standalone objects");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
