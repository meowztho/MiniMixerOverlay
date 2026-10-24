$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$src=Join-Path $PSScriptRoot 'owned_d3d11_provider_hook_probe.cpp'
$dll=Join-Path $root '.tmp\NativeCandidate\LiveInGame\MiniMixerLivePresent.x64.dll'
$out=Join-Path $root '.tmp\NativeCandidate\OwnD3D11Provider'
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC not available'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe=Join-Path $out 'owned_d3d11_provider_hook_probe.exe'
$obj=Join-Path $out 'owned_d3d11_provider_hook_probe.obj'
if(!(Test-Path $dll)){throw 'Build own DLL first with build-live-present-dll.ps1'}
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d11.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw "Own D3D11 native provider probe compilation failed"}
& $exe $dll
if($LASTEXITCODE -ne 0){throw "Own D3D11 native provider runtime failed"}
