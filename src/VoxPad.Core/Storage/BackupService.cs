using VoxPad.Core.Logging;

namespace VoxPad.Core.Storage;

/// <summary>Daily dated backups of the working sound list, created at startup. File > Restore sound list lists them.</summary>
public static class BackupService
{
    public const int KeepDays = 30;

    public sealed record Backup(string Path, DateTime Date, long Size);

    public static string? CreateDailyBackup(string listPath)
    {
        try
        {
            if (!File.Exists(listPath)) return null;
            Directory.CreateDirectory(AppPaths.BackupDirectory);
            var stem = System.IO.Path.GetFileNameWithoutExtension(listPath);
            var target = System.IO.Path.Combine(AppPaths.BackupDirectory, $"{stem}-{DateTime.Now:yyyy-MM-dd}.vpl");
            if (File.Exists(target)) return target;
            File.Copy(listPath, target);
            Prune();
            Log.Info($"Backup written: {target}");
            return target;
        }
        catch (Exception ex)
        {
            Log.Error("Backup failed", ex);
            return null;
        }
    }

    public static IReadOnlyList<Backup> List()
    {
        if (!Directory.Exists(AppPaths.BackupDirectory)) return Array.Empty<Backup>();
        return Directory.GetFiles(AppPaths.BackupDirectory, "*.vpl")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new Backup(f.FullName, f.LastWriteTime, f.Length))
            .ToList();
    }

    private static void Prune()
    {
        foreach (var b in List())
            if (b.Date < DateTime.Now.AddDays(-KeepDays)) { try { File.Delete(b.Path); } catch { } }
    }
}
