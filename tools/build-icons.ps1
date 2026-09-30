$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $taskRoot 'build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$builder = Join-Path $output 'IconBuilder.exe'
& $compiler /nologo /target:winexe /reference:System.Drawing.dll (('/out:' + $builder)) (Join-Path $PSScriptRoot 'IconBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon builder failed to compile.' }
$run = Start-Process -FilePath $builder -ArgumentList ('"' + (Join-Path $taskRoot 'assets') + '" "' + (Join-Path $output 'icon-preview') + '"') -WindowStyle Hidden -Wait -PassThru
if ($run.ExitCode -ne 0) { throw 'Icon generation failed.' }
Write-Output 'Generated the PNG logo and Windows icon with nine sizes.'
