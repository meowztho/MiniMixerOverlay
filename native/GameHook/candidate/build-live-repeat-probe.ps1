$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\LiveInGame'
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'live_repeat_gpu_probe.cpp'
$exe=Join-Path $out 'live_repeat_gpu_probe.exe'
$obj=Join-Path $out 'live_repeat_gpu_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /W4 /WX /EHsc /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d12.lib dxgi.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Live-repeat probe compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Live-repeat probe failed $LASTEXITCODE"}
