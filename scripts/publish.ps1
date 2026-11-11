<#
.SYNOPSIS
    Builds the portable win-x64 release payloads for Win Service Buddy and zips them.

.DESCRIPTION
    Produces the two assets published on the GitHub Releases page, each with a
    sha256sum-style checksum file next to it:

        wsbuddy-app-win-x64-vX.Y.Z.zip (+ .sha256)   the desktop GUI (Win Service Buddy is GUI-first)
        wsbuddy-cli-win-x64-vX.Y.Z.zip (+ .sha256)   the wsbuddy.exe CLI

    Do not rename these assets: downstream launchers key off the "app" vs "cli"
    token to prefer the GUI.

    The version is read from Directory.Build.props so it never has to be passed by hand.
    The version used is also stamped into the binaries (Version, AssemblyVersion,
    FileVersion, InformationalVersion).
    Both payloads are self-contained (no .NET runtime needed on the target machine) and
    ship the README, the LICENCE and the example profiles.

    The ZIPs are written with forward-slash entry names in ordinal order, so the layout
    is identical whichever machine or PowerShell version builds them.

    -p:DebugType=none suppresses the managed .pdb files, but the native SkiaSharp and
    HarfBuzz .pdb files come straight out of their NuGet packages and must be deleted
    explicitly: libSkiaSharp.pdb alone is ~84 MB and leaving them takes the GUI ZIP from
    roughly 50 MB to 76 MB.

    build/release.ps1 calls this script (output in the git-ignored release\ folder),
    and the release workflow calls build/release.ps1, so local and CI ZIPs match.
    The Chocolatey package checksums are stamped from these ZIPs by build/choco-pack.ps1.

.PARAMETER Configuration
    MSBuild configuration to publish. Defaults to Release.

.PARAMETER Runtime
    .NET runtime identifier to publish. Defaults to win-x64, which matches the asset names.

.PARAMETER OutputRoot
    Directory for the staged payloads and the ZIPs. Defaults to <repo>/artifacts.

.PARAMETER Version
    Overrides the version read from Directory.Build.props, for example 0.2.1 or
    0.2.1-rc.1 (no leading v). Used by build/release.ps1 to build from a tag.

.EXAMPLE
    ./scripts/publish.ps1

.EXAMPLE
    ./scripts/publish.ps1 -OutputRoot D:\out
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot,
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot 'Directory.Build.props'

if (-not $Version) {
    if (-not (Test-Path $propsPath)) {
        throw "Directory.Build.props not found at $propsPath"
    }

    $Version = ([xml](Get-Content -Path $propsPath -Raw)).Project.PropertyGroup.Version
    if (-not $Version) {
        throw "Could not read <Version> from $propsPath"
    }
}

$Version = $Version.Trim()
if ($Version -notmatch '^(\d+\.\d+\.\d+)(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Version '$Version' does not look like X.Y.Z or X.Y.Z-prerelease"
}
$coreVersion = $Matches[1]

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'artifacts'
}

Write-Host "Win Service Buddy publish" -ForegroundColor Cyan
Write-Host "  version       : $Version"
Write-Host "  configuration : $Configuration"
Write-Host "  runtime       : $Runtime"
Write-Host "  output        : $OutputRoot"
Write-Host ''

# GUI first: the desktop app is the primary deliverable, the CLI ships alongside it.
$targets = @(
    [pscustomobject]@{
        Name          = 'app'
        Project       = 'src/WinServiceBuddy.App'
        ExtraMsBuild  = @()
        ExpectedExe   = 'WinServiceBuddy.App.exe'
    }
    [pscustomobject]@{
        Name          = 'cli'
        Project       = 'src/WinServiceBuddy.Cli'
        ExtraMsBuild  = @('-p:PublishSingleFile=true')
        ExpectedExe   = 'wsbuddy.exe'
    }
)

$payloadExtras = @('README.md', 'LICENSE')
$exampleProfiles = Join-Path $repoRoot 'profiles/examples'

if (-not (Test-Path $OutputRoot)) {
    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
}
$OutputRoot = (Resolve-Path -LiteralPath $OutputRoot).ProviderPath

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-DeterministicZip([string]$SourceDir, [string]$ZipPath) {
    $src = (Resolve-Path -LiteralPath $SourceDir).ProviderPath.TrimEnd('\')
    $map = @{}
    foreach ($f in @(Get-ChildItem -LiteralPath $src -Recurse -File)) {
        $map[$f.FullName.Substring($src.Length + 1).Replace('\', '/')] = $f.FullName
    }
    [string[]]$entries = @($map.Keys)
    [Array]::Sort($entries, [StringComparer]::Ordinal)
    $stream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in $entries) {
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, $map[$entry], $entry, [System.IO.Compression.CompressionLevel]::Optimal)
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    return $entries.Count
}

$results = @()

foreach ($target in $targets) {
    $stageDir = Join-Path $OutputRoot $target.Name
    $zipName = "wsbuddy-{0}-{1}-v{2}.zip" -f $target.Name, $Runtime, $Version
    $zipPath = Join-Path $OutputRoot $zipName
    $shaPath = "$zipPath.sha256"

    Write-Host "==> publishing $($target.Project) -> $stageDir" -ForegroundColor Yellow

    if (Test-Path $stageDir) {
        Remove-Item $stageDir -Recurse -Force
    }

    $publishArgs = @(
        'publish'
        (Join-Path $repoRoot $target.Project)
        '-c', $Configuration
        '-r', $Runtime
        '--self-contained', 'true'
        '-p:DebugType=none'
        "-p:Version=$Version"
        "-p:AssemblyVersion=$coreVersion.0"
        "-p:FileVersion=$coreVersion.0"
        "-p:InformationalVersion=$Version"
    ) + $target.ExtraMsBuild + @('-o', $stageDir)

    & dotnet @publishArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($target.Project) (exit $LASTEXITCODE)"
    }

    $exePath = Join-Path $stageDir $target.ExpectedExe
    if (-not (Test-Path $exePath)) {
        throw "Expected $($target.ExpectedExe) in $stageDir but it is missing"
    }

    # Native SkiaSharp/HarfBuzz symbols are NOT covered by -p:DebugType=none.
    $pdbs = @(Get-ChildItem -Path $stageDir -Filter '*.pdb' -Recurse -File -ErrorAction SilentlyContinue)
    if ($pdbs.Count -gt 0) {
        $pdbMb = [math]::Round((($pdbs | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
        Write-Host "    stripping $($pdbs.Count) .pdb file(s) ($pdbMb MB)"
        $pdbs | Remove-Item -Force
    }

    foreach ($extra in $payloadExtras) {
        $extraPath = Join-Path $repoRoot $extra
        if (Test-Path $extraPath) {
            Copy-Item $extraPath $stageDir -Force
        }
        else {
            Write-Warning "  $extra not found at repo root; omitted from the $($target.Name) payload"
        }
    }

    if (Test-Path $exampleProfiles) {
        Copy-Item $exampleProfiles (Join-Path $stageDir 'profiles') -Recurse -Force
    }
    else {
        Write-Warning "  profiles/examples not found; omitted from the $($target.Name) payload"
    }

    Write-Host "    zipping -> $zipPath"
    foreach ($p in @($zipPath, $shaPath)) {
        if (Test-Path $p) { Remove-Item $p -Force }
    }
    $entryCount = New-DeterministicZip -SourceDir $stageDir -ZipPath $zipPath

    $hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText($shaPath, "$($hash.ToLowerInvariant())  $zipName`n", (New-Object System.Text.ASCIIEncoding))

    $zipItem = Get-Item $zipPath
    $results += [pscustomobject]@{
        Name       = $target.Name
        Asset      = $zipItem.Name
        Path       = $zipItem.FullName
        Sha256File = $shaPath
        Entries    = $entryCount
        SizeMB     = [math]::Round($zipItem.Length / 1MB, 1)
        Bytes      = $zipItem.Length
        SHA256     = $hash
    }

    Write-Host ''
}

Write-Host "Release assets for v$Version" -ForegroundColor Cyan
$results | Format-Table Asset, Entries, SizeMB, Bytes -AutoSize | Out-Host

Write-Host "SHA256 (also written next to each ZIP as <zip>.sha256):" -ForegroundColor Cyan
foreach ($result in $results) {
    Write-Host ("  {0}" -f $result.Asset)
    Write-Host ("    {0}" -f $result.SHA256)
}

Write-Host ''
Write-Host 'Done. Nothing was uploaded; releases are published by .github/workflows/release.yml when a v* tag is pushed.' -ForegroundColor Green

# Emit the results for callers (build/release.ps1).
$results
