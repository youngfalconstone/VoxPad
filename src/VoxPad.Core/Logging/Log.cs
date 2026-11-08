using System.Diagnostics;
using System.Text;
using VoxPad.Core.Storage;

namespace VoxPad.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>Tiny synchronous-free logger: a background writer thread, daily rolling file, bounded in-memory tail for diagnostics.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Pending = new();
    private static readonly LinkedList<string> Tail = new();
    private static Thread? _writer;
    private static string? _file;
    private static bool _stopping;
    public static LogLevel MinLevel { get; set; } = Environment.GetEnvironmentVariable("VOXPAD_DEBUG") == "1" ? LogLevel.Debug : LogLevel.Info;

    public static string? CurrentFile => _file;

    public static void Start()
    {
        lock (Gate)
        {
            if (_writer != null) return;
            var dir = AppPaths.LogDirectory;
            try { Directory.CreateDirectory(dir); }
            catch { dir = Path.Combine(AppPaths.DataDirectory, "logs"); Directory.CreateDirectory(dir); }
            _file = Path.Combine(dir, $"voxpad-{DateTime.Now:yyyy-MM-dd}.log");
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "VoxPad.Log", Priority = ThreadPriority.BelowNormal };
            _writer.Start();
            CleanOld(dir);
        }
        Info($"=== {AppPaths.AppName} start, portable={AppPaths.IsPortable}, os={Environment.OSVersion}, x64={Environment.Is64BitProcess}, arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
    }

    private static void CleanOld(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "voxpad-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch { }
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warn, msg);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, ex == null ? msg : $"{msg}: {ex}");

    private static void Write(LogLevel level, string msg)
    {
        if (level < MinLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level,-5}] [{Environment.CurrentManagedThreadId,3}] {msg}";
        lock (Gate)
        {
            Pending.Enqueue(line);
            Tail.AddLast(line);
            while (Tail.Count > 500) Tail.RemoveFirst();
            Monitor.Pulse(Gate);
        }
        System.Diagnostics.Debug.WriteLine(line);
    }

    /// <summary>Last ~500 lines, for the troubleshooting report.</summary>
    public static string RecentLines()
    {
        lock (Gate) return string.Join(Environment.NewLine, Tail);
    }

    public static void Flush()
    {
        lock (Gate)
        {
            while (Pending.Count > 0 && _writer != null) Monitor.Wait(Gate, 200);
        }
    }

    public static void Stop()
    {
        lock (Gate) { _stopping = true; Monitor.PulseAll(Gate); }
        _writer?.Join(1000);
    }

    private static void WriterLoop()
    {
        var sb = new StringBuilder();
        while (true)
        {
            lock (Gate)
            {
                while (Pending.Count == 0 && !_stopping) Monitor.Wait(Gate);
                while (Pending.Count > 0) sb.AppendLine(Pending.Dequeue());
                Monitor.PulseAll(Gate);
                if (_stopping && sb.Length == 0) return;
            }
            try { if (_file != null) File.AppendAllText(_file, sb.ToString()); } catch { }
            sb.Clear();
        }
    }
}
