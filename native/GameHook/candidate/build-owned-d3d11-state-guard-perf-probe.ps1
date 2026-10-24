$ErrorActionPreference='Stop'
$out=Join-Path $PSScriptRoot '..\..\..\ .tmp\NativeCandidate\OwnedGuardPerf'
$out=$out.Replace('\ ', '')
New-Item -ItemType Directory -Path $out -Force|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_d3d11_state_guard_perf_probe.cpp'
$obj=Join-Path $out 'guard_perf.obj'
$exe=Join-Path $out 'guard_perf.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /I"'+$PSScriptRoot+'" "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link d3d11.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "WARP state perf failure $LASTEXITCODE"}
& $exe --hardware
if($LASTEXITCODE -ne 0){throw "Hardware state perf failure $LASTEXITCODE"}
