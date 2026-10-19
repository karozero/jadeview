using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Forms = System.Windows.Forms;

namespace JadeView;
public sealed partial class EditorShell
{
    public void PlayGameWithCurrentMod()
    {
        if (Launcher.IsGameRunning(out int? pid))
        {
            SetError($"BGE.exe is already running (PID {pid}).");
            return;
        }

        bool hasModChanges = CurrentProject?.HasChanges == true;
        bool canPrepareMod = hasModChanges && Archive != null && Document != null && CurrentProject != null;
        if (canPrepareMod && !PrepareCurrentMod())
            return;
        var launchRes = Launcher.LaunchGame();
        if (launchRes.IsSuccess)
        {
            _statusMessage = hasModChanges ? $"Mod installed and game started (PID {launchRes.ProcessId})" : $"Game started (PID {launchRes.ProcessId})";
        }
        else
        {
            SetError(launchRes.Error ?? "Failed to start game.");
        }
    }

    public void BuildModdedBf()
    {
        if (Archive == null || Document == null || CurrentProject == null)
        {
            SetError("No archive or project loaded.");
            return;
        }

        using var dialog = new Forms.SaveFileDialog
        {
            Title = "Build modified BigFile (BF)",
            Filter = "Beyond Good & Evil archive (*.bf)|*.bf",
            FileName = "sally_modded.bf"
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;
        string destBf = dialog.FileName;
        bool overwritesSource = Archive.SamePath(destBf, Archive.PathName);
        bool overwritesGame = Archive.SamePath(destBf, GameInstallation.DefaultGameDirectory + "\\" + GameInstallation.CleanBfFileName);
        if (overwritesSource || overwritesGame)
        {
            SetError("Choose a different filename to preserve the original game archive.");
            return;
        }

        var sourceArchive = Archive;
        string sourceContainer = Document.ContainerName;
        try
        {
            PrepareCurrentPickupPersistence();
        }
        catch (Exception error)
        {
            SetError($"Cannot assign pickup persistence: {error.Message}");
            return;
        }
        var projectSnapshot = CurrentProject.CreateSnapshot();
        var sourceResolver = Resolver;
        StartJob("Building and verifying modified BF archive...", token =>
        {
            var sceneEntry = sourceArchive.Entries.FirstOrDefault(candidateEntry => candidateEntry.Name.Equals(sourceContainer, StringComparison.OrdinalIgnoreCase) || candidateEntry.Id.Equals(sourceContainer, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException($"Container {sourceContainer} not found in the base archive.");
            byte[] originalBytes = sourceArchive.Read(sceneEntry);
            var buildDocument = SceneLoader.Load(originalBytes, sourceContainer, sourceArchive.PathName);
            var plan = projectSnapshot.BuildWritePlan(buildDocument, originalBytes, sourceResolver);
            // Создаем точечно пропатченный буфер через SceneWriter
            byte[] patchedBytes = SceneWriter.CreatePatchedContainer(originalBytes, plan);
            // Сборка BF через SceneWriter.BuildAndSaveArchive
            SceneWriter.BuildAndSaveArchive(sourceArchive, destBf, sceneEntry.Index, patchedBytes, progress =>
            {
                token.ThrowIfCancellationRequested();
                _jobProgress = progress;
            });
            return () =>
            {
                _statusMessage = $"Modified archive built and verified: {destBf}";
            };
        });
    }

    private void PrepareCurrentPickupPersistence(bool verifyExecutable = true)
    {
        if (Archive == null || Document == null || CurrentProject == null)
            return;
        var entry = Archive.Entries.First(candidate => MatchesContainer(candidate, Document.ContainerName));
        PcSceneWriter.PreparePickupPersistence(Archive.Read(entry), Document, CurrentProject, verifyExecutable);
    }

    private bool PrepareCurrentMod()
    {
        try
        {
            var entry = Archive!.Entries.FirstOrDefault(candidate => MatchesContainer(candidate, Document!.ContainerName)) ?? Archive.Entries.FirstOrDefault(candidate => candidate.Id.Equals("FF007685", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                SetError($"Container {Document!.ContainerName} not found in the base archive.");
                return false;
            }

            byte[] source = Archive.Read(entry);
            var plan = CurrentProject!.BuildWritePlan(Document!, source, Resolver);
            bool hasExportChanges = plan.Patches.Count > 0 || plan.RebuiltContainer != null;
            if (!hasExportChanges)
                return true;
            byte[] patched = SceneWriter.CreatePatchedContainer(source, plan);
            var installed = Launcher.InstallMod(Archive, plan, patched, CurrentProject.Name);
            if (installed.IsSuccess)
                return true;
            SetError($"Mod installation failed: {installed.Error}");
        }
        catch (Exception error)
        {
            SetError($"Failed to prepare mod: {error.Message}");
        }

        return false;
    }
}
