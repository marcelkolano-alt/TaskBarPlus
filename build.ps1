$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
$output = Join-Path $taskRoot 'build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','Accessibility.dll')
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll') | ForEach-Object {
    $candidate = Join-Path $framework ('WPF\' + $_)
    if (Test-Path -LiteralPath $candidate) { $candidate } else { Join-Path $framework $_ }
}
$icon = Join-Path $taskRoot 'assets\TaskBarPlus.ico'
if (-not (Test-Path -LiteralPath $icon)) { & (Join-Path $taskRoot 'tools\build-icons.ps1') }
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4',('/out:' + (Join-Path $output 'TaskBarPlus.exe')),('/win32manifest:' + (Join-Path $taskRoot 'app.manifest')),('/win32icon:' + $icon),('/resource:' + $icon + ',TaskbarPlus.Icon'),('/resource:' + (Join-Path $taskRoot 'src\MainWindow.xaml') + ',TaskbarPlus.MainWindow.xaml'))
$compilerArgs += $references | ForEach-Object { '/reference:' + $_ }
$compilerArgs += @('Native.cs','Settings.cs','TaskbarEngine.cs','SelfTest.cs','Program.cs') | ForEach-Object { Join-Path $taskRoot ('src\' + $_) }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ('Built ' + (Join-Path $output 'TaskBarPlus.exe'))
