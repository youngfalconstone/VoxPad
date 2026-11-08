<!-- helper: tools/CableCheck (voxpad-cablecheck [seconds] ["CABLE Output"] [timeline]) measures what reaches the cable; 'list' shows default devices; 'setdefault render|capture <name>' fixes defaults VB-CABLE hijacked -->
# VoxPad acceptance checklists

Manual end-to-end checks that cannot run in `dotnet test` because they need real audio devices, a virtual cable, a second PC or a third-party app. Run them before every release on the environment matrix below and record the results.

#<!-- helper: tools/CableCheck (voxpad-cablecheck [seconds] ["CABLE Output"] [timeline]) measures what reaches the cable; 'list' shows default devices; 'setdefault render|capture <name>' fixes defaults VB-CABLE hijacked -->
# Checklists

| <!-- helper: tools/CableCheck (voxpad-cablecheck [seconds] ["CABLE Output"] [timeline]) measures what reaches the cable; 'list' shows default devices; 'setdefault render|capture <name>' fixes defaults VB-CABLE hijacked -->
# | File | Covers |
|---|------|--------|
| 01 | `01-discord-audible.md` | Sound audible in Discord with all Discord processing disabled; Mix vs Block voice; voice-activation pre-roll |
| 02 | `02-auto-keys-ptt.md` | Auto Key holds the push-to-talk key for exactly the clip duration; conditions; troubleshooting dialog |
| 03 | `03-index-hotkey.md` | Index hotkey Alt+1+2+3 plays row 123; numpad variant; "No sound at number" |
| 04 | `04-restore-from-backup.md` | Daily backup at startup and File > Restore sound list |
| 05 | `05-export-second-pc.md` | Export > Sound files, load on a second PC, missing-file greying |
| 06 | `06-trial-limits.md` | 10-sound / 10-play trial limits, registration, streamer toggle |
| 07 | `07-seek-bar-colour.md` | Seek bar red/blue matches where the audio actually goes; routing dot |

#<!-- helper: tools/CableCheck (voxpad-cablecheck [seconds] ["CABLE Output"] [timeline]) measures what reaches the cable; 'list' shows default devices; 'setdefault render|capture <name>' fixes defaults VB-CABLE hijacked -->
# Environment matrix

Run every checklist in each row that applies. "VB-CABLE absent" rows exercise the speakers-only fallback.

| OS | Arch | VB-CABLE | Build | Notes |
|----|------|----------|-------|-------|
| Windows 11 (current) | x64 | present | MSI, framework-dependent | primary target |
| Windows 11 (current) | x64 | absent | MSI | routing must report red; speakers still work |
| Windows 10 22H2 | x64 | present | MSI | |
| Windows 11 | ARM64 | present | portable zip (`win-arm64` publish) | VB-CABLE has an ARM64 driver since 2023; note the version used |
| Windows 11 | x64 | present | portable (`portable.txt`) | storage paths must be `<exe>\Data` |

Also record: VoxPad version (Help > About), .NET Desktop Runtime version, Discord build, microphone model, whether VoxPad or the game ran elevated.

#<!-- helper: tools/CableCheck (voxpad-cablecheck [seconds] ["CABLE Output"] [timeline]) measures what reaches the cable; 'list' shows default devices; 'setdefault render|capture <name>' fixes defaults VB-CABLE hijacked -->
# How to run

1. Build and install from a clean state: uninstall any previous VoxPad, delete `%APPDATA%\VoxPad` and `%ProgramData%\VoxPad` (or start once with `VoxPad.exe --clean` when only preferences must be reset).
2. Work through each checklist top to bottom. Every step has an **Expected** result; a step fails if the observed result differs.
3. Capture the **Evidence** listed at the end of each checklist (screenshots go to `tests/acceptance/results/<date>/`, the diagnostic zip from Help > Troubleshooting is always attached).
4. Fill in the result table below in a copy named `results/<date>-<os>-<arch>.md`.

#<!-- helper: tools/CableCheck (voxpad-cablecheck [seconds] ["CABLE Output"] [timeline]) measures what reaches the cable; 'list' shows default devices; 'setdefault render|capture <name>' fixes defaults VB-CABLE hijacked -->
# Result template

```
VoxPad <version> · <OS> <arch> · VB-CABLE <version|absent> · <date> · tester <initials>

| Checklist | Result (pass/fail/blocked) | Notes / defect id |
|-----------|----------------------------|-------------------|
| 01 Discord audible | | |
| 02 Auto Keys PTT | | |
| 03 Index hotkey | | |
| 04 Restore backup | | |
| 05 Export second PC | | |
| 06 Trial limits | | |
| 07 Seek bar colour | | |
```

A release needs every checklist to pass on the primary row and no failures on the others (a "blocked" row needs a reason, e.g. no ARM64 hardware available).
