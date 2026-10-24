param([switch]$RunTests)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$src=Join-Path $PSScriptRoot 'd3d12_gpu_probe.cpp'
$out=Join-Path $root '.tmp\NativeCandidate'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if(-not (Test-Path $vswhere)){throw 'MSVC Build Tools missing'}
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(-not $vs){throw 'No C++ toolchain found'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$exe=Join-Path $out 'd3d12_gpu_probe.exe'
$obj=Join-Path $out 'd3d12_gpu_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W3 /O2 "'+$src+
     '" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d12.lib d3dcompiler.lib dxgi.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw "D3D12 probe compilation failed: $LASTEXITCODE"}
Write-Output "D3D12_PROBE_READY=$exe"
if($RunTests){
    & $exe
    if($LASTEXITCODE -ne 0){throw "Hardware test failed: $LASTEXITCODE"}
    & $exe --warp
    if($LASTEXITCODE -ne 0){throw "WARP test failed: $LASTEXITCODE"}
    & $exe --hook
    if($LASTEXITCODE -ne 0){throw "Hardware hook lifecycle failed: $LASTEXITCODE"}
    & $exe --hook --warp
    if($LASTEXITCODE -ne 0){throw "WARP hook lifecycle failed: $LASTEXITCODE"}
}
