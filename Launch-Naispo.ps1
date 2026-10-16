$ErrorActionPreference = 'Stop'
try {
    $buildFolder = Join-Path $PSScriptRoot 'Builds\Windows'
    $runtimeFolder = Join-Path $env:USERPROFILE 'NaispoRuntime'
    if (-not (Test-Path -LiteralPath (Join-Path $buildFolder 'Naispo.exe'))) {
        throw 'Build not found. Open Unity and select Naispo / Build Windows Demo.'
    }
    if ($runtimeFolder -match '[^\x00-\x7F]') {
        throw 'The Kinect runtime needs an ASCII-only folder. Please configure runtimeFolder in Launch-Naispo.ps1.'
    }
    # A second instance cannot open the same sensor. Do not overwrite a running app.
    $runningGame = Get-Process -Name Naispo -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -eq (Join-Path $runtimeFolder 'Naispo.exe') -or $_.Path -eq (Join-Path $buildFolder 'Naispo.exe')
    }
    if ($runningGame) { throw 'Naispo is already running. Close its window before starting again.' }
    New-Item -ItemType Directory -Force -Path $runtimeFolder | Out-Null
    # Copy changed build files without deleting existing logs or unrelated files.
    & robocopy.exe $buildFolder $runtimeFolder /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Runtime copy failed (robocopy exit $LASTEXITCODE)." }
    $playerLog = Join-Path $runtimeFolder 'Player.log'
    Start-Process -FilePath (Join-Path $runtimeFolder 'Naispo.exe') -WorkingDirectory $runtimeFolder -ArgumentList (
        '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "' + $playerLog + '"')
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
