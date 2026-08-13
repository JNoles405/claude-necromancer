# Builds Claude Necromancer and packages it for other people to install.
#
# Produces, in dist/:
#
#   ClaudeNecromancer-<version>-win-x64.exe   the thing you attach to the release
#   ClaudeNecromancer-<version>-notes.md      the release notes, with the checksum in them
#
# Then you create the GitHub release yourself and attach the .exe. That part is not automated on
# purpose: publishing a release needs your GitHub credentials, and nothing here should be holding
# those.
#
#   pwsh -File scripts/make-release.ps1
#   pwsh -File scripts/make-release.ps1 -Notes "What changed in this one."
#
# The checksum is not decoration. The in-app updater downloads the .exe from the release, hashes
# what arrived, and refuses to run it unless the hash matches the one in these notes. A release
# published without it will be seen by the updater and deliberately not offered.

param(
    [string]$Notes = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\ClaudeNecromancer\ClaudeNecromancer.csproj'

# ── The version, from the one place it is written ─────────────────────────
$csproj = Get-Content $proj -Raw
if ($csproj -notmatch '<Version>\s*([0-9]+)\.([0-9]+)\.([0-9]+)\s*</Version>') {
    throw "Could not read <Version> out of $proj"
}

$major = [int]$Matches[1]; $minor = [int]$Matches[2]; $patch = [int]$Matches[3]

# Three spellings of the same number, and they are not interchangeable:
#   $version  1.0.0       what the csproj and the assembly carry
#   $padded   1.00.00     the x.xx.xx form — used for tags and filenames
#   $display  v1.00.00    what a person reads
$version = "$major.$minor.$patch"
$padded  = "{0}.{1:d2}.{2:d2}" -f $major, $minor, $patch
$display = "v$padded"
$tag     = "v$padded"

Write-Host "Claude Necromancer $display (assembly $version)" -ForegroundColor Cyan

$outDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$assetName = "ClaudeNecromancer-$padded-win-x64.exe"
$assetPath = Join-Path $outDir $assetName

# ── Build ─────────────────────────────────────────────────────────────────
# Self-contained and single-file: the updater's install step replaces one .exe, so the release has
# to BE one .exe. Self-contained also means it runs on a machine with no .NET installed.
if (-not $SkipBuild) {
    $publishDir = Join-Path $root 'dist\publish'
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish $proj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }

    Copy-Item (Join-Path $publishDir 'ClaudeNecromancer.exe') $assetPath -Force
}

if (-not (Test-Path $assetPath)) { throw "No asset at $assetPath — run without -SkipBuild." }

# ── Checksum ──────────────────────────────────────────────────────────────
$sha = (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToLower()
$size = [math]::Round((Get-Item $assetPath).Length / 1MB, 2)

Write-Host "  $assetName  ($size MB)" -ForegroundColor Green
Write-Host "  sha256: $sha" -ForegroundColor DarkGray

# ── Notes ─────────────────────────────────────────────────────────────────
# The updater looks for a 64-character hex run on a line that also names the asset, so the table
# row below is what makes the release installable. Do not reformat it away.
$notesPath = Join-Path $outDir "ClaudeNecromancer-$padded-notes.md"

$body = @"
# Claude Necromancer $display

$Notes

## Install

Download **$assetName** below and run it. It is self-contained, so no .NET install is needed.
If you already have Claude Necromancer, use **Updates → Check for updates** inside the app instead.

## Checksum

| File | SHA-256 |
| ---- | ------- |
| $assetName | ``$sha`` |

The in-app updater verifies this hash before it will install anything. A download that does not
match is deleted rather than run.
"@

Set-Content -Path $notesPath -Value $body -Encoding UTF8
Write-Host "  $([IO.Path]::GetFileName($notesPath))" -ForegroundColor Green

Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  git tag $tag && git push origin $tag"
Write-Host "  gh release create $tag `"$assetPath`" --title `"$display`" --notes-file `"$notesPath`""
