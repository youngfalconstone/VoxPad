using System.Windows.Threading;
using VoxPad.Audio.Decoding;
using VoxPad.Core.Logging;
using VoxPad.Core.Models;
using VoxPad.Core.Serialization;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App.Services;

/// <summary>
/// Owns the sound list document: the working copy (autosaved every 5 minutes when dirty and on exit), the user's list
/// file, undo/redo snapshots, daily backups, import of files/folders with metadata, and missing-file checks.
/// </summary>
public sealed class DocumentService
{
    public const int AutosaveMinutes = 5;
    private readonly Preferences _prefs;
    private readonly DispatcherTimer _autosave;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private const int MaxUndo = 50;

    public SoundList List { get; private set; } = new();
    public string? Path { get; private set; }
    public bool IsDirty { get; private set; }
    public Category AllSounds { get; private set; } = new() { Name = Category.AllSoundsName, Hidden = true };

    public event Action? Changed;         // structure changed (reload views)
    public event Action? DirtyChanged;

    public DocumentService(Preferences prefs)
    {
        _prefs = prefs;
        _autosave = new DispatcherTimer { Interval = TimeSpan.FromMinutes(AutosaveMinutes) };
        _autosave.Tick += (_, _) => { if (IsDirty) SaveWorkingCopy(); };
        _autosave.Start();
    }

    // ---- lifecycle ----

    public void LoadAtStartup()
    {
        // Prefer the autosaved working copy (it may hold unsaved edits from a crash), else the user's list, else a new list.
        string? source = null;
        if (File.Exists(AppPaths.WorkingListFile)) source = AppPaths.WorkingListFile;
        else if (!string.IsNullOrEmpty(_prefs.CurrentListPath) && File.Exists(_prefs.CurrentListPath)) source = _prefs.CurrentListPath;
        if (source != null)
        {
            try
            {
                List = SoundListFile.Load(source);
                Path = string.IsNullOrEmpty(_prefs.CurrentListPath) ? null : _prefs.CurrentListPath;
                IsDirty = source == AppPaths.WorkingListFile && Path != null && !FilesEqual(source, Path);
            }
            catch (Exception ex) { Log.Error("Loading sound list failed", ex); List = new SoundList(); }
        }
        else { List = new SoundList(); Path = null; IsDirty = false; }
        BackupService.CreateDailyBackup(Path ?? AppPaths.WorkingListFile);
        AfterLoad();
    }

    private static bool FilesEqual(string a, string b)
    {
        try { return File.ReadAllText(a) == File.ReadAllText(b); } catch { return false; }
    }

    private void AfterLoad()
    {
        List.Repair();
        CheckMissingFiles();
        _undo.Clear(); _redo.Clear();
        Changed?.Invoke();
        DirtyChanged?.Invoke();
    }

    public void New()
    {
        List = new SoundList();
        Path = null;
        IsDirty = false;
        try { File.Delete(AppPaths.WorkingListFile); } catch { }
        AfterLoad();
    }

    public void Load(string path)
    {
        List = SoundListFile.Load(path);
        Path = System.IO.Path.GetExtension(path).Equals(SoundListFile.SoundpadExtension, StringComparison.OrdinalIgnoreCase) ? null : path;
        IsDirty = Path == null;
        _prefs.CurrentListPath = Path ?? "";
        if (Path != null) _prefs.AddRecent(Path);
        SaveWorkingCopy();
        AfterLoad();
    }

    public void Save()
    {
        if (Path == null) throw new InvalidOperationException("No path; use SaveAs");
        SoundListFile.Save(List, Path);
        IsDirty = false;
        SaveWorkingCopy();
        DirtyChanged?.Invoke();
    }

    public void SaveAs(string path)
    {
        Path = path;
        _prefs.CurrentListPath = path;
        _prefs.AddRecent(path);
        Save();
    }

    public void SaveWorkingCopy()
    {
        try { SoundListFile.Save(List, AppPaths.WorkingListFile); }
        catch (Exception ex) { Log.Error("Working copy save failed", ex); }
    }

    public void Restore(string backupPath)
    {
        List = SoundListFile.Load(backupPath);
        IsDirty = true;
        SaveWorkingCopy();
        AfterLoad();
    }

    /// <summary>Export > Sound files: copies every audio file into the folder and writes a list that references the copies.</summary>
    public (int copied, int failed) ExportSoundFiles(string folder)
    {
        Directory.CreateDirectory(folder);
        var copy = SoundListFile.Deserialize(SoundListFile.Serialize(List));
        int ok = 0, bad = 0;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in copy.Sounds)
        {
            try
            {
                var name = System.IO.Path.GetFileName(s.FilePath);
                var target = System.IO.Path.Combine(folder, name);
                int n = 1;
                while (used.Contains(target) || (File.Exists(target) && !FilesEqualBinary(s.FilePath, target)))
                    target = System.IO.Path.Combine(folder, $"{System.IO.Path.GetFileNameWithoutExtension(name)} ({n++}){System.IO.Path.GetExtension(name)}");
                if (!File.Exists(target)) File.Copy(s.FilePath, target);
                used.Add(target);
                s.FilePath = target;
                ok++;
            }
            catch (Exception ex) { Log.Warn($"Export: {s.FilePath}: {ex.Message}"); bad++; }
        }
        var listName = System.IO.Path.GetFileNameWithoutExtension(Path ?? "soundlist") + SoundListFile.Extension;
        SoundListFile.SaveRelative(copy, System.IO.Path.Combine(folder, listName));
        return (ok, bad);
    }

    private static bool FilesEqualBinary(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch { return false; }
    }

    // ---- editing with undo ----

    /// <summary>Wraps an edit: snapshot for undo, apply, mark dirty, notify.</summary>
    public void Edit(Action<SoundList> action, bool structural = true)
    {
        _undo.Push(SoundListFile.Serialize(List));
        while (_undo.Count > MaxUndo) { var arr = _undo.Reverse().Skip(1).ToArray(); _undo.Clear(); foreach (var s in arr) _undo.Push(s); }
        _redo.Clear();
        action(List);
        MarkDirty();
        if (structural) Changed?.Invoke();
    }

    public void MarkDirty()
    {
        if (!IsDirty) { IsDirty = true; DirtyChanged?.Invoke(); }
        else DirtyChanged?.Invoke();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(SoundListFile.Serialize(List));
        List = SoundListFile.Deserialize(_undo.Pop());
        MarkDirty(); CheckMissingFiles(); Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(SoundListFile.Serialize(List));
        List = SoundListFile.Deserialize(_redo.Pop());
        MarkDirty(); CheckMissingFiles(); Changed?.Invoke();
    }

    // ---- import ----

    public static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories); } catch { continue; }
                foreach (var f in files.Where(AudioFileDecoder.IsSupported).OrderBy(f => f, Core.NaturalStringComparer.Instance)) yield return f;
            }
            else if (File.Exists(p) && AudioFileDecoder.IsSupported(p)) yield return p;
        }
    }

    /// <summary>Creates entries with metadata (title/artist/duration). Runs on the caller's thread; call from a worker for big folders.</summary>
    public static SoundEntry CreateEntry(string file)
    {
        var e = new SoundEntry { FilePath = System.IO.Path.GetFullPath(file) };
        var meta = AudioFileDecoder.ReadMetadata(e.FilePath);
        e.Title = meta.Title; e.Artist = meta.Artist; e.DurationMs = (long)meta.Duration.TotalMilliseconds;
        return e;
    }

    public void CheckMissingFiles()
    {
        foreach (var s in List.Sounds) s.IsMissing = !File.Exists(s.FilePath);
    }

    public int MissingCount => List.Sounds.Count(s => s.IsMissing);

    public void Shutdown()
    {
        _autosave.Stop();
        if (IsDirty) SaveWorkingCopy();
    }
}
