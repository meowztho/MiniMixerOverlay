$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\FlipD3D11'
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC 64-bit unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$src=Join-Path $PSScriptRoot 'owned_d3d11_flip_compositor_probe.cpp'
$exe=Join-Path $out 'owned_d3d11_flip_compositor_probe.exe'
$obj=Join-Path $out 'owned_d3d11_flip_compositor_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d11.lib d3dcompiler.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw "Owned D3D11 flip compilation failed: $LASTEXITCODE"}
& $exe
if($LASTEXITCODE -ne 0){throw "Owned D3D11 flip GPU verification failed: $LASTEXITCODE"}
