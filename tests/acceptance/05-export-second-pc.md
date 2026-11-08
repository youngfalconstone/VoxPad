# 05 – Export > Sound files, then load the list on a second PC

Verifies that an exported folder is self-contained (audio copies + a list with relative paths), loads unchanged on another machine or after being moved, and that files missing on the target are greyed out rather than breaking the list.

## Preconditions

- PC 1 with VoxPad and a list of at least 6 sounds taken from **two different folders** (e.g. `C:\sfx\a.mp3` and `D:\clips\b.wav`), two of them with the same file name in different folders (e.g. `C:\sfx\hit.wav` and `D:\clips\hit.wav`), 2 categories, colours, 2 hotkeys, 2 hotbar tiles with a tile colour, and play counts > 0 on some sounds. Saved as `export-test.vpl`.
- PC 2 (or a VM) with VoxPad installed, **without** access to PC 1's `C:\sfx` and `D:\clips` paths. A USB stick or network share to carry the folder.

## Steps (PC 1)

1. File > Export > Sound files…, pick an empty folder `E:\voxpad-export` (on the USB stick).
   **Expected:** Status bar "Exported 6 file(s), 0 failed → E:\voxpad-export". The folder contains all audio files; the two `hit.wav` files are both present, one renamed `hit (1).wav`, and `export-test.vpl`.
2. Open `E:\voxpad-export\export-test.vpl` in Notepad.
   **Expected:** Every `"Path"` is relative (`"a.mp3"`, `"hit (1).wav"`), no drive letters; categories, hotkeys, colours, hotbar tiles and play counts are present.
3. Remove read access to the originals to simulate PC 2 without a second machine (optional): rename `C:\sfx` to `C:\sfx-off`. Load the exported list with File > Load sound list… → `E:\voxpad-export\export-test.vpl`.
   **Expected:** All sounds are black (not missing), play correctly, the `#`/order, categories, hotkeys, colours and hotbar tiles are identical to the original list. The title bar shows the exported path. Rename `C:\sfx-off` back afterwards.

## Steps (PC 2)

4. Copy `E:\voxpad-export` to `C:\Users\<me>\Documents\voxpad-export` on PC 2 (a different path than on PC 1). Start VoxPad, File > Load sound list…, choose the copied `export-test.vpl`.
   **Expected:** List loads with all 6 sounds, none greyed; status bar "Loaded …"; File > Recent sound lists shows it.
5. Play two sounds, press both hotkeys, click both hotbar tiles.
   **Expected:** All play; hotkeys and tile colours survived; play counts continue from the exported values (mic plays increment them).
6. Move the whole folder again (e.g. to the Desktop) while VoxPad is closed, start VoxPad.
   **Expected:** VoxPad loads the working copy; because the paths were resolved to absolute paths at load time the sounds are now **missing** (grey, struck through) and the status bar says "6 file(s) in the list are missing (greyed out)". Load the list from its new location via File > Load → all black again (relative paths re-resolved against the list's folder).
7. Delete one audio file from the folder (e.g. `a.mp3`), then File > Load the list again.
   **Expected:** Only that sound is grey/struck through; status bar reports "1 file(s) … missing". Pressing Enter on it shows the red status "File not found: …" and plays nothing; Right-click > Select in Explorer reports the same. The other sounds still play. The missing entry can be removed with Del.
8. Save the list on PC 2 (Ctrl+S), copy it back to PC 1 and load it.
   **Expected:** Loads on PC 1 too (paths are stored absolute after Save; use Export again for a portable folder).

## Expected results summary

- Export copies every file (deduplicating names) and writes a list with relative paths.
- The exported folder loads on another PC and at any path, with order, categories, hotkeys, colours, hotbar and stats intact.
- Missing files are greyed with a count in the status bar and never block the rest of the list.

## Evidence to capture

- Explorer listing of the export folder; the `.vpl` text showing relative paths.
- Screenshots of the loaded list on PC 2 (step 4) and with the greyed entry (step 7).
- Diagnostic zips from both PCs.
