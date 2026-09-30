$ErrorActionPreference = 'Stop'
$installedApp = Join-Path $env:LOCALAPPDATA 'Programs\TaskBarPlus\TaskBarPlus.exe'
if (Test-Path -LiteralPath $installedApp) {
    Start-Process -FilePath $installedApp -ArgumentList '--quit' -WindowStyle Hidden -Wait
    $running = Get-Process -Name TaskBarPlus -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedApp }
    if ($running) { $running | Wait-Process -Timeout 10 -ErrorAction Stop }
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TaskBarPlus' -ErrorAction SilentlyContinue
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'TaskBar+.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
if (Test-Path -LiteralPath $installedApp) { Remove-Item -LiteralPath $installedApp }
$installedIcon = Join-Path $env:LOCALAPPDATA 'Programs\TaskBarPlus\TaskBarPlus.ico'
if (Test-Path -LiteralPath $installedIcon) { Remove-Item -LiteralPath $installedIcon }
Write-Output 'TaskBar+ removed. Preferences and source files were kept. Any clock-seconds preference remains as configured in Windows.'
