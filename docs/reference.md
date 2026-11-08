# VoxPad – full feature reference

The short version is in the [README](../README.md). This page lists everything VoxPad does.

```
VoxPad/
  src/VoxPad.Core        models, list format (.vpl + Soundpad .spl import), preferences, EBU R128 meter, licensing, remote protocol
  src/VoxPad.Audio       WASAPI engine (NAudio): mic injection, speaker copy, normaliser, recorder, editor, TTS
  src/VoxPad.App         WPF application (menus, list, categories, hotbar, dialogs, hooks, Auto Keys, tray, themes, languages)
  src/VoxPad.RemoteClient  voxpad-remote.exe – reference client for the named-pipe API
  tools/KeyGen           voxpad-keygen.exe – vendor-side key pair / serial / update-signature tool
  installer/             WiX v5 MSI
  tests/VoxPad.Tests     xUnit unit tests;  tests/acceptance/  manual acceptance checklists
  docs/                  GitHub Pages site (index.html), manual.html (Help > Manual), audio-routing.md, remote-api.md
```

## 0. Stack and deliverables

**Choice: C# / .NET 8 + WPF + NAudio (WASAPI).** The whole product surface is Windows-only desktop UI plus low-level audio and input hooks; WPF gives a mature, themeable, accessible (UI Automation, high-contrast) UI with data virtualisation for large lists, and NAudio exposes WASAPI shared-mode capture/render, loopback capture, Media Foundation decoders/encoders and session volume with no native build step. Compared with C++/Qt this halves the code size, removes the Qt runtime (~30 MB) and the need for a C++ toolchain, while still being a native Win32 process (no Electron, no browser). The cost is a managed runtime: a cold start of ~1 s and ~70–90 MB working set (vs ~25 MB for Soundpad's C++ core). That is kept in check by workstation non-concurrent GC, streaming decoders (nothing is decoded to RAM except for the editor), a virtualised list and no background polling beyond a 50 ms UI timer. ARM64 is supported out of the box (`-r win-arm64`), which a C++ build would have needed a second toolchain for.

The repository holds the full source, the build guide (`docs/building.md`), the MSI installer project, portable mode (`portable.txt`), unit tests, acceptance checklists and the manual (`docs/manual.html`).

## 1. Audio routing (design first – read `docs/audio-routing.md` for the long version)

Soundpad ships a kernel-mode audio driver (APO/filter) that injects into the real microphone so no app has to change device. **VoxPad does not ship a driver** (a signed APO needs the WDK, an EV certificate and attestation signing, and cannot be built/installed from a user-mode project). We implement the honest, documented fallback the spec allows:

* **Virtual microphone via a virtual cable.** VoxPad renders *physical microphone(s) + sound* into the playback endpoint of a virtual cable (VB-CABLE's "CABLE Input", VB-CABLE A/B, Voicemeeter VAIO/AUX/VAIO3, or any user-chosen cable). Apps must select the cable's recording endpoint ("CABLE Output") as their microphone. The Devices tab says exactly which name to pick.
* **Setup modes (6 + auto):** Auto-select → VB-CABLE → VB-CABLE A/B → Voicemeeter → Other virtual cable (manual pair) → Stereo Mix / What U Hear → Speakers only. **Status** reads *Good* or red with the reason (cable missing/disabled, microphone unplugged, Windows microphone privacy denies desktop apps…). **Refresh** re-enumerates. Each recording device has a **Use** box; nothing ticked = default communications microphone.
* **Mixing modes:** *Mix* (voice + sound) and **Block voice** (voice gated to silence with a 5 ms ramp while a sound plays).
* **Speaker copy:** a second output on the chosen playback device through the normal Windows mixer; its slider is the app's session volume (what the Volume Mixer shows).
* **Voice activation sound:** optional loud pre-roll (250 ms 1 kHz tone or your file) on the mic before every sound, optional headphone copy.
* **Normalisation (EBU R128 / BS.1770-4, per channel):** voice-measurement tool (target > 74 dB on Soundpad's scale, dB = LUFS + 112); *dynamic* mode measures the first 6 s then refines once a second from the live stream; *full-file* mode measures the whole file (cached by path/size/mtime).
* **One sound at a time;** a new sound replaces the current one on every output. No overlapping playback, no queue, no fades, no voice changer, no MIDI (matches Soundpad's scope).
* Clock drift between the physical mic and the cable is compensated (±0.5 % rate trim), so latency stays at ~30 ms without periodic dropouts.

## 2. Main window

Menu bar **File / Edit / Play / Window / Help** with the Soundpad items (new/load/save/save as/recent/restore, export sound files & preferences, statistics, preferences; select/remove/copy/cut/paste/move/undo/redo/edit file/set & remove hotkey/colour/play random/reset play count/select in Explorer; play controls, repeat, continue, auto stop, play mode; panels, icon sizes, columns, hotbar size, hotkey overview, minimize to tray; manual, troubleshooting, updates, register, about).

Playback toolbar: Play · Play on speakers · Play on microphone · Pause/resume · Stop · Previous · Next · elapsed/total timer (double-click jumps to the playing row) · seek slider (**red** while others can hear it, **blue** for speakers only) · speaker volume icon + slider (Windows-mixer bound) · microphone peak meter (icon starts the voice measurement). Toolbar items are configurable in Preferences > Interface.

Left **Categories** panel: right-click to add/rename/remove/hide/colour/set *play random* hotkey; drag to reorder and nest; four icon sizes; info panel with sound count/total duration and a *Hide category* box; the hidden **All sounds** category defines every sound's index.

**Sound list:** columns Tag (Artist – Title from metadata, else file name) · Duration · Hotkey · Play count · Last played on; sortable (natural sort), resizable, reorderable, hideable (header right-click or Window > Columns); multi-select; context menu; drag-and-drop of files and folders (recursive) from Explorer; greyed/struck entries for missing files; per-entry colour swatch; instant search (type while the list has focus, Enter cycles matches, Ctrl+Enter plays, Esc clears).

**Hotbar:** paged grid (default 8×2, 4 pages, configurable); drop sounds on tiles; left-click plays; right-click sets hotkey/colour/removes; drag moves a tile; **Ctrl+drag** keeps the tile colour; hotkeys live on the sound so they are shared across pages. Ctrl+PgUp/PgDn flips pages.

**Status bar:** last operation / warning (red), trial counter, routing dot, **Play mode** (click cycles), **Hotkeys** (double-click toggles; right-click → Pass hotkeys / overview), **Auto Keys** (double-click toggles; right-click → Troubleshooting), **Repeat**, **Continue**. Tray icon with restore, stop and exit; *Start minimized*, *Minimize to tray*, *Close to tray*.

Themes **Light / Dark / Native**; Native is forced when high contrast or a screen reader is active. 21 UI languages via satellite resource assemblies (`Strings.<culture>.resx`).

## 3. Hotkeys

Global low-level keyboard + mouse hooks (work unfocused, even over games): single keys, any Ctrl/Alt/Shift/Win combination, mouse Mid/4/5, modifier+left/right click (bare left/right is refused). Set via right-click, F2 or double-clicking the Hotkey cell: press the combo, **Enter** saves, **Del/Backspace** clears, Esc cancels. Assigning a hotkey already used elsewhere moves it.

**Special hotkeys** (reserved): stop, start, pause, previous/next/selected/random (category) file, random from all, select previous/next file and category, start/stop recording, jump ±N s (N configurable), speaker volume up/down/set/mute, set/cycle play mode, enable/disable/toggle hotkeys and Auto Keys, play TTS. Enable/toggle hotkeys keep working while hotkeys are disabled.

**Numpad hotkeys:** hold the modifier (Alt by default), type the sound's **#** of the current category on the numpad, release. **Index hotkeys:** same gesture with the number row; the number is the row in *All sounds* (Alt+1,2,3 → row 123).

Advanced: **Pass hotkeys** (default off: matched keys are swallowed before the foreground app sees them), **Auto stop** (default on: hotkey-started sounds ignore Repeat/Continue), **Allow left/right modifiers**, **Same hotkeys in all categories**. Window > Hotkey overview lists every binding.

## 4. Auto Keys

Entries with key, Enabled, press mode (*Press and hold for the sound's duration* / *Press before and after*), *only if played by hotkey* (default on), condition (*Always* / *foreground app is fullscreen incl. borderless* / *listed app is running* / *listed app is in foreground*) and an app list. Keys are sent with scan codes (what games read) and only in microphone play modes. Global toggle in the status bar or via special hotkey. The Troubleshooting dialog shows the last evaluation: which entry fired and the exact reason an entry did not.

## 5. Library, lists, stats, import/export

Formats: AAC, FLAC, M4A, MP3, MP4 audio, OGG (Vorbis), Opus, WAV, WMA. Lists are `.vpl` (UTF-8 JSON: references, categories, order, hotkeys, colours, hotbar – never audio); Soundpad `.spl` lists import best-effort. The app edits a working copy (`%APPDATA%\VoxPad\working.vpl`) autosaved every 5 minutes when dirty and on exit; dated daily backups at startup (kept 30 days); **File > Restore sound list**; title bar shows path + `*` when unsaved. **Import from web** downloads a direct URL (optional file name). Stats: per-sound play count/last played (microphone plays only), list total, all-time total (`stats.json`), per-sound and global reset. **Export > Sound files** copies all audio into a folder and writes a list with relative paths; **Export/Import > Preferences** (`.vpprefs`, machine-specific device ids stripped). `VoxPad.exe --clean` resets preferences but keeps the list. Data: `%APPDATA%\VoxPad`; logs/crash dumps: `%ProgramData%\VoxPad`; portable mode: `portable.txt` beside the exe → everything in `Data\`.

## 6. Recorder, editor, TTS

**Recorder (Ctrl+R):** speakers (what-you-hear loopback) or microphone; peak meter; M4A (default) / WAV / MP3, always stereo 48 kHz; insert at beginning/end; auto-normalise to a target (default 89 dB ≈ −23 LUFS, warns above 99); trim silence; max time (default 300 s, 0 = unlimited; raw float buffered in `%TEMP%\VoxPad`); target directory; special hotkeys start/stop; notification beep on the speakers before capture.

**Editor (right-click > Edit file, Esc closes):** waveform, Cut (Del), Crop, volume ±3 dB / custom / normalise, Undo/Redo (20 steps), zoom (Ctrl+wheel, fit, selection), mouse + keyboard selection (Shift+arrows/Home/End, Ctrl+A, Ctrl+D), play selection on speakers (Space), read-only attributes (size, format, sample rate, duration, channels, original volume), warning above 10 minutes, saves in the recorder's format and repoints the list entry.

**TTS (Ctrl+T):** Windows SAPI voices, voice picker and rate, Enter previews on speakers, Ctrl+Enter sends to the microphone, *Save to list* writes the line as a file in the recorder format, special hotkey *Play TTS* speaks the current text.

## 7. Preferences

Tabs **Devices · Audio · Hotkeys (General, Special, Numpad, Auto Keys, Advanced) · Recorder · Interface · Notifications · Updates**, with the defaults above (dynamic normalisation at 74 dB, Block voice off, Auto stop on, Pass hotkeys off, Alt for numpad/index, M4A/89 dB/300 s, Light theme, update check *Never*).

## 8. Permissions, licensing, extras

* VoxPad is a standalone app: release builds are self-contained single-file executables with the .NET runtime bundled, so nothing else has to be installed (VB-CABLE aside). The MSI installs per-machine (UAC); the bare `.exe` runs from anywhere. The app runs unelevated (`asInvoker`); if it is started elevated anyway it warns that Explorer drag-and-drop will not work. The virtual-cable driver (VB-CABLE) is third-party; its first install prompts for a reboot.
* Windows microphone privacy is checked at start (consent store); a clear **Access denied** dialog explains the fix and opens the settings page.
* **Free, no limits.** The release build has no trial limits and no registration. The optional licensing code (name + ECDSA-signed serial, `tools/KeyGen`) stays in the source for anyone building a paid edition; it is off unless `LicenseManager.TrialLimitsEnabled` is set.
* **Help > Troubleshooting** builds a diagnostic zip (log, devices, routing, preferences without ids/licence, recent crash reports).
* **Remote Control API** over `\\.\pipe\VoxPadRemote` (JSON lines; play by index/name/id, stop, pause, play mode, list with tag + colour, categories, volume, status) – see `docs/remote-api.md` and `src/VoxPad.RemoteClient`.
* Updates: Never (default) / Daily / Weekly; manifest + package are ECDSA-signed and SHA-256 verified before `msiexec` is launched.

## 9. Tests

`dotnet test` runs 38 tests: 34 unit tests (list round-trip, `.spl` import, natural sort, hotkey model, preferences defaults/export, protocol, EBU R128 meter against BS.1770 reference levels, gating, per-channel gain, normaliser, encoder/decoder round-trip, editor cut/crop/undo, serial issue/verify, trial limits (off by default), base-32) plus 4 **real-device integration tests** that run when VB-CABLE is installed (routing resolves to the cable; a tone played on the microphone is read back from "CABLE Output" at the right level and frequency; silence with Block voice and no sound; full-file normalisation brings a quiet tone to the 74 dB target). Manual acceptance checklists for the spec's scenarios are in `tests/acceptance/`; `tools/CableCheck` (`voxpad-cablecheck`) records any recording endpoint and prints peak / RMS / loudness / dominant frequency so those checks can be done without Discord.

Verified on a Windows 11 PC with VB-CABLE: Status *Good*, 440 Hz and 330 Hz test tones arrive on "CABLE Output" normalised to the voice target, speakers-only plays never reach the cable, Block voice yields digital silence between sounds, and the voice-activation pre-roll (250 ms, 0.7 peak) precedes the sound.

> **VB-CABLE note:** its installer makes "CABLE Input" / "CABLE Output" the Windows *default* devices. VoxPad never uses a virtual cable as a fallback speaker or microphone (it picks the first physical endpoint and logs it), and `voxpad-cablecheck setdefault render "Speakers"` / `setdefault capture "Microphone"` restores your defaults without opening Sound settings.

## Releasing

Tag a commit `vX.Y.Z` and push the tag: the *Release* workflow builds the standalone exes, installers and portable zips on a Windows runner and attaches them to the GitHub release; the website's download button then points at them automatically. GitHub Pages is served from the `docs/` folder of `main` (Settings → Pages → Deploy from a branch → `main` / `/docs`).

## Building

See [`docs/building.md`](building.md). Short version:

```powershell
dotnet build VoxPad.sln -c Release
dotnet test  tests/VoxPad.Tests -c Release
.\build.ps1            # standalone VoxPad.exe, portable zip and MSI for x64 + ARM64 in artifacts\
```

## Licence

MIT. VoxPad is an independent project and is not affiliated with Leppsoft or Soundpad.
