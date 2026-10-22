param([string]$DotNetPath = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { $DotNetPath = $command.Source }
    else {
        $portable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../Setup/dotnet/dotnet.exe'))
        if (Test-Path -LiteralPath $portable) { $DotNetPath = $portable }
    }
}
if (!(Test-Path -LiteralPath $DotNetPath)) {
    throw 'A .NET 10 SDK is required. Install it or pass -DotNetPath <path to dotnet.exe>.'
}
$DotNetPath = [IO.Path]::GetFullPath($DotNetPath)
$runtimeRoot = Split-Path -Parent $DotNetPath
$project = Join-Path $PSScriptRoot 'KinectWorker.csproj'
$output = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'obj/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'obj/packages'
& $DotNetPath restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Worker restore failed' }
# Publish is required: plain build does not write AppHostRelativeDotNet into the executable.
& $DotNetPath publish $project -c Release --no-restore --self-contained false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed' }
$runtime = Get-ChildItem -LiteralPath (Join-Path $runtimeRoot 'shared/Microsoft.NETCore.App') -Directory |
    Where-Object Name -Match '^10\.0\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$runtime) { throw 'The SDK installation has no .NET 10 runtime to bundle.' }
$hostSource = Join-Path $runtimeRoot ('host/fxr/' + $runtime.Name)
if (!(Test-Path -LiteralPath $hostSource)) { throw "Runtime host is missing: $hostSource" }
$runtimeDestination = Join-Path $output ('shared/Microsoft.NETCore.App/' + $runtime.Name)
$hostDestination = Join-Path $output ('host/fxr/' + $runtime.Name)
New-Item -ItemType Directory -Force -Path $runtimeDestination,$hostDestination | Out-Null
Get-ChildItem -LiteralPath $runtime.FullName -Force | Copy-Item -Destination $runtimeDestination -Recurse -Force
Get-ChildItem -LiteralPath $hostSource -Force | Copy-Item -Destination $hostDestination -Recurse -Force
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    $source = Join-Path $runtimeRoot $notice
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $output -Force }
}
& (Join-Path $output 'KinectWorker.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Bundled worker self-test failed' }
Write-Host "Ready: $output"
