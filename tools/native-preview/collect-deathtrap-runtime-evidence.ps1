param(
  [ValidateRange(1,120)][int]$DurationMinutes=30,
  [ValidateRange(5,300)][int]$SampleSeconds=30,
  [string]$PreviewRoot='F:\mini_mixer_overlay\dist\NativePreviewQueueLifetime\MiniMixerOverlay-win-x64',
  [string]$OutputDirectory='F:\mini_mixer_overlay\.tmp\LiveNativeEvidence'
)
$ErrorActionPreference='Stop'
# READ-ONLY: no hooks, no game interaction or injections. Bound only to
# the one explicitly supplied MiniMixer preview and Deathtrap process.
$previewExe=Join-Path $PreviewRoot 'MiniMixerOverlay.App.exe'
if(!(Test-Path $previewExe)){throw "Preview not found: $previewExe"}
$running=Get-CimInstance Win32_Process
$previewProcesses=@($running|Where-Object {$_.Name -eq 'MiniMixerOverlay.App.exe' -and
  $_.ExecutablePath -and $_.ExecutablePath.Equals($previewExe,[StringComparison]::OrdinalIgnoreCase)})
$games=@($running|Where-Object {$_.Name -eq 'OMDDSteam-Win64-Shipping.exe'})
if($previewProcesses.Count-ne 1 -or $games.Count-ne 1){
 throw "Need exactly one started current preview and one Deathtrap game: host=$($previewProcesses.Count), Deathtrap=$($games.Count). Nothing changed."
}
$hostPid=[int]$previewProcesses[0].ProcessId
$gamePid=[int]$games[0].ProcessId
$path=Join-Path $env:TEMP "MiniMixerLivePresent_$gamePid.txt"
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$destination=Join-Path $OutputDirectory ("Deathtrap_%s_host%s_game%s.csv" -f (Get-Date -Format 'yyyyMMdd_HHmmss'),$hostPid,$gamePid)
$end=(Get-Date).AddMinutes($DurationMinutes)
$count=0
do {
  $h=Get-Process -Id $hostPid -ErrorAction SilentlyContinue
  $g=Get-Process -Id $gamePid -ErrorAction SilentlyContinue
  if(!$h -or !$g){Write-Output 'PROCESS_EXITED_EARLY';break}
  $logInfo=Get-Item $path -ErrorAction SilentlyContinue
  [pscustomobject]@{
    Timestamp=(Get-Date).ToString('o')
    HostPid=$hostPid;GamePid=$gamePid
    HostCpuSeconds=[math]::Round($h.CPU,3)
    HostPrivateMiB=[math]::Round($h.PrivateMemorySize64/1MB,2)
    HostWorkingMiB=[math]::Round($h.WorkingSet64/1MB,2)
    HostHandles=$h.HandleCount;HostThreads=$h.Threads.Count
    GameCpuSeconds=[math]::Round($g.CPU,3)
    GamePrivateMiB=[math]::Round($g.PrivateMemorySize64/1MB,2)
    GameHandles=$g.HandleCount
    NativeLogBytes=if($logInfo){$logInfo.Length}else{0}
    NativeLogUpdated=if($logInfo){$logInfo.LastWriteTime.ToString('o')}else{''}
  } | Export-Csv -LiteralPath $destination -NoTypeInformation -Append -Encoding UTF8
  ++$count
  if((Get-Date) -ge $end){break}
  Start-Sleep -Seconds $SampleSeconds
}while((Get-Date)-lt $end)
Write-Output ("READ_ONLY_NATIVE_RUNTIME_SAMPLES={0} CSV={1}" -f $count,$destination)
Write-Output 'CSV includes host/game process CPU, memory, handles and native log size only; it does not prove correct pixels, audio or absence of GPU stalls.'
