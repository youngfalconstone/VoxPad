using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VoxPad.App.Services;
using VoxPad.App.ViewModels;
using VoxPad.Audio.Devices;
using VoxPad.Core.Logging;
using VoxPad.Core.Models;
using VoxPad.Core.Settings;

namespace VoxPad.App.Views;

public partial class MainWindow : Window, IDialogService, INotifyPropertyChanged
{
    private const string SoundsFormat = "VoxPad.Sounds";
    private const string CategoryFormat = "VoxPad.Category";
    private const string TileFormat = "VoxPad.Tile";

    private readonly MainViewModel _vm;
    private TrayService? _tray;
    private Point _dragStart;
    private bool _dragArmed;
    private bool _showHidden;
    private double _iconSize = 12;
    private bool _exiting;

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool ShowHiddenCategories { get => _showHidden; set { _showHidden = value; PropertyChanged?.Invoke(this, new(nameof(ShowHiddenCategories))); } }
    public double IconSize { get => _iconSize; set { _iconSize = value; PropertyChanged?.Invoke(this, new(nameof(IconSize))); } }
    public double HotbarHeight => _vm.Prefs.HotbarHeight;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.Dialogs = this;
        vm.SelectionRequested += SelectItem;
        vm.SelectAllRequested += () => SoundList.SelectAll();
        vm.RecordingStartRequested += () => ShowRecorder(autoStart: true);
        vm.RecordingStopRequested += () => _recorderWindow?.StopFromHotkey();
        vm.TtsSpeakRequested += () => { if (_ttsWindow == null) ShowTts(); _ttsWindow?.SpeakCurrentToMic(); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.SpeakerMuted) || e.PropertyName == nameof(MainViewModel.SpeakerVolume)) UpdateMuteGlyph(); };
        Seek.SeekRequested += f => _vm.SeekFraction = f;
        SourceInitialized += (_, _) => RestorePlacement();
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        DragOver += Window_DragOver;
        Drop += Window_Drop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var p = _vm.Prefs;
        _tray = new TrayService(this, "VoxPad");
        _tray.PlayStopRequested += () => _vm.Stop();
        _tray.ExitRequested += ExitApplication;
        ShowCategoriesMenu.IsChecked = p.ShowCategories; ShowHotbarMenu.IsChecked = p.ShowHotbar; ShowToolbarMenu.IsChecked = p.ShowToolbar; ShowStatusBarMenu.IsChecked = p.ShowStatusBar;
        ApplyPanels();
        ApplyIconSize(p.CategoryIconSize);
        ApplyColumns();
        ApplyToolbarItems();
        BuildRecentMenu();
        BuildColumnsMenu();
        UpdateMuteGlyph();
        _vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(MainViewModel.WindowTitle)) BuildRecentMenu(); };
        SoundList.Focus();
    }

    // ------------------------------------------------------------------ window placement / tray

    private void RestorePlacement()
    {
        var p = _vm.Prefs;
        Width = Math.Max(MinWidth, p.WindowWidth); Height = Math.Max(MinHeight, p.WindowHeight);
        if (!double.IsNaN(p.WindowLeft) && !double.IsNaN(p.WindowTop))
        {
            var r = SystemParameters.VirtualScreenWidth;
            if (p.WindowLeft < r - 50 && p.WindowTop < SystemParameters.VirtualScreenHeight - 50) { Left = p.WindowLeft; Top = p.WindowTop; WindowStartupLocation = WindowStartupLocation.Manual; }
        }
        if (p.WindowMaximized) WindowState = WindowState.Maximized;
        CategoriesColumn.Width = new GridLength(Math.Max(0, p.CategoriesWidth));
    }

    private void SavePlacement()
    {
        var p = _vm.Prefs;
        p.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { p.WindowWidth = Width; p.WindowHeight = Height; p.WindowLeft = Left; p.WindowTop = Top; }
        if (CategoriesPanel.Visibility == Visibility.Visible) p.CategoriesWidth = CategoriesColumn.ActualWidth;
        p.ColumnWidths.Clear();
        foreach (var (name, col) in Columns()) p.ColumnWidths[name] = col.ActualWidth;
        p.ColumnOrder = SoundGrid.Columns.Select(c => Columns().First(x => x.col == c).name).ToList();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _vm.Prefs.MinimizeToTray) HideToTray();
    }

    public void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (_vm.Prefs.NotifyOnTrayMinimize) _tray?.Notify("VoxPad", Strings.Get("TrayHint", "VoxPad keeps running in the notification area. Double-click the icon to restore."));
    }

    public void RestoreFromTray() => _tray?.Restore();

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _vm.Prefs.CloseToTray) { e.Cancel = true; HideToTray(); return; }
        if (_vm.Prefs.ConfirmQuitWhilePlaying && _vm.IsPlaying && !Confirm("VoxPad", Strings.Get("ConfirmQuitPlaying", "A sound is playing. Quit anyway?"))) { e.Cancel = true; _exiting = false; return; }
        if (_vm.Doc.IsDirty && _vm.Doc.Path != null)
        {
            var r = MessageBox.Show(this, Strings.Get("SaveBeforeExit", "Save changes to the sound list before exiting?"), "VoxPad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; _exiting = false; return; }
            if (r == MessageBoxResult.Yes) { try { _vm.Doc.Save(); } catch (Exception ex) { Warn("VoxPad", ex.Message); } }
        }
        SavePlacement();
        _tray?.Dispose();
        App.Current.SaveEverything();
        Application.Current.Shutdown();
    }

    public void ExitApplication() { _exiting = true; Close(); }

    // ------------------------------------------------------------------ panels / toolbar / columns

    private void PanelToggle_Click(object sender, RoutedEventArgs e)
    {
        var p = _vm.Prefs;
        p.ShowCategories = ShowCategoriesMenu.IsChecked; p.ShowHotbar = ShowHotbarMenu.IsChecked; p.ShowToolbar = ShowToolbarMenu.IsChecked; p.ShowStatusBar = ShowStatusBarMenu.IsChecked;
        ShowHiddenCategories = ShowHiddenMenu.IsChecked;
        ApplyPanels();
    }

    private void ApplyPanels()
    {
        var p = _vm.Prefs;
        CategoriesPanel.Visibility = p.ShowCategories ? Visibility.Visible : Visibility.Collapsed;
        CategoriesSplitter.Visibility = CategoriesPanel.Visibility;
        if (p.ShowCategories) { CategoriesColumn.Width = new GridLength(Math.Max(120, p.CategoriesWidth)); SplitterColumn.Width = new GridLength(4); }
        else { if (CategoriesColumn.ActualWidth > 0) p.CategoriesWidth = CategoriesColumn.ActualWidth; CategoriesColumn.Width = new GridLength(0); SplitterColumn.Width = new GridLength(0); }
        HotbarPanel.Visibility = p.ShowHotbar ? Visibility.Visible : Visibility.Collapsed;
        ToolbarPanel.Visibility = p.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = p.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ApplyToolbarItems()
    {
        var items = _vm.Prefs.ToolbarItems;
        void V(FrameworkElement el, string key) => el.Visibility = items.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
        V(TbPlay, "Play"); V(TbPlaySpeakers, "PlaySpeakers"); V(TbPlayMic, "PlayMic"); V(TbPause, "Pause"); V(TbStop, "Stop"); V(TbPrevious, "Previous"); V(TbNext, "Next");
        V(TbTimer, "Timer"); V(TbSeek, "Seek"); V(TbVolume, "Volume"); V(TbMicLevel, "MicLevel");
    }

    private void IconSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string t } && int.TryParse(t, out var i)) { _vm.Prefs.CategoryIconSize = (IconSize)i; ApplyIconSize((IconSize)i); }
    }

    private void ApplyIconSize(IconSize s) => IconSize = s switch { Core.Models.IconSize.Medium => 16, Core.Models.IconSize.Large => 24, Core.Models.IconSize.ExtraLarge => 32, _ => 12 };

    private IEnumerable<(string name, GridViewColumn col)> Columns()
    {
        yield return ("Index", ColIndex); yield return ("Tag", ColTag); yield return ("Duration", ColDuration);
        yield return ("Hotkey", ColHotkey); yield return ("PlayCount", ColPlayCount); yield return ("LastPlayed", ColLastPlayed);
    }

    private readonly Dictionary<string, double> _hiddenWidths = new();

    private void ApplyColumns()
    {
        var p = _vm.Prefs;
        // Order
        var ordered = p.ColumnOrder.Where(n => Columns().Any(c => c.name == n)).Select(n => Columns().First(c => c.name == n).col).ToList();
        foreach (var (name, col) in Columns()) if (!ordered.Contains(col)) ordered.Add(col);
        SoundGrid.Columns.Clear();
        foreach (var col in ordered) SoundGrid.Columns.Add(col);
        foreach (var (name, col) in Columns())
        {
            if (p.ColumnWidths.TryGetValue(name, out var w) && w > 0) col.Width = w;
            if (!p.VisibleColumns.Contains(name)) { _hiddenWidths[name] = col.Width; col.Width = 0; }
        }
    }

    private void BuildColumnsMenu()
    {
        ColumnsMenu.Items.Clear(); HeaderMenu.Items.Clear();
        foreach (var (name, col) in Columns())
        {
            foreach (var menu in new System.Windows.Controls.ItemsControl[] { ColumnsMenu, HeaderMenu })
            {
                var mi = new MenuItem { Header = col.Header is string h ? h : Strings.Get("Col" + name, name), IsCheckable = true, IsChecked = _vm.Prefs.VisibleColumns.Contains(name), Tag = name };
                mi.Click += (_, _) => ToggleColumn(name);
                menu.Items.Add(mi);
            }
        }
    }

    private void ToggleColumn(string name)
    {
        var p = _vm.Prefs;
        var col = Columns().First(c => c.name == name).col;
        if (p.VisibleColumns.Contains(name)) { p.VisibleColumns.Remove(name); _hiddenWidths[name] = col.ActualWidth; col.Width = 0; }
        else { p.VisibleColumns.Add(name); col.Width = _hiddenWidths.TryGetValue(name, out var w) && w > 10 ? w : 100; }
        BuildColumnsMenu();
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader h || h.Column == null || h.Role == GridViewColumnHeaderRole.Padding) return;
        var name = Columns().FirstOrDefault(c => c.col == h.Column).name;
        if (name != null) _vm.SortBy(name);
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        foreach (var r in _vm.Prefs.RecentLists)
        {
            var mi = new MenuItem { Header = r.Replace("_", "__") };
            mi.Click += (_, _) => _vm.LoadListFrom(r);
            RecentMenu.Items.Add(mi);
        }
        RecentMenu.IsEnabled = RecentMenu.Items.Count > 0;
    }

    private void HotbarSize_Click(object sender, RoutedEventArgs e) => new HotbarSizeDialog(_vm) { Owner = this }.ShowDialog();

    // ------------------------------------------------------------------ toolbar handlers

    private void Timer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && _vm.PlayingItem != null) { SelectItem(_vm.PlayingItem); }
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => _vm.SpeakerMuted = !_vm.SpeakerMuted;
    private void UpdateMuteGlyph() => MuteButton.Content = _vm.SpeakerMuted || _vm.SpeakerVolume <= 0 ? "🔇" : _vm.SpeakerVolume < 0.5 ? "🔉" : "🔊";
    private void MicMeter_Click(object sender, RoutedEventArgs e) => new VoiceMeasureDialog(_vm) { Owner = this }.ShowDialog();

    private void PlayModeIndicator_Click(object sender, MouseButtonEventArgs e) => _vm.PlayMode = _vm.PlayMode.Next();
    private void HotkeysIndicator_Click(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) _vm.HotkeysEnabled = !_vm.HotkeysEnabled; }
    private void AutoKeysIndicator_Click(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) _vm.AutoKeysEnabled = !_vm.AutoKeysEnabled; }
    private void RepeatIndicator_Click(object sender, MouseButtonEventArgs e) => _vm.Repeat = !_vm.Repeat;
    private void ContinueIndicator_Click(object sender, MouseButtonEventArgs e) => _vm.Continue = !_vm.Continue;

    // ------------------------------------------------------------------ sound list

    private void SoundList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SelectedSounds.Clear();
        foreach (var i in SoundList.SelectedItems.OfType<SoundItemViewModel>()) _vm.SelectedSounds.Add(i);
        RelayCommand.Requery();
    }

    private void SelectItem(SoundItemViewModel item)
    {
        SoundList.SelectedItems.Clear();
        SoundList.SelectedItem = item;
        SoundList.ScrollIntoView(item);
        if (SoundList.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem c) c.Focus();
    }

    private void SoundList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt<ListViewItem>(e.OriginalSource) is { DataContext: SoundItemViewModel item })
        {
            // Double-click on the Hotkey column edits the hotkey; elsewhere plays.
            var col = ColumnAt(e.GetPosition(SoundList));
            if (col == ColHotkey) _vm.SetHotkeyCommand.Execute(item);
            else _vm.PlaySound(item.Entry, null, Audio.Engine.PlayTrigger.UserInterface);
        }
    }

    private GridViewColumn? ColumnAt(Point p)
    {
        double x = p.X - 2 + (SoundList.Template.FindName("PART_ScrollViewer", SoundList) is ScrollViewer sv ? sv.HorizontalOffset : 0);
        foreach (var c in SoundGrid.Columns) { if (x < c.ActualWidth) return c; x -= c.ActualWidth; }
        return null;
    }

    private void SoundList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        switch (e.Key)
        {
            case Key.Enter when ctrl: _vm.PlayMicCommand.Execute(null); e.Handled = true; break;
            case Key.Enter: _vm.PlayCommand.Execute(null); e.Handled = true; break;
            case Key.Space: _vm.PauseCommand.Execute(null); e.Handled = true; break;
            case Key.Delete: _vm.RemoveCommand.Execute(null); e.Handled = true; break;
            case Key.C when ctrl: _vm.CopyCommand.Execute(null); e.Handled = true; break;
            case Key.X when ctrl: _vm.CutCommand.Execute(null); e.Handled = true; break;
            case Key.V when ctrl: _vm.PasteCommand.Execute(null); e.Handled = true; break;
            case Key.Up when alt: _vm.MoveUpCommand.Execute(null); e.Handled = true; break;
            case Key.Down when alt: _vm.MoveDownCommand.Execute(null); e.Handled = true; break;
            case Key.F2: _vm.SetHotkeyCommand.Execute(null); e.Handled = true; break;
            case Key.Escape: _vm.SearchText = ""; e.Handled = true; break;
        }
    }

    /// <summary>Instant search: typing while the list has focus goes to the search box.</summary>
    private void SoundList_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == " " || char.IsControl(e.Text[0])) return;
        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Control): _vm.PlaySelected(null); e.Handled = true; break;
            case Key.Enter: _vm.SearchCycle(); e.Handled = true; break;
            case Key.Escape: _vm.SearchText = ""; SoundList.Focus(); e.Handled = true; break;
            case Key.Down: SoundList.Focus(); e.Handled = true; break;
        }
    }

    private void SoundList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // "Move to category" submenu is built dynamically.
        if (SoundList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "MoveToCategoryMenu") is not { } mi)
        {
            mi = (SoundList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header is string h && h == Strings.Get("MoveToCategory", "Move to category")))!;
        }
        if (mi == null) return;
        mi.Items.Clear();
        foreach (var c in _vm.CategoryRoots.SelectMany(r => r.Flatten()).Where(c => !c.IsAllSounds))
        {
            var item = new MenuItem { Header = c.Name.Replace("_", "__"), Command = _vm.MoveToCategoryCommand, CommandParameter = c };
            mi.Items.Add(item);
        }
        mi.IsEnabled = mi.Items.Count > 0;
    }

    // ------------------------------------------------------------------ drag & drop

    private static T? ItemAt<T>(object source) where T : DependencyObject
    {
        var d = source as DependencyObject;
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private void SoundList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragArmed = ItemAt<ListViewItem>(e.OriginalSource) != null && ItemAt<GridViewColumnHeader>(e.OriginalSource) == null;
    }

    private void SoundList_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var ids = SoundList.SelectedItems.OfType<SoundItemViewModel>().Select(s => s.Id).ToArray();
        if (ids.Length == 0) return;
        _dragArmed = false;
        var data = new DataObject(SoundsFormat, ids);
        DragDrop.DoDragDrop(SoundList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void SoundList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SoundsFormat) ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SoundList_Drop(object sender, DragEventArgs e)
    {
        var target = ItemAt<ListViewItem>(e.OriginalSource)?.DataContext as SoundItemViewModel;
        if (e.Data.GetData(SoundsFormat) is Guid[] ids)
        {
            _vm.ReorderInCurrentCategory(ids, target?.Id);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            int? at = target == null ? null : _vm.CurrentCategory.SoundIds.IndexOf(target.Id);
            _vm.ImportPaths(files, null, at);
        }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Handled && e.Data.GetData(DataFormats.FileDrop) is string[] files) { _vm.ImportPaths(files); e.Handled = true; }
    }

    // categories tree
    private CategoryViewModel? _treeDragItem;

    private void CategoryTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _treeDragItem = ItemAt<TreeViewItem>(e.OriginalSource)?.DataContext as CategoryViewModel;
    }

    private void CategoryTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt<TreeViewItem>(e.OriginalSource) is { } tvi) { tvi.IsSelected = true; tvi.Focus(); }
    }

    private void CategoryTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragItem == null || _treeDragItem.IsAllSounds || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _treeDragItem; _treeDragItem = null;
        DragDrop.DoDragDrop(CategoryTree, new DataObject(CategoryFormat, item.Id), DragDropEffects.Move);
    }

    private void CategoryTree_DragOver(object sender, DragEventArgs e)
    {
        var over = ItemAt<TreeViewItem>(e.OriginalSource)?.DataContext as CategoryViewModel;
        if (e.Data.GetDataPresent(SoundsFormat) || e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = over != null && !over.IsAllSounds ? DragDropEffects.Copy : DragDropEffects.None;
        else if (e.Data.GetDataPresent(CategoryFormat)) e.Effects = DragDropEffects.Move;
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void CategoryTree_Drop(object sender, DragEventArgs e)
    {
        var tvi = ItemAt<TreeViewItem>(e.OriginalSource);
        var over = tvi?.DataContext as CategoryViewModel;
        if (e.Data.GetData(SoundsFormat) is Guid[] ids && over != null && !over.IsAllSounds)
            _vm.AddToCategory(ids, over.Model, move: (e.KeyStates & DragDropKeyStates.ShiftKey) != 0);
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && over != null && !over.IsAllSounds)
            _vm.ImportPaths(files, over.Model);
        else if (e.Data.GetData(CategoryFormat) is Guid movedId)
        {
            var moved = _vm.Doc.List.FindCategory(movedId);
            if (moved == null) return;
            if (over == null || over.IsAllSounds) _vm.MoveCategory(moved, null, -1);
            else
            {
                // Drop on the upper half of an item = place before it (same parent); lower half = nest inside.
                var pos = e.GetPosition(tvi);
                var header = tvi!.Template.FindName("PART_Header", tvi) as FrameworkElement;
                double h = header?.ActualHeight ?? 20;
                if (pos.Y < h / 2)
                {
                    var parent = _vm.Doc.List.ParentOf(over.Model);
                    var siblings = parent?.Children ?? _vm.Doc.List.Categories;
                    _vm.MoveCategory(moved, parent, siblings.IndexOf(over.Model));
                }
                else _vm.MoveCategory(moved, over.Model, -1);
            }
        }
        e.Handled = true;
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CategoryViewModel c)
        {
            _vm.SelectedCategory = c;
            HideCategoryCheck.IsChecked = c.IsHidden;
        }
    }

    private void HideCategoryCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCategory is { IsAllSounds: false } c && c.IsHidden != HideCategoryCheck.IsChecked) _vm.HideCategoryCommand.Execute(c);
    }

    // hotbar tiles
    private HotbarTileViewModel? _tileDrag;

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HotbarTileViewModel t })
        {
            _dragStart = e.GetPosition(null);
            _tileDrag = t.IsEmpty ? null : t;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _vm.HotbarPlay(t);
        }
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tileDrag == null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var t = _tileDrag; _tileDrag = null;
        var data = new DataObject(TileFormat, new[] { t.Page, t.Slot });
        data.SetData(SoundsFormat, new[] { t.Sound!.Id });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
    }

    private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HotbarTileViewModel t }) return;
        var menu = new ContextMenu();
        if (!t.IsEmpty)
        {
            menu.Items.Add(new MenuItem { Header = Strings.Get("SetHotkey", "Set hotkey"), Command = new RelayCommand(() => _vm.HotbarSetHotkey(t)) });
            menu.Items.Add(new MenuItem { Header = Strings.Get("RemoveHotkey", "Remove hotkey"), Command = new RelayCommand(() => { if (t.Sound != null) _vm.AssignHotkey(t.Sound, null); }) });
            menu.Items.Add(new MenuItem { Header = Strings.Get("SetColor", "Colour…"), Command = new RelayCommand(() => _vm.HotbarSetColor(t)) });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = Strings.Get("RemoveFromHotbar", "Remove from hotbar"), Command = new RelayCommand(() => _vm.HotbarClear(t.Page, t.Slot)) });
        }
        else menu.Items.Add(new MenuItem { Header = Strings.Get("EmptyTileHint", "Drag a sound here"), IsEnabled = false });
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Tile_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SoundsFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HotbarTileViewModel target }) return;
        if (e.Data.GetData(TileFormat) is int[] from)
        {
            bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            _vm.HotbarMove(from[0], from[1], target.Page, target.Slot, keepColor: ctrl);
        }
        else if (e.Data.GetData(SoundsFormat) is Guid[] ids && ids.Length > 0)
        {
            // Several sounds fill consecutive slots.
            int slot = target.Slot;
            foreach (var id in ids)
            {
                if (slot >= _vm.Hotbar.Columns * _vm.Hotbar.Rows) break;
                _vm.HotbarAssign(target.Page, slot++, id);
            }
        }
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control) { _vm.Hotbar.NextPage(); e.Handled = true; }
        else if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control) { _vm.Hotbar.PreviousPage(); e.Handled = true; }
    }

    // ------------------------------------------------------------------ IDialogService

    public (bool ok, Hotkey? hotkey) CaptureHotkey(string title, Hotkey? current)
    {
        var dlg = new HotkeyCaptureDialog(_vm.Hotkeys, title, current) { Owner = this };
        dlg.ShowDialog();
        return (dlg.Accepted, dlg.Result);
    }

    public string? AskText(string title, string prompt, string initial) => InputDialog.Show(this, title, prompt, initial);
    public uint? PickColor(uint current) => ColorPickerDialog.Show(this, current);
    public bool Confirm(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public void Info(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void Warn(string title, string text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public string[] OpenFiles(string filter, bool multi)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter, Multiselect = multi, InitialDirectory = string.IsNullOrEmpty(_vm.Prefs.LastImportDirectory) ? null : _vm.Prefs.LastImportDirectory };
        if (dlg.ShowDialog(this) != true) return Array.Empty<string>();
        _vm.Prefs.LastImportDirectory = System.IO.Path.GetDirectoryName(dlg.FileName) ?? "";
        return dlg.FileNames;
    }

    public string? SaveFile(string filter, string? suggestedName, string? defaultExt)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = suggestedName ?? "", DefaultExt = defaultExt ?? "" };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false };
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    private RecorderWindow? _recorderWindow;
    private TtsWindow? _ttsWindow;

    public void ShowPreferences(string? tab = null)
    {
        var w = new PreferencesWindow(_vm, tab) { Owner = this };
        w.ShowDialog();
        ApplyToolbarItems(); ApplyPanels(); ApplyIconSize(_vm.Prefs.CategoryIconSize);
    }

    public void ShowRecorder() => ShowRecorder(false);
    private void ShowRecorder(bool autoStart)
    {
        if (_recorderWindow == null)
        {
            _recorderWindow = new RecorderWindow(_vm) { Owner = this };
            _recorderWindow.Closed += (_, _) => _recorderWindow = null;
            _recorderWindow.Show();
        }
        else _recorderWindow.Activate();
        if (autoStart) _recorderWindow.StartFromHotkey();
    }

    public void ShowEditor(SoundEntry entry) => new EditorWindow(_vm, entry) { Owner = this }.ShowDialog();

    public void ShowTts()
    {
        if (_ttsWindow == null)
        {
            _ttsWindow = new TtsWindow(_vm) { Owner = this };
            _ttsWindow.Closed += (_, _) => _ttsWindow = null;
            _ttsWindow.Show();
        }
        else _ttsWindow.Activate();
    }

    public void ShowHotkeyOverview() => new HotkeyOverviewWindow(_vm) { Owner = this }.Show();
    public void ShowAutoKeysTroubleshooting() => new AutoKeysTroubleshootingWindow(_vm) { Owner = this }.ShowDialog();
    public void ShowAbout() => new AboutWindow(_vm) { Owner = this }.ShowDialog();
    public void ShowRegister() => new RegisterWindow(_vm) { Owner = this }.ShowDialog();
    public void ShowStats() => new StatsWindow(_vm) { Owner = this }.ShowDialog();
    public void ShowRestore() => new RestoreListDialog(_vm) { Owner = this }.ShowDialog();
    public void ShowWebImport() => new WebImportDialog(_vm) { Owner = this }.ShowDialog();
    public void ShowUpdate(bool manual) => new UpdateWindow(_vm, manual) { Owner = this }.Show();
    public void ShowTroubleshooting() => new TroubleshootingWindow(_vm) { Owner = this }.ShowDialog();

    public void ShowManual()
    {
        var local = System.IO.Path.Combine(AppContext.BaseDirectory, "docs", "manual.html");
        try
        {
            if (File.Exists(local)) Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("https://github.com/youngfalconstone/VoxPad#readme") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("manual: " + ex.Message); }
    }

    public void ShowMicAccessDenied(DeviceManager.MicPrivacy privacy)
    {
        var text = Strings.Get("MicAccessDenied", "Windows blocks microphone access for desktop apps, so VoxPad cannot read your microphone.\n\nFix: Settings > Privacy & security > Microphone > turn on \"Microphone access\" and \"Let desktop apps access your microphone\", then press Refresh in Preferences > Devices.");
        if (MessageBox.Show(this, text + "\n\n" + Strings.Get("OpenSettingsQuestion", "Open the privacy settings now?"), "VoxPad – " + Strings.Get("AccessDenied", "Access denied"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); } catch { }
        }
    }
}
