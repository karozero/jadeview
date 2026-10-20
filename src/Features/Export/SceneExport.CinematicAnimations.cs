using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private readonly Dictionary<uint, Obj> cinematicAnimations = [];
    private readonly Dictionary<(uint Clip, uint Receiver), CinematicCameraBinding> cinematicCameraBindings = [];
    private readonly HashSet<SceneObject> sourceCameraObjects = [];

    private void ResolveCinematicCameras(WorldData world)
    {
        foreach (var clip in SelectedCinematicClips(world, options))
            foreach (var binding in CinematicCameraBinding.Discover(clip, world.Objects, []))
            {
                if (!binding.FromSourceEvent) continue;
                cinematicCameraBindings.TryAdd((clip.Key, binding.ReceiverKey), binding);
                sourceCameraObjects.UnionWith(binding.Routing?.PositionSources ?? new[] { binding.Source });
            }
    }

    private static AnimationClip[] SelectedCinematicClips(WorldData world, SceneExportOptions options)
    {
        if (!options.Animations || world.CinematicClips.Count == 0) return [];
        if (options.CinematicClipKey is { } key)
            return world.CinematicClips.TryGetValue(key, out var requested) ? [requested] : [];
        if (options.AllClips) return world.CinematicClips.Values.ToArray();
        var selectedClip = world.Actors.Select(actor => actor.Clip)
            .FirstOrDefault(clip => clip?.Cinematic == true);
        return selectedClip != null && world.CinematicClips.TryGetValue(selectedClip.Key, out var sourceClip)
            ? [sourceClip] : [];
    }

    private void AddExportAnimation(Obj animation, AnimationClip? sourceClip = null)
    {
        if (sourceClip?.Cinematic != true)
        {
            animations.Add(animation);
            return;
        }

        if (!cinematicAnimations.TryGetValue(sourceClip.Key, out var combined))
        {
            combined = D("name", sourceClip.Name, "samplers", new List<Obj>(),
                "channels", new List<Obj>());
            cinematicAnimations.Add(sourceClip.Key, combined);
            animations.Add(combined);
        }

        var combinedSamplers = (List<Obj>)combined["samplers"];
        if (string.IsNullOrWhiteSpace((string)combined["name"]) && !string.IsNullOrWhiteSpace(sourceClip.Name))
            combined["name"] = sourceClip.Name;
        var combinedChannels = (List<Obj>)combined["channels"];
        int samplerOffset = combinedSamplers.Count;
        combinedSamplers.AddRange((IEnumerable<Obj>)animation["samplers"]);
        foreach (var channel in (IEnumerable<Obj>)animation["channels"])
        {
            var adjustedChannel = new Obj(channel);
            adjustedChannel["sampler"] = (int)channel["sampler"] + samplerOffset;
            combinedChannels.Add(adjustedChannel);
        }
    }

    private void AddCinematicSourceMetadata(WorldData world)
    {
        foreach (var (clipKey, animation) in cinematicAnimations)
        {
            if (world.CinematicClips.TryGetValue(clipKey, out var sourceClip))
                AddSourceAnimationRecords(animation, sourceClip);
            if (!animation.TryGetValue("extras", out var metadata))
                animation["extras"] = metadata = new Obj();
            ((Obj)metadata)["cinematicClipKey"] = clipKey.ToString("X8");
            string[] facialSources = morphTimelines
                .Where(timeline => timeline.SourceClip?.Key == clipKey)
                .Select(timeline => timeline.Provenance).Distinct().ToArray();
            if (facialSources.Length > 0) ((Obj)metadata)["facialProvenance"] = facialSources;
        }
    }
}
