using JadeView;

internal static class ActorModuleChecks
{
    internal static void Verify()
    {
        var actor = new SceneActor();
        var first = new SceneObject();
        var head = new SceneObject();
        actor.Bones.AddRange([first, null, head]);
        Require(actor.FindModule(0) == first, "Absent map must use component ordinal");
        actor.Shapes[1] = [3, 0, 9, 0, 1, 0, 0, 2, 0, 1];
        actor.ApplyShape(1);
        Require(actor.FindModule(0) == head, "Mapped selector must skip a null component without falling back to ordinal");
        actor.Shapes[2] = [1, 2, 255, 0];
        actor.ApplyShape(2);
        Require(actor.FindModule(0) == head, "FF selector lost the previous shape's module ID");
        actor.Shapes[3] = [1, 2, 4, 0];
        actor.ApplyShape(3);
        Require(actor.FindModule(0) == null && actor.FindModule(4) == head, "Explicit selector override failed");
        actor.Shapes[4] = [2, 0];
        actor.ApplyShape(4);
        Require(actor.FindModule(0) == null, "Malformed map incorrectly fell back to component ordinal");
        actor.ApplyShape(1);
        Require(actor.FindModule(0) == head, "Valid shape did not recover module lookup");

        var duplicate = ActorModuleMap.Read([2, 2, 0, 0, 2, 4, 0]);
        Require(duplicate.FindComponent(0, actor.Bones) == -1 && duplicate.FindComponent(4, actor.Bones) == 2,
            "Repeated component rows must retain the last serialized selector");
        Console.WriteLine("PASS: PC module lookup, source order, null components, shape inheritance and malformed-map rejection");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
