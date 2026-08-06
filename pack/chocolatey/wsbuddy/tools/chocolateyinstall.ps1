$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$version = '0.2.0'
$baseUrl = "https://github.com/kilrkrow/win-service-buddy/releases/download/v$version"

$cliZip = Join-Path $toolsDir "wsbuddy-cli-win-x64-v$version.zip"
$appZip = Join-Path $toolsDir "wsbuddy-app-win-x64-v$version.zip"
$cliOut = Join-Path $toolsDir 'cli'
$appOut = Join-Path $toolsDir 'app'

$cliUrl = "$baseUrl/wsbuddy-cli-win-x64-v$version.zip"
$appUrl = "$baseUrl/wsbuddy-app-win-x64-v$version.zip"

# SHA256 of the official v0.2.0 GitHub release assets
$cliChecksum = '4E1DE17AC51E384E81048AD87E8033A194C1069BAAF656505AE2C3A153E3001B'
$appChecksum = '270B53D9324696A657CB8328940682FED4DFFD7D7FDC8A756D72EB648B39AB53'

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
