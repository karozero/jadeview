using System.Text.Json;
using JadeView;

internal static class CinematicSceneStateAudit
{
    internal static void Write(string archivePath, string container, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var entries = archive.Entries.GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.First());
        var banks = document.CinematicSoundBanks.Select(pair => new
        {
            Key = pair.Key.ToString("X8"),
            Slots = pair.Value.Select((key, index) => new
            {
                Index = index,
                Key = key.ToString("X8"),
                Asset = entries.GetValueOrDefault(key)?.ExportName
            }).ToArray()
        }).ToArray();
        var objects = document.Worlds.SelectMany(world => world.Objects.Select(sceneObject => new
        {
            World = world.Name,
            Object = sceneObject.Name,
            Key = sceneObject.GaoKey.ToString("X8"),
            sceneObject.ControlFlags,
            Modifiers = sceneObject.Modifiers.Where(modifier => modifier.Type == 8).Select(modifier => new
            {
                Data = Convert.ToHexString(modifier.Data),
                Resolved = CinematicSoundModifier.Read(modifier, document)
            }).ToArray()
        })).Where(item => item.Modifiers.Length > 0).ToArray();
        File.WriteAllText(Path.Combine(output, container + "-cinematic-state.json"),
            JsonSerializer.Serialize(new
            {
                Banks = banks,
                Objects = objects,
                CameraCommands = document.CinematicClips.Values.Select(clip => new
                {
                    Key = clip.Key.ToString("X8"),
                    Commands = CinematicCameraEvent.Read(clip).Where(item => item.HasLiteralPair).Select(item => new
                    {
                        item.Time,
                        Source = item.PositionKey.ToString("X8"),
                        item.Transition,
                        item.FocalAngle
                    }).ToArray()
                }).ToArray(),
                FadeReceivers = document.CinematicFadeReceivers.Select(key => key.ToString("X8")).ToArray(),
                EnvironmentControllers = document.CinematicEnvironmentControllers.Values.ToArray(),
                ShockwaveControllers = document.CinematicShockwaveControllers.Values.ToArray(),
                Diagnostics = document.Diagnostics,
                FadeTimelines = document.CinematicClips.Values.Select(clip => new
                {
                    Key = clip.Key.ToString("X8"),
                    Count = document.Worlds.FirstOrDefault(world => world.Key == clip.CinematicWorldKey) is { } world
                        ? new CinematicScreenFadeTimeline(clip, CinematicWorldAssembler.Assemble(document, world), document).Count : 0
                }).ToArray(),
                ShutterControllers = document.CinematicShutterControllers.Select(pair => new
                { Key = pair.Key.ToString("X8"), Profile = pair.Value }).ToArray(),
                ShutterTimelines = document.CinematicClips.Values.Select(clip => new
                {
                    Key = clip.Key.ToString("X8"),
                    Triggers = document.Worlds.FirstOrDefault(world => world.Key == clip.CinematicWorldKey) is { } world
                        ? new CinematicShutterTimeline(clip, CinematicWorldAssembler.Assemble(document, world), document).TriggerTimes : []
                }).ToArray(),
                SceneObjects = document.Worlds.SelectMany(world => world.Objects.Select(item => new
                {
                    World = world.Name,
                    item.Name,
                    Key = item.GaoKey.ToString("X8"),
                    item.Flags,
                    item.DrawFlags,
                    item.ControlFlags,
                    GroupMembers = item.GroupMemberKeys.Select(key => key.ToString("X8")).ToArray(),
                    ModifierTypes = item.Modifiers.Select(modifier => modifier.Type).ToArray()
                })).ToArray()
            },
                new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"STATE AUDIT: {container}: {banks.Length} banks, {objects.Length} sound modifier owners");
    }
}
