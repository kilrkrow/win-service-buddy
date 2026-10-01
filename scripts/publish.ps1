<#
.SYNOPSIS
    Builds the portable win-x64 release payloads for Win Service Buddy and zips them.

.DESCRIPTION
    Produces the two assets published on the GitHub Releases page:

        wsbuddy-app-win-x64-vX.Y.Z.zip   the desktop GUI (Win Service Buddy is GUI-first)
        wsbuddy-cli-win-x64-vX.Y.Z.zip   the wsbuddy.exe CLI

    Do not rename these assets: downstream launchers key off the "app" vs "cli"
    token to prefer the GUI.

    The version is read from Directory.Build.props so it never has to be passed by hand.
    Both payloads are self-contained (no .NET runtime needed on the target machine) and
    ship the README, the LICENCE and the example profiles.

    -p:DebugType=none suppresses the managed .pdb files, but the native SkiaSharp and
    HarfBuzz .pdb files come straight out of their NuGet packages and must be deleted
    explicitly: libSkiaSharp.pdb alone is ~84 MB and leaving them takes the GUI ZIP from
    roughly 50 MB to 76 MB.

    The SHA256 of each ZIP is printed at the end. Those values are pinned in
    pack/chocolatey/wsbuddy/tools/chocolateyinstall.ps1 and VERIFICATION.txt.

.PARAMETER Configuration
    MSBuild configuration to publish. Defaults to Release.

.PARAMETER Runtime
    .NET runtime identifier to publish. Defaults to win-x64, which matches the asset names.

.PARAMETER OutputRoot
    Directory for the staged payloads and the ZIPs. Defaults to <repo>/artifacts.

.PARAMETER Version
    Overrides the version read from Directory.Build.props. For local test builds only;
    a real release should bump Directory.Build.props instead.

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
if ($Version -notmatch '^\d+\.\d+\.\d+') {
    throw "Version '$Version' does not look like X.Y.Z"
}

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

$results = @()

foreach ($target in $targets) {
    $stageDir = Join-Path $OutputRoot $target.Name
    $zipPath = Join-Path $OutputRoot ("wsbuddy-{0}-{1}-v{2}.zip" -f $target.Name, $Runtime, $Version)

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
    ) + $target.ExtraMsBuild + @('-o', $stageDir)

    & dotnet @publishArgs
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
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force

    $zipItem = Get-Item $zipPath
    $results += [pscustomobject]@{
        Asset  = $zipItem.Name
        SizeMB = [math]::Round($zipItem.Length / 1MB, 1)
        Bytes  = $zipItem.Length
        SHA256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
    }

    Write-Host ''
}

Write-Host "Release assets for v$Version" -ForegroundColor Cyan
$results | Format-Table Asset, SizeMB, Bytes -AutoSize

Write-Host "SHA256 (pin these in pack/chocolatey/wsbuddy/tools/chocolateyinstall.ps1 and VERIFICATION.txt):" -ForegroundColor Cyan
foreach ($result in $results) {
    Write-Host ("  {0}" -f $result.Asset)
    Write-Host ("    {0}" -f $result.SHA256)
}

Write-Host ''
Write-Host 'Done. Nothing was uploaded; publish the GitHub release manually.' -ForegroundColor Green
