param([int]$FrameCount = 15)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/KinectWorker.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Run Build.ps1 first.' }
$testRoot = Join-Path $PSScriptRoot ('obj/synthetic-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$processes = @()
$pipes = @()
function Read-Exact([IO.Stream]$Stream, [byte[]]$Bytes) {
    $read = 0
    while ($read -lt $Bytes.Length) {
        $next = $Stream.Read($Bytes,$read,$Bytes.Length-$read)
        if ($next -eq 0) { throw 'Worker closed pipe before a complete frame.' }
        $read += $next
    }
}
try {
    for ($slot=0; $slot -lt 2; $slot++) {
        $serial = 'SMOKE' + $slot + [guid]::NewGuid().ToString('N').Substring(0,12)
        $pipeName = 'NaispoCoinSmoke' + [guid]::NewGuid().ToString('N')
        $config = Join-Path $testRoot ("player$slot.json")
        @{Slot=$slot;Serial=$serial;Pipe=$pipeName;Synthetic=$true;Fps='FPS15';ParentPid=$PID;RunSeconds=15} |
            ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
        $info = [Diagnostics.ProcessStartInfo]::new($exe)
        $info.ArgumentList.Add('--config')
        $info.ArgumentList.Add($config)
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardInput = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($info)
        $processes += $process
        $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::In)
        $pipe.Connect(10000)
        $pipes += @{Stream=$pipe;Serial=$serial;LastSequence=0L;Slot=$slot;Frames=0}
    }
    for ($iteration=0; $iteration -lt $FrameCount; $iteration++) {
        foreach ($entry in $pipes) {
            $prefix = [byte[]]::new(4)
            Read-Exact $entry.Stream $prefix
            $length = [BitConverter]::ToInt32($prefix,0)
            if ($length -lt 100 -or $length -gt 1052672) { throw "Invalid length $length" }
            $data = [byte[]]::new($length)
            Read-Exact $entry.Stream $data
            if ([BitConverter]::ToUInt32($data,0) -ne 0x4B534E50 -or [BitConverter]::ToUInt16($data,4) -ne 1) { throw 'Bad wire header' }
            if ($data[6] -ne $entry.Slot) { throw 'Player slot mismatch' }
            $serial = [Text.Encoding]::ASCII.GetString($data,7,32).TrimEnd([char]0)
            if ($serial -ne $entry.Serial) { throw 'Serial mismatch' }
            $sequence = [BitConverter]::ToInt64($data,47)
            if ($sequence -le $entry.LastSequence) { throw 'Non-increasing frame sequence' }
            $entry.LastSequence = $sequence
            $entry.Frames++
            if ($data[75] -ne 1) { throw 'Synthetic player should be tracked' }
            $maskLength = [BitConverter]::ToInt32($data,96)
            foreach ($joint in @(8,15)) {
                $jointOffset = 100 + $maskLength + $joint * 39
                if ($data[$jointOffset] -ne $joint -or $data[$jointOffset+37] -lt 2 -or $data[$jointOffset+38] -ne 3) { throw 'Invalid hand joint' }
                if ([BitConverter]::ToSingle($data,$jointOffset+9) -le 0) { throw 'Invalid hand depth' }
            }
        }
    }
    foreach ($entry in $pipes) { Write-Host ("PASS: P{0} received {1} frames with serial, sequence, mask, and hand joints" -f ($entry.Slot+1),$entry.Frames) }
}
finally {
    foreach ($process in $processes) {
        if (!$process.HasExited) { $process.StandardInput.WriteLine('stop'); $process.StandardInput.Flush() }
    }
    foreach ($entry in $pipes) { $entry.Stream.Dispose() }
    foreach ($process in $processes) {
        if (!$process.WaitForExit(7000)) { $process.Kill(); $process.WaitForExit() }
        $errorText = $process.StandardError.ReadToEnd()
        if ($process.ExitCode -ne 0) { Write-Warning $errorText }
        $process.Dispose()
    }
}
