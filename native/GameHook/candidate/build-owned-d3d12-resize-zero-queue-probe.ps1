$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\ReReviewResizeZero'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_d3d12_resize_zero_queue_probe.cpp'
$obj=Join-Path $out 'owned_d3d12_resize_zero_queue_probe.obj'
$exe=Join-Path $out 'owned_d3d12_resize_zero_queue_probe.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /I"'+$PSScriptRoot+'" "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d12.lib d3d11.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Owned ResizeBuffers1(count=0) compilation failed'}
$dll=Join-Path $root '.tmp\NativeCandidate\LiveInGame\MiniMixerLivePresent.x64.dll'
& $exe $dll --count-zero
if($LASTEXITCODE -ne 0){throw "Owned ResizeBuffers1(count=0) FAILED $LASTEXITCODE"}
& $exe $dll --count-two
if($LASTEXITCODE -ne 0){throw "Owned ResizeBuffers1(count=2) FAILED $LASTEXITCODE"}
