param([switch]$Kinect)
$ErrorActionPreference = 'Stop'
try {
    $buildFolder = Join-Path $PSScriptRoot 'Builds/CooperativeRally'
    $runtimeFolder = Join-Path $env:USERPROFILE 'NaispoCoopRuntime'
    $gameExe = Join-Path $runtimeFolder 'CoinRally.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $buildFolder 'CoinRally.exe'))) {
        throw 'Build not found. In Unity, select Naispo / Cooperative Rally / Build Windows.'
    }
    if ($runtimeFolder -match '[^\x00-\x7F]') { throw 'The Kinect runtime requires an ASCII-only runtimeFolder.' }
    if (Get-Process -Name CoinRally -ErrorAction SilentlyContinue) { throw 'Coin Rally is already running. Close it first.' }
    New-Item -ItemType Directory -Force -Path $runtimeFolder | Out-Null
    & robocopy.exe $buildFolder $runtimeFolder /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Runtime copy failed: $LASTEXITCODE" }
    $launchArguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "' + (Join-Path $runtimeFolder 'Player.log') + '"'
    if ($Kinect) { $launchArguments += ' --kinect' }
    Start-Process -FilePath $gameExe -WorkingDirectory $runtimeFolder -ArgumentList $launchArguments
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
