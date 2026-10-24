$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedPbgraComposite'
New-Item $out -ItemType Directory -Force | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_pbgra_compositor_probe.cpp'
$exe=Join-Path $out 'owned_pbgra_compositor_probe.exe'
$obj=Join-Path $out 'owned_pbgra_compositor_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d12.lib dxgi.lib d3dcompiler.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Pbgra compositor compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Pbgra compositing GPU test failed exit=$LASTEXITCODE"}
