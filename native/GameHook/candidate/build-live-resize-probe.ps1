$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\LiveInGame'
New-Item -Path $out -ItemType Directory -Force|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select -First 1
if(!$vs){throw 'MSVC not available'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'live_resize_gpu_probe.cpp'
$exe=Join-Path $out 'live_resize_gpu_probe.exe'
$obj=Join-Path $out 'live_resize_gpu_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d12.lib dxgi.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Native D3D12 resize test build failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Real DXGI resize lifecycle failed exit=$LASTEXITCODE"}
