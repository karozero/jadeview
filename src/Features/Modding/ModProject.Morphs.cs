namespace JadeView;

public sealed partial class ModProject
{
    public void CaptureMorphPreviews(SceneDocument document)
    {
        foreach (var actor in document.Worlds.SelectMany(world => world.Actors))
        foreach (var member in actor.Members)
            if (member.MorphPreview is { Data: not null, HasUnmatchedProjectState: false } preview &&
                preview.Geometry.Key == member.RenderGeometryKey && member.Modifiers.Contains(preview.Modifier))
                CaptureMorphPreview(document, actor, member, preview);
    }

    public void CaptureMorphPreview(SceneDocument document, SceneActor actor, SceneObject member, FaceMorphPreview preview)
    {
        int worldIndex = document.Worlds.FindIndex(world => world.Actors.Contains(actor));
        if (worldIndex < 0 || preview.Data == null || preview.Data.Groups.Length == 0) return;
        var world = document.Worlds[worldIndex];
        var weights = preview.CopyWeights();
        bool empty = preview.Timeline.Keys.Count == 0 && weights.All(weight => weight == 0);
        var record = new ModMorphPreview
        {
            Container = document.ContainerName, WorldKey = world.Key, WorldIndex = worldIndex,
            ActorId = actor.Root.Id, ObjectId = member.Id, GeometryKey = preview.Geometry.Key,
            GeometrySha256 = MorphProjectSource.GeometryHash(preview.Geometry),
            ModifierSha256 = MorphProjectSource.ModifierHash(preview), TargetCount = weights.Length,
            Weights = weights, Keys = preview.Timeline.CopyKeys(), Time = preview.Timeline.Time, Loop = preview.Timeline.Loop
        };
        record.Validate();
        MorphPreviews.RemoveAll(candidate => MatchesOwner(candidate, document.ContainerName, world.Key, worldIndex, actor, member) &&
            candidate.GeometryKey == preview.Geometry.Key);
        if (!empty) MorphPreviews.Add(record);
        preview.HasUnmatchedProjectState = false;
        member.RestoreMorphPreview = incoming => RestoreMorphPreview(document, world.Key, worldIndex, actor, member, incoming, []);
    }

    public List<string> BindMorphPreviews(SceneDocument document, ResourceResolver resolver)
    {
        ValidateMorphPreviews();
        bool hasArchiveHash = !string.IsNullOrEmpty(BaseBfSha256) || !string.IsNullOrEmpty(document.BaseBfSha256);
        if (hasArchiveHash && !VerifyBaseBf(document, out var error)) throw new InvalidDataException(error);
        var warnings = new List<string>();
        for (int worldIndex = 0; worldIndex < document.Worlds.Count; worldIndex++)
        {
            var world = document.Worlds[worldIndex];
            int index = worldIndex;
            foreach (var actor in world.Actors)
            foreach (var member in actor.Members)
            {
                member.MorphPreview = null;
                member.RestoreMorphPreview = preview => RestoreMorphPreview(document, world.Key, index, actor, member, preview, warnings);
                if (resolver.Geometries.TryGetValue(member.RenderGeometryKey, out var geometry))
                    FaceMorphPreview.Get(member, geometry);
            }
        }
        foreach (var record in MorphPreviews.Where(record => record.Container == document.ContainerName))
        {
            bool ownerFound = record.WorldIndex < document.Worlds.Count &&
                document.Worlds[record.WorldIndex].Key == record.WorldKey &&
                document.Worlds[record.WorldIndex].Actors.Any(actor => actor.Root.Id == record.ActorId && actor.Members.Any(member => member.Id == record.ObjectId));
            if (ownerFound) continue;
            string warning = $"Saved morph owner '{record.ObjectId}' was not found in the expected world/actor; record retained without applying.";
            warnings.Add(warning); document.Diagnostics.Add(warning);
        }
        return warnings;
    }

    private void RestoreMorphPreview(SceneDocument document, uint worldKey, int worldIndex, SceneActor actor,
        SceneObject member, FaceMorphPreview preview, List<string> warnings)
    {
        var record = MorphPreviews.SingleOrDefault(record => MatchesOwner(record, document.ContainerName, worldKey, worldIndex, actor, member) &&
            record.GeometryKey == preview.Geometry.Key);
        if (record == null) return;
        bool uniqueOwner = document.Worlds[worldIndex].Actors.Where(candidate => candidate.Root.Id == record.ActorId)
            .SelectMany(candidate => candidate.Members).Count(candidate => candidate.Id == record.ObjectId) == 1;
        bool sourceMatches = uniqueOwner && preview.Data?.Groups.Length == record.TargetCount &&
            record.GeometrySha256.Equals(MorphProjectSource.GeometryHash(preview.Geometry), StringComparison.OrdinalIgnoreCase) &&
            record.ModifierSha256.Equals(MorphProjectSource.ModifierHash(preview), StringComparison.OrdinalIgnoreCase);
        if (!sourceMatches)
        {
            preview.HasUnmatchedProjectState = true;
            string warning = $"Morph preview for '{member.Name}' was not applied: saved geometry/modifier does not match the loaded model.";
            warnings.Add(warning);
            document.Diagnostics.Add(warning);
            return;
        }
        foreach (var key in record.Keys) preview.Timeline.Record(key.Time, key.Weights);
        preview.Timeline.Loop = record.Loop;
        preview.Seek(record.Time);
        for (int group = 0; group < record.Weights.Length; group++) preview.SetWeight(group, record.Weights[group]);
        actor.Evaluate(0);
    }

    internal void ValidateMorphPreviews()
    {
        if (MorphPreviews == null) throw new InvalidDataException("Invalid saved morph preview collection.");
        var identities = new HashSet<(string, uint, int, string, string, uint)>();
        foreach (var record in MorphPreviews)
        {
            if (record == null) throw new InvalidDataException("Null saved morph preview.");
            record.Validate();
            if (!identities.Add((record.Container, record.WorldKey, record.WorldIndex, record.ActorId, record.ObjectId, record.GeometryKey)))
                throw new InvalidDataException("Duplicate saved morph preview identity.");
        }
    }

    private static bool MatchesOwner(ModMorphPreview record, string container, uint worldKey, int worldIndex,
        SceneActor actor, SceneObject member) => record.Container == container && record.WorldKey == worldKey &&
            record.WorldIndex == worldIndex && record.ActorId == actor.Root.Id && record.ObjectId == member.Id;
}
