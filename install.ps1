$ErrorActionPreference = 'Stop'
$builtApp = Join-Path $PSScriptRoot 'build\TaskBarPlus.exe'
if (-not (Test-Path -LiteralPath $builtApp)) { $builtApp = Join-Path $PSScriptRoot 'TaskBarPlus.exe' }
if (-not (Test-Path -LiteralPath $builtApp)) { $builtApp = Join-Path $PSScriptRoot 'build\TaskBarPlus.exe' }
if (-not (Test-Path -LiteralPath $builtApp)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\TaskBarPlus'
$installedApp = Join-Path $installFolder 'TaskBarPlus.exe'
if (Test-Path -LiteralPath $installedApp) {
    Start-Process -FilePath $installedApp -ArgumentList '--quit' -WindowStyle Hidden -Wait
    $running = Get-Process -Name TaskBarPlus -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedApp }
    if ($running) { $running | Wait-Process -Timeout 10 -ErrorAction Stop }
}
New-Item -ItemType Directory -Path $installFolder -Force | Out-Null
Copy-Item -LiteralPath $builtApp -Destination $installedApp -Force
$installedIcon = Join-Path $installFolder 'TaskBarPlus.ico'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\TaskBarPlus.ico') -Destination $installedIcon -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'TaskBar+.lnk'))
$shortcut.TargetPath = $installedApp
$shortcut.WorkingDirectory = $installFolder
$shortcut.Description = 'Taskbar transparency, colors, and centered app buttons'
$shortcut.IconLocation = $installedIcon + ',0'
$shortcut.Save()
# Refresh only this app's existing pins, preserving their order and all other pins.
$updatedLinks = @((Join-Path ([Environment]::GetFolderPath('Programs')) 'TaskBar+.lnk'))
$pinnedFolder = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
Get-ChildItem -LiteralPath $pinnedFolder -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
    $pin = $shell.CreateShortcut($_.FullName)
    if ([IO.Path]::GetFileName($pin.TargetPath) -eq 'TaskBarPlus.exe') {
        $pin.TargetPath = $installedApp
        $pin.WorkingDirectory = $installFolder
        $pin.Arguments = ''
        $pin.IconLocation = $installedIcon + ',0'
        $pin.Save()
        $updatedLinks += $_.FullName
    }
}
if (-not ('TaskBarPlus.InstallNotifications' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace TaskBarPlus {
    public static class InstallNotifications {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
    }
}
'@
}
foreach ($linkPath in $updatedLinks) { [TaskBarPlus.InstallNotifications]::SHChangeNotify(0x2000, 0x5, $linkPath, [IntPtr]::Zero) }
[TaskBarPlus.InstallNotifications]::SHChangeNotify(0x2000, 0x5, $installedApp, [IntPtr]::Zero)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
New-ItemProperty -Path $runKey -Name 'TaskBarPlus' -Value ('"' + $installedApp + '" --background') -PropertyType String -Force | Out-Null
Start-Process -FilePath $installedApp -ArgumentList '--background' -WindowStyle Hidden
Write-Output ('Installed and running: ' + $installedApp)
Write-Output 'Start with Windows is enabled. Open TaskBar+ from the Start menu or double-click its tray icon.'
