param([switch]$RunTests)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(-not $vs){throw 'MSVC x64 C++ toolchain missing'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$source=Join-Path $PSScriptRoot 'owned_input_probe.cpp'
$exe=Join-Path $out 'owned_input_probe.exe'
$obj=Join-Path $out 'owned_input_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+
  $source+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw "owned input probe compile failed ($LASTEXITCODE)"}
Write-Output "OWNED_INPUT_PROBE_READY=$exe"
if($RunTests){
 & $exe
 if($LASTEXITCODE -ne 0){throw "owned input probe test failed ($LASTEXITCODE)"}
}
