using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private FaceMorphData? ReadMorph(SceneObject sceneObject, Geometry geometry)
    {
        var modifiers = sceneObject.Modifiers.Where(modifier => modifier.Type == 4).ToArray();
        if (modifiers.Length == 0) return null;
        if (modifiers.Length != 1)
        {
            warnings.Add($"'{sceneObject.Name}' has multiple type-4 modifiers; morph export is omitted.");
            return null;
        }
        try
        {
            var morph = FaceMorphReader.Read(modifiers[0].Data, geometry.Positions.Length);
            if (morph.Groups.Length == 0) return null;
            foreach (var issue in FaceMorphDiagnostics.SourceIssues(geometry, morph))
                if (sceneObject.MorphPreview?.GetWeight(issue.Group) is float weight && weight != 0)
                    warnings.Add($"'{sceneObject.Name}' PC_Morph_{issue.Group:D2}: {issue.Message}");
            warnings.Add($"'{sceneObject.Name}' exports PC morph targets with user-authored preview weights and source channel metadata; original facial scheduling is not reconstructed.");
            return morph;
        }
        catch (InvalidDataException error)
        {
            warnings.Add($"'{sceneObject.Name}' morph export omitted: {error.Message}");
            return null;
        }
    }

    private List<Obj> MorphTargets(FaceMorphData morph, List<int> sourceVertices, Matrix4x4 meshSpace)
    {
        var targets = new List<Obj>();
        foreach (var group in morph.Groups)
        {
            var sparse = group.AccumulatedDeltas();
            var deltas = new List<float>(sourceVertices.Count * 3);
            foreach (int vertex in sourceVertices)
            {
                var delta = Vector3.TransformNormal(sparse.GetValueOrDefault(vertex), meshSpace);
                Append(deltas, Geometry.ToRender(delta));
            }
            // Keep empty groups and duplicate UV/atlas corners: source group index equals target index.
            targets.Add(D("POSITION", Floats(deltas.ToArray(), 3, "VEC3", true, GlbFormat.VertexBuffer)));
        }
        return targets;
    }

    private static void WriteMorphMetadata(Obj mesh, FaceMorphData morph, SceneObject sceneObject)
    {
        mesh["weights"] = sceneObject.MorphPreview?.CopyWeights() ?? new float[morph.Groups.Length];
        var metadata = D("headerWords", morph.HeaderWords, "groupIndexEqualsTargetIndex", true,
                "timeline", "user-authored keys only; original facial scheduling is not reconstructed",
                "sourceIssues", sceneObject.MorphPreview?.SourceIssues.Select(issue => D("group", issue.Group, "message", issue.Message)).ToArray() ?? [],
                "channels", morph.Channels.Select(channel =>
                    D("weightBits", channel.WeightBits, "progressionBits", channel.ProgressionBits,
                        "groupReferences", channel.GroupReferences)).ToArray());
        if (sceneObject.MorphPreview?.CopyChannelState() is { } states)
            metadata["previewChannels"] = states.Select((state, index) =>
                D("channel", index, "weight", state.Weight, "progression", state.Progression)).ToArray();
        mesh["extras"] = D("targetNames", morph.Groups.Select(group => $"PC_Morph_{group.Index:D2}").ToArray(),
            "pcMorph", metadata);
    }
}
