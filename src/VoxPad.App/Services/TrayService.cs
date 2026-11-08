using System.Drawing;
using System.Windows;
using WF = System.Windows.Forms;

namespace VoxPad.App.Services;

/// <summary>Notification-area icon with a restore menu and balloon notifications (WinForms NotifyIcon, no extra dependency).</summary>
public sealed class TrayService : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly Window _window;

    public event Action? PlayStopRequested;
    public event Action? ExitRequested;

    public TrayService(Window window, string tooltip)
    {
        _window = window;
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(Strings.Get("Tray_Show", "Show VoxPad"), null, (_, _) => Restore());
        menu.Items.Add(Strings.Get("Stop", "Stop"), null, (_, _) => PlayStopRequested?.Invoke());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(Strings.Get("Exit", "Exit"), null, (_, _) => ExitRequested?.Invoke());
        _icon = new WF.NotifyIcon
        {
            Text = tooltip.Length > 63 ? tooltip[..63] : tooltip,
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => Restore();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/VoxPad;component/Assets/voxpad.ico");
            using var s = System.Windows.Application.GetResourceStream(uri)!.Stream;
            return new Icon(s);
        }
        catch { return SystemIcons.Application; }
    }

    public void Restore()
    {
        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.ShowInTaskbar = true;
        });
    }

    public void Notify(string title, string text, bool warning = false)
    {
        try { _icon.ShowBalloonTip(3000, title, text, warning ? WF.ToolTipIcon.Warning : WF.ToolTipIcon.Info); } catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
