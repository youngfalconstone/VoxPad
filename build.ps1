<#
.SYNOPSIS
  Publishes VoxPad (framework-dependent or self-contained) for x64 and/or ARM64 and builds the MSI installers.
.EXAMPLE
  .\build.ps1
  .\build.ps1 -Platform x64 -SelfContained
  .\build.ps1 -Version 1.2.0 -SkipInstaller
#>
param(
    [ValidateSet('x64', 'ARM64', 'both')] [string] $Platform = 'both',
    [switch] $FrameworkDependent,
    [switch] $SkipInstaller,
    [switch] $SkipTests,
    [string] $Version = ''
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK not found. Install .NET 8 SDK or set DOTNET_ROOT/PATH.' }

$SelfContained = -not $FrameworkDependent
$versionArg = @()
if ($Version) { $versionArg = @("-p:Version=$Version") }
else {
    [xml]$props = Get-Content Directory.Build.props
    $Version = $props.Project.PropertyGroup.Version
}
Write-Host "VoxPad $Version" -ForegroundColor Cyan

if (-not $SkipTests) {
    dotnet test tests/VoxPad.Tests -c Release --nologo
    if ($LASTEXITCODE) { throw 'Unit tests failed' }
}

$targets = if ($Platform -eq 'both') { @('x64', 'ARM64') } else { @($Platform) }
foreach ($t in $targets) {
    $rid = if ($t -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $out = "artifacts/publish/$rid"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    # Self-contained builds are bundled into one exe (runtime, WPF native libs and language resources included).
    $singleFile = @()
    if ($SelfContained) {
        $singleFile = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
                        '-p:EnableCompressionInSingleFile=true', '-p:DebugType=embedded')
    }
    Write-Host "Publishing $rid (self-contained=$sc)…" -ForegroundColor Cyan
    dotnet publish src/VoxPad.App -c Release -r $rid --self-contained $sc -o $out --nologo @versionArg @singleFile
    if ($LASTEXITCODE) { throw "publish failed ($rid)" }
    dotnet publish src/VoxPad.RemoteClient -c Release -r $rid --self-contained $sc -o $out --nologo @versionArg @singleFile
    if ($LASTEXITCODE) { throw "publish failed (remote client, $rid)" }
    Copy-Item src/VoxPad.App/Assets/portable.txt.template $out -Force
    New-Item -ItemType Directory -Force (Join-Path $out 'docs') | Out-Null
    Copy-Item docs/manual.html (Join-Path $out 'docs') -Force
    # Portable zip
    New-Item -ItemType Directory -Force artifacts | Out-Null
    $zip = "artifacts/VoxPad-$Version-$rid-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path "$out/*" -DestinationPath $zip
    Write-Host "  → $zip"
    if ($SelfContained) {
        # The exe alone is the whole app: one file to download and run.
        $exe = "artifacts/VoxPad-$Version-$rid.exe"
        Copy-Item (Join-Path $out 'VoxPad.exe') $exe -Force
        Write-Host "  → $exe"
    }

    if (-not $SkipInstaller) {
        Write-Host "Building MSI ($t)…" -ForegroundColor Cyan
        $scDefine = if ($SelfContained) { '-p:SelfContained=true' } else { '-p:SelfContained=false' }
        dotnet build installer/VoxPad.Installer.wixproj -c Release -p:Platform=$t "-p:PublishDir=../$out" "-p:Version=$Version" $scDefine --nologo
        if ($LASTEXITCODE) { throw "installer build failed ($t)" }
        Get-ChildItem artifacts/installer -Filter *.msi | ForEach-Object { Write-Host "  → $($_.FullName)" }
    }
}
Write-Host 'Done.' -ForegroundColor Green
