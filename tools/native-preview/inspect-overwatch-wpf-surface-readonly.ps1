param(
 [ValidateSet('Unknown','Borderless','Fullscreen')][string]$UserSelectedMode='Unknown'
)
$ErrorActionPreference='Stop'
# Read only Win32 top-level window inspection. This script never calls
# GetProcessModules/OpenProcess/SetWindowPos or injects in Overwatch.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32OverlaySurfaceProbe {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int L,T,R,B;}
 public delegate bool Callback(IntPtr h,IntPtr data);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr data);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h,StringBuilder s,int max);
 [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT rect);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$allowed=@('Overwatch.exe','Overwatch2.exe','MiniMixerOverlay.App.exe')
$rows=[System.Collections.Generic.List[object]]::new()
$ps=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -in $allowed})
if(!(@($ps|Where-Object {$_.Name -like 'Overwatch*'}).Count)){
 Write-Output 'OVERWATCH_NOT_RUNNING: Cannot claim full-screen or hook/native test.'
}
$byPid=@{}
foreach($p in $ps){$byPid[[uint32]$p.ProcessId]=$p.Name}
$fg=[Win32OverlaySurfaceProbe]::GetForegroundWindow()
$cb=[Win32OverlaySurfaceProbe+Callback]{
 param([IntPtr]$h,[IntPtr]$data)
 [uint32]$ownerPid=0
 [void][Win32OverlaySurfaceProbe]::GetWindowThreadProcessId($h,[ref]$ownerPid)
 if(!$byPid.ContainsKey($ownerPid)){return $true}
 $t=[Text.StringBuilder]::new(256)
 [void][Win32OverlaySurfaceProbe]::GetWindowTextW($h,$t,256)
 $rect=[Win32OverlaySurfaceProbe+RECT]::new()
 [void][Win32OverlaySurfaceProbe]::GetWindowRect($h,[ref]$rect)
 $ex=[uint64][Win32OverlaySurfaceProbe]::GetWindowLongPtrW($h,-20).ToInt64()
 if([Win32OverlaySurfaceProbe]::IsWindowVisible($h)){
   $rows.Add([pscustomobject]@{
     Process=$byPid[$ownerPid];PID=$ownerPid;Handle=('0x{0:X}' -f $h.ToInt64())
     Foreground=($h -eq $fg)
     Title=$t.ToString()
     Topmost=(($ex -band 0x8)-ne 0)
     Layered=(($ex -band 0x80000)-ne 0)
     X=$rect.L;Y=$rect.T;Width=$rect.R-$rect.L;Height=$rect.B-$rect.T
   })
 }
 return $true
}
[void][Win32OverlaySurfaceProbe]::EnumWindows($cb,[IntPtr]::Zero)
$rows|Format-Table -AutoSize | Out-String -Width 200|Write-Output
Write-Output ('USER_SELECTED_GAME_MODE='+$UserSelectedMode)
Write-Output 'RECTANGLES_AND_TOPMOST_ONLY: Windows geometry cannot prove DXGI exclusive fullscreen, actual pixels, active rendering or correct input/audio.'
Write-Output 'NO_EXTERNAL_PROCESS_MODULE_ENUMERATION_OR_INJECTION=1'
