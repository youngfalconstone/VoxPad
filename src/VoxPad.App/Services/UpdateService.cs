using System.Diagnostics;
using System.Net.Http;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;
using VoxPad.Core.Updates;

namespace VoxPad.App.Services;

/// <summary>Never / Daily / Weekly manifest checks; packages are downloaded to %TEMP% and only launched when hash + signature verify.</summary>
public sealed class UpdateService
{
    private readonly Preferences _prefs;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public UpdateService(Preferences prefs)
    {
        _prefs = prefs;
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("VoxPad/" + CurrentVersion);
    }

    public bool IsDue()
    {
        if (_prefs.UpdateCheck == UpdateCheckInterval.Never) return false;
        var last = _prefs.LastUpdateCheck ?? DateTime.MinValue;
        var interval = _prefs.UpdateCheck == UpdateCheckInterval.Daily ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
        return DateTime.UtcNow - last >= interval;
    }

    /// <summary>Returns a verified manifest newer than the running version, or null.</summary>
    public async Task<(UpdateManifest? manifest, string status)> CheckAsync()
    {
        _prefs.LastUpdateCheck = DateTime.UtcNow;
        // No update server is configured by default: nothing is sent; point the user at the releases page instead.
        if (!Uri.TryCreate(_prefs.UpdateManifestUrl, UriKind.Absolute, out var uri) || uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
            return (null, $"You have version {CurrentVersion}. New versions are published at https://github.com/youngfalconstone/VoxPad/releases");
        try
        {
            var json = await Http.GetStringAsync(_prefs.UpdateManifestUrl);
            var m = UpdateManifest.Parse(json);
            if (m == null) return (null, "Invalid update manifest.");
            if (!m.VerifySignature()) { Log.Warn("Update manifest signature invalid"); return (null, "Update manifest signature is invalid; ignoring."); }
            if (!m.IsNewerThan(CurrentVersion)) return (null, $"You have the latest version ({CurrentVersion}).");
            return (m, $"Version {m.Version} is available.");
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed: " + ex.Message);
            return (null, "Update check failed: " + ex.Message);
        }
    }

    public async Task<string> DownloadAndVerifyAsync(UpdateManifest m, IProgress<double>? progress = null)
    {
        Directory.CreateDirectory(AppPaths.TempDirectory);
        var path = Path.Combine(AppPaths.TempDirectory, $"VoxPad-{m.Version}.msi");
        using (var resp = await Http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(path);
            var buf = new byte[81920]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report(done / (double)total);
            }
        }
        if (!m.VerifyPackage(path)) { File.Delete(path); throw new InvalidOperationException("Downloaded package hash does not match the signed manifest."); }
        return path;
    }

    public static void LaunchInstaller(string msiPath)
    {
        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"") { UseShellExecute = true });
    }
}
