namespace VoxPad.Core.Storage;

/// <summary>
/// Where things live. Installed: data in %APPDATA%\VoxPad, logs + crash dumps in %ProgramData%\VoxPad.
/// Portable: a file named <c>portable.txt</c> next to the executable moves everything into <c>&lt;exe dir&gt;\Data</c>.
/// </summary>
public static class AppPaths
{
    public const string AppName = "VoxPad";

    public static string ExeDirectory { get; } = AppContext.BaseDirectory.TrimEnd('\\', '/');
    public static bool IsPortable { get; } = File.Exists(Path.Combine(ExeDirectory, "portable.txt"));

    public static string DataDirectory { get; } = IsPortable
        ? Path.Combine(ExeDirectory, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LogDirectory { get; } = IsPortable
        ? Path.Combine(ExeDirectory, "Data", "logs")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName, "logs");

    public static string CrashDumpDirectory { get; } = IsPortable
        ? Path.Combine(ExeDirectory, "Data", "crashdumps")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName, "crashdumps");

    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");
    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");
    public static string RecordingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), AppName);
    public static string PreferencesFile => Path.Combine(DataDirectory, "preferences.json");
    public static string LicenseFile => Path.Combine(DataDirectory, "license.dat");
    public static string WorkingListFile => Path.Combine(DataDirectory, "working.vpl");
    public static string DefaultListFile => Path.Combine(DataDirectory, "soundlist.vpl");
    public static string StatsFile => Path.Combine(DataDirectory, "stats.json");
    public static string TempDirectory => Path.Combine(Path.GetTempPath(), AppName);

    public static void EnsureDirectories()
    {
        foreach (var d in new[] { DataDirectory, BackupDirectory, CacheDirectory, TempDirectory })
            Directory.CreateDirectory(d);
        // ProgramData may be read-only for standard users if the installer did not create it; fall back silently.
        foreach (var d in new[] { LogDirectory, CrashDumpDirectory })
        {
            try { Directory.CreateDirectory(d); } catch { /* handled by Log fallback */ }
        }
    }
}
