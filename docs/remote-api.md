# Remote Control API

Local-only control channel for Stream Deck, Touch Portal, Loupedeck, macro tools and scripts.

* **Transport:** Windows named pipe `\\.\pipe\VoxPadRemote`, duplex, byte mode, `PipeOptions.CurrentUserOnly` (only processes of the same user can connect).
* **Framing:** one request per line (`\n`), one JSON response line per request, UTF-8 without BOM. Keep the connection open for as many requests as you like.
* **Requests:** a JSON object `{"cmd": "...", ...}` or the plain-text shorthand shown in the table.
* **Responses:** always `{"ok": true|false, ...}`; on failure `error` holds the reason.

## Commands

| JSON | Shorthand | Response fields |
|------|-----------|-----------------|
| `{"cmd":"version"}` | `version` | `version` |
| `{"cmd":"status"}` | `status` | `mode`, `playing`, `paused`, `index`, `tag`, `positionSec`, `durationSec`, `level` |
| `{"cmd":"list"}` | `list` | `sounds[]` → `index`, `id`, `tag`, `color` (`#RRGGBB` or null), `durationSec`, `hotkey`, `playCount`, `missing` |
| `{"cmd":"categories"}` | `categories` | `categories[]` → `id`, `name`, `parent`, `soundCount` |
| `{"cmd":"play","index":3}` | `play 3` | status fields |
| `{"cmd":"play","name":"air horn"}` | `play air horn` | exact tag match first, then substring (case-insensitive) |
| `{"cmd":"play","id":"<guid>"}` | – | |
| `{"cmd":"play","index":3,"target":"mic"}` | `play 3 mic` | target `auto` (current play mode, default), `mic`, `speakers`, `both` |
| `{"cmd":"random"}` / `{"cmd":"random","category":"<guid>"}` | `random` | |
| `{"cmd":"stop"}` | `stop` | |
| `{"cmd":"pause"}` | `pause` | toggles pause/resume |
| `{"cmd":"playMode"}` / `{"cmd":"playMode","mode":"mic"}` | `playmode` / `playmode mic` | `mode` = `both` \| `mic` \| `speakers` |
| `{"cmd":"volume"}` / `{"cmd":"volume","level":0.5}` | `volume` / `volume 0.5` | `level` 0..1 (speaker copy only) |

`index` is the 1-based row in the hidden *All sounds* category (the same number index hotkeys use). Remote plays count as microphone plays for statistics and the trial limit when the target includes the microphone; they never trigger Repeat/Continue.

## Example session

```
> {"cmd":"list"}
{"ok":true,"sounds":[{"index":1,"id":"…","tag":"Air horn","color":"#E57373","durationSec":1.9,"hotkey":"Ctrl+F5","playCount":12,"missing":false}, …]}
> play 1 mic
{"ok":true,"mode":"both","playing":true,"paused":false,"index":1,"tag":"Air horn","positionSec":0.0,"durationSec":1.9,"level":0.8}
> stop
{"ok":true,"mode":"both","playing":false,"paused":false,"positionSec":0.0,"durationSec":0.0,"level":0.8}
```

## Reference client

`voxpad-remote.exe` (project `src/VoxPad.RemoteClient`, ~60 lines) shows the whole handshake:

```
voxpad-remote list
voxpad-remote play 3 mic
voxpad-remote raw "{\"cmd\":\"play\",\"name\":\"air horn\"}"
voxpad-remote            # interactive shell
```

Exit codes: 0 ok, 1 VoxPad not running / transport error, 2 VoxPad answered `ok:false`.

## Writing a plugin (PowerShell)

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'VoxPadRemote', [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(3000)
$w = New-Object System.IO.StreamWriter($pipe); $w.AutoFlush = $true; $w.NewLine = "`n"
$r = New-Object System.IO.StreamReader($pipe)
$w.WriteLine('{"cmd":"play","index":1}'); $r.ReadLine()
```

Node (Stream Deck plugin): `net.connect('\\\\.\\pipe\\VoxPadRemote')`, write `JSON.stringify(req) + '\n'`, split incoming data on `\n`. Colours from `list` map directly onto key backgrounds.
