using System.Diagnostics;
using System.Security.Cryptography;

namespace JadeView;
public sealed partial class GameLauncher
{
    /// <summary>
    /// Собирает новый BF со staging-патчами и устанавливает его в тестовую игру.
    /// Исходный архив baseArchive открывается исключительно на чтение.
    /// Перед установкой автоматически гарантируется создание резервной копии.
    /// </summary>
    public ModInstallResult InstallMod(Archive baseArchive, WritePlan writePlan, byte[] patchedContainerBytes, string modName = "Test mod", Action<float>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(baseArchive);
        ArgumentNullException.ThrowIfNull(writePlan);
        ArgumentNullException.ThrowIfNull(patchedContainerBytes);
        if (IsGameRunning(out int? pid))
        {
            return ModInstallResult.Failed($"Cannot install mod: BGE.exe is running (PID {pid}). Close the game.");
        }

        if (!Installation.IsValid)
        {
            return ModInstallResult.Failed($"Invalid game installation: {string.Join("; ", Installation.ValidationErrors)}");
        }

        // 1. Гарантируем создание бэкапа оригинала перед любой модификацией
        var backupRes = EnsureBackupExists(fraction => progress?.Invoke(fraction * 0.3f));
        if (!backupRes.IsSuccess)
        {
            return ModInstallResult.Failed($"Failed to back up original: {backupRes.Error}");
        }

        // Verify the plan against its source, then verify the exact output to install.
        var sourceEntry = baseArchive.Entries.FirstOrDefault(candidateEntry =>
            string.Equals(candidateEntry.Name, writePlan.ContainerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidateEntry.Id, writePlan.ContainerName, StringComparison.OrdinalIgnoreCase));
        if (sourceEntry == null)
            return ModInstallResult.Failed($"Container '{writePlan.ContainerName}' not found in base archive.");
        byte[] sourceBytes = baseArchive.Read(sourceEntry);
        var preflight = writePlan.PreflightCheck(sourceBytes);
        if (!preflight.IsSuccess)
        {
            return ModInstallResult.Failed($"Write plan preflight rejected: {string.Join("; ", preflight.Errors)}", preflight);
        }

        byte[] expectedOutput = SceneWriter.CreatePatchedContainer(sourceBytes, writePlan);
        bool matchesVerifiedOutput = expectedOutput.AsSpan().SequenceEqual(patchedContainerBytes);
        if (!matchesVerifiedOutput)
            return ModInstallResult.Failed("Container bytes do not match the verified write plan.", preflight);
        // 3. Находим запись контейнера в базовом архиве
        var containerEntry = baseArchive.Entries.FirstOrDefault(candidateEntry =>
            string.Equals(candidateEntry.Name, writePlan.ContainerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidateEntry.Id, writePlan.ContainerName, StringComparison.OrdinalIgnoreCase));
        if (containerEntry == null)
        {
            return ModInstallResult.Failed($"Container '{writePlan.ContainerName}' not found in base archive {baseArchive.PathName}.", preflight);
        }

        // 4. Сборка нового BF во временный staging-файл
        string stagingBf = Path.Combine(Path.GetTempPath(), $"bge_staging_{Guid.NewGuid():N}.bf");
        try
        {
            BuildStagingArchive(baseArchive, stagingBf, containerEntry.Index, patchedContainerBytes, progress);

            // 6. Атомарная установка в игру
            if (IsGameRunning(out pid))
            {
                return ModInstallResult.Failed($"Game was started during the build (PID {pid}). Installation cancelled.");
            }

            File.Move(stagingBf, Installation.BfPath, overwrite: true);
            _isModInstalled = true;
            _installedModName = modName;
            progress?.Invoke(1.0f);
            OnOperationProgress?.Invoke(1.0f);
            return ModInstallResult.Success(Installation.BfPath, stagingBf, writePlan.Patches.Count, preflight.BytesToModify, preflight);
        }
        catch (Exception ex)
        {
            return ModInstallResult.Failed($"Failed to build and install mod: {ex.Message}", preflight);
        }
        finally
        {
            DeleteStagingArchive(stagingBf);
        }
    }

    private void BuildStagingArchive(Archive baseArchive, string stagingPath, int containerIndex,
        byte[] containerBytes, Action<float>? progress)
    {
        SceneWriter.BuildAndSaveArchive(baseArchive, stagingPath, containerIndex, containerBytes, fraction =>
        {
            float totalProgress = 0.3f + fraction * 0.6f;
            progress?.Invoke(totalProgress);
            OnOperationProgress?.Invoke(totalProgress);
        });

        var verifiedArchive = new Archive(stagingPath);
        if (verifiedArchive.Entries.Count != baseArchive.Entries.Count)
            throw new InvalidDataException("Built archive entry count differs from the base archive.");
    }

    private static void DeleteStagingArchive(string stagingPath)
    {
        if (!File.Exists(stagingPath))
            return;
        try
        {
            File.Delete(stagingPath);
        }
        catch
        {
            // Cleanup must not replace the installation result with another error.
        }
    }
}
