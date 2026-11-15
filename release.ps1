# Publishes a new version to GitHub: bumps the version, builds the setup file, commits, tags, pushes,
# and creates a GitHub release with dist\DynamicIslandSetup.exe attached.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File release.ps1 -Version 1.1.0
#   powershell -ExecutionPolicy Bypass -File release.ps1 -Version 1.1.0 -NotesFile notes.md
#
# -NotesFile: a UTF-8 text/markdown file with the release notes. Without it, GitHub writes the notes
#             from the commits since the previous release.
param(
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$NotesFile = ""
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3" }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$tag = "v$Version"

function Run([string]$what, [scriptblock]$command) {
  & $command
  if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# GitHub CLI
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
if (-not (Test-Path $gh)) { throw "GitHub CLI (gh) not found. Install it with: winget install GitHub.cli" }

# If gh itself is not signed in, reuse the GitHub login that Git already has stored on this PC.
# The token stays in this process only and is never printed.
cmd /c "`"$gh`" auth status >nul 2>&1"
if ($LASTEXITCODE -ne 0 -and -not $env:GH_TOKEN) {
  # (the request goes through a plain file so that PowerShell's pipe encoding cannot garble it)
  $ask = [IO.Path]::GetTempFileName()
  [IO.File]::WriteAllText($ask, "protocol=https`nhost=github.com`n`n")
  $answer = cmd /c "git credential fill < `"$ask`""
  Remove-Item $ask
  $line = $answer | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
  if (-not $line) { throw "No GitHub login found. Run: gh auth login" }
  $env:GH_TOKEN = $line.Substring('password='.Length)
}

if (git tag -l $tag) { throw "Tag $tag already exists. Pick a higher version." }
if ($NotesFile -and -not (Test-Path $NotesFile)) { throw "Notes file not found: $NotesFile" }

# 1) version number in the project file (kept as UTF-8 without BOM)
$csproj = Join-Path $root 'DynamicIsland.csproj'
$text = [IO.File]::ReadAllText($csproj)
$text = $text -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
[IO.File]::WriteAllText($csproj, $text, (New-Object Text.UTF8Encoding($false)))

# 2) setup file
Run "setup build" { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'make-setup.ps1') }
$setup = Join-Path $root 'dist\DynamicIslandSetup.exe'
if (-not (Test-Path $setup)) { throw "Setup file was not created" }

# 3) commit (only if something changed), tag, push
Run "git add" { git add -A }
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { Run "git commit" { git commit -m "Release $tag" } }
Run "git tag" { git tag $tag }
Run "git push" { git push origin HEAD }
Run "git push tag" { git push origin $tag }

# 4) GitHub release with the setup file attached
if ($NotesFile) {
  Run "release" { & $gh release create $tag $setup --title "Dynamic Island $tag" --notes-file $NotesFile }
} else {
  Run "release" { & $gh release create $tag $setup --title "Dynamic Island $tag" --generate-notes }
}
"Released $tag"
