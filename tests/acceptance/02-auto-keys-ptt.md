# 02 – Auto Keys: push-to-talk key held for exactly the clip duration

Verifies that an Auto Key presses the game's PTT key when a hotkey-started sound begins on the microphone, holds it for the sound's duration, releases it when the sound ends/stops, and that conditions and the troubleshooting dialog behave.

## Preconditions

- Routing Status = Good (virtual cable present), play mode = **Speakers + Microphone**.
- Auto Keys indicator in the status bar is **on** (not struck through).
- A list with *Clip A* (exactly known duration, e.g. 4.0 s – check the Duration column) bound to hotkey **F9**, and *Clip B* (~10 s) bound to **F10**.
- Preferences > Hotkeys > Auto Keys: one entry
  - Key = **V** (press the V key in the capture dialog)
  - Enabled = on, Press mode = **Press and hold for the sound's duration**
  - Only if played by hotkey = **on**, Condition = **Always**, app list empty
- Notepad open and empty. A stopwatch (phone) or the Windows Clock app.

## Part A – Hold duration measured without a key logger

Notepad's key auto-repeat is the measuring instrument: Windows repeats a held key about 30 times per second after a 0.5 s delay, so the number of "v" characters ≈ 30 × (hold seconds − 0.5).

1. Click into Notepad so it has focus. Press **F9** (Clip A, 4.0 s).
   **Expected:** Clip A plays (you hear it). Notepad fills with `v` characters while the clip plays and stops receiving them exactly when the clip ends. Count the characters: for a 4.0 s clip expect roughly 100–110 `v` (30/s × 3.5 s). Fewer than ~85 or more than ~125 is a fail. The last character arrives within 100 ms of the clip's end (listen).
2. Clear Notepad. Press **F10** (Clip B), and press the special hotkey **Stop** (set one, e.g. Pause/Break) after about 3 s.
   **Expected:** `v` characters stop at the moment you pressed Stop (~75 characters), not at the clip's natural end.
3. Clear Notepad. Press **F10**, then after 2 s press the special **Pause/resume** hotkey, wait 3 s, press it again.
   **Expected:** Characters stop while paused and resume when playback resumes.
4. Clear Notepad. Press **F9**, and 1 s later press **F10**.
   **Expected:** The stream of `v` is continuous (key released and re-pressed instantly when the new sound replaces the old one) and ends when Clip B ends.
5. Click into Notepad and start Clip A by **double-clicking it in VoxPad's list** instead of the hotkey (then click back into Notepad quickly) — or easier: use the remote client `voxpad-remote play 1`.
   **Expected:** No `v` characters: the entry has *Only if played by hotkey* on. Open Help > Auto Keys troubleshooting…: the entry shows ✖ "Sound was not started by a hotkey and 'only if played by hotkey' is on."
6. Set play mode to **Speakers only**, press **F9** with Notepad focused.
   **Expected:** No characters. Troubleshooting dialog shows the global reason "The sound was not played on the microphone…". Restore Speakers + Microphone.

## Part B – Press before and after

7. Change the entry's Press mode to **Press before and after**. Clear Notepad, focus it, press **F9**.
   **Expected:** Exactly one `v` appears when the clip starts and exactly one more when it ends (two characters total, ~4 s apart). No auto-repeat run.
8. Press **F10** and Stop it after 2 s.
   **Expected:** One `v` at start, one `v` at the moment of Stop.
9. Switch back to **Press and hold**.

## Part C – Conditions

10. Condition = **Foreground app is fullscreen (incl. borderless)**. With Notepad windowed and focused press **F9**.
    **Expected:** No characters. Troubleshooting dialog: ✖ "Foreground window is not fullscreen: 'Untitled - Notepad' [Notepad] WxH on monitor WxH".
11. Open a real game (or any app you can make fullscreen/borderless, e.g. a browser with F11), bind the game's PTT to **V** (Discord PTT also works: Input Mode = Push to Talk, shortcut V). Press **F9** while the game is in the foreground.
    **Expected:** The game/Discord shows PTT active for the clip's duration (Discord's green ring stays lit until the clip ends); Listener hears the clip. Troubleshooting dialog: ✔ "Foreground app is fullscreen: '…'".
12. Condition = **A listed app is running**, app list = `notepad.exe`. Close Notepad, press F9 → no PTT (✖ "None of the listed apps is running"). Open Notepad, press F9 → PTT fires (✔ "'notepad' is running"), even if another window is in the foreground.
13. Condition = **A listed app is in the foreground**, app list = `notepad.exe`. Focus Notepad → fires; focus another window → ✖ "Foreground app is 'explorer', not one of: notepad.exe".
14. Disable the entry (untick Enabled) → ✖ "Entry is disabled." Double-click the status-bar Auto Keys indicator to turn Auto Keys off → dialog's global line "Auto Keys are disabled (status bar indicator)."

## Part D – Self-trigger guard

15. Bind a VoxPad sound hotkey to **V** (the same key the Auto Key sends). Press **F9**.
    **Expected:** Only Clip A plays; the injected V does not start the V-bound sound (VoxPad ignores its own injected input). Remove the V binding afterwards.

## Expected results summary

- Hold length tracks the clip within ±0.2 s (Part A), including early Stop, pause and replacement.
- Before/after mode produces exactly two taps.
- Each condition fails and passes with the documented reason text.

## Evidence to capture

- Notepad screenshots with the character count for steps 1, 2, 7.
- Screenshot of the Auto Keys troubleshooting dialog for steps 5, 10, 11, 13.
- Clip durations from the Duration column.
- Diagnostic zip.
