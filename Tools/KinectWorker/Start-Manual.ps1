param([Parameter(Mandatory=$true)][ValidateRange(0,1)][int]$Slot,
      [string]$Serial='', [switch]$Synthetic,
      [ValidateSet('FPS5','FPS15','FPS30')][string]$Fps='FPS15',
      [ValidateSet('NFOV_2x2Binned','NFOV_Unbinned','WFOV_2x2Binned','WFOV_Unbinned')][string]$DepthMode='NFOV_2x2Binned',
      [ValidateSet('DirectML','Cpu','Cuda','Gpu','TensorRT')][string]$ProcessingMode='DirectML')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe=Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/KinectWorker.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Run Build.ps1 first'}
if($Synthetic){$Serial="SYNTHETIC$Slot"}
if([string]::IsNullOrWhiteSpace($Serial)){throw 'Provide the serial from Discover.ps1. Device indices are not accepted.'}
$deviceIndex=-1
if(!$Synthetic){$discovery=(& $exe --list (Join-Path $projectRoot 'Assets/Plugins/AzureKinect/x86_64') | Out-String | ConvertFrom-Json); $match=@($discovery.devices | Where-Object {$_.serial -eq $Serial -and !$_.error}); if($match.Count -ne 1){throw "Kinect serial $Serial is not available. Close viewers/workers and check power/USB."}; $deviceIndex=$match[0].index}
$runtime=Join-Path $PSScriptRoot 'runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
$config=Join-Path $runtime "manual$Slot.json"
@{Slot=$Slot;Serial=$Serial;DeviceIndex=$deviceIndex;Pipe="NaispoCoinP$($Slot+1)";Synthetic=[bool]$Synthetic;Fps=$Fps;DepthMode=$DepthMode;ProcessingMode=$ProcessingMode;
NativeDirectory=(Join-Path $projectRoot 'Assets/Plugins/AzureKinect/x86_64');ModelPath=(Join-Path $projectRoot 'Assets/StreamingAssets/AzureKinect/dnn_model_2_0_op11.onnx');MinDistance=0.5;MaxDistance=4.5;MaxAbsX=1.2;LostTimeout=1;MinimumConfidence=1} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
# Foreground console is intentional: type stop + Enter or Ctrl+C to stop a manual worker.
& $exe --config $config
if($LASTEXITCODE -ne 0){throw 'Worker failed; inspect the error above'}
