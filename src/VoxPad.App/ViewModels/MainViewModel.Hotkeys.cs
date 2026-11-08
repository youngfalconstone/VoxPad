using VoxPad.App.Services;
using VoxPad.Audio.Engine;
using VoxPad.Core.Models;
using VoxPad.Core.Remote;
using VoxPad.Core.Settings;

namespace VoxPad.App.ViewModels;

public sealed partial class MainViewModel : IRemoteTarget
{
    public static readonly string[] SpecialNames = typeof(SpecialHotkeys).GetProperties().Where(p => p.PropertyType == typeof(Hotkey)).Select(p => p.Name).ToArray();

    public event Action? RecordingStartRequested;
    public event Action? RecordingStopRequested;
    public event Action? TtsSpeakRequested;

    /// <summary>Pushes every binding (sounds, category random hotkeys, special hotkeys) into the hook service.</summary>
    public void RebuildBindings()
    {
        Hotkeys.PassHotkeys = Prefs.PassHotkeys;
        Hotkeys.AllowLeftRightModifiers = Prefs.AllowLeftRightModifiers;
        Hotkeys.NumpadEnabled = Prefs.NumpadHotkeysEnabled; Hotkeys.NumpadModifier = Prefs.NumpadModifier;
        Hotkeys.IndexEnabled = Prefs.IndexHotkeysEnabled; Hotkeys.IndexModifier = Prefs.IndexModifier;
        Hotkeys.Enabled = Prefs.HotkeysEnabled;
        var list = new List<(Hotkey, HotkeyAction)>();
        // Sounds (and category random hotkeys), then specials last so reserved keys always win.
        foreach (var (hk, s) in Doc.List.SoundHotkeys()) list.Add((hk, new HotkeyAction(HotkeyActionKind.Sound, s.Id)));
        foreach (var (hk, c) in Doc.List.CategoryHotkeys()) list.Add((hk, new HotkeyAction(HotkeyActionKind.CategoryRandom, c.Id)));
        foreach (var (name, hk) in Prefs.Special.All()) list.Add((hk, new HotkeyAction(HotkeyActionKind.Special, Special: name)));
        Hotkeys.SetBindings(list);
        Raise(nameof(HotkeysEnabled));
    }

    public bool IsSpecialHotkey(Hotkey hk, out string name)
    {
        foreach (var (n, h) in Prefs.Special.All())
            if (h.Normalize(false) == hk.Normalize(false)) { name = n; return true; }
        name = ""; return false;
    }

    /// <summary>Every binding for the Hotkey overview window.</summary>
    public IEnumerable<(string hotkey, string kind, string target)> AllBindings()
    {
        foreach (var (hk, s) in Doc.List.SoundHotkeys()) yield return (hk.ToString(), Strings.Get("Sound", "Sound"), s.Tag);
        foreach (var (hk, c) in Doc.List.CategoryHotkeys()) yield return (hk.ToString(), Strings.Get("CategoryRandom", "Play random in category"), c.Name);
        foreach (var (name, hk) in Prefs.Special.All()) yield return (hk.ToString(), Strings.Get("Special", "Special"), SpecialDisplayName(name));
        if (Prefs.NumpadHotkeysEnabled) yield return ($"{Prefs.NumpadModifier} + Num 0-9", Strings.Get("Numpad", "Numpad"), Strings.Get("NumpadDesc", "Play by number in the current category"));
        if (Prefs.IndexHotkeysEnabled) yield return ($"{Prefs.IndexModifier} + 0-9", Strings.Get("Index", "Index"), Strings.Get("IndexDesc", "Play by row index in All sounds"));
    }

    public static string SpecialDisplayName(string name) => Strings.Get("Special_" + name, System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1"));

    private void OnHotkey(HotkeyAction a)
    {
        switch (a.Kind)
        {
            case HotkeyActionKind.Sound:
            {
                var s = Doc.List.Find(a.Id);
                if (s == null) break;
                if (Prefs.SameHotkeysInAllCategories)
                {
                    // Row-based: the hotkey belongs to the row the sound occupies in its own category; play that row of the selected category.
                    var home = Doc.List.AllCategories().FirstOrDefault(c => c.SoundIds.Contains(s.Id));
                    int row = home != null ? home.SoundIds.IndexOf(s.Id) : Doc.List.AllSoundsOrder.IndexOf(s.Id);
                    var ids = CurrentCategory.SoundIds;
                    if (row >= 0 && row < ids.Count && Doc.List.Find(ids[row]) is { } rowSound) s = rowSound;
                }
                PlaySound(s, null, PlayTrigger.Hotkey);
                break;
            }
            case HotkeyActionKind.CategoryRandom:
                PlayRandom(Doc.List.FindCategory(a.Id), PlayTrigger.Hotkey);
                break;
            case HotkeyActionKind.Numpad:
            {
                var ids = CurrentCategory.SoundIds;
                if (a.Number >= 1 && a.Number <= ids.Count && Doc.List.Find(ids[a.Number - 1]) is { } s) PlaySound(s, null, PlayTrigger.Hotkey);
                else Status(Strings.Format("NoSoundAtNumber", a.Number), true);
                break;
            }
            case HotkeyActionKind.Index:
            {
                var s = Doc.List.ByIndex(a.Number);
                if (s != null) PlaySound(s, null, PlayTrigger.Hotkey);
                else Status(Strings.Format("NoSoundAtNumber", a.Number), true);
                break;
            }
            case HotkeyActionKind.Special:
                RunSpecial(a.Special);
                break;
        }
    }

    public void RunSpecial(string name)
    {
        switch (name)
        {
            case "Stop": Stop(); break;
            case "Start": if (Engine.IsPaused) TogglePause(); else PlaySelected(null, PlayTrigger.Hotkey); break;
            case "Pause": TogglePause(); break;
            case "PlayPrevious": PlayPrevious(PlayTrigger.Hotkey); break;
            case "PlayNext": PlayNext(PlayTrigger.Hotkey); break;
            case "PlaySelected": PlaySelected(null, PlayTrigger.Hotkey); break;
            case "PlayRandomInCategory": PlayRandom(CurrentCategory, PlayTrigger.Hotkey); break;
            case "PlayRandomFromAll": PlayRandom(null, PlayTrigger.Hotkey); break;
            case "SelectPreviousFile": SelectRelative(-1); break;
            case "SelectNextFile": SelectRelative(+1); break;
            case "SelectPreviousCategory": SelectCategoryRelative(-1); break;
            case "SelectNextCategory": SelectCategoryRelative(+1); break;
            case "StartRecording": RecordingStartRequested?.Invoke(); break;
            case "StopRecording": RecordingStopRequested?.Invoke(); break;
            case "JumpBackward": Engine.Jump(-Prefs.JumpSeconds); break;
            case "JumpForward": Engine.Jump(Prefs.JumpSeconds); break;
            case "SpeakerVolumeUp": SpeakerVolume += Prefs.VolumeStepPercent / 100.0; break;
            case "SpeakerVolumeDown": SpeakerVolume -= Prefs.VolumeStepPercent / 100.0; break;
            case "SpeakerVolumeSet": SpeakerVolume = Prefs.VolumeSetPercent / 100.0; break;
            case "SpeakerVolumeMute": SpeakerMuted = !SpeakerMuted; break;
            case "SetPlayModeBoth": PlayMode = PlayMode.Both; break;
            case "SetPlayModeMic": PlayMode = PlayMode.MicrophoneOnly; break;
            case "SetPlayModeSpeakers": PlayMode = PlayMode.SpeakersOnly; break;
            case "CyclePlayMode": PlayMode = PlayMode.Next(); break;
            case "EnableHotkeys": HotkeysEnabled = true; break;
            case "DisableHotkeys": HotkeysEnabled = false; break;
            case "ToggleHotkeys": HotkeysEnabled = !HotkeysEnabled; break;
            case "EnableAutoKeys": AutoKeysEnabled = true; break;
            case "DisableAutoKeys": AutoKeysEnabled = false; break;
            case "ToggleAutoKeys": AutoKeysEnabled = !AutoKeysEnabled; break;
            case "PlayTts": TtsSpeakRequested?.Invoke(); break;
        }
    }

    // ------------------------------------------------------------------ Remote Control API

    public Protocol.Response Handle(Protocol.Request r)
    {
        switch (r.Cmd)
        {
            case Protocol.Command.Version:
                return new Protocol.Response { Version = UpdateService.CurrentVersion };
            case Protocol.Command.Status:
                return StatusResponse();
            case Protocol.Command.List:
                return new Protocol.Response
                {
                    Sounds = Doc.List.SoundsInOrder().Select((s, i) => new Protocol.SoundInfo
                    {
                        Index = i + 1, Id = s.Id, Tag = s.Tag, Color = s.Color == 0 ? null : $"#{s.Color & 0xFFFFFF:X6}",
                        DurationSec = s.DurationMs / 1000.0, Hotkey = s.Hotkey?.ToString(), PlayCount = s.PlayCount, Missing = s.IsMissing,
                    }).ToList(),
                };
            case Protocol.Command.Categories:
                return new Protocol.Response
                {
                    Categories = Doc.List.AllCategories().Select(c => new Protocol.CategoryInfo { Id = c.Id, Name = c.Name, Parent = Doc.List.ParentOf(c)?.Id, SoundCount = c.SoundIds.Count }).ToList(),
                };
            case Protocol.Command.Play:
            {
                SoundEntry? s = null;
                if (r.Index is int i) s = Doc.List.ByIndex(i);
                else if (r.Id is Guid id) s = Doc.List.Find(id);
                else if (!string.IsNullOrEmpty(r.Name)) s = Doc.List.SoundsInOrder().FirstOrDefault(x => x.Tag.Equals(r.Name, StringComparison.OrdinalIgnoreCase))
                                                          ?? Doc.List.SoundsInOrder().FirstOrDefault(x => x.Tag.Contains(r.Name, StringComparison.OrdinalIgnoreCase));
                if (s == null) return Protocol.Fail("Sound not found");
                PlayTarget? t = r.Target switch { Protocol.Target.Mic => PlayTarget.Microphone, Protocol.Target.Speakers => PlayTarget.Speakers, Protocol.Target.Both => PlayTarget.Both, _ => null };
                return PlaySound(s, t, PlayTrigger.Remote) ? StatusResponse() : Protocol.Fail(StatusText);
            }
            case Protocol.Command.Random:
                PlayRandom(r.Category is Guid cid ? Doc.List.FindCategory(cid) : null, PlayTrigger.Remote);
                return StatusResponse();
            case Protocol.Command.Stop:
                Stop(); return StatusResponse();
            case Protocol.Command.Pause:
                TogglePause(); return StatusResponse();
            case Protocol.Command.PlayMode:
                if (!string.IsNullOrEmpty(r.Mode))
                {
                    PlayMode = r.Mode.ToLowerInvariant() switch
                    {
                        "mic" or "microphone" => PlayMode.MicrophoneOnly,
                        "speakers" or "speaker" => PlayMode.SpeakersOnly,
                        "both" => PlayMode.Both,
                        _ => throw new FormatException("mode must be both|mic|speakers"),
                    };
                }
                return StatusResponse();
            case Protocol.Command.Volume:
                if (r.Level is double lv) SpeakerVolume = lv;
                return new Protocol.Response { Level = SpeakerVolume };
            default:
                return Protocol.Fail("Unknown command");
        }
    }

    private Protocol.Response StatusResponse()
    {
        var cur = Engine.Current;
        return new Protocol.Response
        {
            Mode = PlayMode switch { PlayMode.MicrophoneOnly => "mic", PlayMode.SpeakersOnly => "speakers", _ => "both" },
            Playing = cur != null && !Engine.IsPaused,
            Paused = Engine.IsPaused,
            Index = cur?.Sound == null ? null : Doc.List.IndexOf(cur.Sound.Id),
            Tag = cur?.Label,
            PositionSec = Engine.Position.TotalSeconds,
            DurationSec = Engine.Duration.TotalSeconds,
            Level = SpeakerVolume,
        };
    }
}
