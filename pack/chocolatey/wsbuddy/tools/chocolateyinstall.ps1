$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$version = '0.2.1'
$baseUrl = "https://github.com/kilrkrow/win-service-buddy/releases/download/v$version"

$cliZip = Join-Path $toolsDir "wsbuddy-cli-win-x64-v$version.zip"
$appZip = Join-Path $toolsDir "wsbuddy-app-win-x64-v$version.zip"
$cliOut = Join-Path $toolsDir 'cli'
$appOut = Join-Path $toolsDir 'app'

$cliUrl = "$baseUrl/wsbuddy-cli-win-x64-v$version.zip"
$appUrl = "$baseUrl/wsbuddy-app-win-x64-v$version.zip"

# SHA256 of the official v0.2.1 GitHub release assets, as produced by scripts/publish.ps1.
# Re-run that script and repin both values whenever the release assets are rebuilt.
$cliChecksum = '8D33978C37A4126976915F9E3D980C80A1D2D65B04F67ED8584AD29E541543CE'
$appChecksum = '7F285B8203901FB25646A98BF9A05226E4097827506BF3FC25B6B4034103B73C'

Get-ChocolateyWebFile -PackageName 'wsbuddy' -FileFullPath $cliZip -Url $cliUrl `
  -Checksum $cliChecksum -ChecksumType 'sha256'

Get-ChocolateyWebFile -PackageName 'wsbuddy' -FileFullPath $appZip -Url $appUrl `
  -Checksum $appChecksum -ChecksumType 'sha256'

Get-ChocolateyUnzip -FileFullPath $cliZip -Destination $cliOut -PackageName 'wsbuddy'
Get-ChocolateyUnzip -FileFullPath $appZip -Destination $appOut -PackageName 'wsbuddy'

# Shim CLI onto PATH
$cliExe = Join-Path $cliOut 'wsbuddy.exe'
if (-not (Test-Path $cliExe)) {
  throw "wsbuddy.exe not found after unzip: $cliExe"
}
Install-BinFile -Name 'wsbuddy' -Path $cliExe

# Start Menu shortcut for GUI
$appExe = Join-Path $appOut 'WinServiceBuddy.App.exe'
if (Test-Path $appExe) {
  $shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Win Service Buddy.lnk'
  Install-ChocolateyShortcut -ShortcutFilePath $shortcut -TargetPath $appExe -WorkingDirectory $appOut `
    -Description 'Win Service Buddy'
}

# Drop temporary zips to save disk
Remove-Item $cliZip, $appZip -Force -ErrorAction SilentlyContinue
