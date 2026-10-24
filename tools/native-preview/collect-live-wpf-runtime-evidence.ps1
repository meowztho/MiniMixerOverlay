param(
  [Parameter(Mandatory=$true)][string]$ExecutablePath,
  [ValidateRange(1,120)][int]$DurationMinutes=30,
  [ValidateRange(10,300)][int]$SampleSeconds=30,
  [switch]$ProbeOnly,
  [string]$OutputDirectory='F:\mini_mixer_overlay\.tmp\WpfRuntimeEvidence'
)
$ErrorActionPreference='Stop'
# Strictly read only: never starts/stops the app or changes settings,
# audio sessions, process priority, windows, game hooks or injected DLLs.
$full=[System.IO.Path]::GetFullPath($ExecutablePath)
if(!(Test-Path -LiteralPath $full -PathType Leaf)){
 throw "Executable does not exist: $full"
}
$processes=@(Get-CimInstance Win32_Process -Filter "Name='MiniMixerOverlay.App.exe'" |
  Where-Object { $_.ExecutablePath -and
    [string]::Equals($_.ExecutablePath,$full,[StringComparison]::OrdinalIgnoreCase) })
if($ProbeOnly){
 Write-Output ("WPF_READONLY_PREFLIGHT expected={0} matches={1}" -f $full,$processes.Count)
 if($processes.Count -ne 1){Write-Output "UNVERIFIED: Launch the exact WPF build first."}
 exit 0
}
if($processes.Count -ne 1){
 throw "Need exactly one running MiniMixerOverlay.App.exe from supplied image: matches=$($processes.Count). Nothing modified."
}
$matchedPid=[int]$processes[0].ProcessId
$first=Get-Process -Id $matchedPid -ErrorAction Stop
$start=$first.StartTime
$originalCpu=$first.TotalProcessorTime.TotalSeconds
$originalPrivate=$first.PrivateMemorySize64
$originalHandles=$first.HandleCount
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$csv=Join-Path $OutputDirectory ("WPF_{0}_pid{1}.csv" -f
    (Get-Date -Format 'yyyyMMdd_HHmmss'),$matchedPid)
$deadline=(Get-Date).AddMinutes($DurationMinutes)
$sampleCount=0
$firstFailure=''
while($true){
 $p=Get-Process -Id $matchedPid -ErrorAction SilentlyContinue
 if($null -eq $p){$firstFailure='App process exited';break}
 $p.Refresh()
 if($p.StartTime -ne $start){$firstFailure='PID recycled';break}
 $guardLog=Join-Path $env:APPDATA 'MiniMixerOverlay\logs\guard.log'
 $log=Get-Item -LiteralPath $guardLog -ErrorAction SilentlyContinue
 [pscustomobject]@{
   TimestampUtc=[DateTime]::UtcNow.ToString('o')
   Pid=$matchedPid
   ProcessStartUtc=$start.ToUniversalTime().ToString('o')
   CpuSeconds=[math]::Round($p.TotalProcessorTime.TotalSeconds,3)
   PrivateMiB=[math]::Round($p.PrivateMemorySize64/1MB,2)
   WorkingMiB=[math]::Round($p.WorkingSet64/1MB,2)
   Handles=$p.HandleCount
   Threads=$p.Threads.Count
   GuardLogBytes=if($log){$log.Length}else{0}
 }|Export-Csv -LiteralPath $csv -Append -NoTypeInformation -Encoding UTF8
 ++$sampleCount
 if((Get-Date) -ge $deadline){break}
 Start-Sleep -Seconds $SampleSeconds
}
$p=Get-Process -Id $matchedPid -ErrorAction SilentlyContinue
if($p){$p.Refresh()}
Write-Output ("WPF_READONLY_SAMPLES={0} csv={1}" -f $sampleCount,$csv)
if($p -and $p.StartTime -eq $start){
 Write-Output ("WPF_RESOURCE_DELTA private_mib={0:F2} handles={1} cpu_seconds={2:F2}" -f
  (($p.PrivateMemorySize64-$originalPrivate)/1MB),
  ($p.HandleCount-$originalHandles),
  ($p.TotalProcessorTime.TotalSeconds-$originalCpu))
}
if($firstFailure){throw "Stopped: $firstFailure"}
Write-Output 'MEASUREMENT_ONLY: resource trends do not prove correct pixels, slider values, mute state, absence of GPU stalls or 30-minute zero-error experience.'
