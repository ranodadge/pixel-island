# Builds dist\DynamicIslandSetup.exe: one file that contains the app and the .NET runtime.
# Double-clicking it on any 64-bit Windows 10/11 PC installs Dynamic Island (no admin rights, no .NET needed).
# Usage: powershell -ExecutionPolicy Bypass -File make-setup.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$stage = Join-Path $root 'obj\setup-publish'
$dist = Join-Path $root 'dist'

dotnet publish (Join-Path $root 'DynamicIsland.csproj') -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -o $stage --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# The publish leaves an intermediate win-x64 build inside the regular Release output folder.
# Remove it so that "DynamicIsland.exe --install" from that folder does not copy ~270 extra files.
$leftover = Join-Path $root 'bin\Release\net9.0-windows10.0.19041.0\win-x64'
if (Test-Path $leftover) { Remove-Item $leftover -Recurse -Force }

New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $stage 'DynamicIsland.exe') (Join-Path $dist 'DynamicIslandSetup.exe') -Force
Get-Item (Join-Path $dist 'DynamicIslandSetup.exe') | Select-Object FullName, @{n='MB';e={[math]::Round($_.Length / 1MB, 1)}}
