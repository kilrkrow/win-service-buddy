$ErrorActionPreference = 'Stop'

Uninstall-BinFile -Name 'wsbuddy'

$shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Win Service Buddy.lnk'
if (Test-Path $shortcut) {
  Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
}
