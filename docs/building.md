# Building VoxPad

## Prerequisites

| What | Why |
|------|-----|
| Windows 10 1809+ / Windows 11, x64 or ARM64 | WASAPI, Media Foundation, SAPI, low-level hooks |
| .NET 8 SDK (8.0.100 or later) | `dotnet build`; https://dotnet.microsoft.com/download/dotnet/8.0 |
| Internet access on the first build | NuGet packages (NAudio, NAudio.Vorbis, Concentus, TagLibSharp, System.Speech, xunit, WixToolset.Sdk) |
| Optional: VB-CABLE | runtime only – the virtual microphone. https://vb-audio.com/Cable |

No Visual Studio, no WDK, no C++ toolchain is required. The WiX v5 toolset is pulled in as an SDK-style NuGet package by the installer project.

If the SDK is installed per-user (as `dotnet-install.ps1 -InstallDir` does) make sure `DOTNET_ROOT` points at it before running the built executable, otherwise the app host reports "You must install .NET".

## Build everything

```powershell
cd VoxPad
dotnet build VoxPad.sln -c Release
dotnet test  tests/VoxPad.Tests -c Release
```

Outputs: `src/VoxPad.App/bin/x64/Release/net8.0-windows/VoxPad.exe` (x64, framework-dependent, needs the .NET 8 Desktop Runtime; a dev build. Releases come from `build.ps1`, below).

ARM64: publish with `-r win-arm64`; the architecture follows the runtime identifier.

## Publish + installer

`build.ps1` does all of it:

```powershell
.\build.ps1                      # x64 + ARM64 standalone single-file VoxPad.exe (runtime bundled), portable zip, MSI
.\build.ps1 -FrameworkDependent  # small build that needs the .NET 8 Desktop Runtime
.\build.ps1 -Platform x64 -SkipInstaller
```

Manually:

```powershell
$sf = '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true'
dotnet publish src/VoxPad.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 @sf
dotnet publish src/VoxPad.RemoteClient -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 @sf
dotnet build installer/VoxPad.Installer.wixproj -c Release -p:Platform=x64 -p:PublishDir=../artifacts/publish/win-x64 -p:Version=1.0.0
```

The MSI lands in `artifacts/installer/`. It installs per-machine (UAC prompt), creates `%ProgramData%\VoxPad\logs` and `crashdumps` writable by Users, registers `.vpl`, adds Start-menu shortcuts and offers to launch the app. Major upgrades replace older versions in place.

### Portable build

Copy the publish folder anywhere and create an empty `portable.txt` next to `VoxPad.exe` (a template `portable.txt.template` is included). Preferences, lists, backups, cache, logs and crash dumps then live in `Data\` beside the executable.

## Version

Set `<Version>` in `Directory.Build.props`; `build.ps1 -Version 1.2.3` overrides it for the publish and the MSI name.

## Signing

* **Code signing** of `VoxPad.exe` and the MSI is your usual `signtool sign /fd SHA256 /tr <timestamp url> ...` step after `build.ps1`; nothing in the build depends on it.
* **Licence serials and update manifests** are signed with the P-256 key pair handled by `tools/KeyGen`:
  1. `voxpad-keygen newkey` → paste the PUBLIC key into `LicenseManager.PublicKeyBase64` (`src/VoxPad.Core/Licensing/License.cs`) and rebuild. Keep the private key out of the repository.
  2. `voxpad-keygen issue <private> "Jane Streamer" 2` → serial for a customer.
  3. `voxpad-keygen sign-update <private> VoxPad-1.1.0-x64.msi` → `sha256` + `signature` for `update.json`:
     ```json
     { "version": "1.1.0", "url": "https://example.com/VoxPad-1.1.0-x64.msi", "sha256": "…", "signature": "…", "notes": "What changed" }
     ```
  No private key is stored in the repository. The unit tests generate a throwaway key pair at run time. Trial limits and registration are off in the free build (`LicenseManager.TrialLimitsEnabled = false`).

## Running the tests

```powershell
dotnet test tests/VoxPad.Tests -c Release --logger "console;verbosity=normal"
```

The audio tests use synthetic buffers and temporary WAV files; they do not need a sound card. Manual acceptance checklists are in `tests/acceptance/`.

## Repository layout

```
Directory.Build.props   shared build settings (nullable, GC mode, satellite languages, version)
global.json             pins the SDK major version (rolls forward within 8.0.x)
VoxPad.sln
src/                    Core, Audio, App, RemoteClient
tools/KeyGen            vendor tooling (never shipped)
tools/CableCheck        voxpad-cablecheck: record an endpoint (e.g. CABLE Output) and print levels; setdefault restores Windows default devices
installer/              WiX v5 project (Package.wxs, License.rtf)
tests/                  unit tests + acceptance checklists
docs/                   manual.html, audio-routing.md, remote-api.md
```
