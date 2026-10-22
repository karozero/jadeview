using System.Text.Json;
using System.Text.Json.Serialization;
using JadeView;

internal static class CinematicMotionAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id.Equals(container, StringComparison.OrdinalIgnoreCase));
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var clip = document.CinematicClips[clipKey];
        var events = clip.Tracks.SelectMany(track => track.Keys.SelectMany(key => key.OpaquePayloads
            .SelectMany(payload => CinematicOwnerContext.ForTrack(clip, track).Select(context => new
            {
                key.Time,
                track.Bone,
                Target = context.ToString("X8"),
                Action = CinematicActionEvent.Decode(key, context, payload),
                Facial = CinematicFacialEvent.Decode(key),
                Call = CinematicLiteralCall.Decode(payload, track.Target, context),
                PayloadKind = payload.Kind,
                Bytes = Convert.ToHexString(payload.Data)
            })))).ToArray();
        var actions = document.CinematicActionKits.Select(pair => new
        {
            Owner = pair.Key.ToString("X8"),
            Actions = pair.Value.Select(action => new
            {
                Slot = action.Key,
                action.Value.LoopItem,
                Items = action.Value.Items.Select(item =>
                {
                    item.Clip.EnsureLoaded();
                    return new
                    {
                        Key = item.Clip.Key.ToString("X8"),
                        ShapeKey = item.ShapeKey.ToString("X8"),
                        item.Frequency,
                        item.Repetitions,
                        item.BlendFrames,
                        item.Flags,
                        item.DesignFlags,
                        item.Transitions,
                        item.Clip.Duration,
                        item.Clip.Unsupported,
                        PackedRotationKeys = item.Clip.Tracks.SelectMany(track => track.Keys)
                            .Count(key => key.Rotation != null && key.OpaquePayloads.Any(payload => payload.Kind == "packed-rotation-word")),
                        BoneTranslations = item.Clip.Tracks.Where(track => track.Bone >= 0)
                            .Select(track => new { track.Bone, Keys = track.Keys.Where(key => key.Position != null).ToArray() })
                            .Where(track => track.Keys.Length > 0).ToArray(),
                        Roots = item.Clip.Tracks.Where(track => track.Bone < 0).ToArray()
                    };
                }).ToArray()
            }).ToArray()
        }).ToArray();
        var actorAppearances = document.Worlds.SelectMany(world => world.Actors)
            .Where(actor => events.Any(item => item.Action?.Target == actor.Root.GaoKey))
            .Select(actor =>
            {
                var timeline = CinematicBodyTimeline.Create(clip, actor, document.CinematicActionKits);
                return new
                {
                    Actor = actor.Root.Name,
                    ActorKey = actor.Root.GaoKey.ToString("X8"),
                    DefaultShape = actor.DefaultShape.ToString("X8"),
                    BoneAppearances = actor.SourceMeshes.Select(pair => new
                    {
                        Bone = pair.Key,
                        BoneName = pair.Key < actor.Bones.Count ? actor.Bones[pair.Key]?.Name : null,
                        Models = pair.Value.Select((model, index) => new
                        {
                            Index = index,
                            model.Name,
                            Geometry = model.GeometryKey.ToString("X8"),
                            Material = model.MaterialKey.ToString("X8")
                        }).ToArray()
                    }).ToArray(),
                    ResolvedShapes = actor.Shapes.Select(shape => new
                    {
                        Key = shape.Key.ToString("X8"),
                        Entries = shape.Value.Length > 0
                            ? Enumerable.Range(0, shape.Value[0]).Where(index => 1 + index * 3 + 2 < shape.Value.Length)
                                .Select(index => new
                                {
                                    Bone = unchecked((sbyte)shape.Value[1 + index * 3]),
                                    Source = unchecked((sbyte)shape.Value[1 + index * 3 + 2])
                                }).ToArray()
                            : []
                    }).ToArray(),
                    TimelineShapes = new[] { 0f, 1.5f, 6.1f, 8.1f, 8.5f, 9f }
                        .Select(time => new
                        {
                            Time = time,
                            Shape = timeline.ShapeAt(time)?.ToString("X8"),
                            Loaded = timeline.ShapeAt(time) is uint shape && actor.Shapes.ContainsKey(shape)
                        }).ToArray()
                };
            }).ToArray();
        var eventTargets = events.Select(item =>
                item.Action?.Target ?? item.Call?.Target)
            .Where(key => key.HasValue).Select(key => key!.Value).ToHashSet();
        var sceneObjects = document.Worlds.SelectMany(world => world.Objects.Select(sceneObject => new
        {
            World = world.Name,
            Object = sceneObject.Name,
            Key = sceneObject.GaoKey,
            sceneObject.ControlFlags,
            sceneObject.MaterialKey,
            sceneObject.GeometryKey,
            TrackedByClip = clip.Tracks.Any(track => track.Target == sceneObject.GaoKey),
            ReceivesEvent = eventTargets.Contains(sceneObject.GaoKey)
        }))
            .Where(sceneObject => sceneObject.TrackedByClip || sceneObject.ReceivesEvent
                || sceneObject.Object.Contains("mili", StringComparison.OrdinalIgnoreCase)
                || sceneObject.Object.Contains("arme", StringComparison.OrdinalIgnoreCase)
                || sceneObject.Object.Contains("weapon", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        File.WriteAllText(Path.Combine(output, $"{container}-{clipKey:X8}-motion.json"), JsonSerializer.Serialize(new
        {
            clip.Duration,
            Owner = clip.CinematicOwnerKey.ToString("X8"),
            Events = events,
            Kits = actions,
            ActorAppearances = actorAppearances,
            SceneObjects = sceneObjects,
            Roots = clip.Tracks.Where(track => track.Bone < 0 && track.Keys.Any(key => key.Position != null || key.Rotation != null)).ToArray()
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        }));
        Console.WriteLine($"MOTION AUDIT: {container}/{clipKey:X8}");
    }
}
