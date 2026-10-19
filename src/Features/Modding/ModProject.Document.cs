using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;
public sealed partial class ModProject
{
    /// <summary>
    /// Приняет изменения из проекта мода к переданному SceneDocument.
    /// </summary>
    public ModApplyResult ApplyToDocument(SceneDocument document, bool ignoreHashMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new ModApplyResult();
        bool hasFingerprint = !string.IsNullOrWhiteSpace(BaseBfSha256) || !string.IsNullOrWhiteSpace(document.BaseBfSha256);
        if (!ignoreHashMismatch && hasFingerprint && !VerifyBaseBf(document, out var hashError))
        {
            result.Errors.Add(hashError!);
            return result;
        }

        string currentContainer = document.ContainerName;
        ApplySceneStructureChanges(document, result);
        var relevantChanges = string.IsNullOrEmpty(currentContainer) ? Changes : Changes.Where(candidateChange => string.Equals(candidateChange.ContainerName, currentContainer, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var change in relevantChanges)
        {
            var target = FindChangeTarget(document, change);
            if (target == null)
            {
                result.ObjectsNotFound++;
                result.MissingObjectIds.Add(change.ObjectId);
                result.Warnings.Add($"Object '{change.ObjectName}' (ID: {change.ObjectId}, GAO: 0x{change.GaoKey:X8}) not found in scene.");
                continue;
            }

            // Применяем трансформацию
            change.NewTransform.ApplyTo(target.Transform);
            result.ObjectsUpdated++;
            result.UpdatedObjectNames.Add(target.Name);
        }

        return result;
    }

    private static SceneObject? FindChangeTarget(SceneDocument document, ModObjectChange change)
    {
        var worlds = change.WorldKey == 0 ? document.Worlds : document.Worlds.Where(world => world.Key == change.WorldKey);
        var candidates = worlds.SelectMany(world => world.Objects).ToArray();
        var target = candidates.FirstOrDefault(sceneObject => sceneObject.Id == change.ObjectId);
        bool canMatchKey = target == null && change.GaoKey != 0;
        return canMatchKey ? candidates.FirstOrDefault(sceneObject => sceneObject.GaoKey == change.GaoKey) : target;
    }
}
