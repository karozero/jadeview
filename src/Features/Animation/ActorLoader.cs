using System.Numerics;

namespace JadeView;

internal static partial class ActorLoader
{
    private static readonly object FixLock = new();
    private static string? fixPath;
    private static DateTime fixStamp;
    private static byte[]? fixBytes;
    private static PcSceneLoader.Result? fixReplay;
    public static void Build(byte[] bytes, PcSceneLoader.Result replay, SceneDocument doc)
    {
        var origins = replay.Records.ToDictionary(r => r, r => bytes);
        var all = new List<PcSceneLoader.Record>();
        if (doc.Worlds.Any(w => w.Objects.Any(o => (o.Flags & 2) != 0)) && File.Exists(doc.BaseBfPath))
        {
            lock (FixLock)
            {
                var stamp = File.GetLastWriteTimeUtc(doc.BaseBfPath);
                if (fixReplay == null || fixPath != doc.BaseBfPath || fixStamp != stamp)
                {
                    var archive = new Archive(doc.BaseBfPath); var e = archive.Entries.FirstOrDefault(e => e.Id == "FF0084DF");
                    fixBytes = e == null ? null : archive.Read(e); fixReplay = fixBytes == null ? null : PcSceneLoader.Read(fixBytes, doc.BaseBfPath); fixPath = doc.BaseBfPath; fixStamp = stamp;
                }
                if (fixReplay?.Complete == true && fixBytes != null) foreach (var r in fixReplay.Records) { all.Add(r); origins[r] = fixBytes; }
            }
        }
        all.AddRange(replay.Records);
        CinematicActionLibraryReader.Load(all, origins, doc);
        CinematicSoundBankReader.Load(all, origins, doc);
        CinematicScreenControllerReader.Load(all, origins, doc);
        CinematicEnvironmentReader.Load(all, origins, doc);
        CinematicShockwaveReader.Load(all, origins, doc);
        CinematicFlashReader.Load(all, origins, doc);
        CinematicLightningReader.Load(all, origins, doc);
        var resources = all.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.Last());
        var scopedResources = new PcSceneResourceIndex(all);
        CinematicScriptReader.Load(all, origins, doc);
        Dictionary<uint, PcSceneLoader.Record>? scope = null;
        PcSceneLoader.Record? Find(uint key) => scope?.GetValueOrDefault(key) ?? resources.GetValueOrDefault(key);
        List<PcSceneLoader.Record> Group(uint key)
        {
            var g = Find(key); var list = g?.References.FirstOrDefault();
            return list == null ? [] : Find(list.Key)?.References.Select(r => Find(r.Key)).Where(r => r?.Kind == 'l').Cast<PcSceneLoader.Record>().ToList() ?? [];
        }
        var clipCache = new Dictionary<PcSceneLoader.Record, AnimationClip>();
        var sceneEventCache = new Dictionary<PcSceneLoader.Record, AnimationClip?>();
        var cinematicWorlds = replay.Worlds.Where(world => world.Name.Contains("cine", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(world => world.Key, world => world.Name);
        var cinematicOwnersByClip = replay.Records
            .Where(record => record.Kind == 'l' && cinematicWorlds.ContainsKey(record.LoadWorldKey))
            .SelectMany(record => new[] { record.SceneEvents, record.Events }
                .Where(key => key is > 2 and < uint.MaxValue)
                .Select(key => (WorldKey: record.LoadWorldKey, ClipKey: key, OwnerKey: record.Key)))
            .GroupBy(reference => (reference.WorldKey, reference.ClipKey))
            .ToDictionary(group => group.Key, group => group.Select(item => item.OwnerKey).Distinct().ToArray());
        foreach (var events in replay.Records.Where(record => record.Kind == 'i' && cinematicWorlds.ContainsKey(record.LoadWorldKey)))
        {
            try
            {
                var clip = AnimationReader.Read(origins[events], events.Offset, events.Size);
                clip.Key = events.Key;
                clip.Name = $"Cinematic · {cinematicWorlds[events.LoadWorldKey]} · {events.Key:X8}";
                clip.SceneMotion = true;
                clip.Cinematic = true;
                clip.CinematicOwnerKeys = cinematicOwnersByClip.GetValueOrDefault((events.LoadWorldKey, events.Key)) ?? [];
                clip.CinematicOwnerKey = clip.CinematicOwnerKeys.FirstOrDefault();
                clip.CinematicWorldKey = events.LoadWorldKey;
                doc.CinematicClips.TryAdd(events.Key, clip);
                sceneEventCache.TryAdd(events, clip);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException)
            {
                doc.Diagnostics.Add($"Cinematic clip {events.Key:X8} could not be decoded: {ex.Message}");
                sceneEventCache.TryAdd(events, null);
            }
        }
        foreach (var w in doc.Worlds)
        {
            scope = replay.Worlds.FirstOrDefault(world => world.Key == w.Key)?.ActorResources;
            var originals = w.Objects.ToArray(); var templates = new HashSet<SceneObject>();
            foreach (var root in originals.Where(o => (o.Flags & 2) != 0))
            {
                var r = Find(root.GaoKey); if (r == null) continue;
                var children = Group(r.Actions); if (children.Count == 0) continue;
                var actor = new SceneActor { Root = root }; actor.Members.Add(root); root.Actor = actor;
                actor.MouthController = PcMorphControllerDiscovery.Discover(r, Find, record => origins[record], root.Name, out var mouthDiagnostic);
                if (mouthDiagnostic != null) doc.Diagnostics.Add($"{root.Name}: {mouthDiagnostic}");
                root.ActorNode = Node(bytes, root);
                foreach (var child in children)
                {
                    var node = SceneLoader.ParseGao(origins[child], new(child.Offset - 4, child.Size + 4, 0, false), child.Key, 0, doc.Diagnostics);
                    if (node == null) { actor.Bones.Add(null); continue; }
                    node.Modifiers = child.Modifiers.Select(m => new SceneModifier(m.Type, origins[child].AsSpan(m.Offset, m.Size).ToArray())).ToList();
                    node.Name = child.Name; node.Id = $"{root.Id}/part/{actor.Bones.Count}/{child.Key:X8}";
                    node.Actor = actor; node.ActorNode = Node(origins[child], node);
                    node.Capabilities = new() { Flags = EditCapabilities.None, RestrictionReason = "Prefab component: pose is controlled by the skeleton. Select the actor to edit." };
                    int boneIndex = actor.Bones.Count;
                    var models = new List<SceneObject>();
                    foreach (var source in Group(child.Prefab))
                    {
                        var model = SceneLoader.ParseGao(origins[source], new(source.Offset - 4, source.Size + 4, 0, false), source.Key, 0, doc.Diagnostics);
                        if (model == null) continue; model.Modifiers = source.Modifiers.Select(m => new SceneModifier(m.Type, origins[source].AsSpan(m.Offset, m.Size).ToArray())).ToList(); model.ActorNode = Node(origins[source], model); models.Add(model);
                    }
                    if (models.Count > 0) actor.SourceMeshes[boneIndex] = models;
                    actor.Members.Add(node); actor.Bones.Add(node);
                    foreach (var old in originals.Where(o => o.GaoKey == child.Key && o != root && (o.Flags & 2) == 0)) templates.Add(old);
                    w.Objects.Add(node);
                }
                actor.DefaultShape = r.Shape;
                void Shape(uint key)
                {
                    if (key is 0 or uint.MaxValue || actor.Shapes.ContainsKey(key)) return;
                    var shape = Find(key); if (shape != null && shape.Size > 0) actor.Shapes[key] = origins[shape].AsSpan(shape.Offset, shape.Size).ToArray();
                }
                Shape(r.Shape);
                var table = Find(r.Skeleton);
                if (table != null) foreach (var request in table.References.Where(q => q.Kind == 'a'))
                {
                    var action = Find(request.Key); if (action == null) continue;
                    for (int index = 0; action.Size > 1 && index < origins[action][action.Offset]; index++)
                    {
                        int p = action.Offset + 2 + index * 19; if (p > action.Offset + action.Size - 19) break;
                        uint eventKey = Preview.U32(origins[action], p); var events = Find(eventKey); if (events == null) continue;
                        if (!clipCache.TryGetValue(events, out var clip))
                        {
                            clip = new() { Key = eventKey, Name = $"{eventKey:X8}", Read = () => AnimationReader.Read(origins[events], events.Offset, events.Size) }; clipCache[events] = clip;
                        }
                        uint shapeKey = Preview.U32(origins[action], p + 8); Shape(shapeKey);
                        if (!actor.Clips.Contains(clip)) { actor.Clips.Add(clip); actor.ClipShapes[clip] = shapeKey is 0 or uint.MaxValue ? r.Shape : shapeKey; }
                    }
                }
                if (actor.Clips.Count == 0 && Find(r.Events) is { Kind: 'i' } e)
                    actor.Clips.Add(new() { Key = e.Key, Name = $"{e.Key:X8}", Read = () => AnimationReader.Read(origins[e], e.Offset, e.Size) });
                if (actor.Clips.Count == 0)
                {
                    var memberKeys = new HashSet<uint> { root.GaoKey };
                    foreach (var events in replay.Records.Where(e => e.Kind == 'i'))
                    {
                        if (!sceneEventCache.TryGetValue(events, out var sceneClip))
                        {
                            try { sceneClip = AnimationReader.Read(origins[events], events.Offset, events.Size); }
                            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException) { sceneClip = null; }
                            sceneEventCache[events] = sceneClip;
                        }
                        if (sceneClip == null) continue;
                        var tracks = sceneClip.Tracks.Where(t => IsActorTrack(t, root, memberKeys)).ToList();
                        if (tracks.Count == 0) continue;
                        actor.Clips.Add(new() { Key = events.Key, Name = $"{events.Key:X8} (scene motion)", SceneMotion = true, Cinematic = cinematicWorlds.ContainsKey(events.LoadWorldKey), Loaded = true, Tracks = tracks, Duration = sceneClip.Duration });
                    }
                    actor.Diagnostic = actor.Clips.Count > 0
                        ? "Scene motion tracks only. Gameplay animation table is not linked in this actor record."
                        : "No animation table or matching scene motion tracks in loaded data.";
                }
                AddCinematicClips(actor, root, replay, origins, cinematicWorlds, sceneEventCache);
                actor.ApplyShape(r.Shape); actor.Evaluate(0); w.Actors.Add(actor);
            }
            w.Objects.RemoveAll(o => templates.Contains(o));
            foreach (var sceneObject in w.Objects)
            {
                var source = Find(sceneObject.GaoKey);
                if (source != null && (source.Flags & SceneLoader.IdentityFlagGroup) != 0)
                    sceneObject.GroupMemberKeys = Group(source.Prefab).Select(member => member.Key).ToArray();
            }
        }
        foreach (var world in doc.Worlds)
        foreach (var actor in world.Actors)
            foreach (var source in doc.CinematicClips.Values)
            {
                var timeline = CinematicBodyTimeline.Create(source, actor, world.Key,
                    doc.CinematicActionKitsByOwner);
                if (timeline.ShapeKeys.Any())
                    LoadCinematicActionShapes(actor, timeline, world.Key, scopedResources, origins, doc);

                var actorClip = actor.Clips.FirstOrDefault(clip => clip.Cinematic && clip.Key == source.Key);
                if (actorClip != null)
                {
                    actorClip.CinematicBody = timeline;
                }
            }
        doc.Diagnostics.Add($"Actors: {doc.Worlds.Sum(w => w.Actors.Count)}; component instances and animation pose are preview-only.");
    }

    private static void LoadCinematicActionShapes(SceneActor actor, CinematicBodyTimeline timeline,
        uint preferredWorldKey, PcSceneResourceIndex resources,
        Dictionary<PcSceneLoader.Record, byte[]> origins,
        SceneDocument document)
    {
        foreach (uint shapeKey in timeline.ShapeKeys)
        {
            if (actor.Shapes.ContainsKey(shapeKey)) continue;
            if (resources.Resolve(shapeKey, preferredWorldKey) is not { } shapeRecord
                || shapeRecord.Size <= 0 || !origins.TryGetValue(shapeRecord, out var sourceBytes))
            {
                document.Diagnostics.Add($"Cinematic action shape {shapeKey:X8} for {actor.Root.Name} was not found.");
                continue;
            }

            actor.Shapes[shapeKey] = sourceBytes.AsSpan(shapeRecord.Offset, shapeRecord.Size).ToArray();
        }
    }

}

