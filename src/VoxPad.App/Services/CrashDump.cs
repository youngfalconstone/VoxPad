using VoxPad.App.Interop;
using VoxPad.Core.Logging;
using VoxPad.Core.Storage;

namespace VoxPad.App.Services;

/// <summary>Writes a minidump (MiniDumpWithIndirectlyReferencedMemory | WithDataSegs) to %ProgramData%\VoxPad\crashdumps on unhandled exceptions.</summary>
public static class CrashDump
{
    private const int MiniDumpWithDataSegs = 0x1;
    private const int MiniDumpWithIndirectlyReferencedMemory = 0x40;
    private const int MiniDumpWithThreadInfo = 0x1000;

    public static string? Write(Exception ex)
    {
        try
        {
            var dir = AppPaths.CrashDumpDirectory;
            try { Directory.CreateDirectory(dir); } catch { dir = Path.Combine(AppPaths.DataDirectory, "crashdumps"); Directory.CreateDirectory(dir); }
            var path = Path.Combine(dir, $"voxpad-{DateTime.Now:yyyyMMdd-HHmmss}.dmp");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                NativeMethods.MiniDumpWriteDump(NativeMethods.GetCurrentProcess(), NativeMethods.GetCurrentProcessId(), fs.SafeFileHandle,
                    MiniDumpWithDataSegs | MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithThreadInfo, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), ex.ToString());
            Log.Error("Unhandled exception; dump written to " + path, ex);
            Log.Flush();
            // Keep the folder small.
            foreach (var f in Directory.GetFiles(dir).OrderByDescending(File.GetLastWriteTime).Skip(10)) { try { File.Delete(f); } catch { } }
            return path;
        }
        catch { return null; }
    }
}
