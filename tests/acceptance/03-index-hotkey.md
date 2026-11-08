# 03 – Index hotkey Alt+1+2+3 plays row 123

Verifies the index gesture (hold the modifier, type digits on the number row, release) plays the sound at that row of the hidden **All sounds** category, the numpad gesture plays by the # column of the current category, and the "No sound at number" message appears for out-of-range numbers.

## Preconditions

- VoxPad registered (the trial allows only 10 sounds) – see checklist 06 for a dev serial.
- Preferences > Hotkeys > Numpad: Numpad hotkeys **on**, modifier **Alt**; Index hotkeys **on**, modifier **Alt** (defaults).
- Hotkeys indicator on. Pass hotkeys **off** (default).
- A folder with 150 distinguishable short sounds. Generate it with PowerShell (valid 48 kHz, 16-bit, stereo WAV, 0.3 s, each file a different tone so you can hear which one played):

```powershell
$dir = "$env:USERPROFILE\Music\VoxPad-index-test"
New-Item -ItemType Directory -Force $dir | Out-Null
$rate = 48000; $ch = 2; $sec = 0.3; $frames = [int]($rate * $sec)
for ($n = 1; $n -le 150; $n++) {
  $freq = 200 + $n * 10                              # 210 Hz … 1700 Hz, rising with the index
  $data = New-Object byte[] ($frames * $ch * 2)
  for ($i = 0; $i -lt $frames; $i++) {
    $env1 = [Math]::Min(1.0, [Math]::Min($i, $frames - $i) / 480.0)   # 10 ms fades
    $s = [int16](12000 * $env1 * [Math]::Sin(2 * [Math]::PI * $freq * $i / $rate))
    $b = [BitConverter]::GetBytes($s)
    $o = $i * 4; $data[$o] = $b[0]; $data[$o+1] = $b[1]; $data[$o+2] = $b[0]; $data[$o+3] = $b[1]
  }
  $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
  $w.Write([Text.Encoding]::ASCII.GetBytes("RIFF")); $w.Write([int32](36 + $data.Length)); $w.Write([Text.Encoding]::ASCII.GetBytes("WAVE"))
  $w.Write([Text.Encoding]::ASCII.GetBytes("fmt ")); $w.Write([int32]16); $w.Write([int16]1); $w.Write([int16]$ch)
  $w.Write([int32]$rate); $w.Write([int32]($rate * $ch * 2)); $w.Write([int16]($ch * 2)); $w.Write([int16]16)
  $w.Write([Text.Encoding]::ASCII.GetBytes("data")); $w.Write([int32]$data.Length); $w.Write($data); $w.Flush()
  [IO.File]::WriteAllBytes((Join-Path $dir ("sound{0:D3}.wav" -f $n)), $ms.ToArray())
}
"wrote 150 files to $dir"
```

## Steps

1. File > New sound list. Drag the folder `Music\VoxPad-index-test` onto the window (or File > Add folder…).
   **Expected:** Status bar "150 sound(s) added"; All sounds shows rows 1–150 in natural order (`sound001` … `sound150`), so row N = `sound{N}`. Save the list as `index-test.vpl`.
2. Select row 123 and press Enter once to learn its pitch (1430 Hz). Press Stop.
3. Click into another application (e.g. Notepad) so VoxPad is **not** focused. Hold **Alt**, type **1**, **2**, **3** on the **number row**, release **Alt**.
   **Expected:** `sound123` plays (the # column highlights row 123 as bold/playing in VoxPad, the status bar says *Playing 'sound123' → Speakers + Mic*). Notepad received no characters and no Alt-code garbage. Discord/Listener (if connected) hears it.
4. Hold Alt, type **7**, release.
   **Expected:** `sound007` plays.
5. Hold Alt, type **1**, **5**, **1**, release.
   **Expected:** No playback; status bar shows **No sound at number 151** in red.
6. Hold Alt, type **0**, release.
   **Expected:** Nothing happens (0 is ignored), no error.
7. Create a category *Sub* with only `sound140`, `sound141`, `sound142` (drag them onto the category) and select *Sub*. Hold Alt, type **1**, **2**, **3** on the number row, release.
   **Expected:** `sound123` still plays – the index gesture always refers to All sounds, regardless of the selected category.
8. Numpad variant: with *Sub* still selected, hold Alt, press **Num 2**, release.
   **Expected:** `sound141` plays – the numpad gesture uses the **#** column of the current category (row 2 of *Sub*).
9. Hold Alt, press **Num 4**, release.
   **Expected:** **No sound at number 4** (Sub has only 3 rows).
10. Preferences > Hotkeys > Numpad: change the Index modifier to **Ctrl+Alt**, OK. Hold Alt alone, type 1 2 3, release.
    **Expected:** Nothing plays (Alt-digits now only match the numpad gesture, and these were number-row digits). Hold Ctrl+Alt, type 1 2 3, release → `sound123` plays. Restore the modifier to Alt.
11. Untick **Index hotkeys enabled**, OK. Hold Alt, type 1 2 3, release.
    **Expected:** Nothing plays; if Notepad is focused it may now receive Alt-code input (the digits are no longer swallowed). Re-enable.
12. Disable hotkeys (double-click the Hotkeys indicator). Hold Alt, type 1 2 3, release.
    **Expected:** Nothing plays. Re-enable.
13. Sort the list by Duration (click the header) and repeat step 3.
    **Expected:** Still `sound123` – sorting the view does not change the All sounds index.

## Expected results summary

- Alt+1+2+3 released → row 123 of All sounds plays, from any focused app, regardless of the selected category or sort.
- Numpad digits → # column of the current category.
- Out-of-range numbers produce the red "No sound at number N" status and no playback.

## Evidence to capture

- Screenshot of the list with row 123 bold and the status bar text after step 3.
- Screenshot of the status bar after step 5 and step 9.
- The generated folder listing (`dir` output) and `index-test.vpl`.
