using System.Numerics;
using System.Text;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Color = Raylib_cs.Color;
using Image = Raylib_cs.Image;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    #region Вспомогательные методы каталога и фоновых задач

    private void BuildCatalogFolders()
    {
        _rootFolder = new("All folders", "");
        if (Archive == null) return;

        foreach (var e in Archive.Entries)
        {
            FolderNode current = _rootFolder;
            current.Count++;
            string path = "";
            foreach (string part in e.Folder.Split('/'))
            {
                path = path.Length == 0 ? part : path + "/" + part;
                if (!current.Children.TryGetValue(part, out var child))
                    current.Children[part] = child = new(part, path);
                child.Count++;
                current = child;
            }
        }
    }

    private void FilterCatalog()
    {
        if (Archive == null) { _visibleCatalogEntries = []; return; }
        _visibleCatalogEntries = Archive.Entries.Where(e =>
            (_catalogFolder.Length == 0 || e.Folder == _catalogFolder || e.Folder.StartsWith(_catalogFolder + "/", StringComparison.Ordinal)) &&
            (_catalogSearch.Length == 0 || (e.Name + " " + e.Id + " " + e.Folder).Contains(_catalogSearch, StringComparison.OrdinalIgnoreCase)) &&
            (_catalogFilter switch
            {
                1 => Path.GetExtension(e.Name).ToLowerInvariant() is ".wad" or ".waa" or ".wam" or ".wac",
                2 => e.Name.StartsWith("ff8", StringComparison.OrdinalIgnoreCase),
                3 => e.Name.StartsWith("fd", StringComparison.OrdinalIgnoreCase),
                4 => e.Name.StartsWith("ff0", StringComparison.OrdinalIgnoreCase),
                5 => _stagedReplacements.ContainsKey(e.Index),
                _ => true
            })
        ).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void SelectCatalogEntry(Entry entry)
    {
        if (_catalogPreviewTask != null || _activeJob != null) return;
        _selectedCatalogEntry = entry;
        _catalogPreview = null;

        var a = Archive!;
        _stagedReplacements.TryGetValue(entry.Index, out string? staged);

        _catalogPreviewTask = Task.Run(() =>
        {
            byte[] bytes = staged != null ? File.ReadAllBytes(staged) : a.Read(entry);
            var p = Preview.Parse(entry, bytes);
            return (entry.Index, p);
        });
    }

    private unsafe void LoadCatalogAudio()
    {
        _waveform = [];
        if (_catalogPreview?.Audio is not { } audio) return;

        _waveform = new float[250];
        for (int i = 0; i < _waveform.Length; i++)
        {
            int start = (int)((long)i * audio.Samples.Length / _waveform.Length);
            int end = (int)((long)(i + 1) * audio.Samples.Length / _waveform.Length);
            for (int p = start; p < end; p++)
                _waveform[i] = Math.Max(_waveform[i], Math.Abs((int)audio.Samples[p]) / 32768.0f);
        }

        if (Raylib.IsAudioDeviceReady())
        {
            Wave wave = Raylib.LoadWaveFromMemory(".wav", audio.Wav);
            if (wave.Data != null)
            {
                if (_soundLoaded) Raylib.UnloadSound(_sound);
                _sound = Raylib.LoadSoundFromWave(wave);
                Raylib.UnloadWave(wave);
                _soundLoaded = Raylib.IsSoundValid(_sound);
                if (_soundLoaded) Raylib.SetSoundVolume(_sound, _volume);
            }
        }
    }

    private void StartJob(string name, Func<CancellationToken, Action> work)
    {
        if (_activeJob != null) return;
        _jobName = name;
        _statusMessage = name;
        _jobProgress = 0;
        _jobCancellation = new CancellationTokenSource();
        var token = _jobCancellation.Token;
        _activeJob = Task.Run(() => work(token), token);
    }

    private static string FormatSize(long bytes)
    {
        return bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):F1} MB" :
               bytes >= 1024 ? $"{bytes / 1024.0:F1} KB" :
               $"{bytes} B";
    }

    private static unsafe byte[] Png(Image image)
    {
        int length = 0;
        fixed (byte* ext = ".png\0"u8)
        {
            byte* buffer = Raylib.ExportImageToMemory(image, (sbyte*)ext, &length);
            if (buffer == null || length <= 0) throw new IOException("PNG export failed.");
            try { return new ReadOnlySpan<byte>(buffer, length).ToArray(); }
            finally { Raylib.MemFree(buffer); }
        }
    }

    public void Dispose()
    {
        Renderer.Dispose();
        Launcher.Dispose();
        if (_soundLoaded)
        {
            Raylib.StopSound(_sound);
            Raylib.UnloadSound(_sound);
        }
        if (Directory.Exists(_cacheDir))
        {
            try { Directory.Delete(_cacheDir, true); } catch { }
        }
    }

    #endregion
}
