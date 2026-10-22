$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe=Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/KinectWorker.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Run Build.ps1 first'}
& $exe --list (Join-Path $projectRoot 'Assets/Plugins/AzureKinect/x86_64')
if($LASTEXITCODE -ne 0){throw 'Discovery failed; see error above'}
