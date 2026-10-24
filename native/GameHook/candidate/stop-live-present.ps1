param([Parameter(Mandatory=$true)][int]$GamePid,[switch]$NoWait)
$ErrorActionPreference='Stop'
$game=Get-Process -Id $GamePid -ErrorAction Stop
$allowedPath='D:\SteamLibrary\steamapps\common\Orcs Must Die Deathtrap\OMDD\Binaries\Win64\OMDDSteam-Win64-Shipping.exe'
if($game.Path -ine $allowedPath){throw 'Wrong process identity'}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$log=Join-Path $env:TEMP "MiniMixerLivePresent_$GamePid.txt"
if(!(Test-Path $log)){throw 'No live overlay telemetry for this process'}
$ledgerDir=Join-Path $root '.tmp\NativeCandidate\LiveInGame\injection-ledger'
$identity='{0}-{1}' -f $game.Id,$game.StartTime.ToUniversalTime().Ticks
$ledger=Join-Path $ledgerDir ($identity+'.json')
if(!(Test-Path $ledger)){
 throw "No matching game-start injection ledger: $ledger"
}
$record=Get-Content -LiteralPath $ledger -Raw | ConvertFrom-Json
if($record.pid -ne $game.Id -or
   $record.gameStartUtc -ne $game.StartTime.ToUniversalTime().ToString('o')){
 throw 'Game start identity changed; refusing stale PID stop request'
}
if(-not (Select-String -Path $log -Pattern 'INSTALLED' -Quiet)){
 throw 'No native DLL installation telemetry for the matching game'
}
# A stop requested in the first three seconds is valid. Never require
# the first periodic PERSISTENT_ACTIVE checkpoint to signal shutdown.
if(!(Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NativeOverlayStopSignal {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 public static extern IntPtr OpenEvent(uint rights,bool inherit,string name);
 [DllImport("kernel32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 public static extern bool SetEvent(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 public static extern bool CloseHandle(IntPtr handle);
}
'@ -PassThru -ErrorAction Stop)){throw 'Win32 stop signaling unavailable'}
$name='Local\MiniMixerOverlayStop_'+$GamePid
$handle=[NativeOverlayStopSignal]::OpenEvent(2,$false,$name)
if($handle -eq [IntPtr]::Zero){throw "Stop event inaccessible: $name"}
try{
 if(![NativeOverlayStopSignal]::SetEvent($handle)){throw 'SetEvent failed'}
}finally{
 [NativeOverlayStopSignal]::CloseHandle($handle)|Out-Null
}
Write-Output "STOP_SIGNAL_SENT PID=$GamePid"
if($NoWait){return}
$watch=[Diagnostics.Stopwatch]::StartNew()
do{
 Start-Sleep -Milliseconds 250
 $lines=Get-Content $log -ErrorAction SilentlyContinue
 if($lines -match 'UNHOOKED'){Write-Output 'PASS IN_GAME_PRESENT_HOOKS_REMOVED';return}
 if($lines -match 'UNHOOK_FAILED_PINNED'){
   throw 'Inline detach failed. Module remains pinned; restart game to fully stop'
 }
}while($watch.ElapsedMilliseconds -lt 7000)
throw 'Stop signaled but no confirmed hook detach after seven seconds'
