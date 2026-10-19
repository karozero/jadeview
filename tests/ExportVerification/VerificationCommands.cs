internal static class VerificationCommands
{
    public static bool TryRun(string[] arguments, string output)
    {
        switch (arguments)
        {
            case ["--actor-module-audit", var archive, var container, var actorKey, ..]:
                ActorScriptAudit.WriteModules(archive, container, Convert.ToUInt32(actorKey, 16), output);
                break;
            case ["--container-event-audit", var archive, var container, ..]:
                ActorScriptAudit.WriteContainerEvents(archive, container, output);
                break;
            case ["--scene-event-audit", var archive, var container, ..]:
                SceneAnimationEventAudit.Write(archive, container, output);
                break;
            case ["--scene-script-audit", var archive, var container, ..]:
                ActorScriptAudit.WriteScene(archive, container, output);
                break;
            case ["--object-audit", var archive, var objectName, ..]:
                ObjectMaterialAudit.Run(archive, objectName, output, OptionalArgument(arguments, 3));
                break;
            case ["--actor-audit", var archive, var actorName, ..]:
                ActorAssetAudit.Run(archive, actorName, output);
                break;
            case ["--morph-discovery", var archive, ..]:
                MorphDiscoveryAudit.Run(archive, output, OptionalArgument(arguments, 2));
                break;
            default:
                return false;
        }
        return true;
    }

    private static string? OptionalArgument(string[] arguments, int index) =>
        arguments.Length > index ? arguments[index] : null;
}
