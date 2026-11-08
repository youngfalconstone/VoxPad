# 04 – Restore the sound list from a daily backup

Verifies that VoxPad writes one dated backup of the list per day at startup, keeps them, and that File > Restore sound list… brings a damaged list back.

## Preconditions

- VoxPad installed (not portable), started once. Note the data folder: `%APPDATA%\VoxPad` (About > Open data folder).
- A sound list with at least 5 sounds, 2 categories (one nested) and 2 hotkeys, saved as `restore-test.vpl` (File > Save sound list as…). The title bar shows the path without an asterisk.

## Steps

1. Exit VoxPad (File > Exit). Open `%APPDATA%\VoxPad\backups` in Explorer.
   **Expected:** The folder may contain `restore-test-<today>.vpl` if the list was already loaded at a previous start today; note what is there.
2. Delete any `restore-test-<today>.vpl` backup so the next start must create one. Start VoxPad.
   **Expected:** Within a few seconds `backups\restore-test-YYYY-MM-DD.vpl` (today's date) exists and its size is roughly that of `restore-test.vpl`. The log (`%ProgramData%\VoxPad\logs\voxpad-<date>.log`) contains "Backup written: …".
3. Start VoxPad a second time the same day (exit, start again).
   **Expected:** No second file for today; the existing backup is kept (one per day).
4. Damage the list: remove three sounds (select, Del, confirm), delete the nested category (right-click > Remove category), remove one hotkey. Save (Ctrl+S).
   **Expected:** Title bar without asterisk; the list now has fewer sounds. The backup file from step 2 is unchanged (check its modified time).
5. File > Restore sound list….
   **Expected:** A dialog lists the backups newest first as "`<date time>   restore-test-YYYY-MM-DD.vpl   (<n> KB)`" with an *Open folder* button that opens the backups directory.
6. Select today's backup, press **Restore**, confirm "Restore the backup from …?".
   **Expected:** The list reloads with all 5 sounds, both categories (including the nested one) and both hotkeys. The status bar says "Restored the backup from …". The title bar now shows an **asterisk** (restored content is unsaved) and the path is still `restore-test.vpl`.
7. Press a restored hotkey.
   **Expected:** The sound plays – bindings were re-registered after the restore.
8. Exit without saving when asked? Choose **Yes** (save).
   **Expected:** `restore-test.vpl` on disk now contains the restored content (re-open it to verify).
9. Retention: create fake old backups to prove pruning. In PowerShell:
   ```powershell
   $b = "$env:APPDATA\VoxPad\backups"
   Copy-Item "$b\restore-test-$(Get-Date -f yyyy-MM-dd).vpl" "$b\restore-test-2020-01-01.vpl"
   (Get-Item "$b\restore-test-2020-01-01.vpl").LastWriteTime = Get-Date "2020-01-01"
   ```
   Delete today's backup, start VoxPad.
   **Expected:** Today's backup is recreated and `restore-test-2020-01-01.vpl` is gone (backups older than 30 days are pruned when a new backup is written).
10. Crash-recovery variant: add a sound, do **not** save, wait more than 5 minutes (autosave interval) or kill `VoxPad.exe` from Task Manager after the status bar confirmed the import. Start VoxPad.
    **Expected:** The added sound is present (loaded from `working.vpl`) and the title bar shows an asterisk because the working copy differs from the saved file.

## Expected results summary

- Exactly one dated backup per day, created at startup, pruned after 30 days.
- Restore replaces the current list completely, marks it dirty, re-registers hotkeys.
- The working copy survives a crash.

## Evidence to capture

- Explorer screenshot of the backups folder after step 2 and step 9.
- Screenshot of the Restore dialog (step 5) and of the restored list with the asterisk in the title bar (step 6).
- The "Backup written" log line.
