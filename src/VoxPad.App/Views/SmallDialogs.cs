using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VoxPad.App.Controls;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.Audio.Engine;
using VoxPad.Core.Licensing;
using VoxPad.Core.Models;
using VoxPad.Core.Storage;

namespace VoxPad.App.Views;

/// <summary>Base for the code-built dialogs: themed, centred on the owner, Esc closes.</summary>
public abstract class DialogBase : Window
{
    protected DialogBase(string title, double width = 420)
    {
        Title = title; Width = width; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource("ThemedWindow");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { OnEscape(); } };
    }
    protected virtual void OnEscape() => Close();

    protected static StackPanel Column(double margin = 12) => new() { Margin = new Thickness(margin) };
    protected static TextBlock Text(string s, bool hint = false) => new() { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6), Style = hint ? (Style)Application.Current.FindResource("Hint") : null };
    protected static Button Btn(string text, RoutedEventHandler onClick, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
        b.Click += onClick; return b;
    }
    protected static StackPanel ButtonRow(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }
}

// ---------------------------------------------------------------- hotkey capture

public sealed class HotkeyCaptureDialog : DialogBase
{
    private readonly HotkeyService _hooks;
    private readonly TextBlock _display;
    public bool Accepted { get; private set; }
    public Hotkey? Result { get; private set; }

    public HotkeyCaptureDialog(HotkeyService hooks, string title, Hotkey? current) : base(title, 400)
    {
        _hooks = hooks; Result = current;
        var col = Column();
        col.Children.Add(Text(Strings.Get("HotkeyCapturePrompt", "Press the key or key combination now. Mouse buttons Mid, 4 and 5 work too (left/right need a modifier). Enter saves, Delete or Backspace clears, Esc cancels.")));
        _display = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 10), Text = current?.ToString() ?? Strings.Get("None", "(none)") };
        col.Children.Add(_display);
        col.Children.Add(ButtonRow(
            Btn(Strings.Get("Clear", "Clear"), (_, _) => { Result = null; Accepted = true; Close(); }),
            Btn(Strings.Get("OK", "OK"), (_, _) => { Accepted = true; Close(); }),
            Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close())));
        Content = col;
        Loaded += (_, _) => Arm();
        Closed += (_, _) => _hooks.EndCapture();
    }

    private void Arm() => _hooks.BeginCapture(hk =>
    {
        // Enter/Delete/Backspace are reserved as dialog actions when pressed alone.
        if (hk.Modifiers == HotkeyModifiers.None && hk.Input == HotkeyInput.Keyboard)
        {
            if (hk.VirtualKey == 0x0D) { Accepted = true; Close(); return; }
            if (hk.VirtualKey is 0x2E or 0x08) { Result = null; _display.Text = Strings.Get("None", "(none)"); Arm(); return; }
            if (hk.VirtualKey == 0x1B) { Close(); return; }
        }
        if (!hk.IsAllowed) { _display.Text = Strings.Get("BareMouseNotAllowed", "Left/right mouse buttons need a modifier key."); Arm(); return; }
        Result = hk; _display.Text = hk.ToString();
        Arm();
    });

    protected override void OnEscape() { Accepted = false; Close(); }
}

// ---------------------------------------------------------------- input / colour / hotbar size

public sealed class InputDialog : DialogBase
{
    private readonly TextBox _box;
    public string? Value { get; private set; }
    private InputDialog(string title, string prompt, string initial) : base(title)
    {
        var col = Column();
        col.Children.Add(Text(prompt));
        _box = new TextBox { Text = initial };
        _box.SelectAll();
        col.Children.Add(_box);
        col.Children.Add(ButtonRow(Btn(Strings.Get("OK", "OK"), (_, _) => { Value = _box.Text; Close(); }, isDefault: true), Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close(), isCancel: true)));
        Content = col;
        Loaded += (_, _) => _box.Focus();
    }
    public static string? Show(Window owner, string title, string prompt, string initial)
    {
        var d = new InputDialog(title, prompt, initial) { Owner = owner };
        d.ShowDialog();
        return d.Value;
    }
}

public sealed class ColorPickerDialog : DialogBase
{
    private static readonly uint[] Palette =
    {
        0xFFE57373, 0xFFF06292, 0xFFBA68C8, 0xFF9575CD, 0xFF7986CB, 0xFF64B5F6, 0xFF4FC3F7, 0xFF4DD0E1,
        0xFF4DB6AC, 0xFF81C784, 0xFFAED581, 0xFFDCE775, 0xFFFFF176, 0xFFFFD54F, 0xFFFFB74D, 0xFFFF8A65,
        0xFFA1887F, 0xFFE0E0E0, 0xFF90A4AE, 0xFFD32F2F, 0xFF7B1FA2, 0xFF303F9F, 0xFF0288D1, 0xFF00796B,
        0xFF388E3C, 0xFFAFB42B, 0xFFF57C00, 0xFF5D4037, 0xFF455A64, 0xFF616161, 0xFF212121, 0xFF5848C8,
    };
    public uint? Value { get; private set; }
    private readonly TextBox _hex;

    private ColorPickerDialog(uint current) : base(Strings.Get("SetColor", "Colour"), 360)
    {
        var col = Column();
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in Palette)
        {
            var b = new Border { Width = 32, Height = 28, Margin = new Thickness(2), CornerRadius = new CornerRadius(3), Background = SoundItemViewModel.BrushFor(c), Cursor = Cursors.Hand, BorderThickness = new Thickness(c == current ? 2 : 0), BorderBrush = Brushes.Black };
            b.MouseLeftButtonDown += (_, _) => { Value = c; Close(); };
            grid.Children.Add(b);
        }
        col.Children.Add(grid);
        var row = new DockPanel();
        _hex = new TextBox { Text = current == 0 ? "" : $"#{current & 0xFFFFFF:X6}", Width = 100 };
        row.Children.Add(new TextBlock { Text = Strings.Get("HexColor", "Hex:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(_hex);
        col.Children.Add(row);
        col.Children.Add(ButtonRow(
            Btn(Strings.Get("NoColor", "No colour"), (_, _) => { Value = 0; Close(); }),
            Btn(Strings.Get("OK", "OK"), (_, _) => { var t = _hex.Text.TrimStart('#'); if (uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v)) { Value = 0xFF000000 | (v & 0xFFFFFF); Close(); } }, isDefault: true),
            Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }

    public static uint? Show(Window owner, uint current)
    {
        var d = new ColorPickerDialog(current) { Owner = owner };
        d.ShowDialog();
        return d.Value;
    }
}

public sealed class HotbarSizeDialog : DialogBase
{
    public HotbarSizeDialog(MainViewModel vm) : base(Strings.Get("HotbarSize", "Hotbar size"), 320)
    {
        var col = Column();
        var cols = new TextBox { Text = vm.Doc.List.HotbarColumns.ToString() };
        var rows = new TextBox { Text = vm.Doc.List.HotbarRows.ToString() };
        var pages = new TextBox { Text = vm.Doc.List.HotbarPages.ToString() };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, TextBox tb, int r)
        {
            g.RowDefinitions.Add(new RowDefinition());
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(l, r); g.Children.Add(l);
            Grid.SetRow(tb, r); Grid.SetColumn(tb, 1); tb.Margin = new Thickness(0, 2, 0, 2); g.Children.Add(tb);
        }
        Row(Strings.Get("Columns", "Columns:"), cols, 0); Row(Strings.Get("Rows", "Rows:"), rows, 1); Row(Strings.Get("Pages", "Pages:"), pages, 2);
        col.Children.Add(g);
        col.Children.Add(Text(Strings.Get("HotbarSizeHint", "Hotkeys are shared across pages. Tiles outside the new grid are removed."), hint: true));
        col.Children.Add(ButtonRow(Btn(Strings.Get("OK", "OK"), (_, _) =>
        {
            if (int.TryParse(cols.Text, out var c) && int.TryParse(rows.Text, out var r) && int.TryParse(pages.Text, out var p)) { vm.HotbarResize(c, r, p); Close(); }
        }, isDefault: true), Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }
}

// ---------------------------------------------------------------- hotkey overview / auto keys troubleshooting

public sealed class HotkeyOverviewWindow : Window
{
    public HotkeyOverviewWindow(MainViewModel vm)
    {
        Title = Strings.Get("HotkeyOverview", "Hotkey overview"); Width = 560; Height = 480; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Style = (Style)Application.Current.FindResource("ThemedWindow");
        var lv = new ListView { Margin = new Thickness(8) };
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = Strings.Get("ColHotkey", "Hotkey"), Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding("hotkey") });
        gv.Columns.Add(new GridViewColumn { Header = Strings.Get("Type", "Type"), Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding("kind") });
        gv.Columns.Add(new GridViewColumn { Header = Strings.Get("Target", "Target"), Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding("target") });
        lv.View = gv;
        lv.ItemsSource = vm.AllBindings().OrderBy(b => b.kind).ThenBy(b => b.hotkey).Select(b => new { b.hotkey, b.kind, b.target }).ToList();
        var dock = new DockPanel();
        var hint = new TextBlock { Text = Strings.Format("HotkeyOverviewHint", vm.HotkeysEnabled ? Strings.Get("Enabled", "enabled") : Strings.Get("Disabled", "disabled"), vm.PassHotkeys ? Strings.Get("On", "on") : Strings.Get("Off", "off")), Margin = new Thickness(8, 8, 8, 0), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(hint, Dock.Top); dock.Children.Add(hint); dock.Children.Add(lv);
        Content = dock;
    }
}

public sealed class AutoKeysTroubleshootingWindow : DialogBase
{
    public AutoKeysTroubleshootingWindow(MainViewModel vm) : base(Strings.Get("AutoKeysTroubleshooting", "Auto Keys troubleshooting"), 560)
    {
        var col = Column();
        var ak = vm.AutoKeys;
        col.Children.Add(Text(Strings.Get("AutoKeysTroubleHint", "Auto Keys press your push-to-talk key when a sound starts on the microphone. The last evaluation shows why each entry did or did not fire. Play a sound, then open this dialog again to re-check.")));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{Strings.Get("AutoKeys", "Auto Keys")}: {(ak.Enabled ? Strings.Get("Enabled", "enabled") : Strings.Get("Disabled", "disabled"))}");
        sb.AppendLine($"{Strings.Get("PlayModeMenu", "Play mode")}: {vm.PlayModeText}  ({(vm.PlayMode.IncludesMic() ? Strings.Get("MicPlayMode", "microphone play mode") : Strings.Get("NotMicPlayMode", "NOT a microphone play mode – Auto Keys never fire"))})");
        sb.AppendLine($"{Strings.Get("Routing", "Routing")}: {vm.Engine.Routing.Level} – {vm.Engine.Routing.Message}");
        sb.AppendLine($"{Strings.Get("Entries", "Entries")}: {vm.Prefs.AutoKeys.Count}");
        var fg = Interop.NativeMethods.GetForegroundWindow();
        sb.AppendLine($"{Strings.Get("ForegroundNow", "Foreground app now")}: {AutoKeysService.ProcessNameOf(fg)} ({(AutoKeysService.IsFullscreen(fg, out var det) ? "fullscreen" : "windowed")}: {det})");
        sb.AppendLine();
        if (ak.LastEvaluationTime == null) sb.AppendLine(Strings.Get("NoEvaluationYet", "No sound has been played yet in this session."));
        else
        {
            sb.AppendLine($"{Strings.Get("LastEvaluation", "Last evaluation")}: {ak.LastEvaluationTime:T}");
            if (ak.LastGlobalReason.Length > 0) sb.AppendLine("  ✖ " + ak.LastGlobalReason);
            foreach (var e in ak.LastEvaluation) sb.AppendLine($"  {(e.Fired ? "✔" : "✖")} {e.Entry.Key} [{e.Entry.Condition}] – {e.Reason}");
        }
        col.Children.Add(new TextBox { Text = sb.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 220, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") });
        col.Children.Add(ButtonRow(Btn(Strings.Get("OpenAutoKeysPrefs", "Auto Keys settings…"), (_, _) => { Close(); vm.Dialogs.ShowPreferences("AutoKeys"); }), Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }
}

// ---------------------------------------------------------------- about / register / stats

public sealed class AboutWindow : DialogBase
{
    public AboutWindow(MainViewModel vm) : base(Strings.Get("About", "About VoxPad"), 440)
    {
        var col = Column();
        col.Children.Add(new TextBlock { Text = "VoxPad", FontSize = 22, FontWeight = FontWeights.Bold });
        col.Children.Add(Text($"{Strings.Get("Version", "Version")} {UpdateService.CurrentVersion}  ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})"));
        var lic = LicenseManager.Current;
        string licText = lic == null ? (LicenseManager.IsLimited ? Strings.Get("TrialEdition", "Trial – max 10 sounds, 10 microphone plays per launch") : "Free and open source (MIT licence)")
            : vm.Prefs.HideLicenseeName ? Strings.Format("LicensedTo", "••••••••", lic.Edition) : Strings.Format("LicensedTo", lic.Name, lic.Edition);
        col.Children.Add(Text(licText));
        col.Children.Add(Text(Strings.Get("AboutText", "A Windows soundboard that routes sounds into your microphone. Audio engine: NAudio, NVorbis, Concentus, TagLib#. Virtual microphone via VB-CABLE / Voicemeeter."), hint: true));
        col.Children.Add(Text($"{Strings.Get("DataFolder", "Data")}: {AppPaths.DataDirectory}\n{Strings.Get("LogFolder", "Logs")}: {AppPaths.LogDirectory}", hint: true));
        col.Children.Add(ButtonRow(Btn(Strings.Get("OpenDataFolder", "Open data folder"), (_, _) => { try { Process.Start(new ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true }); } catch { } }), Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }
}

public sealed class RegisterWindow : DialogBase
{
    public RegisterWindow(MainViewModel vm) : base(Strings.Get("Register", "Register"), 460)
    {
        var col = Column();
        if (LicenseManager.IsRegistered)
        {
            var lic = LicenseManager.Current!;
            col.Children.Add(Text(Strings.Format("LicensedTo", vm.Prefs.HideLicenseeName ? "••••••••" : lic.Name, lic.Edition)));
            var hide = new CheckBox { Content = Strings.Get("HideLicenseeName", "Hide my name (streamer mode)"), IsChecked = vm.Prefs.HideLicenseeName };
            hide.Click += (_, _) => { vm.Prefs.HideLicenseeName = hide.IsChecked == true; };
            col.Children.Add(hide);
            col.Children.Add(ButtonRow(Btn(Strings.Get("RemoveLicense", "Remove license"), (_, _) => { if (vm.Dialogs.Confirm("VoxPad", Strings.Get("ConfirmRemoveLicense", "Remove the license from this PC?"))) { LicenseManager.Unregister(); vm.RefreshLicenseState(); Close(); } }), Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        }
        else
        {
            col.Children.Add(Text(Strings.Get("RegisterPrompt", "Enter the name and serial from your purchase e-mail. The serial is checked offline and stored in license.dat.")));
            col.Children.Add(new TextBlock { Text = Strings.Get("Name", "Name:") });
            var name = new TextBox(); col.Children.Add(name);
            col.Children.Add(new TextBlock { Text = Strings.Get("Serial", "Serial:"), Margin = new Thickness(0, 6, 0, 0) });
            var serial = new TextBox { AcceptsReturn = false }; col.Children.Add(serial);
            var hide = new CheckBox { Content = Strings.Get("HideLicenseeName", "Hide my name (streamer mode)"), IsChecked = vm.Prefs.HideLicenseeName, Margin = new Thickness(0, 8, 0, 0) };
            col.Children.Add(hide);
            col.Children.Add(ButtonRow(Btn(Strings.Get("Register", "Register"), (_, _) =>
            {
                if (LicenseManager.Register(name.Text, serial.Text))
                {
                    vm.Prefs.HideLicenseeName = hide.IsChecked == true;
                    vm.RefreshLicenseState();
                    vm.Dialogs.Info("VoxPad", Strings.Get("RegisteredThanks", "Thank you! VoxPad is now registered."));
                    Close();
                }
                else vm.Dialogs.Warn("VoxPad", Strings.Get("SerialInvalid", "The serial does not match the name. Check for typos (I/1 and O/0 are interchangeable)."));
            }, isDefault: true), Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close(), isCancel: true)));
        }
        Content = col;
    }
}

public sealed class StatsWindow : DialogBase
{
    public StatsWindow(MainViewModel vm) : base(Strings.Get("Statistics", "Statistics"), 520)
    {
        var col = Column();
        var list = vm.Doc.List;
        col.Children.Add(Text($"{Strings.Get("AllTimePlays", "All-time microphone plays")}: {vm.Stats.AllTimePlays:N0}\n{Strings.Get("ListPlays", "Plays in this list")}: {list.TotalPlays:N0}\n{Strings.Get("SoundsInList", "Sounds in list")}: {list.Sounds.Count}\n{Strings.Get("Launches", "Launches")}: {vm.Stats.Launches}\n{Strings.Get("Since", "Since")}: {vm.Stats.FirstRun:d}"));
        col.Children.Add(new TextBlock { Text = Strings.Get("TopSounds", "Most played"), Style = (Style)Application.Current.FindResource("Heading") });
        var lv = new ListView { MaxHeight = 260 };
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = Strings.Get("ColTag", "Tag"), Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding("Tag") });
        gv.Columns.Add(new GridViewColumn { Header = Strings.Get("ColPlayCount", "Play count"), Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding("PlayCount") });
        gv.Columns.Add(new GridViewColumn { Header = Strings.Get("ColLastPlayed", "Last played on"), Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("LastPlayed") { StringFormat = "g" } });
        lv.View = gv;
        lv.ItemsSource = list.Sounds.OrderByDescending(s => s.PlayCount).ThenByDescending(s => s.LastPlayed).Take(50).ToList();
        col.Children.Add(lv);
        col.Children.Add(ButtonRow(
            Btn(Strings.Get("ResetAllPlayCounts", "Reset all play counts"), (_, _) => { vm.ResetAllPlayCountsCommand.Execute(null); Close(); }),
            Btn(Strings.Get("ResetAllTime", "Reset all-time total"), (_, _) => { if (vm.Dialogs.Confirm("VoxPad", Strings.Get("ConfirmResetAllTime", "Reset the all-time total?"))) { vm.Stats.AllTimePlays = 0; vm.Stats.Save(); Close(); } }),
            Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }
}

// ---------------------------------------------------------------- web import / restore / update / voice measurement / troubleshooting

public sealed class WebImportDialog : DialogBase
{
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public WebImportDialog(MainViewModel vm) : base(Strings.Get("WebImport", "Import from web"), 480)
    {
        var col = Column();
        col.Children.Add(Text(Strings.Get("WebImportPrompt", "Paste a direct link to an audio file (mp3, ogg, wav…). The file is downloaded into your VoxPad music folder and added to the list.")));
        col.Children.Add(new TextBlock { Text = "URL:" });
        var url = new TextBox(); col.Children.Add(url);
        col.Children.Add(new TextBlock { Text = Strings.Get("FileNameOptional", "File name (optional):"), Margin = new Thickness(0, 6, 0, 0) });
        var name = new TextBox(); col.Children.Add(name);
        var status = Text("", hint: true); col.Children.Add(status);
        var ok = Btn(Strings.Get("Import", "Import"), null!, isDefault: true);
        ok.Click += async (_, _) =>
        {
            ok.IsEnabled = false;
            try
            {
                var uri = new Uri(url.Text.Trim());
                var fileName = string.IsNullOrWhiteSpace(name.Text) ? System.IO.Path.GetFileName(uri.LocalPath) : name.Text.Trim();
                if (string.IsNullOrWhiteSpace(System.IO.Path.GetExtension(fileName))) fileName += System.IO.Path.GetExtension(uri.LocalPath);
                if (string.IsNullOrWhiteSpace(System.IO.Path.GetFileNameWithoutExtension(fileName))) fileName = "download" + System.IO.Path.GetExtension(uri.LocalPath);
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) fileName = fileName.Replace(ch, '_');
                Directory.CreateDirectory(AppPaths.RecordingsDirectory);
                var target = System.IO.Path.Combine(AppPaths.RecordingsDirectory, fileName);
                int n = 1; while (File.Exists(target)) target = System.IO.Path.Combine(AppPaths.RecordingsDirectory, $"{System.IO.Path.GetFileNameWithoutExtension(fileName)} ({n++}){System.IO.Path.GetExtension(fileName)}");
                status.Text = Strings.Get("Downloading", "Downloading…");
                var bytes = await Http.GetByteArrayAsync(uri);
                await File.WriteAllBytesAsync(target, bytes);
                if (!Audio.Decoding.AudioFileDecoder.IsSupported(target)) { File.Delete(target); throw new InvalidOperationException(Strings.Get("UnsupportedFormat", "Unsupported audio format")); }
                vm.ImportPaths(new[] { target });
                Close();
            }
            catch (Exception ex) { status.Text = ex.Message; ok.IsEnabled = true; }
        };
        col.Children.Add(ButtonRow(ok, Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close(), isCancel: true)));
        Content = col;
        Loaded += (_, _) => { url.Focus(); if (Clipboard.ContainsText() && Uri.IsWellFormedUriString(Clipboard.GetText().Trim(), UriKind.Absolute)) url.Text = Clipboard.GetText().Trim(); };
    }
}

public sealed class RestoreListDialog : DialogBase
{
    public RestoreListDialog(MainViewModel vm) : base(Strings.Get("RestoreList", "Restore sound list"), 520)
    {
        var col = Column();
        col.Children.Add(Text(Strings.Get("RestorePrompt", "Daily backups are written at startup. Restoring replaces the current list (you can still undo by loading your saved list).")));
        var lb = new ListBox { Height = 220 };
        var backups = BackupService.List();
        lb.ItemsSource = backups.Select(b => $"{b.Date:g}   {System.IO.Path.GetFileName(b.Path)}   ({b.Size / 1024} KB)").ToList();
        col.Children.Add(lb);
        col.Children.Add(ButtonRow(
            Btn(Strings.Get("OpenBackupFolder", "Open folder"), (_, _) => { try { Process.Start(new ProcessStartInfo(AppPaths.BackupDirectory) { UseShellExecute = true }); } catch { } }),
            Btn(Strings.Get("Restore", "Restore"), (_, _) =>
            {
                if (lb.SelectedIndex < 0) return;
                var b = backups[lb.SelectedIndex];
                if (!vm.Dialogs.Confirm("VoxPad", Strings.Format("ConfirmRestore", b.Date))) return;
                try { vm.Stop(); vm.Doc.Restore(b.Path); vm.Status(Strings.Format("Restored", b.Date)); Close(); }
                catch (Exception ex) { vm.Dialogs.Warn("VoxPad", ex.Message); }
            }, isDefault: true),
            Btn(Strings.Get("Cancel", "Cancel"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }
}

public sealed class UpdateWindow : DialogBase
{
    public UpdateWindow(MainViewModel vm, bool manual) : base(Strings.Get("CheckForUpdates", "Check for updates"), 460)
    {
        var col = Column();
        var status = Text(Strings.Get("Checking", "Checking…")); col.Children.Add(status);
        var notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Visibility = Visibility.Collapsed }; col.Children.Add(notes);
        var bar = new ProgressBar { Height = 14, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) }; col.Children.Add(bar);
        var install = Btn(Strings.Get("DownloadInstall", "Download and install"), null!); install.IsEnabled = false;
        col.Children.Add(ButtonRow(install, Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        Content = col;
        var svc = new UpdateService(vm.Prefs);
        Loaded += async (_, _) =>
        {
            var (m, text) = await svc.CheckAsync();
            status.Text = text;
            if (m == null) { if (!manual) Close(); return; }
            notes.Text = m.Notes ?? ""; notes.Visibility = Visibility.Visible;
            install.IsEnabled = true;
            install.Click += async (_, _) =>
            {
                install.IsEnabled = false; bar.Visibility = Visibility.Visible;
                try
                {
                    var path = await svc.DownloadAndVerifyAsync(m, new Progress<double>(p => bar.Value = p));
                    status.Text = Strings.Get("PackageVerified", "Package verified. The installer will start; VoxPad closes now.");
                    UpdateService.LaunchInstaller(path);
                    vm.Dialogs.ExitApplication();
                }
                catch (Exception ex) { status.Text = ex.Message; install.IsEnabled = true; }
            };
        };
    }
}

public sealed class VoiceMeasureDialog : DialogBase
{
    private readonly VoiceMeasurement _vm;
    public VoiceMeasureDialog(MainViewModel main) : base(Strings.Get("VoiceMeasurement", "Voice measurement"), 460)
    {
        var col = Column();
        col.Children.Add(Text(Strings.Get("VoiceMeasurePrompt", "Speak normally into your microphone for about 8 seconds. The measured loudness becomes the normalisation target so sounds match your voice (a good result is above 74 dB).")));
        var meter = new LevelMeter { Height = 14, Background = (Brush)Application.Current.FindResource("Theme.MeterBackground") }; col.Children.Add(meter);
        var bar = new ProgressBar { Height = 10, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 0) }; col.Children.Add(bar);
        var result = Text($"{Strings.Get("CurrentTarget", "Current target")}: {main.Prefs.NormalizationTargetDb:F1} dB{(main.Prefs.VoiceMeasured ? "" : "  (" + Strings.Get("DefaultNotMeasured", "default, not measured") + ")")}"); col.Children.Add(result);
        var start = Btn(Strings.Get("Start", "Start"), null!, isDefault: true);
        var apply = Btn(Strings.Get("UseAsTarget", "Use as target"), null!); apply.IsEnabled = false;
        double measured = double.NaN;
        _vm = new VoiceMeasurement(main.Devices);
        _vm.Progress += (p, peak, live) => Dispatcher.BeginInvoke(() => { bar.Value = p; meter.Level = peak; if (!double.IsNaN(live)) result.Text = $"{Strings.Get("Measuring", "Measuring")}: {live:F1} dB"; });
        _vm.Completed += (db, quiet) => Dispatcher.BeginInvoke(() =>
        {
            start.IsEnabled = true;
            if (double.IsNaN(db)) { result.Text = Strings.Get("NothingMeasured", "Nothing measured – is the microphone muted or the wrong device selected?"); return; }
            measured = db; apply.IsEnabled = true;
            result.Text = $"{Strings.Get("Result", "Result")}: {db:F1} dB" + (quiet ? "   ⚠ " + Strings.Get("TooQuiet", "very quiet – raise the microphone level in Windows") : db < 74 ? "   ⚠ " + Strings.Get("BelowTarget", "below 74 dB; consider a higher mic level") : "   ✔");
        });
        _vm.Error += msg => Dispatcher.BeginInvoke(() => { result.Text = msg; start.IsEnabled = true; });
        start.Click += (_, _) => { start.IsEnabled = false; apply.IsEnabled = false; bar.Value = 0; _vm.Start(main.Prefs.MicrophoneDeviceIds.FirstOrDefault()); };
        apply.Click += (_, _) =>
        {
            main.Prefs.NormalizationTargetDb = measured; main.Prefs.VoiceMeasured = true;
            main.Engine.Normalizer.TargetLufs = Core.Loudness.EbuR128Meter.DbToLufs(measured);
            main.Status(Strings.Format("TargetSet", measured));
            Close();
        };
        col.Children.Add(ButtonRow(start, apply, Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        Content = col;
        Closed += (_, _) => _vm.Dispose();
    }
}

public sealed class TroubleshootingWindow : DialogBase
{
    public TroubleshootingWindow(MainViewModel vm) : base(Strings.Get("Troubleshooting", "Troubleshooting"), 520)
    {
        var col = Column();
        var r = vm.Engine.Routing;
        col.Children.Add(new TextBlock { Text = Strings.Get("Routing", "Routing"), Style = (Style)Application.Current.FindResource("Heading") });
        col.Children.Add(Text($"{(r.IsGood ? "✔" : "✖")} {r.Message}"));
        col.Children.Add(Text(Strings.Get("TroubleChecklist", "Checklist:\n• Install VB-CABLE (free) and press Refresh in Preferences > Devices.\n• In Discord/Teams/the game select 'CABLE Output' as the microphone and disable noise suppression / echo cancellation there.\n• Keep your real microphone selected under 'Use' so your voice is mixed in.\n• Windows privacy: allow desktop apps to use the microphone.\n• Hotkeys do nothing in a game? Run VoxPad with the same elevation as the game, or lower the game's elevation.")));
        col.Children.Add(new TextBlock { Text = Strings.Get("DiagnosticReport", "Diagnostic report"), Style = (Style)Application.Current.FindResource("Heading") });
        col.Children.Add(Text(Strings.Get("DiagnosticHint", "Creates a zip with the log, device list, routing state and preferences (device ids only, no license data) that you can attach to a bug report."), hint: true));
        var path = Text("", hint: true); col.Children.Add(path);
        col.Children.Add(ButtonRow(
            Btn(Strings.Get("CreateReport", "Create report"), (_, _) =>
            {
                try
                {
                    var p = DiagnosticReport.Create(vm.Devices, vm.Engine, vm.Prefs, vm.AutoKeys, vm.Hotkeys);
                    path.Text = p;
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
                }
                catch (Exception ex) { path.Text = ex.Message; }
            }, isDefault: true),
            Btn(Strings.Get("OpenDevices", "Devices…"), (_, _) => { Close(); vm.Dialogs.ShowPreferences("Devices"); }),
            Btn(Strings.Get("Close", "Close"), (_, _) => Close(), isCancel: true)));
        Content = col;
    }
}
