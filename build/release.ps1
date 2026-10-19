<#
.SYNOPSIS
  Release-spine entry point: builds the Win Service Buddy release ZIPs plus
  .sha256 files into <repo>\release (git-ignored).

.DESCRIPTION
  Thin wrapper around scripts/publish.ps1 that takes a tag-style version.
  The GitHub Actions release workflow (.github/workflows/release.yml) calls
  this same script, so a local run produces the same file names and the same
  ZIP layout as the assets attached to the GitHub release:

    release\wsbuddy-app-win-x64-<tag>.zip   + .zip.sha256
    release\wsbuddy-cli-win-x64-<tag>.zip   + .zip.sha256

  Works in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Version
  Tag-style version, for example v0.2.1 or v0.2.1-rc.1. Defaults to "v" plus
  the <Version> in Directory.Build.props.

.PARAMETER OutDir
  Output folder. Defaults to <repo>\release.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\release.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\release.ps1 -Version v0.2.1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'release' }

if (-not $Version) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    $node = $props.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $node -or -not $node.InnerText) { throw 'No <Version> in Directory.Build.props' }
    $Version = 'v' + $node.InnerText.Trim()
}
if ($Version -notmatch '^v(\d+\.\d+\.\d+)(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Version '$Version' is not a release tag like v1.2.3 or v1.2.3-rc.1"
}

$results = @(& (Join-Path $repoRoot 'scripts\publish.ps1') -Version $Version.Substring(1) -OutputRoot $OutDir |
    Where-Object { $null -ne $_ -and $null -ne $_.PSObject.Properties['Sha256File'] })
if ($results.Count -ne 2) { throw "Expected 2 release ZIPs, got $($results.Count)" }

foreach ($r in $results) {
    $expected = ((Get-Content -LiteralPath $r.Sha256File -Raw).Trim() -split '\s+')[0]
    $actual = (Get-FileHash -LiteralPath $r.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "sha256 mismatch for $($r.Asset)" }
    Write-Host "sha256 OK  $actual  $($r.Asset)"
}

if ($env:GITHUB_OUTPUT) {
    $app = $results | Where-Object { $_.Name -eq 'app' }
    $cli = $results | Where-Object { $_.Name -eq 'cli' }
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding ascii -Value @(
        "release_dir=$((Resolve-Path -LiteralPath $OutDir).ProviderPath)",
        "app_zip=$($app.Path)", "app_sha256_file=$($app.Sha256File)", "app_sha256=$($app.SHA256)",
        "cli_zip=$($cli.Path)", "cli_sha256_file=$($cli.Sha256File)", "cli_sha256=$($cli.SHA256)")
}
