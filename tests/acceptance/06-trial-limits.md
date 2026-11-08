# 06 – Trial limits enforced, registration, streamer toggle

Verifies the unregistered limits (10 sounds in the list, 10 microphone plays per launch), that speakers-only plays are unlimited, that a valid serial lifts the limits, and that the streamer toggle hides the licensee name.

## Preconditions

- Fresh state: exit VoxPad, delete `%APPDATA%\VoxPad\license.dat` if present (or Help > Register… > Remove license). Start VoxPad.
  **Expected:** Title bar ends with `[Trial]`; status bar shows "Trial: max 10 sounds, 10 mic plays left this launch"; Help > About says "Trial – max 10 sounds, 10 microphone plays per launch".
- Routing Status = Good (a cable is required for microphone plays to count; without a cable every play is speakers-only and never limited – do not run this checklist without the cable).
- A folder with 12 audio files (the 150 generated in checklist 03 work; copy 12 of them into `Music\trial-test`).
- Notifications > "Show a dialog when a trial limit is hit" **on** (default).
- A build with `LicenseManager.TrialLimitsEnabled = true` (the free release build has no limits, so this checklist only applies to a paid edition).
- A built `voxpad-keygen.exe` (`dotnet build tools/KeyGen`), a key pair from `voxpad-keygen newkey` whose public key is in `LicenseManager.PublicKeyBase64`, and its private key kept outside the repository.

## Part A – Sound limit

1. File > New sound list. Drag 8 files from `Music\trial-test` onto the window.
   **Expected:** "8 sound(s) added". Status bar trial text unchanged.
2. Drag 2 more files.
   **Expected:** "2 sound(s) added" (10 in the list).
3. Drag 1 more (the 11th).
   **Expected:** A warning dialog and the red status "Trial: the list can hold at most 10 sounds. Register to remove the limit." The list still has 10 rows.
4. Drag the whole folder (12 files, 10 already present).
   **Expected:** Nothing new is added; the same status message.
5. Remove one sound (Del) and drag one of the two not-yet-added files.
   **Expected:** It is added (back to 10).
6. Recorder (Ctrl+R) > Record 2 s from the microphone > Stop.
   **Expected:** The file is written to the recorder directory, but the red status "Trial: the list can hold at most 10 sounds…" appears and the list stays at 10. TTS > Save to list behaves the same.

## Part B – Microphone play limit

7. Play mode = **Speakers + Microphone**. Press Enter on sound 1, wait for it to finish; repeat for 10 plays total (any sounds). Watch the status-bar trial text.
   **Expected:** The text counts down: "… 9 mic plays left …", … "0 mic plays left this launch". Each play is audible to the Listener / shows a red seek bar.
8. Press Enter an 11th time.
   **Expected:** Nothing plays; red status "Trial: 10 microphone plays per launch reached. Register to remove the limit." and (notifications on) a warning dialog. The play count of the sound did not increase.
9. Press the sound's **hotkey** and use `voxpad-remote play 1`.
   **Expected:** Both refused with the same status; the remote client prints `"ok":false` with the error text.
10. Set play mode to **Speakers only** and press Enter five times.
    **Expected:** All five play (blue seek bar); the trial text stays at 0 left – speakers-only plays are not limited and not counted.
11. Set play mode back to Speakers + Microphone, use the toolbar **▶🔊 Play on speakers** button.
    **Expected:** Plays (speakers only, blue). **▶🎤 Play on microphone** is refused.
12. Exit VoxPad, start it again, play one sound to the mic.
    **Expected:** Plays; trial text "9 mic plays left" – the counter is per launch.

## Part C – Registration

13. Issue a serial: `voxpad-keygen issue <DevPrivate base64> "Acceptance Tester" 2`
    **Expected:** Prints `Serial: XXXXXX-XXXXXX-…` (Crockford base-32 groups).
14. Help > Register…. Enter Name = `Acceptance Tester`, Serial = a **wrong** value (change one character), press Register.
    **Expected:** "The serial does not match the name…" dialog; still trial.
15. Enter the correct serial, but with dashes removed, lower-case and an `O` typed instead of a `0` (if present). Press Register.
    **Expected:** "Thank you! VoxPad is now registered." `%APPDATA%\VoxPad\license.dat` exists. The title bar loses `[Trial]`, the trial text disappears from the status bar. About shows "Licensed to Acceptance Tester (Streamer edition)".
16. Drag the remaining files from `Music\trial-test` (now 12 in the list) and play to the microphone 15 times.
    **Expected:** No limit messages.
17. Exit and restart.
    **Expected:** Still registered (license.dat verified at start). Check the log for no "license.dat present but the serial does not verify" warning.
18. Tamper: open license.dat in Notepad and change a letter of the name, save, restart VoxPad.
    **Expected:** Back in trial (the log says the serial does not verify); Register again with the correct values to continue.

## Part D – Streamer toggle (hide licensee name)

19. Help > About.
    **Expected:** "Licensed to Acceptance Tester (Streamer edition)".
20. Help > Register… > tick **Hide my name (streamer mode)** (also available in Preferences > Interface). Close. Help > About.
    **Expected:** "Licensed to •••••••• (Streamer edition)"; the Register dialog also shows the masked name. The title bar never shows the name in either state.
21. Preferences > Interface: untick the option, OK, About shows the name again.
22. Register dialog > **Remove license** > confirm.
    **Expected:** license.dat deleted, title bar shows `[Trial]` again, trial text back in the status bar.

## Expected results summary

- 11th sound refused from every import path (drop, folder, recorder, TTS); removing one makes room.
- 11th microphone play per launch refused via UI, hotkey and remote; speakers-only unlimited; counter resets on restart.
- Only a serial signed for the exact name registers; serial typing tolerant; tampering reverts to trial.
- Streamer toggle masks the name everywhere it is shown.

## Evidence to capture

- Screenshots: status bar after step 3 and step 8, About before/after step 20, Register dialog after step 15.
- The keygen output (serial can be included; the private key must not).
- `license.dat` contents after step 15 (name + serial only).
