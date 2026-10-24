$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$src=Join-Path $PSScriptRoot 'owned_d3d12_verified_queue_probe.cpp'
$out=Join-Path $root '.tmp\NativeCandidate\OwnD3D12QueueIdentity'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC x64 unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$exe=Join-Path $out 'owned_d3d12_verified_queue_probe.exe'
$obj=Join-Path $out 'owned_d3d12_verified_queue_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d12.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw "Queue test build failed $LASTEXITCODE"}
$dll=Join-Path $root '.tmp\NativeCandidate\LiveInGame\MiniMixerLivePresent.x64.dll'
& $exe $dll
if($LASTEXITCODE -ne 0){throw "Owned queue integration test failed $LASTEXITCODE"}
