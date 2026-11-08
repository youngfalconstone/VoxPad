using System.IO.Compression;
using System.Text;
using VoxPad.Audio.Devices;
using VoxPad.Audio.Engine;
using VoxPad.Core.Licensing;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App.Services;

/// <summary>Help > Troubleshooting: builds a zip with the log tail, devices, routing status, preferences (device ids only) and system info.</summary>
public static class DiagnosticReport
{
    public static string Create(DeviceManager devices, SoundboardEngine engine, Preferences prefs, AutoKeysService autoKeys, HotkeyService hotkeys)
    {
        var dir = Path.Combine(Path.GetTempPath(), "VoxPad");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"VoxPad-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var sb = new StringBuilder();
        sb.AppendLine($"VoxPad {UpdateService.CurrentVersion}  {DateTime.Now:u}");
        sb.AppendLine($"OS: {Environment.OSVersion} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        sb.AppendLine($"Process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, elevated={App.IsElevated}, portable={AppPaths.IsPortable}");
        sb.AppendLine($"Working set: {Environment.WorkingSet / 1024 / 1024} MB");
        sb.AppendLine($"Licensed: {LicenseManager.IsRegistered} ({LicenseManager.Current?.Edition})");
        sb.AppendLine($"Mic privacy: {DeviceManager.CheckMicrophonePrivacy()}");
        sb.AppendLine($"Hooks installed: {hotkeys.IsInstalled} {hotkeys.InstallError}");
        sb.AppendLine();
        sb.AppendLine("== Routing ==");
        sb.AppendLine($"{engine.Routing.Level}: {engine.Routing.Message}");
        sb.AppendLine($"Mode={engine.Routing.ResolvedMode} render={engine.Routing.VirtualRenderId} capture='{engine.Routing.VirtualCaptureName}'");
        sb.AppendLine($"Mic running={engine.Mic.IsRunning} on '{engine.Mic.RenderDeviceName}' err={engine.Mic.LastError}");
        sb.AppendLine($"Speakers running={engine.Speakers.IsRunning} on '{engine.Speakers.DeviceName}' vol={engine.Speakers.Volume:F2} mute={engine.Speakers.Muted} err={engine.Speakers.LastError}");
        sb.AppendLine();
        sb.AppendLine("== Playback devices ==");
        foreach (var d in devices.RenderDevices) sb.AppendLine($"[{d.State}] {(d.IsDefault ? "*" : " ")}{(d.IsDefaultCommunications ? "C" : " ")} {d.Name}  {d.Id}");
        sb.AppendLine("== Recording devices ==");
        foreach (var d in devices.CaptureDevices) sb.AppendLine($"[{d.State}] {(d.IsDefault ? "*" : " ")}{(d.IsDefaultCommunications ? "C" : " ")} {d.Name}  {d.Id}{(prefs.MicrophoneDeviceIds.Contains(d.Id) ? "  (Use)" : "")}");
        sb.AppendLine();
        sb.AppendLine("== Auto Keys last evaluation ==");
        sb.AppendLine($"{autoKeys.LastEvaluationTime}: {autoKeys.LastGlobalReason}");
        foreach (var e in autoKeys.LastEvaluation) sb.AppendLine($"  {e.Entry.Key} enabled={e.Entry.Enabled} fired={e.Fired}: {e.Reason}");
        Write(zip, "report.txt", sb.ToString());

        Write(zip, "log-tail.txt", Log.RecentLines());
        try { if (Log.CurrentFile != null && File.Exists(Log.CurrentFile)) zip.CreateEntryFromFile(Log.CurrentFile, "log-full.txt"); } catch { }
        try
        {
            var tmp = Path.Combine(dir, "preferences-export.json");
            prefs.Export(tmp);
            zip.CreateEntryFromFile(tmp, "preferences.json");
            File.Delete(tmp);
        }
        catch { }
        try
        {
            var dumps = Directory.Exists(AppPaths.CrashDumpDirectory) ? Directory.GetFiles(AppPaths.CrashDumpDirectory, "*.txt").OrderByDescending(File.GetLastWriteTime).Take(3) : Array.Empty<string>();
            foreach (var d in dumps) zip.CreateEntryFromFile(d, "crash/" + Path.GetFileName(d));
        }
        catch { }
        return path;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var w = new StreamWriter(e.Open(), Encoding.UTF8);
        w.Write(content);
    }
}
