using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.App.Views;
using VoxPad.Audio.Devices;
using VoxPad.Audio.Engine;
using VoxPad.Core.Licensing;
using VoxPad.Core.Logging;
using VoxPad.Core.Settings;
using VoxPad.Core.Storage;

namespace VoxPad.App;

public partial class App : Application
{
    private Mutex? _mutex;
    public static new App Current => (App)Application.Current;
    public static bool IsElevated { get; private set; }
    public static bool CleanStart { get; private set; }

    public Preferences Prefs { get; private set; } = null!;
    public DeviceManager Devices { get; private set; } = null!;
    public SoundboardEngine Engine { get; private set; } = null!;
    public DocumentService Doc { get; private set; } = null!;
    public HotkeyService Hotkeys { get; private set; } = null!;
    public AutoKeysService AutoKeys { get; private set; } = null!;
    public GlobalStats Stats { get; private set; } = null!;
    public MainViewModel Vm { get; private set; } = null!;
    public RemoteControlServer? Remote { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureDirectories();
        Log.Start();
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { if (a.ExceptionObject is Exception ex) CrashDump.Write(ex); };
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Unobserved task exception", a.Exception); a.SetObserved(); };

        // Single instance: a second launch just shows the first window (via the remote pipe's existence we could signal; a mutex + named event is simpler).
        _mutex = new Mutex(true, "Local\\VoxPad.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting("Local\\VoxPad.ShowWindow").Set(); } catch { }
            Shutdown();
            return;
        }

        CleanStart = e.Args.Any(a => a.Equals("--clean", StringComparison.OrdinalIgnoreCase));
        if (CleanStart)
        {
            // --clean: reset preferences but keep the sound list.
            try { File.Delete(AppPaths.PreferencesFile); } catch { }
            Log.Info("--clean: preferences reset");
        }
        Prefs = Preferences.Load();
        Strings.SetLanguage(Prefs.Language);
        ThemeService.Apply(Prefs.Theme);
        LicenseManager.LoadFromDisk();
        Stats = GlobalStats.Load();
        Stats.Launches++;
        IsElevated = CheckElevated();

        Devices = new DeviceManager();
        Engine = new SoundboardEngine(Devices, Prefs);
        Doc = new DocumentService(Prefs);
        Hotkeys = new HotkeyService(Dispatcher);
        AutoKeys = new AutoKeysService(Prefs);
        Vm = new MainViewModel(Prefs, Doc, Devices, Engine, Hotkeys, AutoKeys, Stats);

        var window = new MainWindow(Vm);
        MainWindow = window;
        Doc.LoadAtStartup();
        Engine.ApplyDevices();
        Hotkeys.Start();
        Remote = new RemoteControlServer(Vm, Dispatcher);
        Remote.Start();
        StartShowWindowListener(window);

        if (Prefs.StartMinimized)
        {
            window.WindowState = WindowState.Minimized;
            window.Show();
            if (Prefs.MinimizeToTray) window.HideToTray();
        }
        else window.Show();

        if (IsElevated)
            Vm.Status(Strings.Get("ElevatedWarning", "VoxPad is running as administrator: drag-and-drop from Explorer will not work (Windows blocks drops into elevated windows)."), true);
        var privacy = DeviceManager.CheckMicrophonePrivacy();
        if (privacy is DeviceManager.MicPrivacy.DeniedForDesktopApps or DeviceManager.MicPrivacy.DeniedForUser or DeviceManager.MicPrivacy.DeniedSystemWide)
            window.ShowMicAccessDenied(privacy);
        if (Prefs.UpdateCheck != UpdateCheckInterval.Never && new UpdateService(Prefs).IsDue())
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => window.ShowUpdate(manual: false));

        // Developer/CI aid: "--screenshot <file.png>" renders the main window after start-up and exits.
        int shotIdx = Array.FindIndex(e.Args, a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (shotIdx >= 0 && shotIdx + 1 < e.Args.Length)
        {
            var target = e.Args[shotIdx + 1];
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try
                {
                    var root = (System.Windows.Media.Visual)window.Content;
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(root);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using var fs = File.Create(target);
                    enc.Save(fs);
                    Log.Info("Screenshot written: " + target);
                }
                catch (Exception ex) { Log.Error("screenshot", ex); }
                window.ExitApplication();
            };
            t.Start();
        }
    }

    private void StartShowWindowListener(MainWindow window)
    {
        var evt = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\VoxPad.ShowWindow");
        var t = new Thread(() =>
        {
            while (evt.WaitOne()) Dispatcher.BeginInvoke(() => window.RestoreFromTray());
        }) { IsBackground = true, Name = "VoxPad.ShowWindow" };
        t.Start();
    }

    private static bool CheckElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Dispatcher exception", e.Exception);
        var dump = CrashDump.Write(e.Exception);
        try
        {
            MessageBox.Show($"{Strings.Get("UnexpectedError", "An unexpected error occurred.")}\n\n{e.Exception.Message}\n\n{Strings.Get("DumpWritten", "Details were written to")} {dump}",
                "VoxPad", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        e.Handled = true;
    }

    public void SaveEverything()
    {
        try { Prefs.Save(); } catch (Exception ex) { Log.Error("prefs save", ex); }
        try { Stats.Save(); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Vm?.Shutdown();
            Doc?.Shutdown();
            Remote?.Dispose();
            Hotkeys?.Dispose();
            Engine?.Dispose();
            Devices?.Dispose();
            SaveEverything();
        }
        catch (Exception ex) { Log.Error("exit", ex); }
        Log.Info("=== exit");
        Log.Stop();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
