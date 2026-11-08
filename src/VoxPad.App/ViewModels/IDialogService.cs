using VoxPad.Core.Models;

namespace VoxPad.App.ViewModels;

/// <summary>Everything the view model needs from the window layer (dialogs). Implemented by MainWindow.</summary>
public interface IDialogService
{
    /// <summary>Returns (true, hotkey) to assign, (true, null) to clear, (false, _) when cancelled.</summary>
    (bool ok, Hotkey? hotkey) CaptureHotkey(string title, Hotkey? current);
    string? AskText(string title, string prompt, string initial);
    uint? PickColor(uint current);
    bool Confirm(string title, string text);
    void Info(string title, string text);
    void Warn(string title, string text);
    string[] OpenFiles(string filter, bool multi);
    string? SaveFile(string filter, string? suggestedName, string? defaultExt);
    string? PickFolder(string title);
    void ShowPreferences(string? tab = null);
    void ShowRecorder();
    void ShowEditor(SoundEntry entry);
    void ShowTts();
    void ShowHotkeyOverview();
    void ShowAutoKeysTroubleshooting();
    void ShowAbout();
    void ShowRegister();
    void ShowStats();
    void ShowRestore();
    void ShowWebImport();
    void ShowUpdate(bool manual);
    void ShowManual();
    void ShowTroubleshooting();
    void ExitApplication();
}
