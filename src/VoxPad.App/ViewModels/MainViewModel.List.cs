using System.Diagnostics;
using System.Windows.Input;
using VoxPad.App.Services;
using VoxPad.Audio.Decoding;
using VoxPad.Audio.Engine;
using VoxPad.Core;
using LicenseManager = VoxPad.Core.Licensing.LicenseManager;
using VoxPad.Core.Logging;
using VoxPad.Core.Models;
using VoxPad.Core.Serialization;
using VoxPad.Core.Storage;

namespace VoxPad.App.ViewModels;

public sealed partial class MainViewModel
{
    private CategoryViewModel? _selectedCategory;
    private string _searchText = "";
    private List<SoundEntry> _clipboard = new();
    private bool _clipboardCut;

    // ------------------------------------------------------------------ document → view models

    private void RebuildFromDocument()
    {
        var selectedId = _selectedCategory?.Id;
        CategoryRoots.Clear();
        Doc.AllSounds.SoundIds = Doc.List.AllSoundsOrder; // the hidden category shares the master order
        AllSoundsVm = new CategoryViewModel(Doc.AllSounds, isAllSounds: true);
        CategoryRoots.Add(AllSoundsVm);
        foreach (var c in Doc.List.Categories) CategoryRoots.Add(new CategoryViewModel(c));
        var sel = selectedId == null ? null : CategoryRoots.SelectMany(c => c.Flatten()).FirstOrDefault(c => c.Id == selectedId);
        _selectedCategory = sel ?? AllSoundsVm;
        _selectedCategory.IsSelected = true;
        Raise(nameof(SelectedCategory));
        RebuildSounds();
        Hotbar.Attach(Doc.List);
        RebuildBindings();
        Raise(nameof(WindowTitle));
        RefreshLicenseState();
        if (Prefs.NotifyMissingFiles && Doc.MissingCount > 0) Status(Strings.Format("MissingFiles", Doc.MissingCount), true);
        RelayCommand.Requery();
    }

    public CategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) { RebuildSounds(); Raise(nameof(CategoryInfo)); Raise(nameof(IsAllSoundsSelected)); } }
    }

    public Category CurrentCategory => _selectedCategory?.Model ?? Doc.AllSounds;
    public bool IsAllSoundsSelected => _selectedCategory == null || _selectedCategory.IsAllSounds;

    public string CategoryInfo => _selectedCategory == null ? "" :
        Strings.Format("CategoryInfo", _selectedCategory.Name, _selectedCategory.Count, SoundItemViewModel.Format(TimeSpan.FromMilliseconds(CurrentCategory.SoundIds.Select(id => Doc.List.Find(id)?.DurationMs ?? 0).Sum())));

    private void RebuildSounds()
    {
        var playingId = Engine.Current?.Sound?.Id;
        Sounds.Clear();
        int i = 0;
        foreach (var id in CurrentCategory.SoundIds)
        {
            var e = Doc.List.Find(id);
            if (e == null) continue;
            Sounds.Add(new SoundItemViewModel(e, ++i) { IsPlaying = e.Id == playingId });
        }
        ApplySort();
        SoundsView.Refresh();
        Raise(nameof(CategoryInfo));
        UpdatePlaybackState();
    }

    public void ApplySort()
    {
        if (SoundsView is System.Windows.Data.ListCollectionView lcv)
        {
            lcv.CustomSort = string.IsNullOrEmpty(Prefs.SortColumn) || Prefs.DisableSorting ? null : new RowComparer(Prefs.SortColumn, Prefs.SortDescending);
        }
    }

    private sealed class RowComparer : System.Collections.IComparer
    {
        private readonly string _col; private readonly bool _desc;
        public RowComparer(string col, bool desc) { _col = col; _desc = desc; }
        public int Compare(object? x, object? y)
        {
            var a = (SoundItemViewModel)x!; var b = (SoundItemViewModel)y!;
            int c = _col switch
            {
                "Tag" => NaturalStringComparer.Instance.Compare(a.Tag, b.Tag),
                "Duration" => a.Duration.CompareTo(b.Duration),
                "Hotkey" => NaturalStringComparer.Instance.Compare(a.HotkeyText, b.HotkeyText),
                "PlayCount" => a.PlayCount.CompareTo(b.PlayCount),
                "LastPlayed" => Nullable.Compare(a.LastPlayed, b.LastPlayed),
                _ => a.Index.CompareTo(b.Index),
            };
            if (c == 0) c = a.Index.CompareTo(b.Index);
            return _desc ? -c : c;
        }
    }

    public void SortBy(string column)
    {
        if (Prefs.DisableSorting) return;
        if (Prefs.SortColumn == column) { if (!Prefs.SortDescending) Prefs.SortDescending = true; else { Prefs.SortColumn = ""; Prefs.SortDescending = false; } }
        else { Prefs.SortColumn = column; Prefs.SortDescending = false; }
        ApplySort();
    }

    // ------------------------------------------------------------------ search

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) { SoundsView.Refresh(); SearchCycle(); } }
    }

    private bool FilterSound(object o) => string.IsNullOrEmpty(_searchText) || o is SoundItemViewModel s && s.Tag.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Enter in the search box: select the next match.</summary>
    public void SearchCycle()
    {
        var rows = SoundsView.Cast<SoundItemViewModel>().ToList();
        if (rows.Count == 0) return;
        var cur = PrimarySelection;
        int i = cur == null ? -1 : rows.IndexOf(cur);
        SelectOnly(rows[(i + 1) % rows.Count]);
    }

    // ------------------------------------------------------------------ commands

    public ICommand PlayCommand { get; private set; } = null!;
    public ICommand PlaySpeakersCommand { get; private set; } = null!;
    public ICommand PlayMicCommand { get; private set; } = null!;
    public ICommand PauseCommand { get; private set; } = null!;
    public ICommand StopCommand { get; private set; } = null!;
    public ICommand PreviousCommand { get; private set; } = null!;
    public ICommand NextCommand { get; private set; } = null!;
    public ICommand PlayRandomCommand { get; private set; } = null!;
    public ICommand CyclePlayModeCommand { get; private set; } = null!;
    public ICommand SetPlayModeCommand { get; private set; } = null!;
    public ICommand NewListCommand { get; private set; } = null!;
    public ICommand LoadListCommand { get; private set; } = null!;
    public ICommand SaveListCommand { get; private set; } = null!;
    public ICommand SaveListAsCommand { get; private set; } = null!;
    public ICommand OpenRecentCommand { get; private set; } = null!;
    public ICommand RestoreListCommand { get; private set; } = null!;
    public ICommand ExportSoundFilesCommand { get; private set; } = null!;
    public ICommand ExportPreferencesCommand { get; private set; } = null!;
    public ICommand ImportPreferencesCommand { get; private set; } = null!;
    public ICommand AddFilesCommand { get; private set; } = null!;
    public ICommand AddFolderCommand { get; private set; } = null!;
    public ICommand WebImportCommand { get; private set; } = null!;
    public ICommand StatsCommand { get; private set; } = null!;
    public ICommand PreferencesCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;
    public ICommand SelectAllCommand { get; private set; } = null!;
    public ICommand RemoveCommand { get; private set; } = null!;
    public ICommand CopyCommand { get; private set; } = null!;
    public ICommand CutCommand { get; private set; } = null!;
    public ICommand PasteCommand { get; private set; } = null!;
    public ICommand MoveUpCommand { get; private set; } = null!;
    public ICommand MoveDownCommand { get; private set; } = null!;
    public ICommand UndoCommand { get; private set; } = null!;
    public ICommand RedoCommand { get; private set; } = null!;
    public ICommand EditFileCommand { get; private set; } = null!;
    public ICommand SetHotkeyCommand { get; private set; } = null!;
    public ICommand RemoveHotkeyCommand { get; private set; } = null!;
    public ICommand SetColorCommand { get; private set; } = null!;
    public ICommand ResetPlayCountCommand { get; private set; } = null!;
    public ICommand ResetAllPlayCountsCommand { get; private set; } = null!;
    public ICommand SelectInExplorerCommand { get; private set; } = null!;
    public ICommand RecorderCommand { get; private set; } = null!;
    public ICommand TtsCommand { get; private set; } = null!;
    public ICommand HotkeyOverviewCommand { get; private set; } = null!;
    public ICommand AutoKeysTroubleshootingCommand { get; private set; } = null!;
    public ICommand AboutCommand { get; private set; } = null!;
    public ICommand RegisterCommand { get; private set; } = null!;
    public ICommand ManualCommand { get; private set; } = null!;
    public ICommand TroubleshootingCommand { get; private set; } = null!;
    public ICommand CheckUpdateCommand { get; private set; } = null!;
    public ICommand AddCategoryCommand { get; private set; } = null!;
    public ICommand RenameCategoryCommand { get; private set; } = null!;
    public ICommand RemoveCategoryCommand { get; private set; } = null!;
    public ICommand CategoryHotkeyCommand { get; private set; } = null!;
    public ICommand CategoryColorCommand { get; private set; } = null!;
    public ICommand HideCategoryCommand { get; private set; } = null!;
    public ICommand MoveToCategoryCommand { get; private set; } = null!;
    public ICommand RemoveFromCategoryCommand { get; private set; } = null!;
    public ICommand HotbarNextPageCommand { get; private set; } = null!;
    public ICommand HotbarPrevPageCommand { get; private set; } = null!;
    public ICommand JumpCommand { get; private set; } = null!;

    private void CreateCommands()
    {
        bool HasSel() => SelectedSounds.Count > 0;
        PlayCommand = new RelayCommand(() => PlaySelected(null));
        PlaySpeakersCommand = new RelayCommand(() => PlaySelected(PlayTarget.Speakers));
        PlayMicCommand = new RelayCommand(() => PlaySelected(PlayTarget.Microphone));
        PauseCommand = new RelayCommand(TogglePause, () => Engine.IsLoaded);
        StopCommand = new RelayCommand(Stop, () => Engine.IsLoaded);
        PreviousCommand = new RelayCommand(() => PlayPrevious());
        NextCommand = new RelayCommand(() => PlayNext());
        PlayRandomCommand = new RelayCommand(() => PlayRandom(CurrentCategory, PlayTrigger.UserInterface));
        CyclePlayModeCommand = new RelayCommand(() => PlayMode = PlayMode.Next());
        SetPlayModeCommand = new RelayCommand(p => { if (p is PlayMode m) PlayMode = m; else if (p is string s && Enum.TryParse<PlayMode>(s, out var pm)) PlayMode = pm; });
        JumpCommand = new RelayCommand(p => Engine.Jump(p is string s && int.TryParse(s, out var n) ? n : Prefs.JumpSeconds), _ => Engine.IsLoaded);

        NewListCommand = new RelayCommand(NewList);
        LoadListCommand = new RelayCommand(LoadList);
        SaveListCommand = new RelayCommand(SaveList);
        SaveListAsCommand = new RelayCommand(SaveListAs);
        OpenRecentCommand = new RelayCommand(p => { if (p is string path) LoadListFrom(path); });
        RestoreListCommand = new RelayCommand(() => Dialogs.ShowRestore());
        ExportSoundFilesCommand = new RelayCommand(ExportSoundFiles);
        ExportPreferencesCommand = new RelayCommand(ExportPreferences);
        ImportPreferencesCommand = new RelayCommand(ImportPreferences);
        AddFilesCommand = new RelayCommand(() => ImportPaths(Dialogs.OpenFiles(AudioFileDecoder.FileDialogFilter, true)));
        AddFolderCommand = new RelayCommand(() => { var f = Dialogs.PickFolder(Strings.Get("AddFolder", "Add folder")); if (f != null) ImportPaths(new[] { f }); });
        WebImportCommand = new RelayCommand(() => Dialogs.ShowWebImport());
        StatsCommand = new RelayCommand(() => Dialogs.ShowStats());
        PreferencesCommand = new RelayCommand(p => Dialogs.ShowPreferences(p as string));
        ExitCommand = new RelayCommand(() => Dialogs.ExitApplication());

        SelectAllCommand = new RelayCommand(() => SelectAllRequested?.Invoke());
        RemoveCommand = new RelayCommand(RemoveSelected, HasSel);
        CopyCommand = new RelayCommand(() => { _clipboard = SelectedSounds.Select(s => s.Entry).ToList(); _clipboardCut = false; Status(Strings.Format("Copied", _clipboard.Count)); }, HasSel);
        CutCommand = new RelayCommand(() => { _clipboard = SelectedSounds.Select(s => s.Entry).ToList(); _clipboardCut = true; Status(Strings.Format("CutStatus", _clipboard.Count)); }, HasSel);
        PasteCommand = new RelayCommand(Paste, () => _clipboard.Count > 0);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => HasSel() && !IsSorted);
        MoveDownCommand = new RelayCommand(() => MoveSelected(+1), () => HasSel() && !IsSorted);
        UndoCommand = new RelayCommand(() => Doc.Undo(), () => Doc.CanUndo);
        RedoCommand = new RelayCommand(() => Doc.Redo(), () => Doc.CanRedo);
        EditFileCommand = new RelayCommand(() => { var s = PrimarySelection; if (s != null) Dialogs.ShowEditor(s.Entry); }, HasSel);
        SetHotkeyCommand = new RelayCommand(p => SetHotkeyFor((p as SoundItemViewModel) ?? PrimarySelection), _ => HasSel());
        RemoveHotkeyCommand = new RelayCommand(() => { foreach (var s in SelectedSounds.ToList()) AssignHotkey(s.Entry, null); }, HasSel);
        SetColorCommand = new RelayCommand(SetColor, HasSel);
        ResetPlayCountCommand = new RelayCommand(ResetPlayCount, HasSel);
        ResetAllPlayCountsCommand = new RelayCommand(ResetAllPlayCounts);
        SelectInExplorerCommand = new RelayCommand(SelectInExplorer, HasSel);

        RecorderCommand = new RelayCommand(() => Dialogs.ShowRecorder());
        TtsCommand = new RelayCommand(() => Dialogs.ShowTts());
        HotkeyOverviewCommand = new RelayCommand(() => Dialogs.ShowHotkeyOverview());
        AutoKeysTroubleshootingCommand = new RelayCommand(() => Dialogs.ShowAutoKeysTroubleshooting());
        AboutCommand = new RelayCommand(() => Dialogs.ShowAbout());
        RegisterCommand = new RelayCommand(() => Dialogs.ShowRegister());
        ManualCommand = new RelayCommand(() => Dialogs.ShowManual());
        TroubleshootingCommand = new RelayCommand(() => Dialogs.ShowTroubleshooting());
        CheckUpdateCommand = new RelayCommand(() => Dialogs.ShowUpdate(true));

        AddCategoryCommand = new RelayCommand(p => AddCategory(p as CategoryViewModel));
        RenameCategoryCommand = new RelayCommand(p => RenameCategory(p as CategoryViewModel ?? _selectedCategory));
        RemoveCategoryCommand = new RelayCommand(p => RemoveCategory(p as CategoryViewModel ?? _selectedCategory));
        CategoryHotkeyCommand = new RelayCommand(p => SetCategoryHotkey(p as CategoryViewModel ?? _selectedCategory));
        CategoryColorCommand = new RelayCommand(p => SetCategoryColor(p as CategoryViewModel ?? _selectedCategory));
        HideCategoryCommand = new RelayCommand(p => ToggleHideCategory(p as CategoryViewModel ?? _selectedCategory));
        MoveToCategoryCommand = new RelayCommand(p => { if (p is CategoryViewModel c) AddSelectedToCategory(c.Model, move: true); });
        RemoveFromCategoryCommand = new RelayCommand(RemoveSelectedFromCategory, () => HasSel() && !IsAllSoundsSelected);
        HotbarNextPageCommand = new RelayCommand(Hotbar.NextPage);
        HotbarPrevPageCommand = new RelayCommand(Hotbar.PreviousPage);
    }

    public event Action? SelectAllRequested;
    public bool IsSorted => !string.IsNullOrEmpty(Prefs.SortColumn) && !Prefs.DisableSorting;

    // ------------------------------------------------------------------ file menu

    private bool ConfirmDiscard()
    {
        if (!Doc.IsDirty) return true;
        return Dialogs.Confirm(Strings.Get("UnsavedTitle", "Unsaved changes"), Strings.Get("UnsavedText", "The sound list has unsaved changes. Discard them?"));
    }

    private void NewList() { if (ConfirmDiscard()) { Stop(); Doc.New(); Status(Strings.Get("NewListCreated", "New sound list")); } }

    private void LoadList()
    {
        if (!ConfirmDiscard()) return;
        var f = Dialogs.OpenFiles($"Sound lists|*{SoundListFile.Extension};*{SoundListFile.SoundpadExtension}|VoxPad list|*{SoundListFile.Extension}|Soundpad list|*{SoundListFile.SoundpadExtension}", false);
        if (f.Length == 1) LoadListFrom(f[0]);
    }

    public void LoadListFrom(string path)
    {
        try { Stop(); Doc.Load(path); Status(Strings.Format("ListLoaded", path)); }
        catch (Exception ex) { Log.Error("Load list", ex); Dialogs.Warn("VoxPad", Strings.Format("ListLoadFailed", ex.Message)); Prefs.RecentLists.Remove(path); }
    }

    private void SaveList()
    {
        if (Doc.Path == null) { SaveListAs(); return; }
        try { Doc.Save(); Status(Strings.Format("ListSaved", Doc.Path)); }
        catch (Exception ex) { Dialogs.Warn("VoxPad", ex.Message); }
    }

    private void SaveListAs()
    {
        var p = Dialogs.SaveFile($"VoxPad list|*{SoundListFile.Extension}", Doc.Path != null ? Path.GetFileName(Doc.Path) : "soundlist" + SoundListFile.Extension, SoundListFile.Extension);
        if (p == null) return;
        try { Doc.SaveAs(p); Status(Strings.Format("ListSaved", p)); }
        catch (Exception ex) { Dialogs.Warn("VoxPad", ex.Message); }
    }

    private void ExportSoundFiles()
    {
        var folder = Dialogs.PickFolder(Strings.Get("ExportSoundFiles", "Export sound files"));
        if (folder == null) return;
        try
        {
            var (ok, bad) = Doc.ExportSoundFiles(folder);
            Status(Strings.Format("Exported", ok, bad, folder), bad > 0);
        }
        catch (Exception ex) { Dialogs.Warn("VoxPad", ex.Message); }
    }

    private void ExportPreferences()
    {
        var p = Dialogs.SaveFile("VoxPad preferences|*.vpprefs", "voxpad-preferences.vpprefs", ".vpprefs");
        if (p == null) return;
        try { Prefs.Export(p); Status(Strings.Format("PrefsExported", p)); } catch (Exception ex) { Dialogs.Warn("VoxPad", ex.Message); }
    }

    private void ImportPreferences()
    {
        var f = Dialogs.OpenFiles("VoxPad preferences|*.vpprefs|All files|*.*", false);
        if (f.Length != 1) return;
        try
        {
            var imported = Core.Settings.Preferences.Import(f[0]);
            imported.Save();
            Dialogs.Info("VoxPad", Strings.Get("PrefsImportedRestart", "Preferences imported. Restart VoxPad to apply them."));
        }
        catch (Exception ex) { Dialogs.Warn("VoxPad", ex.Message); }
    }

    // ------------------------------------------------------------------ import

    /// <summary>Imports files and folders (recursively) into the current category at an optional position. Metadata is read on a worker thread.</summary>
    public void ImportPaths(IEnumerable<string> paths, Category? into = null, int? insertAt = null)
    {
        var files = DocumentService.ExpandPaths(paths).ToList();
        if (files.Count == 0) { Status(Strings.Get("NoAudioFiles", "No supported audio files found"), true); return; }
        var existing = Doc.List.Sounds.Select(s => s.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var target = into ?? (IsAllSoundsSelected ? null : CurrentCategory);
        var newFiles = files.Where(f => !existing.Contains(Path.GetFullPath(f))).ToList();
        if (!LicenseManager.CanAddSounds(Doc.List.Sounds.Count, newFiles.Count))
        {
            int room = Math.Max(0, LicenseManager.TrialMaxSounds - Doc.List.Sounds.Count);
            Status(Strings.Format("TrialSoundLimit", LicenseManager.TrialMaxSounds), true);
            if (Prefs.NotifyTrialLimit) Dialogs.Warn("VoxPad", Strings.Format("TrialSoundLimit", LicenseManager.TrialMaxSounds));
            newFiles = newFiles.Take(room).ToList();
            if (newFiles.Count == 0 && files.Count == newFiles.Count) return;
        }
        Status(Strings.Format("Importing", files.Count));
        Task.Run(() =>
        {
            var entries = newFiles.Select(DocumentService.CreateEntry).ToList();
            App.Current.Dispatcher.Invoke(() =>
            {
                Doc.Edit(list =>
                {
                    int pos = insertAt ?? -1;
                    foreach (var e in entries)
                    {
                        list.Add(e, target, pos >= 0 ? pos : null);
                        if (pos >= 0) pos++;
                    }
                    // Files already in the list but not in this category are added to it.
                    if (target != null)
                        foreach (var f in files.Where(f => existing.Contains(Path.GetFullPath(f))))
                        {
                            var s = list.Sounds.FirstOrDefault(x => x.FilePath.Equals(Path.GetFullPath(f), StringComparison.OrdinalIgnoreCase));
                            if (s != null && !target.SoundIds.Contains(s.Id)) target.SoundIds.Add(s.Id);
                        }
                });
                Status(Strings.Format("Imported", entries.Count));
            });
        });
    }

    public void AddEntryFromFile(string file, bool atBeginning)
    {
        var e = DocumentService.CreateEntry(file);
        if (!LicenseManager.CanAddSounds(Doc.List.Sounds.Count, 1)) { Status(Strings.Format("TrialSoundLimit", LicenseManager.TrialMaxSounds), true); return; }
        var target = IsAllSoundsSelected ? null : CurrentCategory;
        Doc.Edit(list => list.Add(e, target, atBeginning ? 0 : null));
        Status(Strings.Format("Imported", 1));
    }

    // ------------------------------------------------------------------ edit menu

    private void RemoveSelected()
    {
        var items = SelectedSounds.ToList();
        if (items.Count == 0) return;
        if (Prefs.ConfirmRemove && !Dialogs.Confirm(Strings.Get("Remove", "Remove"), Strings.Format("ConfirmRemove", items.Count))) return;
        Doc.Edit(list => { foreach (var s in items) list.Remove(s.Id); });
        Status(Strings.Format("Removed", items.Count));
    }

    private void RemoveSelectedFromCategory()
    {
        var items = SelectedSounds.ToList();
        var cat = CurrentCategory;
        if (items.Count == 0 || cat == Doc.AllSounds) return;
        Doc.Edit(_ => { foreach (var s in items) cat.SoundIds.Remove(s.Id); });
    }

    private void Paste()
    {
        if (_clipboard.Count == 0) return;
        var cat = IsAllSoundsSelected ? null : CurrentCategory;
        var anchor = PrimarySelection;
        Doc.Edit(list =>
        {
            int? pos = anchor == null ? null : (cat?.SoundIds ?? list.AllSoundsOrder).IndexOf(anchor.Id) + 1;
            foreach (var e in _clipboard)
            {
                if (list.Find(e.Id) == null) list.Add(e, cat, pos);
                else if (cat != null)
                {
                    if (_clipboardCut) foreach (var c in list.AllCategories()) if (c != cat) c.SoundIds.Remove(e.Id);
                    if (!cat.SoundIds.Contains(e.Id)) { if (pos is int p && p <= cat.SoundIds.Count) cat.SoundIds.Insert(p, e.Id); else cat.SoundIds.Add(e.Id); }
                }
                if (pos != null) pos++;
            }
        });
        if (_clipboardCut) { _clipboard = new(); _clipboardCut = false; }
    }

    private void MoveSelected(int delta)
    {
        var ids = SelectedSounds.Select(s => s.Id).ToList();
        if (ids.Count == 0) return;
        var order = CurrentCategory.SoundIds;
        Doc.Edit(_ =>
        {
            var idx = ids.Select(id => order.IndexOf(id)).Where(i => i >= 0).OrderBy(i => i).ToList();
            if (delta < 0 && idx.First() == 0) return;
            if (delta > 0 && idx.Last() == order.Count - 1) return;
            foreach (var i in delta < 0 ? idx : idx.AsEnumerable().Reverse())
            {
                (order[i], order[i + delta]) = (order[i + delta], order[i]);
            }
        });
        foreach (var vm in Sounds.Where(s => ids.Contains(s.Id))) SelectionRequested?.Invoke(vm);
    }

    /// <summary>Drag-and-drop reorder: moves the given sounds so that they sit before <paramref name="beforeId"/> (or at the end).</summary>
    public void ReorderInCurrentCategory(IList<Guid> ids, Guid? beforeId)
    {
        if (IsSorted) { Status(Strings.Get("ReorderWhileSorted", "Clear the sort (click the column header) to reorder manually."), true); return; }
        var order = CurrentCategory.SoundIds;
        Doc.Edit(_ =>
        {
            foreach (var id in ids) order.Remove(id);
            int at = beforeId == null ? order.Count : order.IndexOf(beforeId.Value);
            if (at < 0) at = order.Count;
            order.InsertRange(at, ids);
        });
    }

    public void AddSelectedToCategory(Category target, bool move) => AddToCategory(SelectedSounds.Select(s => s.Id).ToList(), target, move);

    public void AddToCategory(IList<Guid> ids, Category target, bool move)
    {
        if (ids.Count == 0 || target == Doc.AllSounds) return;
        var from = CurrentCategory;
        Doc.Edit(_ =>
        {
            foreach (var id in ids)
            {
                if (!target.SoundIds.Contains(id)) target.SoundIds.Add(id);
                if (move && from != Doc.AllSounds && from != target) from.SoundIds.Remove(id);
            }
        });
        Status(Strings.Format("AddedToCategory", ids.Count, target.Name));
    }

    private void SetHotkeyFor(SoundItemViewModel? item)
    {
        if (item == null) return;
        var (ok, hk) = Dialogs.CaptureHotkey(Strings.Format("HotkeyFor", item.Tag), item.Entry.Hotkey);
        if (ok) AssignHotkey(item.Entry, hk);
    }

    /// <summary>Assigns (or clears) a hotkey; the same hotkey elsewhere is removed ("reassigning moves the binding").</summary>
    public void AssignHotkey(SoundEntry entry, Hotkey? hk)
    {
        if (hk != null && !hk.IsAllowed) { Dialogs.Warn("VoxPad", Strings.Get("BareMouseNotAllowed", "Left and right mouse buttons need a modifier key.")); return; }
        if (hk != null && IsSpecialHotkey(hk, out var special)) { Dialogs.Warn("VoxPad", Strings.Format("ReservedHotkey", hk, special)); return; }
        Doc.Edit(list =>
        {
            if (hk != null)
            {
                foreach (var s in list.Sounds) if (s.Hotkey != null && s.Hotkey.Normalize(false) == hk.Normalize(false)) s.Hotkey = null;
                foreach (var c in list.AllCategories()) if (c.RandomHotkey != null && c.RandomHotkey.Normalize(false) == hk.Normalize(false)) c.RandomHotkey = null;
            }
            entry.Hotkey = hk;
        }, structural: false);
        foreach (var vm in Sounds) vm.Refresh();
        Hotbar.Rebuild();
        RebuildBindings();
        Status(hk == null ? Strings.Format("HotkeyRemoved", entry.Tag) : Strings.Format("HotkeySet", hk, entry.Tag));
    }

    private void SetColor()
    {
        var items = SelectedSounds.ToList();
        if (items.Count == 0) return;
        var c = Dialogs.PickColor(items[0].Entry.Color);
        if (c == null) return;
        Doc.Edit(_ => { foreach (var s in items) s.Entry.Color = c.Value; }, structural: false);
        foreach (var vm in items) vm.Refresh();
        Hotbar.Rebuild();
    }

    private void ResetPlayCount()
    {
        var items = SelectedSounds.ToList();
        if (items.Count == 0) return;
        if (Prefs.ConfirmResetPlayCount && !Dialogs.Confirm(Strings.Get("ResetPlayCount", "Reset play count"), Strings.Format("ConfirmResetPlayCount", items.Count))) return;
        Doc.Edit(_ => { foreach (var s in items) { s.Entry.PlayCount = 0; s.Entry.LastPlayed = null; } }, structural: false);
        foreach (var vm in items) vm.Refresh();
    }

    private void ResetAllPlayCounts()
    {
        if (!Dialogs.Confirm(Strings.Get("ResetPlayCount", "Reset play count"), Strings.Get("ConfirmResetAll", "Reset the play count of every sound and the list total?"))) return;
        Doc.Edit(list => { foreach (var s in list.Sounds) { s.PlayCount = 0; s.LastPlayed = null; } list.TotalPlays = 0; }, structural: false);
        foreach (var vm in Sounds) vm.Refresh();
    }

    private void SelectInExplorer()
    {
        var s = PrimarySelection;
        if (s == null) return;
        if (!File.Exists(s.FilePath)) { Status(Strings.Format("FileMissing", s.FilePath), true); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{s.FilePath}\"") { UseShellExecute = true }); } catch (Exception ex) { Status(ex.Message, true); }
    }

    /// <summary>After the editor saved to a new path/format, point the entry at the new file.</summary>
    public void ReplaceEntryFile(SoundEntry entry, string newPath)
    {
        var meta = AudioFileDecoder.ReadMetadata(newPath);
        Doc.Edit(_ => { entry.FilePath = newPath; entry.DurationMs = (long)meta.Duration.TotalMilliseconds; entry.IsMissing = false; }, structural: false);
        foreach (var vm in Sounds) vm.Refresh();
        Engine.Normalizer.MeasureFullFileCached(newPath);
    }

    // ------------------------------------------------------------------ categories

    private void AddCategory(CategoryViewModel? parent)
    {
        var name = Dialogs.AskText(Strings.Get("AddCategory", "Add category"), Strings.Get("CategoryName", "Name:"), Strings.Get("NewCategory", "New category"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var cat = new Category { Name = name.Trim() };
        Doc.Edit(list => { if (parent == null || parent.IsAllSounds) list.Categories.Add(cat); else parent.Model.Children.Add(cat); });
        var vm = CategoryRoots.SelectMany(c => c.Flatten()).FirstOrDefault(c => c.Id == cat.Id);
        if (vm != null) { vm.IsSelected = true; SelectedCategory = vm; }
    }

    private void RenameCategory(CategoryViewModel? vm)
    {
        if (vm == null || vm.IsAllSounds) return;
        var name = Dialogs.AskText(Strings.Get("RenameCategory", "Rename category"), Strings.Get("CategoryName", "Name:"), vm.Model.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        Doc.Edit(_ => vm.Model.Name = name.Trim());
    }

    private void RemoveCategory(CategoryViewModel? vm)
    {
        if (vm == null || vm.IsAllSounds) return;
        if (!Dialogs.Confirm(Strings.Get("RemoveCategory", "Remove category"), Strings.Format("ConfirmRemoveCategory", vm.Model.Name))) return;
        Doc.Edit(list =>
        {
            var parent = list.ParentOf(vm.Model);
            if (parent != null) parent.Children.Remove(vm.Model); else list.Categories.Remove(vm.Model);
        });
    }

    private void SetCategoryHotkey(CategoryViewModel? vm)
    {
        if (vm == null) return;
        var (ok, hk) = Dialogs.CaptureHotkey(Strings.Format("RandomHotkeyFor", vm.Name), vm.Model.RandomHotkey);
        if (!ok) return;
        if (hk != null && !hk.IsAllowed) { Dialogs.Warn("VoxPad", Strings.Get("BareMouseNotAllowed", "Left and right mouse buttons need a modifier key.")); return; }
        if (hk != null && IsSpecialHotkey(hk, out var special)) { Dialogs.Warn("VoxPad", Strings.Format("ReservedHotkey", hk, special)); return; }
        Doc.Edit(list =>
        {
            if (hk != null)
            {
                foreach (var s in list.Sounds) if (s.Hotkey != null && s.Hotkey.Normalize(false) == hk.Normalize(false)) s.Hotkey = null;
                foreach (var c in list.AllCategories()) if (c.RandomHotkey != null && c.RandomHotkey.Normalize(false) == hk.Normalize(false)) c.RandomHotkey = null;
            }
            vm.Model.RandomHotkey = hk;
        });
    }

    private void SetCategoryColor(CategoryViewModel? vm)
    {
        if (vm == null || vm.IsAllSounds) return;
        var c = Dialogs.PickColor(vm.Model.Color);
        if (c != null) Doc.Edit(_ => vm.Model.Color = c.Value);
    }

    private void ToggleHideCategory(CategoryViewModel? vm)
    {
        if (vm == null || vm.IsAllSounds) return;
        Doc.Edit(_ => vm.Model.Hidden = !vm.Model.Hidden);
    }

    /// <summary>Drag-and-drop in the tree: nest <paramref name="moved"/> under <paramref name="newParent"/> (null = root) at a position.</summary>
    public void MoveCategory(Category moved, Category? newParent, int index)
    {
        if (newParent == moved || (newParent != null && moved.Descendants().Contains(newParent))) return;
        Doc.Edit(list =>
        {
            var oldParent = list.ParentOf(moved);
            var oldList = oldParent?.Children ?? list.Categories;
            oldList.Remove(moved);
            var newList = newParent?.Children ?? list.Categories;
            if (index < 0 || index > newList.Count) index = newList.Count;
            newList.Insert(index, moved);
        });
    }

    // ------------------------------------------------------------------ hotbar

    public void HotbarAssign(int page, int slot, Guid soundId)
    {
        Doc.Edit(list =>
        {
            list.HotbarTiles.RemoveAll(t => t.Page == page && t.Slot == slot);
            list.HotbarTiles.Add(new HotbarTile { Page = page, Slot = slot, SoundId = soundId });
        }, structural: false);
        Hotbar.Rebuild();
    }

    public void HotbarClear(int page, int slot)
    {
        Doc.Edit(list => list.HotbarTiles.RemoveAll(t => t.Page == page && t.Slot == slot), structural: false);
        Hotbar.Rebuild();
    }

    /// <summary>Moves a tile; with Ctrl the tile keeps its colour (hotkeys live on the sound so they always follow).</summary>
    public void HotbarMove(int fromPage, int fromSlot, int toPage, int toSlot, bool keepColor)
    {
        Doc.Edit(list =>
        {
            var tile = list.HotbarTiles.FirstOrDefault(t => t.Page == fromPage && t.Slot == fromSlot);
            if (tile == null) return;
            list.HotbarTiles.RemoveAll(t => t.Page == toPage && t.Slot == toSlot);
            tile.Page = toPage; tile.Slot = toSlot;
            if (!keepColor) tile.Color = 0;
        }, structural: false);
        Hotbar.Rebuild();
    }

    public void HotbarSetColor(HotbarTileViewModel tile)
    {
        if (tile.Tile == null) return;
        var c = Dialogs.PickColor(tile.EffectiveColor);
        if (c == null) return;
        Doc.Edit(_ => tile.Tile.Color = c.Value, structural: false);
        Hotbar.Rebuild();
    }

    public void HotbarSetHotkey(HotbarTileViewModel tile)
    {
        if (tile.Sound == null) return;
        var (ok, hk) = Dialogs.CaptureHotkey(Strings.Format("HotkeyFor", tile.Sound.Tag), tile.Sound.Hotkey);
        if (ok) AssignHotkey(tile.Sound, hk);
    }

    public void HotbarPlay(HotbarTileViewModel tile)
    {
        if (tile.Sound != null) PlaySound(tile.Sound, null, PlayTrigger.UserInterface);
    }

    public void HotbarResize(int columns, int rows, int pages)
    {
        Doc.Edit(list =>
        {
            list.HotbarColumns = Math.Clamp(columns, 1, 20); list.HotbarRows = Math.Clamp(rows, 1, 10); list.HotbarPages = Math.Clamp(pages, 1, 50);
            list.HotbarTiles.RemoveAll(t => t.Page >= list.HotbarPages || t.Slot >= list.HotbarColumns * list.HotbarRows);
        }, structural: false);
        Hotbar.Attach(Doc.List);
    }
}
