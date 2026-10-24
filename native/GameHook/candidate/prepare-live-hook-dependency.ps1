$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$src=Join-Path $root 'native\third_party\minhook-1.3.4'
$required=@(
  'LICENSE.txt','AUTHORS.txt','include\MinHook.h',
  'src\buffer.c','src\buffer.h','src\hook.c',
  'src\trampoline.c','src\trampoline.h',
  'src\hde\hde64.c','src\hde\hde64.h',
  'src\hde\pstdint.h','src\hde\table64.h'
)
foreach($relative in $required){
  if(!(Test-Path (Join-Path $src $relative) -PathType Leaf)){
    throw "MINHOOK_VERSIONED_SOURCE_MISSING: $relative; no auto network download"
  }
}
Write-Output "PASS VERSIONED_MINHOOK_SOURCE files=$($required.Count) root=$src offline=1"
