using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;
public sealed partial class ModProject
{
    /// <summary>
    /// Создает WritePlan для SceneWriter на основе зарегистрированных изменений для указанного контейнера.
    /// </summary>
    public WritePlan BuildWritePlan(SceneDocument document, byte[] containerBytes, ResourceResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(containerBytes);
        string containerName = document.ContainerName;
        bool hasStructureChanges = SceneStructureChanges.Any(change =>
            string.Equals(change.ContainerName, containerName, StringComparison.OrdinalIgnoreCase));
        if (hasStructureChanges)
            return new WritePlan
            {
                ContainerName = containerName,
                ExpectedContainerSize = containerBytes.Length,
                ExpectedContainerSha256 = Convert.ToHexStringLower(SHA256.HashData(containerBytes)),
                RebuiltContainer = PcSceneWriter.Rebuild(containerBytes, document, this, resolver)
            };
        var plan = new WritePlan
        {
            ContainerName = containerName,
            ExpectedContainerSize = containerBytes.Length,
            ExpectedContainerSha256 = ""
        };
        var containerChanges = GetChangesForContainer(containerName);
        foreach (var change in containerChanges)
        {
            if (!change.HasChanges)
                continue;
            var target = FindChangeTarget(document, change);
            if (target == null)
            {
                plan.Diagnostics.Add($"Warning: object '{change.ObjectName}' (0x{change.GaoKey:X8}) skipped: not found in SceneDocument.");
                continue;
            }

            bool hasValidMatrix = target.Transform.MatrixByteRange.IsValid &&
                target.Transform.MatrixByteRange.Size == JadeMatrix.SizeInBytes;
            if (!hasValidMatrix)
            {
                plan.Diagnostics.Add($"Warning: object '{change.ObjectName}' skipped: invalid MatrixByteRange.");
                continue;
            }

            if (change.IsTranslationOnly)
            {
                plan.AddTranslationPatch(target, change.NewTransform.ToTranslation(), containerBytes);
            }
            else
            {
                plan.AddTransformPatch(target, change.NewTransform.ToTranslation(),
                    change.NewTransform.ToRotation(), change.NewTransform.ToScale(), containerBytes);
            }
        }

        return plan;
    }
}
