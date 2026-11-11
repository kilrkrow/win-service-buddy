<#
.SYNOPSIS
  Stages and packs the wsbuddy Chocolatey package from the built release ZIPs
  (never pushes).

.DESCRIPTION
  Copies pack/chocolatey/wsbuddy into a staging folder and stamps it from the
  release tag and the ZIPs produced by build/release.ps1:
    - wsbuddy.nuspec          <version>, <releaseNotes>
    - chocolateyinstall.ps1   $version, $cliChecksum, $appChecksum (and $baseUrl if -BaseUrl)
    - VERIFICATION.txt        regenerated with the same version and checksums
  then runs `choco pack`. Pushing is done only by the release workflow, and
  only for real releases.

  The package does not embed binaries: at install time it downloads the two
  ZIPs from the GitHub release for the tag and checks their SHA256.

.PARAMETER Version
  Release tag, for example v0.2.1 or v0.2.1-rc.1.

.PARAMETER ReleaseDir
  Folder with wsbuddy-app/cli-win-x64-<tag>.zip. Defaults to <repo>\release.

.PARAMETER OutDir
  Folder for the staging folder and the .nupkg. Defaults to <repo>\release\choco.

.PARAMETER BaseUrl
  Test-only override of the download base URL (for example a file:/// URL to
  ReleaseDir), so CI can install-test a package for a draft release whose
  assets are not public. Never used for the package that is pushed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ReleaseDir,
    [string]$OutDir,
    [string]$BaseUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReleaseDir) { $ReleaseDir = Join-Path $repoRoot 'release' }
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'release\choco' }

if ($Version -notmatch '^v(\d+\.\d+\.\d+)(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Version '$Version' is not a release tag like v1.2.3 or v1.2.3-rc.1"
}
$pkgVersion = $Version.Substring(1)

$appZip = Join-Path $ReleaseDir "wsbuddy-app-win-x64-$Version.zip"
$cliZip = Join-Path $ReleaseDir "wsbuddy-cli-win-x64-$Version.zip"
foreach ($z in @($appZip, $cliZip)) { if (-not (Test-Path -LiteralPath $z)) { throw "Release ZIP not found: $z" } }
$appSha = (Get-FileHash -LiteralPath $appZip -Algorithm SHA256).Hash
$cliSha = (Get-FileHash -LiteralPath $cliZip -Algorithm SHA256).Hash

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outFull = (Resolve-Path -LiteralPath $OutDir).ProviderPath
$stage = Join-Path $outFull 'pkg'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $repoRoot 'pack\chocolatey\wsbuddy') -Destination $stage -Recurse
$tools = Join-Path $stage 'tools'
$ascii = New-Object System.Text.ASCIIEncoding

function Set-Line([string]$Text, [string]$Pattern, [string]$Replacement) {
    $rx = New-Object System.Text.RegularExpressions.Regex($Pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if ($rx.Matches($Text).Count -ne 1) { throw "Expected exactly one match for /$Pattern/ in chocolateyinstall.ps1" }
    return $rx.Replace($Text, $Replacement.Replace('$', '$$'))
}

# chocolateyinstall.ps1
$installPath = Join-Path $tools 'chocolateyinstall.ps1'
$install = [System.IO.File]::ReadAllText($installPath)
$install = Set-Line $install '^\$version = .*$' "`$version = '$pkgVersion'"
$install = Set-Line $install '^\$cliChecksum = .*$' "`$cliChecksum = '$cliSha'"
$install = Set-Line $install '^\$appChecksum = .*$' "`$appChecksum = '$appSha'"
if ($BaseUrl) {
    Write-Warning "TEST PACKAGE: download base URL overridden to $BaseUrl"
    $install = Set-Line $install '^\$baseUrl = .*$' "`$baseUrl = '$BaseUrl'"
}
[System.IO.File]::WriteAllText($installPath, $install, $ascii)

# VERIFICATION.txt
$verification = @"
VERIFICATION
Verification is intended to assist the Chocolatey moderators and community
in verifying that this package's contents are trustworthy.

1. Download the official release assets from:
   https://github.com/kilrkrow/win-service-buddy/releases/tag/$Version

   - wsbuddy-cli-win-x64-$Version.zip
   - wsbuddy-app-win-x64-$Version.zip

   Each asset has a matching .sha256 file on the same release page.

2. Obtain the SHA256 checksum of each file, for example:

   Get-FileHash .\wsbuddy-cli-win-x64-$Version.zip -Algorithm SHA256
   Get-FileHash .\wsbuddy-app-win-x64-$Version.zip -Algorithm SHA256

3. Compare to the values used in tools\chocolateyinstall.ps1:

   CLI: $cliSha
   APP: $appSha

The package does not embed the binaries; it downloads them from GitHub Releases at install time.

The release assets are built from source for the tag by the GitHub Actions
release workflow (.github/workflows/release.yml), which runs build/release.ps1
(scripts/publish.ps1) and stamps these checksums with build/choco-pack.ps1.

The software is licensed under the Prosperity Public License 3.0.0
(SPDX: Prosperity-3.0.0); see LICENSE.txt in this directory.
"@
[System.IO.File]::WriteAllText((Join-Path $tools 'VERIFICATION.txt'), $verification.Replace("`r`n", "`n").Replace("`n", "`r`n"), $ascii)

# wsbuddy.nuspec
$nuspecPath = Join-Path $stage 'wsbuddy.nuspec'
[xml]$nuspec = Get-Content -LiteralPath $nuspecPath -Raw -Encoding UTF8
$ns = New-Object System.Xml.XmlNamespaceManager($nuspec.NameTable)
$ns.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
$nuspec.SelectSingleNode('/n:package/n:metadata/n:version', $ns).InnerText = $pkgVersion
$nuspec.SelectSingleNode('/n:package/n:metadata/n:releaseNotes', $ns).InnerText = "https://github.com/kilrkrow/win-service-buddy/releases/tag/$Version"
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$settings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($nuspecPath, $settings)
try { $nuspec.Save($writer) } finally { $writer.Dispose() }

$nupkg = Join-Path $outFull "wsbuddy.$pkgVersion.nupkg"
if (Test-Path -LiteralPath $nupkg) { Remove-Item -LiteralPath $nupkg -Force }
Push-Location $stage
try {
    # Pack inside the staging folder and move the result (some choco versions ignore --out-dir).
    & choco pack wsbuddy.nuspec --limit-output
    if ($LASTEXITCODE -ne 0) { throw "choco pack failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }
$packed = Join-Path $stage "wsbuddy.$pkgVersion.nupkg"
if (-not (Test-Path -LiteralPath $packed)) { throw "choco pack did not produce $packed" }
Move-Item -LiteralPath $packed -Destination $nupkg

Write-Host "nupkg: $nupkg"
Write-Host "app sha256: $appSha"
Write-Host "cli sha256: $cliSha"
if ($env:GITHUB_OUTPUT) {
    $prefix = if ($BaseUrl) { 'test_' } else { '' }
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding ascii -Value @(
        "${prefix}nupkg=$nupkg", "${prefix}package_dir=$outFull", "package_version=$pkgVersion")
}
